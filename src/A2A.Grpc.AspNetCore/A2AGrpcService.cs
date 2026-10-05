namespace A2A.Grpc.AspNetCore;

using Google.Protobuf.WellKnownTypes;
using global::Grpc.Core;

/// <summary>
/// gRPC service implementation that adapts the generated <see cref="Protos.A2AService.A2AServiceBase"/>
/// onto the same <see cref="A2AOperationCatalog"/> / <see cref="A2AOperationHandlerCatalog"/> pipeline used
/// by the JSON-RPC and HTTP+JSON bindings. Each RPC validates its request against the catalog's semantic
/// validator and dispatches through the shared handler catalog, so gRPC gets the exact same validation and
/// dispatch behavior as the other transports. This type otherwise only performs protocol translation
/// and maps <see cref="A2AException"/> to gRPC <see cref="RpcException"/>.
/// </summary>
/// <remarks>
/// Standard operations (those backed by a fixed RPC in the vendored <c>.proto</c>) are routed through the
/// catalog here. Custom/extension operations registered dynamically via
/// <see cref="A2AOperationCatalogBuilder.DefineUnary{TRequest,TResult}"/>/<c>DefineStreaming</c> are not
/// reachable from this fixed-contract service; see <c>A2AGrpcExtensionService</c> for the generic envelope
/// binding that lets such operations run over gRPC without changing the vendored protocol.
/// </remarks>
internal sealed class A2AGrpcService : Protos.A2AService.A2AServiceBase
{
    private const string VersionHeader = "a2a-version";
    private const string SupportedVersion = "1.0";

    private static readonly Lazy<(
        A2AOperationHandlerCatalog Handlers,
        A2AStandardOperations Standard)> StandardDispatch = new(CreateStandardDispatch);

    private readonly A2AOperationContext _context;
    private readonly A2AOperationHandlerCatalog _handlers;
    private readonly A2AStandardOperations _standard;

    public A2AGrpcService(IA2ARequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _context = new A2AOperationContext(handler);
        var dispatch = StandardDispatch.Value;
        _handlers = dispatch.Handlers;
        _standard = dispatch.Standard;
    }

    private static (
        A2AOperationHandlerCatalog Handlers,
        A2AStandardOperations Standard) CreateStandardDispatch()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .AddStandardA2AHandlers(standard)
            .Build(operationCatalog);
        return (handlers, standard);
    }

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
            var response = await InvokeAsync(_standard.SendMessage, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var task = await InvokeAsync(_standard.GetTask, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var response = await InvokeAsync(_standard.ListTasks, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var task = await InvokeAsync(_standard.CancelTask, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var config = await InvokeAsync(_standard.CreateTaskPushNotificationConfig, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var config = await InvokeAsync(_standard.GetTaskPushNotificationConfig, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var response = await InvokeAsync(_standard.ListTaskPushNotificationConfigs, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            await InvokeAsync(_standard.DeleteTaskPushNotificationConfig, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            var card = await InvokeAsync(_standard.GetExtendedAgentCard, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false);
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
            await foreach (var streamEvent in InvokeStreamingAsync(_standard.SendStreamingMessage, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false))
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
            await foreach (var streamEvent in InvokeStreamingAsync(_standard.SubscribeToTask, request, ProtoMap.ToDomain, context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(ProtoMap.ToProto(streamEvent)).ConfigureAwait(false);
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    // Validates the domain request against the same catalog-owned semantic validator used by the
    // JSON-RPC/HTTP+JSON bindings, then dispatches through the shared handler catalog, so all three
    // transports enforce identical validation/dispatch behavior for standard operations.
    private ValueTask<TResult> InvokeAsync<TProto, TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        TProto protoRequest,
        Func<TProto, TRequest> toDomain,
        CancellationToken cancellationToken)
    {
        var request = toDomain(protoRequest);
        _handlers.OperationCatalog.Validate(operation, request);
        return _handlers.InvokeAsync(operation, _context, request, cancellationToken);
    }

    private IAsyncEnumerable<TEvent> InvokeStreamingAsync<TProto, TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TProto protoRequest,
        Func<TProto, TRequest> toDomain,
        CancellationToken cancellationToken)
    {
        var request = toDomain(protoRequest);
        _handlers.OperationCatalog.ValidateStreaming(operation, request);
        return _handlers.InvokeStreamingAsync(operation, _context, request, cancellationToken);
    }
}
