namespace A2A.Grpc.UnitTests;

using A2A;
using A2A.Grpc;
using global::Google.Protobuf;
using global::Google.Protobuf.WellKnownTypes;
using global::Google.Rpc;
using global::Grpc.Core;
using GrpcStatus = global::Grpc.Core.Status;
using RpcStatus = global::Google.Rpc.Status;

/// <summary>Regression tests for issues raised in code review.</summary>
public class ReviewRegressionTests
{
    [Fact]
    public void ListPushConfigResponse_NullNextPageToken_MapsToEmptyString()
    {
        var result = ProtoMap.ToProto(new ListTaskPushNotificationConfigsResponse { NextPageToken = null });

        Assert.Equal(string.Empty, result.NextPageToken);
    }

    [Fact]
    public void SendMessageRequest_MissingMessage_ThrowsInvalidParams()
    {
        var exception = Assert.Throws<A2AException>(() => ProtoMap.ToDomain(new Protos.SendMessageRequest()));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public void ToA2AException_ForeignErrorInfoDomain_IsIgnored()
    {
        // A non-A2A ErrorInfo must not be trusted even when its reason collides with an A2A reason.
        var rpcException = BuildRpcException(StatusCode.FailedPrecondition, "TASK_NOT_FOUND", "example.com");

        var result = GrpcErrorMapping.ToA2AException(rpcException);

        Assert.Equal(A2AErrorCode.TaskNotCancelable, result.ErrorCode); // status-code fallback, not the foreign reason
    }

    [Fact]
    public void ToA2AException_A2AErrorInfoDomain_IsHonored()
    {
        var rpcException = BuildRpcException(StatusCode.FailedPrecondition, "PUSH_NOTIFICATION_NOT_SUPPORTED", "a2a-protocol.org");

        var result = GrpcErrorMapping.ToA2AException(rpcException);

        Assert.Equal(A2AErrorCode.PushNotificationNotSupported, result.ErrorCode);
    }

    [Fact]
    public void ToClientException_LocalCancellation_PreservesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var rpcException = new RpcException(new GrpcStatus(StatusCode.Cancelled, "Cancelled"));

        var result = GrpcErrorMapping.ToClientException(rpcException, cts.Token);

        Assert.IsType<OperationCanceledException>(result);
    }

    [Fact]
    public void ToClientException_ServerCancellation_MapsToA2AException()
    {
        var rpcException = new RpcException(new GrpcStatus(StatusCode.Cancelled, "Cancelled"));

        var result = GrpcErrorMapping.ToClientException(rpcException, CancellationToken.None);

        Assert.IsType<A2AException>(result);
    }

    private static RpcException BuildRpcException(StatusCode statusCode, string reason, string domain)
    {
        var richStatus = new RpcStatus { Code = (int)statusCode, Message = "failed" };
        richStatus.Details.Add(Any.Pack(new ErrorInfo { Reason = reason, Domain = domain }));

        var trailers = new Metadata { { "grpc-status-details-bin", richStatus.ToByteArray() } };
        return new RpcException(new GrpcStatus(statusCode, "failed"), trailers);
    }
}
