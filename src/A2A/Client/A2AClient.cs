using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Implementation of A2A client for communicating with agents.
/// </summary>
public sealed class A2AClient : IA2AClient
{
    internal static readonly HttpClient s_sharedClient = new();
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    /// <summary>
    /// Initializes a new instance of <see cref="A2AClient"/>.
    /// </summary>
    /// <param name="baseUrl">The base url of the agent's hosting service.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        if (baseUrl is null)
        {
            throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null.");
        }

        _baseUri = baseUrl;

        _httpClient = httpClient ?? s_sharedClient;
    }

    /// <inheritdoc />
    public Task<A2AResponse> SendMessageAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            taskSendParams ?? throw new ArgumentNullException(nameof(taskSendParams)),
            A2AMethods.MessageSend,
            A2AJsonUtilities.JsonContext.Default.MessageSendParams,
            A2AJsonUtilities.JsonContext.Default.A2AResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            new() { Id = string.IsNullOrEmpty(taskId) ? throw new ArgumentNullException(nameof(taskId)) : taskId },
            A2AMethods.TaskGet,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            taskIdParams ?? throw new ArgumentNullException(nameof(taskIdParams)),
            A2AMethods.TaskCancel,
            A2AJsonUtilities.JsonContext.Default.TaskIdParams,
            A2AJsonUtilities.JsonContext.Default.AgentTask,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            pushNotificationConfig ?? throw new ArgumentNullException(nameof(pushNotificationConfig)),
            A2AMethods.TaskPushNotificationConfigSet,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig> GetPushNotificationAsync(GetTaskPushNotificationConfigParams notificationConfigParams, CancellationToken cancellationToken = default) =>
        SendRpcRequestAsync(
            notificationConfigParams ?? throw new ArgumentNullException(nameof(notificationConfigParams)),
            A2AMethods.TaskPushNotificationConfigGet,
            A2AJsonUtilities.JsonContext.Default.GetTaskPushNotificationConfigParams,
            A2AJsonUtilities.JsonContext.Default.TaskPushNotificationConfig,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<SseItem<A2AEvent>> SendMessageStreamingAsync(MessageSendParams taskSendParams, CancellationToken cancellationToken = default) =>
        SendRpcSseRequestAsync(
            taskSendParams ?? throw new ArgumentNullException(nameof(taskSendParams)),
            A2AMethods.MessageStream,
            A2AJsonUtilities.JsonContext.Default.MessageSendParams,
            A2AJsonUtilities.JsonContext.Default.A2AEvent,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<SseItem<A2AEvent>> SubscribeToTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        SendRpcSseRequestAsync(
            new() { Id = string.IsNullOrEmpty(taskId) ? throw new ArgumentNullException(nameof(taskId)) : taskId },
            A2AMethods.TaskSubscribe,
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
        cancellationToken.ThrowIfCancellationRequested();

        using var responseStream = await SendAndReadResponseStreamAsync(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "application/json",
            cancellationToken).ConfigureAwait(false);

        var responseObject = await JsonSerializer.DeserializeAsync(responseStream, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse, cancellationToken).ConfigureAwait(false);

        if (responseObject?.Error is { } error)
        {
            throw new A2AException(error.Message, (A2AErrorCode)error.Code);
        }

        return responseObject?.Result?.Deserialize(outputTypeInfo) ??
            throw new InvalidOperationException("Response does not contain a result.");
    }

    private async IAsyncEnumerable<SseItem<TOutput>> SendRpcSseRequestAsync<TInput, TOutput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        JsonTypeInfo<TOutput> outputTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var responseStream = await SendAndReadResponseStreamAsync(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "text/event-stream",
            cancellationToken).ConfigureAwait(false);

        var sseParser = SseParser.Create(responseStream, (eventType, data) =>
        {
            var reader = new Utf8JsonReader(data);

            var responseObject = JsonSerializer.Deserialize(ref reader, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse);

            if (responseObject?.Error is { } error)
            {
                throw new A2AException(error.Message, (A2AErrorCode)error.Code);
            }

            return JsonSerializer.Deserialize(responseObject?.Result, outputTypeInfo) ??
                throw new InvalidOperationException("Failed to deserialize the event.");
        });

        await foreach (var item in sseParser.EnumerateAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private async ValueTask<Stream> SendAndReadResponseStreamAsync<TInput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        string expectedContentType,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(new(HttpMethod.Post, _baseUri)
        {
            Content = new JsonRpcContent(new JsonRpcRequest()
            {
                Id = Guid.NewGuid().ToString(),
                Method = method,
                Params = JsonSerializer.SerializeToElement(jsonRpcParams, inputTypeInfo),
            })
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

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
                ).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}