using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.V0_3.UnitTests.Client;

public class JsonRpcResponseIdTests
{
    [Fact]
    public async Task GetTaskAsync_RejectsMismatchedResponseId()
    {
        string? sentId = null;
        var rpc = new JsonRpcResponse { Id = "other", Result = JsonSerializer.SerializeToNode(new AgentTask { Id = "t" }) };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(rpc), Encoding.UTF8, "application/json") };
        using var http = new HttpClient(new MockHttpMessageHandler(response, req => { using var requestDocument = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); sentId = requestDocument.RootElement.GetProperty("id").GetString(); }));
        var client = new A2AClient(new Uri("http://localhost"), http);
        await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync("t"));
        Assert.NotEqual("other", sentId);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("number")]
    public async Task GetTaskAsync_RejectsInvalidSuccessId(string idKind)
    {
        string idField = idKind switch { "missing" => "", "null" => "\"id\":null,", _ => "\"id\":1," };
        var body = "{\"jsonrpc\":\"2.0\"," + idField + "\"result\":{\"id\":\"t\"}}";
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var http = new HttpClient(new MockHttpMessageHandler(response));
        var client = new A2AClient(new Uri("http://localhost"), http);
        var error = await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync("t"));
        Assert.Equal(A2AErrorCode.InternalError, error.ErrorCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Streaming_ErrorIdIsValidated(bool sse, bool nullId)
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":" + (nullId ? "null" : "\"wrong\"") + ",\"error\":{\"code\":-32001,\"message\":\"missing\"}}";
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse ? $"data: {body}\n\n" : body, Encoding.UTF8, sse ? "text/event-stream" : "application/json") };
        using var http = new HttpClient(new MockHttpMessageHandler(response));
        var client = new A2AClient(new Uri("http://localhost"), http);
        var error = await Assert.ThrowsAsync<A2AException>(async () => { await foreach (var _ in client.SendMessageStreamingAsync(new MessageSendParams())) { } });
        Assert.Equal(nullId ? A2AErrorCode.TaskNotFound : A2AErrorCode.InternalError, error.ErrorCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("null")]
    public async Task GetTaskAsync_ErrorIdIsValidated(string kind)
    {
        string idField = kind switch { "missing" => "", "null" => "\"id\":null,", _ => "\"id\":\"wrong\"," };
        var body = "{\"jsonrpc\":\"2.0\"," + idField + "\"error\":{\"code\":-32001,\"message\":\"missing\"}}";
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var http = new HttpClient(new MockHttpMessageHandler(response));
        var client = new A2AClient(new Uri("http://localhost"), http);
        var error = await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync("t"));
        Assert.Equal(kind == "null" ? A2AErrorCode.TaskNotFound : A2AErrorCode.InternalError, error.ErrorCode);
    }

    [Fact]
    public async Task Streaming_MismatchedSuccessIdIsRejectedBeforeResult()
    {
        var body = "data: {\"jsonrpc\":\"2.0\",\"id\":\"wrong\",\"result\":{}}\n\n";
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        var responseStream = await response.Content.ReadAsStreamAsync();
        using var http = new HttpClient(new MockHttpMessageHandler(response));
        var client = new A2AClient(new Uri("http://localhost"), http);
        var error = await Assert.ThrowsAsync<A2AException>(async () => { await foreach (var _ in client.SendMessageStreamingAsync(new MessageSendParams())) { } });
        Assert.Equal(A2AErrorCode.InternalError, error.ErrorCode);
        Assert.False(responseStream.CanRead);
    }

}
