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

        // Parse body once to peek at "method" and route v0.3 vs v1.0 before full validation.
        // V03.JsonRpcRequestConverter rejects v1.0 method names, so we must detect them early.
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
                return MakeErrorResult(default, V03.JsonRpcResponse.ParseErrorResponse);
            }

            var method = methodProp.GetString() ?? string.Empty;

            // v1.0 method names: bypass V03 validator, delegate directly to v1.0 processor.
            if (!V03.A2AMethods.IsValidMethod(method))
            {
                return await HandleV1RequestAsync(requestHandler, root, method, cancellationToken)
                    .ConfigureAwait(false);
            }

            // v0.3 method names: deserialize with full V03 validation.
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
    // Extracts id and params from the already-parsed JsonElement and delegates to the v1.0 processor.
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
                return A2AJsonRpcProcessor.StreamResponse(handler, id, method, paramsEl, ct);
            }
            return await A2AJsonRpcProcessor.SingleResponseAsync(handler, id, method, paramsEl, ct)
                .ConfigureAwait(false);
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
            {
                // Unrecognized v0.3 method — delegate to v1.0 processor.
                // This handles v1.0 clients sending v1.0 method names (e.g. "SendMessage").
                var v1Id = ToV1Id(rpcRequest.Id);
                if (A2AMethods.IsStreamingMethod(rpcRequest.Method))
                {
                    return A2AJsonRpcProcessor.StreamResponse(handler, v1Id, rpcRequest.Method, rpcRequest.Params, ct);
                }
                return await A2AJsonRpcProcessor.SingleResponseAsync(handler, v1Id, rpcRequest.Method, rpcRequest.Params, ct).ConfigureAwait(false);
            }
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
