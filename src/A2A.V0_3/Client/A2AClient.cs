using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.V0_3;

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

        var response = await SendAndReadResponseStreamAsync(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "application/json",
            cancellationToken).ConfigureAwait(false);
        using var responseStream = response.Stream;

        var responseObject = await JsonSerializer.DeserializeAsync(responseStream, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse, cancellationToken).ConfigureAwait(false);

        if (responseObject is not null) ValidateResponseId(responseObject, response.RequestId);

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
        var response = await SendAndReadResponseStreamAsync(
            jsonRpcParams,
            method,
            inputTypeInfo,
            "text/event-stream",
            cancellationToken).ConfigureAwait(false);
        using var responseStream = response.Stream;

        var sseParser = SseParser.Create(responseStream, (_, data) =>
        {
            var reader = new Utf8JsonReader(data);

            var responseObject = JsonSerializer.Deserialize(ref reader, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse);

            if (responseObject is not null) ValidateResponseId(responseObject, response.RequestId);

            if (responseObject?.Error is { } error)
            {
                throw new A2AException(error.Message, (A2AErrorCode)error.Code);
            }

            if (responseObject?.Result is null)
            {
                throw new InvalidOperationException("Failed to deserialize the event: Result is null.");
            }

            return responseObject.Result.Deserialize(outputTypeInfo) ??
                throw new InvalidOperationException("Failed to deserialize the event.");
        });

        await foreach (var item in sseParser.EnumerateAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private static void ValidateResponseId(JsonRpcResponse response, JsonRpcId requestId)
    {
        if (!response.HasId || (response.Id != requestId && (response.Id.HasValue || response.Error is null)))
        {
            throw new A2AException("JSON-RPC response ID does not match the request ID.", A2AErrorCode.InternalError);
        }
    }

    private async ValueTask<(Stream Stream, JsonRpcId RequestId)> SendAndReadResponseStreamAsync<TInput>(
        TInput jsonRpcParams,
        string method,
        JsonTypeInfo<TInput> inputTypeInfo,
        string expectedContentType,
        CancellationToken cancellationToken)
    {
        JsonRpcId requestId = Guid.NewGuid().ToString();
        var response = await _httpClient.SendAsync(new(HttpMethod.Post, _baseUri)
        {
            Content = new JsonRpcContent(new JsonRpcRequest()
            {
                Id = requestId,
                Method = method,
                Params = JsonSerializer.SerializeToElement(jsonRpcParams, inputTypeInfo),
            })
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();

            if (expectedContentType == "text/event-stream" &&
                string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var rejection = await JsonSerializer.DeserializeAsync(errorStream, A2AJsonUtilities.JsonContext.Default.JsonRpcResponse, cancellationToken).ConfigureAwait(false);
                if (rejection is not null) ValidateResponseId(rejection, requestId);
                throw rejection?.Error is { } error
                    ? new A2AException(error.Message, (A2AErrorCode)error.Code)
                    : new A2AException("Expected a streaming JSON-RPC response.", A2AErrorCode.InternalError);
            }

            if (response.Content.Headers.ContentType?.MediaType != expectedContentType)
            {
                throw new InvalidOperationException($"Invalid content type. Expected '{expectedContentType}' but got '{response.Content.Headers.ContentType?.MediaType}'.");
            }

            return (await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), requestId);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}