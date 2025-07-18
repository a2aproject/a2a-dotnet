using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    /// <param name="rpcRequestBody">The JSON-RPC request containing method, parameters, and request ID.</param>
    /// <returns>An HTTP result containing either a single JSON-RPC response or a streaming SSE response.</returns>
    internal static async Task<IResult> ProcessRequest(TaskManager taskManager, Stream rpcRequestBody)
    {
        using var activity = ActivitySource.StartActivity("HandleA2ARequest", ActivityKind.Server);

        JsonDocument rpcRequestJson;
        JsonRpcRequest? rpcRequest;
        try
        {
            rpcRequestJson = await JsonDocument.ParseAsync(rpcRequestBody);
            rpcRequest = (JsonRpcRequest)rpcRequestJson.Deserialize(jsonTypeInfo: A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)))!;
        }
        catch (JsonException idEx) when (idEx.Message.StartsWith("The JSON value could not be converted to System.String. Path: $.id", StringComparison.Ordinal))
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidJsonRpcResponse(string.Empty));
        }
        catch (JsonException)
        {
            return new JsonRpcResponseResult(JsonRpcResponse.ParseErrorResponse(string.Empty));
        }

        if (rpcRequestJson.RootElement.TryGetProperty("id", out var id))
        {
            if (id.ValueKind is not JsonValueKind.String)
            {
                return new JsonRpcResponseResult(JsonRpcResponse.ParseErrorResponse(string.Empty));
            }
        }

        if (rpcRequest.JsonRpc is not "2.0" || string.IsNullOrWhiteSpace(rpcRequest.Method))
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidJsonRpcResponse(rpcRequest.Id));
        }
        else if (!A2AMethods.IsValid(rpcRequest.Method))
        {
            return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(rpcRequest.Id));
        }
        else if (rpcRequest.Params is null or { ValueKind: not JsonValueKind.Object })
        {
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(rpcRequest.Id));
        }
        else if (string.IsNullOrWhiteSpace(rpcRequest.Id))
        {   // Why don't we put this up with the JsonRpc check?
            // Because the a2a-tck compliance test suite sends a payload that doesn't have request id OR method, and expects it to fail with the method error.
            return new JsonRpcResponseResult(JsonRpcResponse.InvalidJsonRpcResponse(rpcRequest.Id));
        }
        else
        {
            var response = new JsonRpcResponse
            {
                Id = rpcRequest.Id,
            };

            activity?.AddTag("request.id", rpcRequest.Id);
            activity?.AddTag("request.method", rpcRequest.Method);

            var parsedParameters = rpcRequest.Params;
            try
            {
                // Dispatch based on return type
                if (A2AMethods.IsStreamingMethod(rpcRequest.Method))
                {
                    return await StreamResponse(taskManager, rpcRequest.Id, rpcRequest.Method, parsedParameters);
                }

                return await SingleResponse(taskManager, rpcRequest.Id, rpcRequest.Method, parsedParameters);
            }
            catch (ArgumentNullException e)
            {
                if (e.ParamName is "requestId")
                {
                    response = JsonRpcResponse.InvalidJsonRpcResponse(rpcRequest.Id);
                }
                else
                {
                    response = JsonRpcResponse.InvalidParamsResponse(rpcRequest.Id);
                }
            }
            catch (Exception e)
            {
                response = JsonRpcResponse.InternalErrorResponse(rpcRequest.Id, e.Message);
                return new JsonRpcResponseResult(response, StatusCodes.Status500InternalServerError);
            }

            return new JsonRpcResponseResult(response);
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
    internal static async Task<JsonRpcResponseResult> SingleResponse(TaskManager taskManager, string requestId, string method, JsonElement? parameters)
    {
        using var activity = ActivitySource.StartActivity($"SingleResponse/{method}", ActivityKind.Server);
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("request.method", method);

        JsonRpcResponse? response = null;
        int statusCode = StatusCodes.Status200OK;

        if (parameters == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
            response = JsonRpcResponse.InvalidParamsResponse(requestId);
            return new JsonRpcResponseResult(response, statusCode);
        }

        // Local function to handle deserialization and error trapping
        T? TryDeserialize<T>(JsonElement element, JsonTypeInfo<T> typeInfo, string reqId, out JsonRpcResponse? errorResponse)
        {
            try
            {
                errorResponse = null;
                return element.Deserialize(typeInfo);
            }
            catch (JsonException)
            {
                errorResponse = JsonRpcResponse.InvalidParamsResponse(reqId);
                return default;
            }
            catch (InvalidOperationException)
            {
                errorResponse = JsonRpcResponse.InvalidParamsResponse(reqId);
                return default;
            }
        }

        try
        {
            switch (method)
            {
                case A2AMethods.MessageSend:
                    JsonRpcResponse? errResp;
                    var taskSendParams = TryDeserialize(parameters.Value, (JsonTypeInfo<MessageSendParams>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(MessageSendParams)), requestId, out errResp);
                    if (errResp != null || taskSendParams == null)
                        return new JsonRpcResponseResult(errResp ?? JsonRpcResponse.InvalidParamsResponse(requestId));
                    var a2aResponse = await taskManager.SendMessageAsync(taskSendParams);
                    response = JsonRpcResponse.CreateJsonRpcResponse(requestId, a2aResponse);
                    break;
                case A2AMethods.TaskGet:
                    var taskIdParams = TryDeserialize(parameters.Value, (JsonTypeInfo<TaskQueryParams>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskQueryParams)), requestId, out errResp);
                    if (errResp != null || taskIdParams == null)
                        return new JsonRpcResponseResult(errResp ?? JsonRpcResponse.InvalidParamsResponse(requestId));
                    var getAgentTask = await taskManager.GetTaskAsync(taskIdParams);
                    response = getAgentTask is null
                        ? JsonRpcResponse.TaskNotFoundResponse(requestId)
                        : JsonRpcResponse.CreateJsonRpcResponse(requestId, getAgentTask);
                    break;
                case A2AMethods.TaskCancel:
                    var taskIdParamsCancel = TryDeserialize(parameters.Value, (JsonTypeInfo<TaskIdParams>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskIdParams)), requestId, out errResp);
                    if (errResp != null || taskIdParamsCancel == null)
                        return new JsonRpcResponseResult(errResp ?? JsonRpcResponse.InvalidParamsResponse(requestId));
                    try
                    {
                        var cancelledTask = await taskManager.CancelTaskAsync(taskIdParamsCancel);
                        response = JsonRpcResponse.CreateJsonRpcResponse(requestId, cancelledTask);
                    }
                    catch (KeyNotFoundException)
                    {
                        response = JsonRpcResponse.TaskNotFoundResponse(requestId);
                    }
                    break;
                case A2AMethods.TaskPushNotificationConfigSet:
                    var taskPushNotificationConfig = TryDeserialize(parameters.Value, (JsonTypeInfo<TaskPushNotificationConfig>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskPushNotificationConfig))!, requestId, out errResp);
                    if (errResp != null || taskPushNotificationConfig == null)
                        return new JsonRpcResponseResult(errResp ?? JsonRpcResponse.InvalidParamsResponse(requestId));
                    var setConfig = await taskManager.SetPushNotificationAsync(taskPushNotificationConfig);
                    response = JsonRpcResponse.CreateJsonRpcResponse(requestId, setConfig);
                    break;
                case A2AMethods.TaskPushNotificationConfigGet:
                    var notificationConfigParams = TryDeserialize(parameters.Value, (JsonTypeInfo<GetTaskPushNotificationConfigParams>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(GetTaskPushNotificationConfigParams))!, requestId, out errResp);
                    if (errResp != null || notificationConfigParams == null)
                        return new JsonRpcResponseResult(errResp ?? JsonRpcResponse.InvalidParamsResponse(requestId));
                    var getConfig = await taskManager.GetPushNotificationAsync(notificationConfigParams);
                    response = JsonRpcResponse.CreateJsonRpcResponse(requestId, getConfig);
                    break;
                default:
                    response = JsonRpcResponse.MethodNotFoundResponse(requestId);
                    statusCode = StatusCodes.Status404NotFound;
                    return new JsonRpcResponseResult(response, statusCode);
            }
        }
        catch (ArgumentException argX)
        {
            response = JsonRpcResponse.InvalidParamsResponse(requestId);
            response.Error!.Message = argX.Message;
            return new JsonRpcResponseResult(response);
        }
        catch (Exception ex)
        {
            response = JsonRpcResponse.InternalErrorResponse(requestId, ex.Message);
            statusCode = StatusCodes.Status500InternalServerError;
            return new JsonRpcResponseResult(response, statusCode);
        }

        return new JsonRpcResponseResult(response, statusCode);
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
    internal static async Task<IResult> StreamResponse(TaskManager taskManager, string requestId, string method, JsonElement? parameters)
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
            case A2AMethods.TaskResubscribe:
                var taskIdParams = (TaskIdParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskIdParams)));
                if (taskIdParams == null)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
                    return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
                }

                var taskEvents = taskManager.ResubscribeAsync(taskIdParams);
                return new JsonRpcStreamedResult(taskEvents, requestId);
            case A2AMethods.MessageStream:
                try
                {
                    var taskSendParams = (MessageSendParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(MessageSendParams)));
                    if (taskSendParams == null)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, "Invalid parameters");
                        return new JsonRpcResponseResult(JsonRpcResponse.InvalidParamsResponse(requestId));
                    }

                    var sendEvents = await taskManager.SendMessageStreamAsync(taskSendParams);
                    return new JsonRpcStreamedResult(sendEvents, requestId);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(requestId, ex.Message), StatusCodes.Status500InternalServerError);
                }
            default:
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(requestId), StatusCodes.Status404NotFound);
        }
    }
}

/// <summary>
/// Result type for returning JSON-RPC responses as JSON in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide custom serialization of JSON-RPC response objects
/// with appropriate HTTP status codes based on success or error conditions.
/// </remarks>
public class JsonRpcResponseResult : IResult
{
    private readonly JsonRpcResponse jsonRpcResponse;
    private readonly int statusCode;

    /// <summary>
    /// Initializes a new instance of the JsonRpcResponseResult class.
    /// </summary>
    /// <param name="jsonRpcResponse">The JSON-RPC response object to serialize and return in the HTTP response.</param>
    /// <param name="statusCode">The HTTP status code to set for the response.</param>
    public JsonRpcResponseResult(JsonRpcResponse jsonRpcResponse, int statusCode = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(jsonRpcResponse);

        this.jsonRpcResponse = jsonRpcResponse;
        this.statusCode = statusCode;
    }

    /// <summary>
    /// Executes the result by serializing the JSON-RPC response as JSON to the HTTP response body.
    /// </summary>
    /// <remarks>
    /// Sets the appropriate content type and HTTP status code (200 for success, 400 for JSON-RPC errors).
    /// Uses the default A2A JSON serialization options for consistent formatting.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to write the response to.</param>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = statusCode;

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, jsonRpcResponse, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse)));
    }
}

/// <summary>
/// Result type for streaming JSON-RPC responses as Server-Sent Events (SSE) in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide real-time streaming of JSON-RPC responses for continuous
/// event streams like task updates, status changes, and artifact notifications.
/// </remarks>
public class JsonRpcStreamedResult : IResult
{
    private readonly IAsyncEnumerable<A2AEvent> _events;
    private readonly string requestId;

    /// <summary>
    /// Initializes a new instance of the JsonRpcStreamedResult class.
    /// </summary>
    /// <param name="events">The async enumerable stream of A2A events to send as Server-Sent Events.</param>
    /// <param name="requestId">The JSON-RPC request ID used for correlating responses with the original request.</param>
    public JsonRpcStreamedResult(IAsyncEnumerable<A2AEvent> events, string requestId)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrEmpty(requestId);

        _events = events;
        this.requestId = requestId;
    }

    /// <summary>
    /// Executes the result by streaming JSON-RPC responses as Server-Sent Events to the HTTP response.
    /// </summary>
    /// <remarks>
    /// Sets appropriate SSE headers, wraps each A2A event in a JSON-RPC response format,
    /// and streams them using the SSE protocol with proper formatting and encoding.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to stream the responses to.</param>
    /// <returns>A task representing the asynchronous streaming operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");

        var responseTypeInfo = A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse));
        await SseFormatter.WriteAsync(
            _events.Select(e => new SseItem<JsonRpcResponse>(JsonRpcResponse.CreateJsonRpcResponse(requestId, e))),
            httpContext.Response.Body,
            (item, writer) =>
            {
                using Utf8JsonWriter json = new(writer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                JsonSerializer.Serialize(json, item.Data, responseTypeInfo);
            },
            httpContext.RequestAborted);
    }
}