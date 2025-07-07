using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Implementation of A2A client for communicating with agents
/// </summary>
public sealed class A2AClient : IA2AClient
{
    private static readonly HttpClient s_sharedClient = new();

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the A2AClient class
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests</param>
    public A2AClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? s_sharedClient;
    }

    /// <inheritdoc />
    public Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            taskSendParams,
            A2AMethods.MessageSend,
            A2AJsonUtilities.JsonContext.Default.MessageSendParams,
            A2AJsonUtilities.JsonContext.Default.A2AResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            new() { Id = taskId },
            A2AMethods.TaskGet,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            taskIdParams,
            A2AMethods.TaskCancel,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            pushNotificationConfig,
            "task/pushNotification/set",
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> GetPushNotificationAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            taskIdParams,
            "task/pushNotification/get",
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default) =>
        SendRpcSseRequestAsync(
            taskSendParams,
            A2AMethods.MessageStream,
            A2AJsonUtilities.JsonContext.Default.MessageSendParams,
            A2AJsonUtilities.JsonContext.Default.A2AEvent,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<SseItem<A2AEvent>> ResubscribeToTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        SendRpcSseRequestAsync(
            new() { Id = taskId },
            A2AMethods.TaskResubscribe,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.A2AEvent,
            cancellationToken);

    private async Task<TOutput> SendRpcRequestAsync<TInput, TOutput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        JsonTypeInfo<TOutput> outputTypeInfo,
        CancellationToken cancellationToken) where TOutput : class
    {
        using var responseStream = await SendAndReadResponseStream(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "application/json",
            cancellationToken).ConfigureAwait(false);

        var responseObject = await JsonSerializer.DeserializeAsync(responseStream, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse, cancellationToken) ??
            throw new InvalidOperationException("Failed to deserialize the response.");

        return responseObject.Result?.Deserialize(outputTypeInfo) ??
            throw new InvalidOperationException("Response does not contain a result.");
    }

    private async IAsyncEnumerable<SseItem<TOutput>> SendRpcSseRequestAsync<TInput, TOutput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        JsonTypeInfo<TOutput> outputTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var responseStream = await SendAndReadResponseStream(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "text/event-stream",
            cancellationToken).ConfigureAwait(false);

        var sseParser = SseParser.Create(responseStream, (eventType, data) =>
        {
            var reader = new Utf8JsonReader(data);
            return JsonSerializer.Deserialize(ref reader, outputTypeInfo) ?? throw new InvalidOperationException("Failed to deserialize the event.");
        });

        await foreach (var item in sseParser.EnumerateAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private async ValueTask<Stream> SendAndReadResponseStream<TInput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        string expectedContentType,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(new(HttpMethod.Post, "")
        {
            Content = new JsonRpcContent(new JsonRpcRequest()
            {
                Id = Guid.NewGuid().ToString(),
                Method = method,
                Params = JsonSerializer.SerializeToElement(jsonRpcParams, inputTypeInfo),
            })
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentType?.MediaType != expectedContentType)
            {
                throw new InvalidOperationException($"Invalid content type. Expected '{expectedContentType}' but got '{response.Content.Headers.ContentType?.MediaType}'.");
            }

            return await response.Content.ReadAsStreamAsync(
#if NET
                cancellationToken
#endif
                );
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}