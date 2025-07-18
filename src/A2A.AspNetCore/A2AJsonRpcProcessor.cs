using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A JSON-RPC requests in ASP.NET Core applications.
/// </summary>
/// <remarks>
/// Provides methods for processing JSON-RPC 2.0 protocol requests including message sending,
/// task operations, streaming responses, and push notification configuration.
/// </remarks>
public static class A2AJsonRpcProcessor
{
    /// <summary>
    /// OpenTelemetry ActivitySource for tracing JSON-RPC processor operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("A2A.Processor", "1.0.0");

    /// <summary>
    /// Processes an incoming JSON-RPC request and routes it to the appropriate handler.
    /// </summary>
    /// <remarks>
    /// Determines whether the request requires a single response or streaming response.
    /// based on the method name and dispatches accordingly.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling A2A operations.</param>
    /// <param name="request">Http request containing the JSON-RPC request body.</param>
    /// <returns>An HTTP result containing either a single JSON-RPC response or a streaming SSE response.</returns>
    internal static async Task<IResult> ProcessRequest(ITaskManager taskManager, HttpRequest request)
    {
        using var activity = ActivitySource.StartActivity("HandleA2ARequest", ActivityKind.Server);

        JsonRpcRequest? rpcRequest = null;

        try
        {
            rpcRequest = (JsonRpcRequest?)await JsonSerializer.DeserializeAsync(request.Body, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)));

            activity?.AddTag("request.id", rpcRequest!.Id);
            activity?.AddTag("request.method", rpcRequest!.Method);

            // Dispatch based on return type
            if (A2AMethods.IsStreamingMethod(rpcRequest!.Method))
            {
                return await StreamResponse(taskManager, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params);
            }

            return await SingleResponse(taskManager, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params);
        }
        catch (A2AException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new JsonRpcResponseResult(JsonRpcResponse.CreateJsonRpcErrorResponse(rpcRequest?.Id ?? ex.GetRequestId(), ex));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(rpcRequest?.Id, ex.Message));
        }
    }

    /// <summary>
    /// Processes JSON-RPC requests that require a single response (non-streaming).
    /// </summary>
    /// <remarks>
    /// Handles methods like message sending, task retrieval, task cancellation,
    /// and push notification configuration operations.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling A2A operations.</param>
    /// <param name="requestId">The JSON-RPC request ID for response correlation.</param>
    /// <param name="method">The JSON-RPC method name to execute.</param>
    /// <param name="parameters">The JSON parameters for the method call.</param>
    /// <returns>A JSON-RPC response result containing the operation result or error.</returns>
    internal static async Task<JsonRpcResponseResult> SingleResponse(ITaskManager taskManager, string? requestId, string method, JsonElement? parameters)
    {
        using var activity = ActivitySource.StartActivity($"SingleResponse/{method}", ActivityKind.Server);
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("request.method", method);

        JsonRpcResponse? response = null;

        if (parameters == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
        }

        switch (method)
        {
            case A2AMethods.MessageSend:
                var taskSendParams = DeserializeOrThrow<MessageSendParams>(parameters.Value);
                var a2aResponse = await taskManager.SendMessageAsync(taskSendParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, a2aResponse);
                break;
            case A2AMethods.TaskGet:
                var taskIdParams = DeserializeOrThrow<TaskQueryParams>(parameters.Value);
                var getAgentTask = await taskManager.GetTaskAsync(taskIdParams);
                response = getAgentTask is null
                    ? JsonRpcResponse.TaskNotFoundResponse(requestId)
                    : JsonRpcResponse.CreateJsonRpcResponse(requestId, getAgentTask);
                break;
            case A2AMethods.TaskCancel:
                var taskIdParamsCancel = DeserializeOrThrow<TaskIdParams>(parameters.Value);
                var cancelledTask = await taskManager.CancelTaskAsync(taskIdParamsCancel);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, cancelledTask);
                break;
            case A2AMethods.TaskPushNotificationConfigSet:
                var taskPushNotificationConfig = DeserializeOrThrow<TaskPushNotificationConfig>(parameters.Value);
                var setConfig = await taskManager.SetPushNotificationAsync(taskPushNotificationConfig);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, setConfig);
                break;
            case A2AMethods.TaskPushNotificationConfigGet:
                var notificationConfigParams = DeserializeOrThrow<GetTaskPushNotificationConfigParams>(parameters.Value);
                var getConfig = await taskManager.GetPushNotificationAsync(notificationConfigParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, getConfig);
                break;
            default:
                response = JsonRpcResponse.MethodNotFoundResponse(requestId);
                break;
        }

        return new JsonRpcResponseResult(response);
    }

    private static T DeserializeOrThrow<T>(JsonElement? jsonParamValue) where T : class
    {
        T? parms;
        try
        {
            parms = jsonParamValue?.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T))) as T;
        }
        catch (JsonException)
        {
            parms = null;
        }

        return parms ?? throw new A2AException("Invalid parameters", A2AErrorCode.InvalidParams);
    }

    /// <summary>
    /// Processes JSON-RPC requests that require streaming responses using Server-Sent Events.
    /// </summary>
    /// <remarks>
    /// Handles methods like task resubscription and streaming message sending that return
    /// continuous streams of events rather than single responses.
    /// </remarks>
    /// <param name="taskManager">The task manager instance for handling streaming A2A operations.</param>
    /// <param name="requestId">The JSON-RPC request ID for response correlation.</param>
    /// <param name="method">The JSON-RPC streaming method name to execute.</param>
    /// <param name="parameters">The JSON parameters for the streaming method call.</param>
    /// <returns>An HTTP result that streams JSON-RPC responses as Server-Sent Events or an error response.</returns>
    internal static async Task<IResult> StreamResponse(ITaskManager taskManager, string? requestId, string method, JsonElement? parameters)
    {
        using var activity = ActivitySource.StartActivity("StreamResponse", ActivityKind.Server);
        activity?.SetTag("request.id", requestId);

        if (parameters == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
        }

        switch (method)
        {
            case A2AMethods.TaskSubscribe:
                var taskIdParams = DeserializeOrThrow<TaskIdParams>(parameters.Value);
                var taskEvents = taskManager.SubscribeToTaskAsync(taskIdParams);
                return new JsonRpcStreamedResult(taskEvents, requestId);
            case A2AMethods.MessageStream:
                var taskSendParams = DeserializeOrThrow<MessageSendParams>(parameters.Value);
                var sendEvents = await taskManager.SendMessageStreamAsync(taskSendParams);
                return new JsonRpcStreamedResult(sendEvents, requestId);
            default:
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(requestId));
        }
    }
}