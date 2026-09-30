namespace A2A;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

/// <summary>Client for communicating with an A2A agent via JSON-RPC over HTTP.</summary>
public sealed class A2AClient : IA2AClient, IDisposable
{
    internal static readonly HttpClient s_sharedClient = new();
    private readonly HttpClient _httpClient;
    private readonly A2AClientOperationBindings _operationBindings;
    private readonly A2AStandardOperations _standardOperations;
    private readonly string _url;

    /// <summary>Initializes a new instance of the <see cref="A2AClient"/> class.</summary>
    /// <param name="baseUrl">The JSON-RPC endpoint URL.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        _url = baseUrl.ToString();
        _operationBindings = A2AStandardClientBindings.Default.Bindings;
        _standardOperations = A2AStandardClientBindings.Default.Operations;
        _httpClient = httpClient ?? s_sharedClient;
    }

    /// <summary>Initializes a client with custom operation bindings.</summary>
    /// <param name="baseUrl">The JSON-RPC endpoint URL.</param>
    /// <param name="operationBindings">The client operation bindings.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AClient(
        Uri baseUrl,
        A2AClientOperationBindings operationBindings,
        HttpClient httpClient)
    {
        if (baseUrl is null)
        {
            throw new ArgumentNullException(
                nameof(baseUrl),
                "Base URL cannot be null.");
        }
        ArgumentNullException.ThrowIfNull(operationBindings);
        ArgumentNullException.ThrowIfNull(httpClient);

        _url = baseUrl.ToString();
        _operationBindings =
            A2AStandardClientBindings.IncludeDefaults(operationBindings);
        _standardOperations = _operationBindings.StandardOperations
            ?? throw new InvalidOperationException(
                "Standard A2A client operations are not configured.");
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<TResult> InvokeAsync<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = _operationBindings.GetJsonRpc(operation);
        binding.Validate(request);
        var parameters = JsonSerializer.SerializeToElement(
            request,
            binding.RequestTypeInfo);
        var response = await SendJsonRpcRequestCoreAsync(
            binding.Method,
            parameters,
            binding.RequestCustomizer is null
                ? null
                : message => binding.RequestCustomizer(message, request),
            error => binding.TryCreateOperationException(
                error,
                out var operationException)
                    ? operationException
                    : null,
            cancellationToken).ConfigureAwait(false);

        return DeserializeResult(response, binding.ResponseTypeInfo);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEvent> InvokeStreamingAsync<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var binding = _operationBindings.GetJsonRpcStreaming(operation);
        binding.Validate(request);
        var parameters = JsonSerializer.SerializeToElement(
            request,
            binding.RequestTypeInfo);

        await foreach (var item in SendStreamingJsonRpcRequestAsync(
            binding.Method,
            parameters,
            binding.RequestCustomizer is null
                ? null
                : message => binding.RequestCustomizer(message, request),
            binding.ResponseTypeInfo,
            error => binding.TryCreateOperationException(
                error,
                out var operationException)
                    ? operationException
                    : null,
            cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public Task<SendMessageResponse> SendMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.SendMessage,
            request,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeStreamingAsync(
            _standardOperations.SendStreamingMessage,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> GetTaskAsync(
        GetTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(
        ListTasksRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.ListTasks,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> CancelTaskAsync(
        CancelTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.CancelTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
        SubscribeToTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeStreamingAsync(
            _standardOperations.SubscribeToTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig>
        CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.CreateTaskPushNotificationConfig,
            config,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig>
        GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetTaskPushNotificationConfig,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<ListTaskPushNotificationConfigsResponse>
        ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.ListTaskPushNotificationConfigs,
            request,
            cancellationToken);

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(
        DeleteTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await InvokeAsync(
            _standardOperations.DeleteTaskPushNotificationConfig,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AgentCard> GetExtendedAgentCardAsync(
        GetExtendedAgentCardRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetExtendedAgentCard,
            request,
            cancellationToken);

    /// <inheritdoc />
    /// <summary>No-op. The HttpClient is either shared or externally owned.</summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private async Task<JsonRpcResponse> SendJsonRpcRequestCoreAsync(
        string method,
        JsonElement? parameters,
        Action<HttpRequestMessage>? requestCustomizer,
        Func<JsonRpcError, A2AOperationException?> mapOperationError,
        CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity(
            $"A2AClient/{method}",
            ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var rpcRequest = new JsonRpcRequest
        {
            Method = method,
            Id = new JsonRpcId(Guid.NewGuid().ToString()),
            Params = parameters,
        };

        activity?.SetTag("rpc.system", "jsonrpc");
        activity?.SetTag("rpc.method", method);
        activity?.SetTag("url.full", _url);
        activity?.SetTag(
            "rpc.jsonrpc.request_id",
            rpcRequest.Id.ToString());

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            using var content = new JsonRpcContent(rpcRequest);
            using var requestMessage = new HttpRequestMessage(
                HttpMethod.Post,
                _url)
            {
                Content = content,
            };
            requestMessage.Headers.TryAddWithoutValidation(
                "A2A-Version",
                "1.0");
            requestCustomizer?.Invoke(requestMessage);
            using var response = await _httpClient.SendAsync(
                requestMessage,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            var rpcResponse = (JsonRpcResponse?)await JsonSerializer
                .DeserializeAsync(
                    stream,
                    A2AJsonUtilities.DefaultOptions.GetTypeInfo(
                        typeof(JsonRpcResponse)),
                    cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException(
                    "Failed to deserialize JSON-RPC response.",
                    A2AErrorCode.InternalError);

            if (rpcResponse.Error is { } error)
            {
                var operationException = mapOperationError(error);
                if (operationException is not null)
                {
                    throw operationException;
                }

                throw new A2AException(
                    error.Message,
                    (A2AErrorCode)error.Code);
            }

            return rpcResponse;
        }
        catch (Exception exception)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(
                ActivityStatusCode.Error,
                exception.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async IAsyncEnumerable<TEvent> SendStreamingJsonRpcRequestAsync<
        TEvent>(
        string method,
        JsonElement? parameters,
        Action<HttpRequestMessage>? requestCustomizer,
        JsonTypeInfo<TEvent> eventTypeInfo,
        Func<JsonRpcError, A2AOperationException?> mapOperationError,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity(
            $"A2AClient/{method}",
            ActivityKind.Client);
        A2ADiagnostics.ClientRequestCount.Add(1);
        var eventCount = 0;

        var rpcRequest = new JsonRpcRequest
        {
            Method = method,
            Id = new JsonRpcId(Guid.NewGuid().ToString()),
            Params = parameters,
        };

        activity?.SetTag("rpc.system", "jsonrpc");
        activity?.SetTag("rpc.method", method);
        activity?.SetTag("url.full", _url);
        activity?.SetTag(
            "rpc.jsonrpc.request_id",
            rpcRequest.Id.ToString());

        HttpResponseMessage? response = null;
        Stream? stream = null;

        try
        {
            using var content = new JsonRpcContent(rpcRequest);
            using var requestMessage = new HttpRequestMessage(
                HttpMethod.Post,
                _url)
            {
                Content = content,
            };
            requestMessage.Headers.TryAddWithoutValidation(
                "A2A-Version",
                "1.0");
            requestMessage.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/event-stream"));
            requestCustomizer?.Invoke(requestMessage);

            response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(
                ActivityStatusCode.Error,
                exception.Message);
            response?.Dispose();
            throw;
        }

        using (response)
        using (stream)
        {
            await foreach (var sseItem in SseParser.Create(stream)
                .EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                var rpcResponse = JsonSerializer.Deserialize(
                    sseItem.Data,
                    (JsonTypeInfo<JsonRpcResponse>)A2AJsonUtilities
                        .DefaultOptions
                        .GetTypeInfo(typeof(JsonRpcResponse)))
                    ?? throw new A2AException(
                        "Failed to deserialize streaming JSON-RPC response.",
                        A2AErrorCode.InternalError);

                if (rpcResponse.Error is { } error)
                {
                    var operationException = mapOperationError(error);
                    if (operationException is not null)
                    {
                        throw operationException;
                    }

                    throw new A2AException(
                        error.Message,
                        (A2AErrorCode)error.Code);
                }

                var result = rpcResponse.Result.Deserialize(eventTypeInfo)
                    ?? throw new A2AException(
                        "Failed to deserialize streaming JSON-RPC result.",
                        A2AErrorCode.InternalError);

                eventCount++;
                yield return result;
            }
        }

        A2ADiagnostics.ClientStreamEventCount.Record(eventCount);
    }

    private static TResult DeserializeResult<TResult>(
        JsonRpcResponse response,
        JsonTypeInfo<TResult> resultTypeInfo)
    {
        if (response.Result is null
            && typeof(TResult) == typeof(A2AEmptyResult))
        {
            return (TResult)(object)A2AEmptyResult.Instance;
        }

        return response.Result.Deserialize(resultTypeInfo)
            ?? throw new A2AException(
                "Failed to deserialize JSON-RPC result.",
                A2AErrorCode.InternalError);
    }
}
