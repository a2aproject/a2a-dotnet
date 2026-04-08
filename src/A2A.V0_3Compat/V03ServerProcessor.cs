namespace A2A.V0_3Compat;

using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using V03 = A2A.V0_3;

/// <summary>
/// Processes incoming HTTP requests that may use A2A v0.3 or v1.0 JSON-RPC format.
/// Automatically translates v0.3 requests to v1.0 and v0.3 responses back to v0.3 wire format.
/// </summary>
public static class V03ServerProcessor
{
    /// <summary>
    /// Processes an A2A JSON-RPC request, handling both v0.3 and v1.0 wire formats.
    /// v0.3 requests are translated to v1.0, processed, and responses are translated back to v0.3.
    /// v1.0 requests are passed through to the handler unchanged.
    /// </summary>
    /// <param name="requestHandler">The v1.0 A2A request handler.</param>
    /// <param name="request">The incoming HTTP request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<IResult> ProcessRequestAsync(
        IA2ARequestHandler requestHandler,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(request);

        // Version preflight: reject headers that are neither 1.0 nor 0.3.
        // Per spec, valid values are "0.3" and "1.0"; absent header is treated as v0.3.
        var preflightVersion = request.Headers["A2A-Version"].FirstOrDefault();
        if (!string.IsNullOrEmpty(preflightVersion) && preflightVersion != "1.0" && preflightVersion != "0.3")
        {
            return MakeV03ErrorResult(default, new A2AException(
                $"Protocol version '{preflightVersion}' is not supported. Supported versions: 0.3, 1.0",
                A2AErrorCode.VersionNotSupported));
        }

        // Route by A2A-Version header: per spec, v1.0 clients MUST send this header;
        // absent header indicates a v0.3 client.
        var version = request.Headers["A2A-Version"].FirstOrDefault();
        if (!string.IsNullOrEmpty(version) && version != "0.3")
        {
            // v1.0 request: parse body and delegate directly to v1.0 processor.
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return MakeErrorResult(default, V03.JsonRpcResponse.ParseErrorResponse);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("method", out var methodProp) ||
                    methodProp.ValueKind != JsonValueKind.String)
                {
                    // JSON is valid but request object is missing the required "method" field → InvalidRequest.
                    return MakeErrorResult(default, V03.JsonRpcResponse.InvalidRequestResponse);
                }

                var method = methodProp.GetString() ?? string.Empty;
                return await HandleV1RequestAsync(requestHandler, root, method, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // v0.3 request: peek at method name first — V03.JsonRpcRequestConverter may throw
        // for method names it doesn't recognise, so validate before attempting full deserialization.
        JsonDocument v03Doc;
        try
        {
            v03Doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return MakeErrorResult(default, V03.JsonRpcResponse.ParseErrorResponse);
        }

        using (v03Doc)
        {
            var root = v03Doc.RootElement;
            if (!root.TryGetProperty("method", out var methodProp) ||
                methodProp.ValueKind != JsonValueKind.String)
            {
                // JSON is valid but request object is missing the required "method" field → InvalidRequest.
                return MakeErrorResult(default, V03.JsonRpcResponse.InvalidRequestResponse);
            }

            var method = methodProp.GetString() ?? string.Empty;
            if (!V03.A2AMethods.IsValidMethod(method))
            {
                return MakeErrorResult(default, V03.JsonRpcResponse.MethodNotFoundResponse);
            }

            V03.JsonRpcRequest? rpcRequest = null;
            try
            {
                var typeInfo = (JsonTypeInfo<V03.JsonRpcRequest>)V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.JsonRpcRequest));
                rpcRequest = JsonSerializer.Deserialize(root, typeInfo);

                if (rpcRequest is null)
                {
                    return MakeErrorResult(default, V03.JsonRpcResponse.ParseErrorResponse);
                }

                if (V03.A2AMethods.IsStreamingMethod(rpcRequest.Method))
                {
                    return HandleStreaming(requestHandler, rpcRequest, cancellationToken);
                }

                return await HandleSingleAsync(requestHandler, rpcRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (A2AException ex)
            {
                var id = rpcRequest?.Id ?? default;
                return MakeV03ErrorResult(id, ex);
            }
            catch (JsonException)
            {
                return MakeErrorResult(default, V03.JsonRpcResponse.ParseErrorResponse);
            }
            catch (Exception)
            {
                var id = rpcRequest?.Id ?? default;
                return MakeErrorResult(id, V03.JsonRpcResponse.InternalErrorResponse);
            }
        }
    }

    // Handles v1.0 method names routed directly from ProcessRequestAsync.
    // Extracts id and params from the already-parsed JsonElement and dispatches directly to the handler.
    private static async Task<IResult> HandleV1RequestAsync(
        IA2ARequestHandler handler,
        JsonElement root,
        string method,
        CancellationToken ct)
    {
        var id = root.TryGetProperty("id", out var idEl)
            ? idEl.ValueKind switch
            {
                JsonValueKind.String => new JsonRpcId(idEl.GetString()),
                JsonValueKind.Number when idEl.TryGetInt64(out var n) => new JsonRpcId(n),
                _ => new JsonRpcId((string?)null)
            }
            : new JsonRpcId((string?)null);

        JsonElement? paramsEl = null;
        if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            paramsEl = p.Clone();
        }

        try
        {
            if (A2AMethods.IsStreamingMethod(method))
            {
                return DispatchV1Streaming(handler, id, method, paramsEl, ct);
            }
            return await DispatchV1SingleAsync(handler, id, method, paramsEl, ct).ConfigureAwait(false);
        }
        catch (A2AException ex)
        {
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(id, ex));
        }
        catch (Exception)
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(id, "An internal error occurred."));
        }
    }

    // Dispatches a v1.0 non-streaming request directly to the handler.
    private static async Task<IResult> DispatchV1SingleAsync(
        IA2ARequestHandler handler,
        JsonRpcId id,
        string method,
        JsonElement? paramsEl,
        CancellationToken ct)
    {
        if (paramsEl is null)
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(id));
        }

        // For push notification methods, probe support before deserializing params.
        if (A2AMethods.IsPushNotificationMethod(method))
        {
            try { await handler.GetTaskPushNotificationConfigAsync(null!, ct).ConfigureAwait(false); }
            catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.PushNotificationNotSupported) { throw; }
            catch { /* any other exception means push is supported; continue */ }
        }

        JsonRpcResponse? response;
        switch (method)
        {
            case A2AMethods.SendMessage:
            {
                var req = DeserializeV1Params<SendMessageRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.SendMessageAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.GetTask:
            {
                var req = DeserializeV1Params<GetTaskRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.GetTaskAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.ListTasks:
            {
                var req = DeserializeV1Params<ListTasksRequest>(paramsEl.Value, id);
                if (req.PageSize is { } ps && (ps <= 0 || ps > 100))
                    throw new A2AException($"Invalid pageSize: {ps}. Must be between 1 and 100.", A2AErrorCode.InvalidParams);
                if (req.HistoryLength is { } hl && hl < 0)
                    throw new A2AException($"Invalid historyLength: {hl}. Must be non-negative.", A2AErrorCode.InvalidParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.ListTasksAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.CancelTask:
            {
                var req = DeserializeV1Params<CancelTaskRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.CancelTaskAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.CreateTaskPushNotificationConfig:
            {
                var req = DeserializeV1Params<CreateTaskPushNotificationConfigRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.CreateTaskPushNotificationConfigAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.GetTaskPushNotificationConfig:
            {
                var req = DeserializeV1Params<GetTaskPushNotificationConfigRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.GetTaskPushNotificationConfigAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.ListTaskPushNotificationConfig:
            {
                var req = DeserializeV1Params<ListTaskPushNotificationConfigRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.ListTaskPushNotificationConfigAsync(req, ct).ConfigureAwait(false));
                break;
            }
            case A2AMethods.DeleteTaskPushNotificationConfig:
            {
                var req = DeserializeV1Params<DeleteTaskPushNotificationConfigRequest>(paramsEl.Value, id);
                await handler.DeleteTaskPushNotificationConfigAsync(req, ct).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, (object?)null);
                break;
            }
            case A2AMethods.GetExtendedAgentCard:
            {
                var req = DeserializeV1Params<GetExtendedAgentCardRequest>(paramsEl.Value, id);
                response = JsonRpcResponse.CreateJsonRpcResponse(id, await handler.GetExtendedAgentCardAsync(req, ct).ConfigureAwait(false));
                break;
            }
            default:
                response = JsonRpcResponse.MethodNotFoundResponse(id);
                break;
        }

        return new JsonRpcResponseResult(response);
    }

    // Dispatches a v1.0 streaming request directly to the handler.
    private static IResult DispatchV1Streaming(
        IA2ARequestHandler handler,
        JsonRpcId id,
        string method,
        JsonElement? paramsEl,
        CancellationToken ct)
    {
        if (paramsEl is null)
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(id));
        }

        switch (method)
        {
            case A2AMethods.SubscribeToTask:
            {
                var req = DeserializeV1Params<SubscribeToTaskRequest>(paramsEl.Value, id);
                return new JsonRpcStreamedResult(handler.SubscribeToTaskAsync(req, ct), id);
            }
            case A2AMethods.SendStreamingMessage:
            {
                var req = DeserializeV1Params<SendMessageRequest>(paramsEl.Value, id);
                return new JsonRpcStreamedResult(handler.SendStreamingMessageAsync(req, ct), id);
            }
            default:
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(id));
        }
    }

    private static T DeserializeV1Params<T>(JsonElement element, JsonRpcId id) where T : class
    {
        T? result;
        try
        {
            result = (T?)element.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T)));
        }
        catch (JsonException ex)
        {
            throw new A2AException($"Invalid parameters: {ex.Message}", A2AErrorCode.InvalidParams);
        }

        if (result is null)
        {
            throw new A2AException($"Parameters could not be deserialized as {typeof(T).Name}", A2AErrorCode.InvalidParams);
        }

        if (result is SendMessageRequest sendMsg && sendMsg.Message.Parts.Count == 0)
        {
            throw new A2AException("Message parts cannot be empty", A2AErrorCode.InvalidParams);
        }

        return result;
    }

    private static async Task<IResult> HandleSingleAsync(
        IA2ARequestHandler handler,
        V03.JsonRpcRequest rpcRequest,
        CancellationToken ct)
    {
        if (rpcRequest.Params is null)
        {
            return MakeErrorResult(rpcRequest.Id, V03.JsonRpcResponse.InvalidParamsResponse);
        }

        switch (rpcRequest.Method)
        {
            case V03.A2AMethods.MessageSend:
            {
                var v03Params = DeserializeParams<V03.MessageSendParams>(rpcRequest.Params.Value);
                var v1Request = V03TypeConverter.ToV1SendMessageRequest(v03Params);
                var v1Response = await handler.SendMessageAsync(v1Request, ct).ConfigureAwait(false);
                var v03Response = V03TypeConverter.ToV03Response(v1Response);
                return MakeSuccessResult(rpcRequest.Id, v03Response, typeof(V03.A2AResponse));
            }

            case V03.A2AMethods.TaskGet:
            {
                var v03Params = DeserializeParams<V03.TaskQueryParams>(rpcRequest.Params.Value);
                var v1Request = V03TypeConverter.ToV1GetTaskRequest(v03Params);
                var agentTask = await handler.GetTaskAsync(v1Request, ct).ConfigureAwait(false);
                return MakeSuccessResult(rpcRequest.Id, V03TypeConverter.ToV03AgentTask(agentTask), typeof(V03.AgentTask));
            }

            case V03.A2AMethods.TaskCancel:
            {
                var v03Params = DeserializeParams<V03.TaskIdParams>(rpcRequest.Params.Value);
                var v1Request = V03TypeConverter.ToV1CancelTaskRequest(v03Params);
                var agentTask = await handler.CancelTaskAsync(v1Request, ct).ConfigureAwait(false);
                return MakeSuccessResult(rpcRequest.Id, V03TypeConverter.ToV03AgentTask(agentTask), typeof(V03.AgentTask));
            }

            case V03.A2AMethods.TaskPushNotificationConfigSet:
            {
                var v03Config = DeserializeParams<V03.TaskPushNotificationConfig>(rpcRequest.Params.Value);
                var v1Request = new CreateTaskPushNotificationConfigRequest
                {
                    TaskId = v03Config.TaskId,
                    Config = V03TypeConverter.ToV1PushNotificationConfig(v03Config.PushNotificationConfig),
                };
                var v1Result = await handler.CreateTaskPushNotificationConfigAsync(v1Request, ct).ConfigureAwait(false);
                var v03Result = new V03.TaskPushNotificationConfig
                {
                    TaskId = v1Result.TaskId,
                    PushNotificationConfig = V03TypeConverter.ToV03PushNotificationConfig(v1Result.PushNotificationConfig),
                };
                return MakeSuccessResult(rpcRequest.Id, v03Result, typeof(V03.TaskPushNotificationConfig));
            }

            case V03.A2AMethods.TaskPushNotificationConfigGet:
            {
                var v03Params = DeserializeParams<V03.GetTaskPushNotificationConfigParams>(rpcRequest.Params.Value);
                var v1Request = new GetTaskPushNotificationConfigRequest
                {
                    TaskId = v03Params.Id,
                    Id = v03Params.PushNotificationConfigId ?? string.Empty,
                };
                var v1Result = await handler.GetTaskPushNotificationConfigAsync(v1Request, ct).ConfigureAwait(false);
                var v03Result = new V03.TaskPushNotificationConfig
                {
                    TaskId = v1Result.TaskId,
                    PushNotificationConfig = V03TypeConverter.ToV03PushNotificationConfig(v1Result.PushNotificationConfig),
                };
                return MakeSuccessResult(rpcRequest.Id, v03Result, typeof(V03.TaskPushNotificationConfig));
            }

            default:
                // Unrecognized v0.3 method name. v1.0 clients are routed via the
                // A2A-Version header before reaching here, so this is a genuine v0.3 unknown method.
                return MakeErrorResult(rpcRequest.Id, V03.JsonRpcResponse.MethodNotFoundResponse);
        }
    }

    private static IResult HandleStreaming(
        IA2ARequestHandler handler,
        V03.JsonRpcRequest rpcRequest,
        CancellationToken ct)
    {
        if (rpcRequest.Params is null)
        {
            return MakeErrorResult(rpcRequest.Id, V03.JsonRpcResponse.InvalidParamsResponse);
        }

        switch (rpcRequest.Method)
        {
            case V03.A2AMethods.MessageStream:
            {
                var v03Params = DeserializeParams<V03.MessageSendParams>(rpcRequest.Params.Value);
                var v1Request = V03TypeConverter.ToV1SendMessageRequest(v03Params);
                var v1Events = handler.SendStreamingMessageAsync(v1Request, ct);
                return new V03JsonRpcStreamedResult(v1Events.Select(V03TypeConverter.ToV03Event), rpcRequest.Id);
            }

            case V03.A2AMethods.TaskSubscribe:
            {
                var v03Params = DeserializeParams<V03.TaskIdParams>(rpcRequest.Params.Value);
                var subscribeRequest = new SubscribeToTaskRequest { Id = v03Params.Id };
                var v1Events = handler.SubscribeToTaskAsync(subscribeRequest, ct);
                return new V03JsonRpcStreamedResult(v1Events.Select(V03TypeConverter.ToV03Event), rpcRequest.Id);
            }

            default:
                return MakeErrorResult(rpcRequest.Id, V03.JsonRpcResponse.MethodNotFoundResponse);
        }
    }

    private static T DeserializeParams<T>(JsonElement element) where T : class
    {
        T? result;
        try
        {
            result = (T?)element.Deserialize(V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T)));
        }
        catch (JsonException ex)
        {
            throw new A2AException($"Invalid parameters: {ex.Message}", A2AErrorCode.InvalidParams);
        }

        return result ?? throw new A2AException($"Parameters could not be deserialized as {typeof(T).Name}", A2AErrorCode.InvalidParams);
    }

    private static V03JsonRpcResponseResult MakeSuccessResult<T>(V03.JsonRpcId id, T result, Type resultType)
    {
        var typeInfo = V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(resultType);
        var response = V03.JsonRpcResponse.CreateJsonRpcResponse(id, result, typeInfo);
        return new V03JsonRpcResponseResult(response);
    }

    private static V03JsonRpcResponseResult MakeErrorResult(V03.JsonRpcId id, Func<V03.JsonRpcId, string?, V03.JsonRpcResponse> factory)
        => new(factory(id, null));

    private static V03JsonRpcResponseResult MakeV03ErrorResult(V03.JsonRpcId id, A2AException ex)
        => new(V03.JsonRpcResponse.CreateJsonRpcErrorResponse(id,
            new V03.A2AException(ex.Message, (V03.A2AErrorCode)(int)ex.ErrorCode)));

    private static JsonRpcId ToV1Id(V03.JsonRpcId v03Id) =>
        v03Id.IsString ? new JsonRpcId(v03Id.AsString()) :
        v03Id.IsNumber ? new JsonRpcId(v03Id.AsNumber()!.Value) :
        new JsonRpcId((string?)null);
}
