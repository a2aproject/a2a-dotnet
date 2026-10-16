namespace A2A.Grpc;

using System.Runtime.CompilerServices;
using global::Grpc.Core;
using global::Grpc.Net.Client;

/// <summary>
/// An <see cref="IA2AClient"/> implementation that talks to an A2A agent over the gRPC binding.
/// </summary>
/// <remarks>
/// Requests and responses are translated to and from the protobuf contract by <see cref="ProtoMap"/>,
/// and gRPC faults are surfaced as <see cref="A2AException"/> via <see cref="GrpcErrorMapping"/>, giving
/// behavioral parity with the JSON-RPC and HTTP+JSON clients.
/// </remarks>
public sealed class A2AGrpcClient : IA2AClient, IDisposable
{
    private const string ProtocolVersion = "1.0";

    private readonly Protos.A2AService.A2AServiceClient _client;
    private readonly GrpcChannel? _ownedChannel;
    private readonly Metadata _headers = new() { { "a2a-version", ProtocolVersion } };

    /// <summary>Initializes a new client for the agent at <paramref name="baseUrl"/>.</summary>
    /// <param name="baseUrl">The base address of the gRPC endpoint (e.g. <c>https://agent.example.com</c>).</param>
    /// <param name="httpClient">An optional <see cref="HttpClient"/> to use for the underlying channel. The caller retains ownership.</param>
    public A2AGrpcClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        _ownedChannel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions { HttpClient = httpClient });
        _client = new Protos.A2AService.A2AServiceClient(_ownedChannel);
    }

    /// <summary>Initializes a new client over an existing <see cref="GrpcChannel"/>. The caller retains ownership of the channel.</summary>
    /// <param name="channel">The channel to use.</param>
    public A2AGrpcClient(GrpcChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _client = new Protos.A2AService.A2AServiceClient(channel);
    }

    /// <summary>Initializes a new client over a pre-built generated gRPC client. Intended for testing.</summary>
    /// <param name="client">The generated gRPC client to wrap.</param>
    internal A2AGrpcClient(Protos.A2AService.A2AServiceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.SendMessageAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.GetTaskAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.ListTasksAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.CancelTaskAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        try
        {
            return await InvokeAsync(_client.CreateTaskPushNotificationConfigAsync(ProtoMap.ToProto(config), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.GetTaskPushNotificationConfigAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.ListTaskPushNotificationConfigsAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await InvokeAsync(_client.DeleteTaskPushNotificationConfigAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => response, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await InvokeAsync(_client.GetExtendedAgentCardAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken),
                static response => ProtoMap.ToDomain(response), cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToClientException(exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = _client.SendStreamingMessage(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken);
        return ReadStreamAsync(call, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = _client.SubscribeToTask(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken);
        return ReadStreamAsync(call, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _ownedChannel?.Dispose();

    // gRPC owns these I/O completion tasks; they do not require the caller's synchronization
    // context. ConfigureAwait(false) is used throughout, and the call is disposed in this scope.
#pragma warning disable VSTHRD003
    private static async Task<TResult> InvokeAsync<TResponse, TResult>(
        AsyncUnaryCall<TResponse> call, Func<TResponse, TResult> convert, CancellationToken cancellationToken)
        where TResponse : class
    {
        using (call)
        {
            Metadata? headers = null;
            try
            {
                var response = await call.ResponseAsync.ConfigureAwait(false);
                headers = await call.ResponseHeadersAsync.ConfigureAwait(false);
                return convert(response);
            }
            catch (A2AException exception)
            {
                exception.ActivatedExtensions = ReadActivation(headers);
                throw;
            }
            catch (RpcException exception)
            {
                var error = GrpcErrorMapping.ToClientException(exception, cancellationToken);
                if (error is A2AException a2aException)
                {
                    headers ??= await TryReadHeadersAsync(call.ResponseHeadersAsync, cancellationToken).ConfigureAwait(false);
                    // The initial snapshot takes precedence; trailers are a fallback for peers
                    // that omit initial metadata. Never union conflicting activation lists.
                    if (headers?.Get("a2a-extensions") is not null) a2aException.ActivatedExtensions = ReadActivation(headers);
                }
                throw error;
            }
        }
    }

    private static IReadOnlySet<string> ReadActivation(Metadata? headers)
        => A2AResponseActivation.Read(headers?.Where(entry => !entry.IsBinary && entry.Key == "a2a-extensions")
            .Select(entry => entry.Value) ?? []);

    private static async Task<Metadata?> TryReadHeadersAsync(Task<Metadata> headers, CancellationToken cancellationToken)
    {
        try { return await headers.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (RpcException) { return null; }
        catch (OperationCanceledException) { return null; }
    }

    private static async IAsyncEnumerable<StreamResponse> ReadStreamAsync(AsyncServerStreamingCall<Protos.StreamResponse> call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (call)
        {
            Metadata? headers = null;
            try { headers = await call.ResponseHeadersAsync.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (RpcException exception) { throw GrpcErrorMapping.ToClientException(exception, cancellationToken); }
            var activation = ReadActivation(headers);
            var stream = call.ResponseStream;
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await stream.MoveNext(cancellationToken).ConfigureAwait(false);
                }
                catch (RpcException exception)
                {
                    var error = GrpcErrorMapping.ToClientException(exception, cancellationToken);
                    if (error is A2AException a2aException && headers.Get("a2a-extensions") is not null)
                        a2aException.ActivatedExtensions = activation;
                    throw error;
                }

                if (!hasNext)
                {
                    yield break;
                }

                StreamResponse response;
                try { response = ProtoMap.ToDomain(stream.Current); }
                catch (A2AException exception) { exception.ActivatedExtensions = activation; throw; }
                yield return response;
            }
        }
    }
#pragma warning restore VSTHRD003
}
