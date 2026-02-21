namespace A2A;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

/// <summary>Client for communicating with an A2A agent via JSON-RPC over HTTP.</summary>
public sealed class A2AClient : IA2AClient, IDisposable
{
    internal static readonly HttpClient s_sharedClient = new();
    private readonly HttpClient _httpClient;
    private readonly string _url;

    /// <summary>Initializes a new instance of the <see cref="A2AClient"/> class.</summary>
    /// <param name="baseUrl">The base url of the agent's hosting service.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        if (baseUrl is null)
        {
            throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null.");
        }

        _url = baseUrl.ToString();
        _httpClient = httpClient ?? s_sharedClient;
    }

    /// <inheritdoc />
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var rpcResponse = await SendJsonRpcRequestAsync<SendMessageResponse>(A2AMethods.SendMessage, request, cancellationToken).ConfigureAwait(false);
        return rpcResponse;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in SendStreamingJsonRpcRequestAsync<StreamResponse>(A2AMethods.SendStreamingMessage, request, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<AgentTask>(A2AMethods.GetTask, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<ListTasksResponse>(A2AMethods.ListTasks, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<AgentTask>(A2AMethods.CancelTask, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in SendStreamingJsonRpcRequestAsync<StreamResponse>(A2AMethods.SubscribeToTask, request, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<TaskPushNotificationConfig>(A2AMethods.CreateTaskPushNotificationConfig, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<TaskPushNotificationConfig>(A2AMethods.GetTaskPushNotificationConfig, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<ListTaskPushNotificationConfigResponse>(A2AMethods.ListTaskPushNotificationConfig, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        await SendJsonRpcRequestAsync<object>(A2AMethods.DeleteTaskPushNotificationConfig, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        return await SendJsonRpcRequestAsync<AgentCard>(A2AMethods.GetExtendedAgentCard, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <summary>No-op. The HttpClient is either shared or externally owned.</summary>
    public void Dispose()
    {
        // HttpClient lifetime is managed externally or via the shared static instance.
        GC.SuppressFinalize(this);
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async Task<TResult> SendJsonRpcRequestAsync<TResult>(string method, object? @params, CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{method}", ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var rpcRequest = new JsonRpcRequest
        {
            Method = method,
            Id = new JsonRpcId(Guid.NewGuid().ToString()),
            Params = @params is not null ? JsonSerializer.SerializeToElement(@params, A2AJsonUtilities.DefaultOptions) : null,
        };

        activity?.SetTag("rpc.system", "jsonrpc");
        activity?.SetTag("rpc.method", method);
        activity?.SetTag("url.full", _url);
        activity?.SetTag("rpc.jsonrpc.request_id", rpcRequest.Id.ToString());

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            using var content = new JsonRpcContent(rpcRequest);
            using var response = await _httpClient.PostAsync(_url, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var rpcResponse = await JsonSerializer.DeserializeAsync<JsonRpcResponse>(stream, A2AJsonUtilities.DefaultOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException("Failed to deserialize JSON-RPC response.", A2AErrorCode.InternalError);

            if (rpcResponse.Error is { } error)
            {
                throw new A2AException(error.Message, (A2AErrorCode)error.Code);
            }

            return rpcResponse.Result.Deserialize<TResult>(A2AJsonUtilities.DefaultOptions)
                ?? throw new A2AException("Failed to deserialize JSON-RPC result.", A2AErrorCode.InternalError);
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async IAsyncEnumerable<TResult> SendStreamingJsonRpcRequestAsync<TResult>(string method, object? @params, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{method}", ActivityKind.Client);
        A2ADiagnostics.ClientRequestCount.Add(1);
        int eventCount = 0;

        var rpcRequest = new JsonRpcRequest
        {
            Method = method,
            Id = new JsonRpcId(Guid.NewGuid().ToString()),
            Params = @params is not null ? JsonSerializer.SerializeToElement(@params, A2AJsonUtilities.DefaultOptions) : null,
        };

        activity?.SetTag("rpc.system", "jsonrpc");
        activity?.SetTag("rpc.method", method);
        activity?.SetTag("url.full", _url);
        activity?.SetTag("rpc.jsonrpc.request_id", rpcRequest.Id.ToString());

        HttpResponseMessage? response = null;
        Stream? stream = null;

        try
        {
            using var content = new JsonRpcContent(rpcRequest);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = content,
            };
            requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            response?.Dispose();
            throw;
        }

        using (response)
        using (stream)
        {
            await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(sseItem.Data, A2AJsonUtilities.DefaultOptions)
                    ?? throw new A2AException("Failed to deserialize streaming JSON-RPC response.", A2AErrorCode.InternalError);

                if (rpcResponse.Error is { } error)
                {
                    throw new A2AException(error.Message, (A2AErrorCode)error.Code);
                }

                var result = rpcResponse.Result.Deserialize<TResult>(A2AJsonUtilities.DefaultOptions)
                    ?? throw new A2AException("Failed to deserialize streaming JSON-RPC result.", A2AErrorCode.InternalError);

                eventCount++;
                yield return result;
            }
        }

        A2ADiagnostics.ClientStreamEventCount.Record(eventCount);
    }
}