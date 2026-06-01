using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AErrorResultTests
{
    private static async Task<(int statusCode, JsonDocument body)> ExecuteResultAsync(A2AErrorResult result)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        var doc = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, doc);
    }

    [Fact]
    public async Task TaskNotFound_ReturnsGoogleRpcStatusWithErrorInfo()
    {
        var result = new A2AErrorResult(new A2AException("Task not found", A2AErrorCode.TaskNotFound));

        var (statusCode, doc) = await ExecuteResultAsync(result);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(404, statusCode);
        Assert.Equal(404, error.GetProperty("code").GetInt32());
        Assert.Equal("NOT_FOUND", error.GetProperty("status").GetString());
        Assert.Equal("Task not found", error.GetProperty("message").GetString());

        var details = error.GetProperty("details");
        Assert.Equal(JsonValueKind.Array, details.ValueKind);
        Assert.Equal(1, details.GetArrayLength());

        var detail = details[0];
        Assert.Equal("type.googleapis.com/google.rpc.ErrorInfo", detail.GetProperty("@type").GetString());
        Assert.Equal("TASK_NOT_FOUND", detail.GetProperty("reason").GetString());
        Assert.Equal("a2a-protocol.org", detail.GetProperty("domain").GetString());
    }

    [Fact]
    public async Task ContentTypeNotSupported_Returns400()
    {
        var result = new A2AErrorResult(new A2AException("Unsupported content type", A2AErrorCode.ContentTypeNotSupported));

        var (statusCode, doc) = await ExecuteResultAsync(result);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(400, statusCode);
        Assert.Equal(400, error.GetProperty("code").GetInt32());
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("status").GetString());
        Assert.Equal("CONTENT_TYPE_NOT_SUPPORTED", error.GetProperty("details")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task InternalError_ReturnsEmptyDetailsArray()
    {
        var result = new A2AErrorResult(new A2AException("Something broke", A2AErrorCode.InternalError));

        var (statusCode, doc) = await ExecuteResultAsync(result);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(500, statusCode);
        Assert.Equal(500, error.GetProperty("code").GetInt32());
        Assert.Equal("INTERNAL", error.GetProperty("status").GetString());
        Assert.Equal("Something broke", error.GetProperty("message").GetString());
        Assert.Equal(0, error.GetProperty("details").GetArrayLength());
    }

    [Fact]
    public async Task ParseError_ReturnsEmptyDetailsArray()
    {
        var result = new A2AErrorResult(new A2AException("Parse failed", A2AErrorCode.ParseError));

        var (statusCode, doc) = await ExecuteResultAsync(result);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(400, statusCode);
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("status").GetString());
        Assert.Equal(0, error.GetProperty("details").GetArrayLength());
    }

    [Theory]
    [InlineData(A2AErrorCode.TaskNotCancelable, "TASK_NOT_CANCELABLE")]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, "PUSH_NOTIFICATION_NOT_SUPPORTED")]
    [InlineData(A2AErrorCode.UnsupportedOperation, "UNSUPPORTED_OPERATION")]
    [InlineData(A2AErrorCode.InvalidAgentResponse, "INVALID_AGENT_RESPONSE")]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, "EXTENDED_AGENT_CARD_NOT_CONFIGURED")]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, "EXTENSION_SUPPORT_REQUIRED")]
    [InlineData(A2AErrorCode.VersionNotSupported, "VERSION_NOT_SUPPORTED")]
    public async Task A2ASpecificErrors_IncludeErrorInfoInDetails(A2AErrorCode errorCode, string expectedReason)
    {
        var result = new A2AErrorResult(new A2AException("test", errorCode));

        var (_, doc) = await ExecuteResultAsync(result);
        var details = doc.RootElement.GetProperty("error").GetProperty("details");

        Assert.Equal(1, details.GetArrayLength());
        Assert.Equal("type.googleapis.com/google.rpc.ErrorInfo", details[0].GetProperty("@type").GetString());
        Assert.Equal(expectedReason, details[0].GetProperty("reason").GetString());
        Assert.Equal("a2a-protocol.org", details[0].GetProperty("domain").GetString());
    }

    [Fact]
    public async Task ResponseContentType_IsApplicationJson()
    {
        var result = new A2AErrorResult(new A2AException("test", A2AErrorCode.InternalError));

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        Assert.Equal("application/json", httpContext.Response.ContentType);
    }

    [Fact]
    public void StatusCode_MatchesMappedHttpStatus()
    {
        var result = new A2AErrorResult(new A2AException("test", A2AErrorCode.TaskNotFound));
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);

        var result2 = new A2AErrorResult(new A2AException("test", A2AErrorCode.InternalError));
        Assert.Equal(500, ((IStatusCodeHttpResult)result2).StatusCode);
    }
}
