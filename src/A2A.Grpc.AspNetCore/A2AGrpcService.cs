namespace A2A.Grpc.AspNetCore;

using Google.Protobuf.WellKnownTypes;
using global::Grpc.Core;

/// <summary>
/// gRPC service implementation that adapts the generated <see cref="Protos.A2AService.A2AServiceBase"/>
/// onto the shared <see cref="IA2ARequestHandler"/> pipeline. All business logic (task lifecycle,
/// history, streaming, cancellation) lives in the handler; this type only performs protocol translation
/// and maps <see cref="A2AException"/> to gRPC <see cref="RpcException"/>.
/// </summary>
internal sealed class A2AGrpcService : Protos.A2AService.A2AServiceBase
{
    private const string VersionHeader = "a2a-version";
    private const string SupportedVersion = "1.0";

    private readonly IA2ARequestHandler _handler;
    private readonly A2ACustomOperationRegistry? _registry;

    public A2AGrpcService(IA2ARequestHandler handler, A2ACustomOperationRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _registry = registry;
    }

    private IA2ARequestHandler HandlerFor(ServerCallContext context) => _registry is null
        ? _handler
        : _handler.WithRequestContext(context.GetHttpContext().Features.Get<A2ARequestContext>());

    // Rejects requests declaring a protocol version this endpoint does not implement.
    // Unlike the JSON-RPC binding, which also serves v0.3 through the compatibility processor,
    // this endpoint only implements the v1 service contract (`lf.a2a.v1`), so "0.3" is rejected
    // as well. An absent header means "unspecified" and is accepted.
    private static void EnsureSupportedVersion(ServerCallContext context)
    {
        var version = context.RequestHeaders.GetValue(VersionHeader);
        if (!string.IsNullOrEmpty(version) && version != SupportedVersion)
        {
            throw new A2AException(
                $"Protocol version '{version}' is not supported. Supported versions: {SupportedVersion}",
                A2AErrorCode.VersionNotSupported);
        }
    }

    public override async Task<Protos.SendMessageResponse> SendMessage(Protos.SendMessageRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var response = await HandlerFor(context).SendMessageAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(response);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.Task> GetTask(Protos.GetTaskRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var task = await HandlerFor(context).GetTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(task);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.ListTasksResponse> ListTasks(Protos.ListTasksRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var response = await HandlerFor(context).ListTasksAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(response);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.Task> CancelTask(Protos.CancelTaskRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var task = await HandlerFor(context).CancelTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(task);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.TaskPushNotificationConfig> CreateTaskPushNotificationConfig(Protos.TaskPushNotificationConfig request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var config = await HandlerFor(context).CreateTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(config);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.TaskPushNotificationConfig> GetTaskPushNotificationConfig(Protos.GetTaskPushNotificationConfigRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var config = await HandlerFor(context).GetTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(config);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigs(Protos.ListTaskPushNotificationConfigsRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var response = await HandlerFor(context).ListTaskPushNotificationConfigsAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return ProtoMap.ToProto(response);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Empty> DeleteTaskPushNotificationConfig(Protos.DeleteTaskPushNotificationConfigRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            await HandlerFor(context).DeleteTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            return new Empty();
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task<Protos.AgentCard> GetExtendedAgentCard(Protos.GetExtendedAgentCardRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var card = await HandlerFor(context).GetExtendedAgentCardAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
            if (_registry is not null && context.GetHttpContext().Features.Get<A2ARequestContext>() is { } activation)
            {
                _registry.EnsureOwnership(activation.RegistryToken);
                card = _registry.PrepareAgentCard(card);
            }
            return ProtoMap.ToProto(card);
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task SendStreamingMessage(Protos.SendMessageRequest request, IServerStreamWriter<Protos.StreamResponse> responseStream, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            await foreach (var streamEvent in HandlerFor(context).SendStreamingMessageAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(ProtoMap.ToProto(streamEvent)).ConfigureAwait(false);
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task SubscribeToTask(Protos.SubscribeToTaskRequest request, IServerStreamWriter<Protos.StreamResponse> responseStream, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            await foreach (var streamEvent in HandlerFor(context).SubscribeToTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(ProtoMap.ToProto(streamEvent)).ConfigureAwait(false);
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }
}
