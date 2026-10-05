namespace A2A.Grpc.AspNetCore;

using global::Grpc.Core;
using Google.Protobuf;

/// <summary>
/// gRPC service implementation that adapts the generated <see cref="Protos.A2AExtensionService.A2AExtensionServiceBase"/>
/// onto the <see cref="A2AOperationCatalog"/> / <see cref="A2AOperationHandlerCatalog"/> pipeline, letting
/// custom/extension operations — registered via
/// <see cref="A2AOperationCatalogBuilder.DefineUnary{TRequest,TResult}"/>/<c>DefineStreaming</c> and bound
/// via <see cref="A2AGrpcExtensionOperationBindingBuilder"/> — run over gRPC even though they have no fixed
/// RPC in the vendored <c>a2a.proto</c> contract. Requests/responses/events are carried as UTF-8 JSON bytes
/// using the same <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> the operation
/// already uses for its JSON-RPC/HTTP+JSON bindings, so all three transports share identical wire semantics
/// for extension operations. This type only performs envelope lookup/dispatch and maps
/// <see cref="A2AException"/> to gRPC <see cref="RpcException"/>; see <see cref="A2AGrpcService"/> for the
/// fixed-contract standard-operation service.
/// </summary>
internal sealed class A2AGrpcExtensionService : Protos.A2AExtensionService.A2AExtensionServiceBase
{
    private readonly A2AOperationContext _context;
    private readonly A2AOperationHandlerCatalog _handlers;
    private readonly A2AGrpcExtensionOperationBindings _bindings;

    public A2AGrpcExtensionService(
        IA2ARequestHandler handler,
        A2AOperationHandlerCatalog handlers,
        A2AGrpcExtensionOperationBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(bindings);

        _context = new A2AOperationContext(handler);
        _handlers = handlers;
        _bindings = bindings;
    }

    public override async Task<Protos.ExtensionOperationResponse> InvokeExtensionOperation(
        Protos.ExtensionOperationRequest request,
        ServerCallContext context)
    {
        try
        {
            var binding = ResolveBinding(request.OperationId);
            if (binding is not IA2AGrpcExtensionUnaryOperationBinding unaryBinding)
            {
                throw new A2AException(
                    $"Extension operation '{request.OperationId}' is not a unary operation.",
                    A2AErrorCode.InvalidRequest);
            }

            var payload = await unaryBinding.InvokeAsync(
                _context,
                _handlers,
                request.Payload.Memory,
                context.CancellationToken).ConfigureAwait(false);
            return new Protos.ExtensionOperationResponse { Payload = ByteString.CopyFrom(payload.AsSpan()) };
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    public override async Task InvokeStreamingExtensionOperation(
        Protos.ExtensionOperationRequest request,
        IServerStreamWriter<Protos.ExtensionOperationEvent> responseStream,
        ServerCallContext context)
    {
        try
        {
            var binding = ResolveBinding(request.OperationId);
            if (binding is not IA2AGrpcExtensionStreamingOperationBinding streamingBinding)
            {
                throw new A2AException(
                    $"Extension operation '{request.OperationId}' is not a streaming operation.",
                    A2AErrorCode.InvalidRequest);
            }

            await foreach (var payload in streamingBinding.InvokeAsync(
                _context,
                _handlers,
                request.Payload.Memory,
                context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(
                    new Protos.ExtensionOperationEvent { Payload = ByteString.CopyFrom(payload.AsSpan()) })
                    .ConfigureAwait(false);
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
    }

    private IA2AGrpcExtensionOperationBinding ResolveBinding(string operationId)
    {
        if (string.IsNullOrEmpty(operationId)
            || !_bindings.TryGetBinding(operationId, out var binding))
        {
            throw new A2AException(
                $"No gRPC extension operation binding is registered for operation '{operationId}'.",
                A2AErrorCode.MethodNotFound);
        }

        return binding;
    }
}
