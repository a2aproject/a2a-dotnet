using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Text.Encodings.Web;
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
    internal static async Task<IResult> ProcessRequest(TaskManager taskManager, HttpRequest request)
    {
        using var activity = ActivitySource.StartActivity("HandleA2ARequest", ActivityKind.Server);

        JsonRpcRequest? rpcRequest = null;

        try
        {
            rpcRequest = await ReadAndValidateJsonRpcRequest(request);

            activity?.AddTag("request.id", rpcRequest.Id);
            activity?.AddTag("request.method", rpcRequest.Method);

            // Dispatch based on return type
            if (A2AMethods.IsStreamingMethod(rpcRequest.Method))
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
    internal static async Task<JsonRpcResponseResult> SingleResponse(TaskManager taskManager, string? requestId, string method, JsonElement? parameters)
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
                var taskSendParams = (MessageSendParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(MessageSendParams))); //TODO stop the double parsing
                if (taskSendParams == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var a2aResponse = await taskManager.SendMessageAsync(taskSendParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, a2aResponse);
                break;
            case A2AMethods.TaskGet:
                var taskIdParams = (TaskQueryParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskQueryParams)));
                if (taskIdParams == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var getAgentTask = await taskManager.GetTaskAsync(taskIdParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, getAgentTask);
                break;
            case A2AMethods.TaskCancel:
                var taskIdParamsCancel = (TaskIdParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskIdParams)));
                if (taskIdParamsCancel == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var cancelledTask = await taskManager.CancelTaskAsync(taskIdParamsCancel);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, cancelledTask);
                break;
            case A2AMethods.TaskPushNotificationConfigSet:
                var taskPushNotificationConfig = (TaskPushNotificationConfig?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskPushNotificationConfig))!);
                if (taskPushNotificationConfig == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var setConfig = await taskManager.SetPushNotificationAsync(taskPushNotificationConfig);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, setConfig);
                break;
            case A2AMethods.TaskPushNotificationConfigGet:
                var notificationConfigParams = (GetTaskPushNotificationConfigParams?)parameters.Value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(GetTaskPushNotificationConfigParams))!);
                if (notificationConfigParams == null)
                {
                    response = JsonRpcResponse.InvalidParamsResponse(requestId);
                    break;
                }
                var getConfig = await taskManager.GetPushNotificationAsync(notificationConfigParams);
                response = JsonRpcResponse.CreateJsonRpcResponse(requestId, getConfig);
                break;
            default:
                response = JsonRpcResponse.MethodNotFoundResponse(requestId);
                break;
        }

        return new JsonRpcResponseResult(response);
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
    internal static async Task<IResult> StreamResponse(TaskManager taskManager, string? requestId, string method, JsonElement? parameters)
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
                    return new JsonRpcResponseResult(JsonRpcResponse.InternalErrorResponse(requestId, ex.Message));
                }
            default:
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
                return new JsonRpcResponseResult(JsonRpcResponse.MethodNotFoundResponse(requestId));
        }
    }

    /// <summary>
    /// Reads a JSON-RPC request from the HTTP request body and validates it.
    /// </summary>
    /// <remarks>
    /// This method parses the JSON request body and validates all JSON-RPC 2.0 protocol fields
    /// including 'jsonrpc', 'method', 'id', and 'params' fields according to the specification.
    /// </remarks>
    /// <param name="request">The HTTP request containing the JSON-RPC request body.</param>
    /// <returns>A validated and deserialized JsonRpcRequest object.</returns>
    private static async Task<JsonRpcRequest> ReadAndValidateJsonRpcRequest(HttpRequest request)
    {
        JsonDocument? jsonDoc = null;
        string? requestId = null;

        try
        {
            // Parse the JSON document first to validate structure
            jsonDoc = await JsonDocument.ParseAsync(request.Body);

            JsonElement rootElement = jsonDoc.RootElement;

            // Validate the JSON-RPC request structure
            requestId = ValidateIdField(rootElement);

            ValidateJsonRpcField(rootElement, requestId);

            ValidateMethodField(rootElement, requestId);

            ValidateParamsField(rootElement, requestId);

            // Deserialize the JSON-RPC request
            var rpcRequest = (JsonRpcRequest?)JsonSerializer.Deserialize(rootElement, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)));
            if (rpcRequest == null)
            {
                throw new A2AException("No JSON-RPC request found in the body.", A2AErrorCode.InvalidRequest)
                    .WithRequestId(requestId);
            }

            return rpcRequest;
        }
        catch (JsonException ex)
        {
            throw new A2AException("Invalid JSON-RPC request payload.", ex, A2AErrorCode.ParseError)
                .WithRequestId(requestId);
        }
        finally
        {
            jsonDoc?.Dispose();
        }
    }

    /// <summary>
    /// Validates the 'id' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <returns>The extracted request ID as a string, or null if not present or null.</returns>
    /// <exception cref="A2AException">Thrown when the 'id' field has an invalid type.</exception>
    private static string? ValidateIdField(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty("id", out var idElement))
        {
            if (idElement.ValueKind != JsonValueKind.String &&
                idElement.ValueKind != JsonValueKind.Number &&
                idElement.ValueKind != JsonValueKind.Null)
            {
                throw new A2AException("Invalid JSON-RPC request: 'id' field must be a string, number, or null.", A2AErrorCode.InvalidRequest);
            }

            // TODO: Handle is as number rather than converting to string
            return idElement.ValueKind == JsonValueKind.Null ? null : idElement.ToString();
        }

        return null;
    }

    /// <summary>
    /// Validates the 'jsonrpc' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <exception cref="A2AException">Thrown when the 'jsonrpc' field is missing or invalid.</exception>
    private static void ValidateJsonRpcField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("jsonrpc", out var jsonRpcElement))
        {
            if (jsonRpcElement.GetString() != "2.0")
            {
                throw new A2AException("Invalid JSON-RPC request: 'jsonrpc' field must be '2.0'.", A2AErrorCode.InvalidRequest)
                    .WithRequestId(requestId);
            }
        }
        else
        {
            throw new A2AException("Invalid JSON-RPC request: missing 'jsonrpc' field.", A2AErrorCode.InvalidRequest)
                .WithRequestId(requestId);
        }
    }

    /// <summary>
    /// Validates the 'method' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <exception cref="A2AException">Thrown when the 'method' field is missing or invalid.</exception>
    private static void ValidateMethodField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("method", out var methodElement))
        {
            var method = methodElement.GetString();
            if (string.IsNullOrEmpty(method))
            {
                throw new A2AException("Invalid JSON-RPC request: missing 'method' field.", A2AErrorCode.InvalidRequest)
                    .WithRequestId(requestId);
            }

            if (!A2AMethods.IsValidMethod(method))
            {
                throw new A2AException("Invalid JSON-RPC request: 'method' field is not a valid A2A method.", A2AErrorCode.MethodNotFound)
                    .WithRequestId(requestId);
            }
        }
        else
        {
            throw new A2AException("Invalid JSON-RPC request: missing 'method' field.", A2AErrorCode.InvalidRequest)
                .WithRequestId(requestId);
        }
    }

    /// <summary>
    /// Validates the 'params' field of a JSON-RPC request.
    /// </summary>
    /// <param name="rootElement">The root JSON element containing the request.</param>
    /// <param name="requestId">The request ID for error context.</param>
    /// <exception cref="A2AException">Thrown when the 'params' field has an invalid type.</exception>
    private static void ValidateParamsField(JsonElement rootElement, string? requestId)
    {
        if (rootElement.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind != JsonValueKind.Object)
        {
            throw new A2AException("Invalid JSON-RPC request: 'params' field must be an object.", A2AErrorCode.InvalidParams)
                .WithRequestId(requestId);
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

    /// <summary>
    /// Initializes a new instance of the JsonRpcResponseResult class.
    /// </summary>
    /// <param name="jsonRpcResponse">The JSON-RPC response object to serialize and return in the HTTP response.</param>
    public JsonRpcResponseResult(JsonRpcResponse jsonRpcResponse)
    {
        ArgumentNullException.ThrowIfNull(jsonRpcResponse);

        this.jsonRpcResponse = jsonRpcResponse;
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
        httpContext.Response.StatusCode = StatusCodes.Status200OK;

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
    private readonly string? requestId;

    /// <summary>
    /// Initializes a new instance of the JsonRpcStreamedResult class.
    /// </summary>
    /// <param name="events">The async enumerable stream of A2A events to send as Server-Sent Events.</param>
    /// <param name="requestId">The JSON-RPC request ID used for correlating responses with the original request.</param>
    public JsonRpcStreamedResult(IAsyncEnumerable<A2AEvent> events, string? requestId)
    {
        ArgumentNullException.ThrowIfNull(events);

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