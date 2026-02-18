using Microsoft.AspNetCore.Http;

using System.Diagnostics;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A JSON-RPC requests in ASP.NET Core applications.
/// </summary>
public static class A2AJsonRpcProcessor
{
    /// <summary>Activity source for tracing A2A JSON-RPC operations.</summary>
    public static readonly ActivitySource ActivitySource = new("A2A.Processor", "1.0.0");

    internal static async Task<IResult> ProcessRequestAsync(ITaskManager taskManager, HttpRequest request, CancellationToken cancellationToken)
    {
        // Version negotiation: check A2A-Version header
        var version = request.Headers["A2A-Version"].FirstOrDefault();
        if (!string.IsNullOrEmpty(version) && version != "1.0" && version != "0.3")
        {
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(
                new JsonRpcId((string?)null),
                new A2AException(
                    $"Protocol version '{version}' is not supported. Supported versions: 0.3, 1.0",
                    A2AErrorCode.VersionNotSupported)));
        }

        using var activity = ActivitySource.StartActivity("HandleA2ARequest", ActivityKind.Server);

        JsonRpcRequest? rpcRequest = null;

        try
        {
            rpcRequest = (JsonRpcRequest?)await JsonSerializer.DeserializeAsync(request.Body, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)), cancellationToken).ConfigureAwait(false);

            activity?.AddTag("request.id", rpcRequest!.Id.ToString());
            activity?.AddTag("request.method", rpcRequest!.Method);

            if (A2AMethods.IsStreamingMethod(rpcRequest!.Method))
            {
                return StreamResponse(taskManager, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params, cancellationToken);
            }

            return await SingleResponseAsync(taskManager, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params, cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var errorId = rpcRequest?.Id ?? new JsonRpcId(ex.GetRequestId());
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(errorId, ex));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var errorId = rpcRequest?.Id ?? new JsonRpcId((string?)null);
            return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(errorId, "An internal error occurred."));
        }
    }

    internal static async Task<JsonRpcResponseResult> SingleResponseAsync(ITaskManager taskManager, JsonRpcId requestId, string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"SingleResponse/{method}", ActivityKind.Server);
        activity?.SetTag("request.id", requestId.ToString());
        activity?.SetTag("request.method", method);

        JsonRpcResponse? response = null;

        if (parameters == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
        }

        // For push notification methods, check if push notifications are supported
        // BEFORE deserializing params. DeserializeAndValidate would throw InvalidParams
        // for malformed requests, masking the PushNotificationNotSupported error.
        if (A2AMethods.IsPushNotificationMethod(method))
        {
            try
            {
                await taskManager.GetTaskPushNotificationConfigAsync(null!, cancellationToken).ConfigureAwait(false);
            }
            catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.PushNotificationNotSupported)
            {
                throw;
            }
            catch
            {
                // Any other exception means push notifications are supported;
                // continue with normal deserialization and handling.
            }
        }

        switch (method)
        {
            case A2AMethods.SendMessage:
                var sendRequest = DeserializeAndValidate<SendMessageRequest>(parameters.Value);
                var sendResult = await taskManager.SendMessageAsync(sendRequest, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, sendResult);
                break;
            case A2AMethods.GetTask:
                var getTaskRequest = DeserializeAndValidate<GetTaskRequest>(parameters.Value);
                var agentTask = await taskManager.GetTaskAsync(getTaskRequest, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, agentTask);
                break;
            case A2AMethods.ListTasks:
                var listTasksRequest = DeserializeAndValidate<ListTasksRequest>(parameters.Value);

                // Validate pageSize: must be 1-100 if specified
                if (listTasksRequest.PageSize is { } ps && (ps <= 0 || ps > 100))
                {
                    throw new A2AException(
                        $"Invalid pageSize: {ps}. Must be between 1 and 100.",
                        A2AErrorCode.InvalidParams);
                }

                // Validate historyLength: must be >= 0 if specified
                if (listTasksRequest.HistoryLength is { } hl && hl < 0)
                {
                    throw new A2AException(
                        $"Invalid historyLength: {hl}. Must be non-negative.",
                        A2AErrorCode.InvalidParams);
                }

                var listResult = await taskManager.ListTasksAsync(listTasksRequest, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, listResult);
                break;
            case A2AMethods.CancelTask:
                var cancelRequest = DeserializeAndValidate<CancelTaskRequest>(parameters.Value);
                var cancelledTask = await taskManager.CancelTaskAsync(cancelRequest, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, cancelledTask);
                break;
            case A2AMethods.CreateTaskPushNotificationConfig:
                var createPnConfig = DeserializeAndValidate<CreateTaskPushNotificationConfigRequest>(parameters.Value);
                var createdConfig = await taskManager.CreateTaskPushNotificationConfigAsync(createPnConfig, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, createdConfig);
                break;
            case A2AMethods.GetTaskPushNotificationConfig:
                var getPnConfig = DeserializeAndValidate<GetTaskPushNotificationConfigRequest>(parameters.Value);
                var gotConfig = await taskManager.GetTaskPushNotificationConfigAsync(getPnConfig, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, gotConfig);
                break;
            case A2AMethods.ListTaskPushNotificationConfig:
                var listPnConfig = DeserializeAndValidate<ListTaskPushNotificationConfigRequest>(parameters.Value);
                var listPnResult = await taskManager.ListTaskPushNotificationConfigAsync(listPnConfig, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, listPnResult);
                break;
            case A2AMethods.DeleteTaskPushNotificationConfig:
                var deletePnConfig = DeserializeAndValidate<DeleteTaskPushNotificationConfigRequest>(parameters.Value);
                await taskManager.DeleteTaskPushNotificationConfigAsync(deletePnConfig, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, (object?)null);
                break;
            case A2AMethods.GetExtendedAgentCard:
                var getCardRequest = DeserializeAndValidate<GetExtendedAgentCardRequest>(parameters.Value);
                var extCard = await taskManager.GetExtendedAgentCardAsync(getCardRequest, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, extCard);
                break;
            default:
                response = JsonRpcResponse.MethodNotFoundResponse(requestId);
                break;
        }

        return new JsonRpcResponseResult(response);
    }

    private static T DeserializeAndValidate<T>(JsonElement jsonParamValue) where T : class
    {
        T? parms;
        try
        {
            parms = jsonParamValue.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T))) as T;
        }
        catch (JsonException ex)
        {
            throw new A2AException($"Invalid parameters: request body could not be deserialized as {typeof(T).Name}.", ex, A2AErrorCode.InvalidParams);
        }

        if (parms is null)
        {
            throw new A2AException($"Failed to deserialize parameters as {typeof(T).Name}", A2AErrorCode.InvalidParams);
        }

        if (parms is SendMessageRequest sendMsgRequest && sendMsgRequest.Message.Parts.Count == 0)
        {
            throw new A2AException("Message parts cannot be empty", A2AErrorCode.InvalidParams);
        }

        return parms;
    }

    internal static IResult StreamResponse(ITaskManager taskManager, JsonRpcId requestId, string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("StreamResponse", ActivityKind.Server);
        activity?.SetTag("request.id", requestId.ToString());

        if (parameters == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
        }

        switch (method)
        {
            case A2AMethods.SubscribeToTask:
                var subscribeRequest = DeserializeAndValidate<SubscribeToTaskRequest>(parameters.Value);
                var taskEvents = taskManager.SubscribeToTaskAsync(subscribeRequest, cancellationToken);
                return new JsonRpcStreamedResult(taskEvents, requestId);
            case A2AMethods.SendStreamingMessage:
                var sendRequest = DeserializeAndValidate<SendMessageRequest>(parameters.Value);
                var sendEvents = taskManager.SendStreamingMessageAsync(sendRequest, cancellationToken);
                return new JsonRpcStreamedResult(sendEvents, requestId);
            default:
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(requestId));
        }
    }
}