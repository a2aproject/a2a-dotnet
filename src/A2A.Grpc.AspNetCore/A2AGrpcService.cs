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

    private readonly IA2ARequestHandler _handler;

    public A2AGrpcService(IA2ARequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    // Rejects requests declaring an unsupported A2A protocol version, mirroring the JSON-RPC
    // binding's preflight check. An absent header means "unspecified" and is accepted.
    private static void EnsureSupportedVersion(ServerCallContext context)
    {
        var version = context.RequestHeaders.GetValue(VersionHeader);
        if (!string.IsNullOrEmpty(version) && version != "1.0" && version != "0.3")
        {
            throw new A2AException(
                $"Protocol version '{version}' is not supported. Supported versions: 0.3, 1.0",
                A2AErrorCode.VersionNotSupported);
        }
    }

    public override async Task<Protos.SendMessageResponse> SendMessage(Protos.SendMessageRequest request, ServerCallContext context)
    {
        try
        {
            EnsureSupportedVersion(context);
            var response = await _handler.SendMessageAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var task = await _handler.GetTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var response = await _handler.ListTasksAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var task = await _handler.CancelTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var config = await _handler.CreateTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var config = await _handler.GetTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var response = await _handler.ListTaskPushNotificationConfigsAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            await _handler.DeleteTaskPushNotificationConfigAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            var card = await _handler.GetExtendedAgentCardAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false);
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
            await foreach (var streamEvent in _handler.SendStreamingMessageAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false))
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
            await foreach (var streamEvent in _handler.SubscribeToTaskAsync(ProtoMap.ToDomain(request), context.CancellationToken).ConfigureAwait(false))
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
