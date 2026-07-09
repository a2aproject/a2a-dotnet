namespace A2A.Grpc;

using global::Google.Protobuf;
using global::Google.Protobuf.WellKnownTypes;
using global::Google.Rpc;
using global::Grpc.Core;
using GrpcStatus = global::Grpc.Core.Status;
using RpcStatus = global::Google.Rpc.Status;

/// <summary>
/// Translates between <see cref="A2AException"/>/<see cref="A2AErrorCode"/> and gRPC
/// <see cref="RpcException"/>/<see cref="StatusCode"/>, reusing the shared
/// <see cref="A2AErrorCodeMapping"/> so the wire behavior matches the other bindings.
/// </summary>
/// <remarks>
/// The precise A2A error is conveyed the same way as the HTTP+JSON binding — an
/// <see cref="ErrorInfo"/> detail (UPPER_SNAKE_CASE <c>reason</c> in the <c>a2a-protocol.org</c>
/// domain) carried in the standard <c>google.rpc.Status</c> attached to the gRPC status. This lets
/// clients recover the exact <see cref="A2AErrorCode"/> even though several codes share one gRPC
/// status code; when no detail is present the coarse status-code mapping is used as a fallback.
/// </remarks>
internal static class GrpcErrorMapping
{
    private const string ErrorDomain = "a2a-protocol.org";
    private const string StatusDetailsTrailer = "grpc-status-details-bin";

    private static readonly Dictionary<string, A2AErrorCode> s_reasonToErrorCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TASK_NOT_FOUND"] = A2AErrorCode.TaskNotFound,
        ["TASK_NOT_CANCELABLE"] = A2AErrorCode.TaskNotCancelable,
        ["PUSH_NOTIFICATION_NOT_SUPPORTED"] = A2AErrorCode.PushNotificationNotSupported,
        ["UNSUPPORTED_OPERATION"] = A2AErrorCode.UnsupportedOperation,
        ["CONTENT_TYPE_NOT_SUPPORTED"] = A2AErrorCode.ContentTypeNotSupported,
        ["INVALID_AGENT_RESPONSE"] = A2AErrorCode.InvalidAgentResponse,
        ["EXTENDED_AGENT_CARD_NOT_CONFIGURED"] = A2AErrorCode.ExtendedAgentCardNotConfigured,
        ["EXTENSION_SUPPORT_REQUIRED"] = A2AErrorCode.ExtensionSupportRequired,
        ["VERSION_NOT_SUPPORTED"] = A2AErrorCode.VersionNotSupported,
        ["METHOD_NOT_FOUND"] = A2AErrorCode.MethodNotFound,
        ["INVALID_PARAMS"] = A2AErrorCode.InvalidParams,
        ["INVALID_REQUEST"] = A2AErrorCode.InvalidRequest,
        ["PARSE_ERROR"] = A2AErrorCode.ParseError,
        ["INTERNAL_ERROR"] = A2AErrorCode.InternalError,
    };

    /// <summary>Maps an <see cref="A2AErrorCode"/> to the gRPC <see cref="StatusCode"/> per spec Section 5.4.</summary>
    /// <param name="code">The A2A error code to map.</param>
    public static StatusCode ToStatusCode(A2AErrorCode code) => A2AErrorCodeMapping.GetGrpcStatus(code) switch
    {
        "NOT_FOUND" => StatusCode.NotFound,
        "INVALID_ARGUMENT" => StatusCode.InvalidArgument,
        "FAILED_PRECONDITION" => StatusCode.FailedPrecondition,
        _ => StatusCode.Internal,
    };

    /// <summary>Maps a gRPC <see cref="StatusCode"/> to a representative <see cref="A2AErrorCode"/> (coarse fallback).</summary>
    /// <param name="code">The gRPC status code to map.</param>
    /// <remarks>
    /// Used only when a peer omits the <see cref="ErrorInfo"/> detail. Several A2A errors share
    /// <see cref="StatusCode.FailedPrecondition"/>; <see cref="A2AErrorCode.TaskNotCancelable"/> is the
    /// most common case and matches the reverse-mapping the other A2A SDKs use for detail-less errors.
    /// </remarks>
    public static A2AErrorCode FromStatusCode(StatusCode code) => code switch
    {
        StatusCode.NotFound => A2AErrorCode.TaskNotFound,
        StatusCode.InvalidArgument => A2AErrorCode.InvalidRequest,
        StatusCode.FailedPrecondition => A2AErrorCode.TaskNotCancelable,
        StatusCode.Unimplemented => A2AErrorCode.MethodNotFound,
        _ => A2AErrorCode.InternalError,
    };

    /// <summary>Builds an <see cref="RpcException"/> for an <see cref="A2AException"/>, attaching a google.rpc.Status detail.</summary>
    /// <param name="exception">The A2A exception to translate.</param>
    public static RpcException ToRpcException(A2AException exception)
    {
        var statusCode = ToStatusCode(exception.ErrorCode);
        var reason = A2AErrorCodeMapping.GetReasonString(exception.ErrorCode) ?? DefaultReason(exception.ErrorCode);

        var richStatus = new RpcStatus
        {
            Code = (int)statusCode,
            Message = exception.Message,
        };
        richStatus.Details.Add(Any.Pack(new ErrorInfo
        {
            Reason = reason,
            Domain = ErrorDomain,
        }));

        var trailers = new Metadata
        {
            { StatusDetailsTrailer, richStatus.ToByteArray() },
        };

        return new RpcException(new GrpcStatus(statusCode, exception.Message), trailers);
    }

    /// <summary>Reconstructs an <see cref="A2AException"/> from an <see cref="RpcException"/>, preferring the ErrorInfo reason.</summary>
    /// <param name="exception">The gRPC exception to translate.</param>
    public static A2AException ToA2AException(RpcException exception)
    {
        var code = FromStatusCode(exception.StatusCode);

        if (TryReadReason(exception, out var reason)
            && s_reasonToErrorCode.TryGetValue(reason, out var mapped))
        {
            code = mapped;
        }

        return new A2AException(exception.Status.Detail, exception, code);
    }

    private static bool TryReadReason(RpcException exception, out string reason)
    {
        reason = string.Empty;

        var entry = exception.Trailers.Get(StatusDetailsTrailer);
        if (entry is null)
        {
            return false;
        }

        RpcStatus status;
        try
        {
            status = RpcStatus.Parser.ParseFrom(entry.ValueBytes);
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }

        foreach (var detail in status.Details)
        {
            if (detail.Is(ErrorInfo.Descriptor))
            {
                reason = detail.Unpack<ErrorInfo>().Reason;
                return !string.IsNullOrEmpty(reason);
            }
        }

        return false;
    }

    private static string DefaultReason(A2AErrorCode code) => code switch
    {
        A2AErrorCode.MethodNotFound => "METHOD_NOT_FOUND",
        A2AErrorCode.InvalidParams => "INVALID_PARAMS",
        A2AErrorCode.InvalidRequest => "INVALID_REQUEST",
        A2AErrorCode.ParseError => "PARSE_ERROR",
        _ => "INTERNAL_ERROR",
    };
}
