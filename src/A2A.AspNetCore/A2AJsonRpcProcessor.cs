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
    /// <param name="agentCardProvider">The agent card provider instance for handling agent card queries.</param>
    /// <param name="request">Http request containing the JSON-RPC request body.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation if needed.</param>
    /// <returns>An HTTP result containing either a single JSON-RPC response or a streaming SSE response.</returns>
    internal static async Task<IResult> ProcessRequestAsync(ITaskManager taskManager, IAgentCardProvider agentCardProvider, HttpRequest request, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HandleA2ARequest", ActivityKind.Server);

        JsonRpcRequest? rpcRequest = null;

        try
        {
            rpcRequest = (JsonRpcRequest?)await JsonSerializer.DeserializeAsync(request.Body, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)), cancellationToken).ConfigureAwait(false);

            activity?.AddTag("request.id", rpcRequest!.Id.ToString());
            activity?.AddTag("request.method", rpcRequest!.Method);

            // Dispatch based on return type
            if (A2AMethods.IsStreamingMethod(rpcRequest!.Method))
            {
                return StreamResponse(taskManager, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params, cancellationToken);
            }

            return await SingleResponseAsync(taskManager, agentCardProvider, request, rpcRequest.Id, rpcRequest.Method, rpcRequest.Params, cancellationToken).ConfigureAwait(false);
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
            return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(errorId, ex.Message));
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
    /// <param name="agentCardProvider">The agent card provider instance for handling agent card queries.</param>
    /// <param name="request">The HTTP request for accessing authentication context.</param>
    /// <param name="requestId">The JSON-RPC request ID for response correlation.</param>
    /// <param name="method">The JSON-RPC method name to execute.</param>
    /// <param name="parameters">The JSON parameters for the method call.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A JSON-RPC response result containing the operation result or error.</returns>
    internal static async Task<JsonRpcResponseResult> SingleResponseAsync(ITaskManager taskManager, IAgentCardProvider agentCardProvider, HttpRequest request, JsonRpcId requestId, string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"SingleResponse/{method}", ActivityKind.Server);
        activity?.SetTag("request.id", requestId.ToString());
        activity?.SetTag("request.method", method);

        JsonRpcResponse? response = null;

        if (parameters == null && method != A2AMethods.AgentGetAuthenticatedExtendedCard)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
        }

        switch (method)
        {
            case A2AMethods.MessageSend:
                if (parameters == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var taskSendParams = DeserializeAndValidate<MessageSendParams>(parameters.Value);
                var a2aResponse = await taskManager.SendMessageAsync(taskSendParams, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, a2aResponse);
                break;
            case A2AMethods.TaskGet:
                if (parameters == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var taskIdParams = DeserializeAndValidate<TaskQueryParams>(parameters.Value);
                var getAgentTask = await taskManager.GetTaskAsync(taskIdParams, cancellationToken).ConfigureAwait(false);
                response = getAgentTask is null
                    ? JsonRpcResponse.TaskNotFoundResponse(requestId)
                    : JsonRpcResponse.CreateJsonRpcResponse(requestId, getAgentTask);
                break;
            case A2AMethods.TaskCancel:
                if (parameters == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var taskIdParamsCancel = DeserializeAndValidate<TaskIdParams>(parameters.Value);
                var cancelledTask = await taskManager.CancelTaskAsync(taskIdParamsCancel, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, cancelledTask);
                break;
            case A2AMethods.TaskPushNotificationConfigSet:
                if (parameters == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var taskPushNotificationConfig = DeserializeAndValidate<TaskPushNotificationConfig>(parameters.Value);
                var setConfig = await taskManager.SetPushNotificationAsync(taskPushNotificationConfig, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, setConfig);
                break;
            case A2AMethods.TaskPushNotificationConfigGet:
                if (parameters == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var notificationConfigParams = DeserializeAndValidate<GetTaskPushNotificationConfigParams>(parameters.Value);
                var getConfig = await taskManager.GetPushNotificationAsync(notificationConfigParams, cancellationToken).ConfigureAwait(false);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, getConfig);
                break;
            case A2AMethods.AgentGetAuthenticatedExtendedCard:
                try
                {
                    var agentUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
                    var authContext = A2AHttpProcessor.ExtractAuthenticationContext(request);
                    var agentCard = await A2AHttpProcessor.GetAuthenticatedAgentCardCoreAsync(agentCardProvider, agentUrl, authContext, cancellationToken).ConfigureAwait(false);
                    response = JsonRpcResponse.CreateJsonRpcResponse(requestId, agentCard);
                }
                catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.AuthenticationRequired)
                {
                    response = JsonRpcResponse.AuthenticationRequiredResponse(requestId, ex.Message);
                }
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
        catch (JsonException)
        {
            parms = null;
        }

        switch (parms)
        {
            case null:
                throw new A2AException("Invalid parameters", A2AErrorCode.InvalidParams);
            case MessageSendParams messageSendParams when messageSendParams.Message.Parts.Count == 0:
                throw new A2AException("Message parts cannot be empty", A2AErrorCode.InvalidParams);
            case TaskQueryParams taskQueryParams when taskQueryParams.HistoryLength < 0:
                throw new A2AException("History length cannot be negative", A2AErrorCode.InvalidParams);
            default:
                return parms;
        }
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
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An HTTP result that streams JSON-RPC responses as Server-Sent Events or an error response.</returns>
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
            case A2AMethods.TaskSubscribe:
                var taskIdParams = DeserializeAndValidate<TaskIdParams>(parameters.Value);
                var taskEvents = taskManager.SubscribeToTaskAsync(taskIdParams, cancellationToken);
                return new JsonRpcStreamedResult(taskEvents, requestId);
            case A2AMethods.MessageStream:
                var taskSendParams = DeserializeAndValidate<MessageSendParams>(parameters.Value);
                var sendEvents = taskManager.SendMessageStreamingAsync(taskSendParams, cancellationToken);
                return new JsonRpcStreamedResult(sendEvents, requestId);
            default:
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(requestId));
        }
    }
}