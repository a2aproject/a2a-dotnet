namespace A2A.Grpc;

using System.Globalization;
using global::Grpc.Core;

/// <summary>
/// Translates between <see cref="A2AException"/>/<see cref="A2AErrorCode"/> and gRPC
/// <see cref="RpcException"/>/<see cref="StatusCode"/>, reusing the shared
/// <see cref="A2AErrorCodeMapping"/> so the wire behavior matches the other bindings.
/// </summary>
/// <remarks>
/// The precise <see cref="A2AErrorCode"/> is carried in the <see cref="ErrorCodeTrailer"/> response
/// trailer so a first-party client recovers the exact code; when the trailer is absent (e.g. a
/// non-A2A gRPC server) the client falls back to a coarse mapping from the gRPC status code.
/// </remarks>
internal static class GrpcErrorMapping
{
    /// <summary>Response trailer carrying the numeric <see cref="A2AErrorCode"/>.</summary>
    public const string ErrorCodeTrailer = "a2a-error-code";

    /// <summary>Maps an <see cref="A2AErrorCode"/> to the gRPC <see cref="StatusCode"/> per spec Section 5.4.</summary>
    /// <param name="code">The A2A error code to map.</param>
    public static StatusCode ToStatusCode(A2AErrorCode code) => A2AErrorCodeMapping.GetGrpcStatus(code) switch
    {
        "NOT_FOUND" => StatusCode.NotFound,
        "INVALID_ARGUMENT" => StatusCode.InvalidArgument,
        "FAILED_PRECONDITION" => StatusCode.FailedPrecondition,
        _ => StatusCode.Internal,
    };

    /// <summary>Maps a gRPC <see cref="StatusCode"/> to a representative <see cref="A2AErrorCode"/>.</summary>
    /// <param name="code">The gRPC status code to map.</param>
    public static A2AErrorCode FromStatusCode(StatusCode code) => code switch
    {
        StatusCode.NotFound => A2AErrorCode.TaskNotFound,
        StatusCode.InvalidArgument => A2AErrorCode.InvalidRequest,
        StatusCode.FailedPrecondition => A2AErrorCode.UnsupportedOperation,
        StatusCode.Unimplemented => A2AErrorCode.MethodNotFound,
        _ => A2AErrorCode.InternalError,
    };

    /// <summary>Builds an <see cref="RpcException"/> for an <see cref="A2AException"/>, tagging the exact code in trailers.</summary>
    /// <param name="exception">The A2A exception to translate.</param>
    public static RpcException ToRpcException(A2AException exception)
    {
        var trailers = new Metadata
        {
            { ErrorCodeTrailer, ((int)exception.ErrorCode).ToString(CultureInfo.InvariantCulture) },
        };

        return new RpcException(new Status(ToStatusCode(exception.ErrorCode), exception.Message), trailers);
    }

    /// <summary>Reconstructs an <see cref="A2AException"/> from an <see cref="RpcException"/>, preferring the trailer code.</summary>
    /// <param name="exception">The gRPC exception to translate.</param>
    public static A2AException ToA2AException(RpcException exception)
    {
        var code = FromStatusCode(exception.StatusCode);

        var trailer = exception.Trailers.GetValue(ErrorCodeTrailer);
        if (trailer is not null
            && int.TryParse(trailer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
            && Enum.IsDefined(typeof(A2AErrorCode), raw))
        {
            code = (A2AErrorCode)raw;
        }

        return new A2AException(exception.Status.Detail, exception, code);
    }
}
