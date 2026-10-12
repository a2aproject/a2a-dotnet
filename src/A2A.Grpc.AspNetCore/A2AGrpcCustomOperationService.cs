namespace A2A.Grpc.AspNetCore;

using A2A.Grpc.Extensions.Protos;
using global::Google.Protobuf;
using global::Grpc.Core;
using global::Grpc.AspNetCore.Server;
using Microsoft.Extensions.Logging;
using System.Text.Json;

internal sealed class A2AGrpcCustomOperationService(
    A2ACustomOperationRegistry registry,
    ILogger<A2AGrpcCustomOperationService> logger)
    : A2AExtensionService.A2AExtensionServiceBase
{
    public override async Task<ExtensionOperationResponse> InvokeExtensionOperation(
        ExtensionOperationRequest request,
        ServerCallContext context)
    {
        try
        {
            var registration = Resolve(request.OperationId, A2ACustomOperationKind.Unary);
            var operationRequest = await DeserializeRequestAsync(
                request.Payload,
                registration,
                context.CancellationToken).ConfigureAwait(false);
            var result = await registry.InvokeAsync(
                registration,
                CreateOperationContext(context),
                operationRequest,
                context.CancellationToken).ConfigureAwait(false);
            return new ExtensionOperationResponse
            {
                Payload = await SerializeAsync(
                    result,
                    registration.OutputTypeInfo,
                    context.CancellationToken).ConfigureAwait(false),
            };
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
        catch (JsonException exception)
        {
            throw GrpcErrorMapping.ToRpcException(
                new A2AException(
                    "The extension operation payload is invalid.",
                    exception,
                    A2AErrorCode.InvalidParams));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.UnexpectedCustomOperationError(exception);
            throw new RpcException(
                new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }

    public override async Task InvokeStreamingExtensionOperation(
        ExtensionOperationRequest request,
        IServerStreamWriter<ExtensionOperationEvent> responseStream,
        ServerCallContext context)
    {
        try
        {
            var registration = Resolve(request.OperationId, A2ACustomOperationKind.Streaming);
            var operationRequest = await DeserializeRequestAsync(
                request.Payload,
                registration,
                context.CancellationToken).ConfigureAwait(false);

            await foreach (var streamEvent in registry.InvokeStreamingAsync(
                registration,
                CreateOperationContext(context),
                operationRequest,
                context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(new ExtensionOperationEvent
                {
                    Payload = await SerializeAsync(
                        streamEvent,
                        registration.OutputTypeInfo,
                        context.CancellationToken).ConfigureAwait(false),
                }).ConfigureAwait(false);
            }
        }
        catch (A2AException exception)
        {
            throw GrpcErrorMapping.ToRpcException(exception);
        }
        catch (JsonException exception)
        {
            throw GrpcErrorMapping.ToRpcException(
                new A2AException(
                    "The extension operation payload is invalid.",
                    exception,
                    A2AErrorCode.InvalidParams));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.UnexpectedCustomOperationError(exception);
            throw new RpcException(
                new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }

    private CustomOperationRegistration Resolve(
        string operationId,
        A2ACustomOperationKind expectedKind)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new A2AException(
                "The extension operation ID is required.",
                A2AErrorCode.InvalidRequest);
        }

        var id = new A2AOperationId(operationId);
        if (!registry.TryGetRegistration(id, out var registration))
        {
            throw new A2AException(
                $"Extension operation '{operationId}' was not found.",
                A2AErrorCode.MethodNotFound);
        }

        if (registration.Kind != expectedKind)
        {
            var expectedKindName = expectedKind == A2ACustomOperationKind.Streaming
                ? "streaming"
                : "unary";
            throw new A2AException(
                $"Extension operation '{operationId}' is not a {expectedKindName} operation.",
                A2AErrorCode.InvalidRequest);
        }

        return registration;
    }

    private static A2ACustomOperationContext CreateOperationContext(
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        return new A2ACustomOperationContext(
            httpContext.RequestServices,
            httpContext,
            context);
    }

    private static async Task<object> DeserializeRequestAsync(
        ByteString payload,
        CustomOperationRegistration registration,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(payload.ToByteArray(), writable: false);
        return await JsonSerializer.DeserializeAsync(
            stream,
            registration.RequestTypeInfo,
            cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException(
                "The extension operation payload cannot be null.",
                A2AErrorCode.InvalidParams);
    }

    private static async Task<ByteString> SerializeAsync(
        object? value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            typeInfo,
            cancellationToken).ConfigureAwait(false);
        return ByteString.CopyFrom(stream.ToArray());
    }
}

internal static partial class GrpcCustomOperationLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled gRPC custom operation failure.")]
    internal static partial void UnexpectedCustomOperationError(
        this ILogger logger,
        Exception exception);
}
