namespace A2A.Grpc.AspNetCore;

using global::Grpc.Core;
using global::Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

/// <summary>Negotiates extensions once before an A2A unary or server-streaming call.</summary>
/// <param name="options">The authoritative registry and availability policy.</param>
/// <param name="logger">The error logger.</param>
internal sealed class A2AGrpcExtensionInterceptor(
    A2AGrpcExtensionOptions options,
    ILogger<A2AGrpcExtensionInterceptor> logger) : Interceptor
{
    private const string MetadataKey = "a2a-extensions";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await PrepareRequestAsync(context, context.CancellationToken).ConfigureAwait(false);
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (A2AException exception) { throw GrpcErrorMapping.ToRpcException(exception); }
        catch (RpcException) { throw; }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "The request was canceled."));
        }
        catch (Exception exception)
        {
            logger.UnexpectedCustomOperationError(exception);
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await PrepareRequestAsync(context, context.CancellationToken).ConfigureAwait(false);
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        catch (A2AException exception) { throw GrpcErrorMapping.ToRpcException(exception); }
        catch (RpcException) { throw; }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "The request was canceled."));
        }
        catch (Exception exception)
        {
            logger.UnexpectedCustomOperationError(exception);
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }

    private async ValueTask PrepareRequestAsync(ServerCallContext context, CancellationToken cancellationToken)
    {
        // Registry-aware gRPC hosting implements the v1 binding. Check every repeated
        // version value before invoking the policy or publishing activation metadata.
        foreach (var entry in context.RequestHeaders)
        {
            if (!entry.IsBinary && entry.Key == "a2a-version" && !string.IsNullOrEmpty(entry.Value) && entry.Value != "1.0")
            {
                throw new A2AException(
                    $"Protocol version '{entry.Value}' is not supported. Supported versions: 1.0",
                    A2AErrorCode.VersionNotSupported);
            }
        }

        var httpContext = context.GetHttpContext();
        httpContext.Features.Set<A2ARequestContext>(null);
        var requested = A2AExtensionIdentifiers.Parse(context.RequestHeaders
            .Where(entry => !entry.IsBinary && entry.Key == MetadataKey).Select(entry => entry.Value));
        var policy = options.CanActivate;
        var activation = await options.Registry.NegotiateExtensionsAsync(
            requested,
            policy is null ? null : (extension, ct) => policy(context, extension, ct),
            cancellationToken).ConfigureAwait(false);

        httpContext.Features.Set(activation);
        if (activation.ActivatedExtensions.Count > 0)
        {
            var value = string.Join(", ", activation.ActivatedExtensions.Order(StringComparer.Ordinal));
            // Stage the initial metadata without sending it yet: dedicated services may
            // still call WriteResponseHeadersAsync with their own metadata. gRPC sends
            // these headers before the first response or when completing an error.
            httpContext.Response.Headers[MetadataKey] = value;
            // Keep rich error trailers intact and carry the same snapshot on RPC failures.
            context.ResponseTrailers.Add(MetadataKey, value);
        }
    }
}
