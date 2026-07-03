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
            var response = await _client.SendMessageAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.GetTaskAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.ListTasksAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.CancelTaskAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.CreateTaskPushNotificationConfigAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.GetTaskPushNotificationConfigAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.ListTaskPushNotificationConfigsAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _client.DeleteTaskPushNotificationConfigAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var response = await _client.GetExtendedAgentCardAsync(ProtoMap.ToProto(request), _headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProtoMap.ToDomain(response);
        }
        catch (RpcException exception)
        {
            throw GrpcErrorMapping.ToA2AException(exception);
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

    private static async IAsyncEnumerable<StreamResponse> ReadStreamAsync(AsyncServerStreamingCall<Protos.StreamResponse> call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (call)
        {
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
                    throw GrpcErrorMapping.ToA2AException(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return ProtoMap.ToDomain(stream.Current);
            }
        }
    }
}
