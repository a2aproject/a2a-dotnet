using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Implementation of A2A client for communicating with agents
/// </summary>
public class A2AClient : IA2AClient
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the A2AClient class
    /// </summary>
    /// <param name="client">The HTTP client to use for requests</param>
    public A2AClient(HttpClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams) =>
        RpcRequest(
            taskSendParams,
            A2AMethods.MessageSend,
            A2AJsonUtilities.JsonContext.Default.MessageSendParams,
            A2AJsonUtilities.JsonContext.Default.A2AResponse);

    /// <inheritdoc />
    public Task<AgentTask> GetTaskAsync(string taskId) =>
        RpcRequest(
            new() { Id = taskId },
            A2AMethods.TaskGet,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask);

    /// <inheritdoc />
    public Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams) =>
        RpcRequest(
            taskIdParams,
            A2AMethods.TaskCancel,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig) =>
        RpcRequest(
            pushNotificationConfig,
            "task/pushNotification/set",
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams) =>
        RpcRequest(
            taskIdParams,
            "task/pushNotification/get",
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig);

    /// <inheritdoc />
    public async IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams)
    {
        var request = new JsonRpcRequest()
        {
            Id = Guid.NewGuid().ToString(),
            Method = A2AMethods.MessageStream,
            Params = JsonSerializer.SerializeToElement(taskSendParams, A2AJsonUtilities.JsonContext.Default.MessageSendParams),
        };
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new JsonRpcContent(request)
        });
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        var sseParser = SseParser.Create(stream, (eventType, data) =>
        {
            var reader = new Utf8JsonReader(data);
            return JsonSerializer.Deserialize(ref reader, A2AJsonUtilities.JsonContext.Default.A2AEvent) ?? throw new InvalidOperationException("Failed to deserialize the event.");
        });
        await foreach (var item in sseParser.EnumerateAsync())
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId)
    {
        var request = new JsonRpcRequest()
        {
            Id = Guid.NewGuid().ToString(),
            Method = A2AMethods.TaskResubscribe,
            Params = JsonSerializer.SerializeToElement(new TaskIdParams() { Id = taskId }, A2AJsonUtilities.JsonContext.Default.TaskIdParams),
        };
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new JsonRpcContent(request)
        });
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        var sseParser = SseParser.Create(stream, (eventType, data) =>
        {
            var reader = new Utf8JsonReader(data);
            return JsonSerializer.Deserialize(ref reader, A2AJsonUtilities.JsonContext.Default.A2AEvent) ?? throw new InvalidOperationException("Failed to deserialize the event.");
        });
        await foreach (var item in sseParser.EnumerateAsync())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Executes a JSON-RPC request to the agent
    /// </summary>
    /// <typeparam name="TInput">The type of the input parameters</typeparam>
    /// <typeparam name="TOutput">The type of the expected output</typeparam>
    /// <param name="jsonRpcParams">The parameters to send</param>
    /// <param name="method">The JSON-RPC method name</param>
    /// <param name="inputTypeInfo">Type information for input serialization</param>
    /// <param name="outputTypeInfo">Type information for output deserialization</param>
    /// <returns>The deserialized response</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response is invalid</exception>
    private async Task<TOutput> RpcRequest<TInput, TOutput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        JsonTypeInfo<TOutput> outputTypeInfo) where TOutput : class
    {
        var request = new JsonRpcRequest()
        {
            Id = Guid.NewGuid().ToString(),
            Method = method,
            Params = JsonSerializer.SerializeToElement(jsonRpcParams, inputTypeInfo),
        };

        using var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new JsonRpcContent(request)
        });
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new InvalidOperationException("Invalid content type.");
        }

        using var responseStream = await response.Content.ReadAsStreamAsync();

        var responseObject = await JsonSerializer.DeserializeAsync(responseStream, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse) ??
            throw new InvalidOperationException("Failed to deserialize the response.");

        return responseObject.Result?.Deserialize(outputTypeInfo) ??
            throw new InvalidOperationException("Response does not contain a result.");
    }
}
