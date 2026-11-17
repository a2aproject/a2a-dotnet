using System.Net;
using System.Text;
using System.Text.Json;

using V03 = A2A.V0_3;

namespace A2A.V0_3Compat.UnitTests;

public class V03ClientAdapterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    public async Task GetTaskAsync_ForwardsHistoryLength(int? historyLength)
    {
        HttpRequestMessage? capturedRequest = null;
        var response = new V03.JsonRpcResponse
        {
            Id = new V03.JsonRpcId("request-1"),
            Result = JsonSerializer.SerializeToNode(new V03.AgentTask
            {
                Id = "task-1",
                ContextId = "context-1",
                Status = new V03.AgentTaskStatus { State = V03.TaskState.Working },
            }, V03.A2AJsonUtilities.DefaultOptions),
        };
        using var client = new HttpClient(new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, V03.A2AJsonUtilities.DefaultOptions), Encoding.UTF8, "application/json"),
            },
            request => capturedRequest = request));
        using var adapter = new V03ClientAdapter(new V03.A2AClient(new Uri("http://localhost"), client));

        var task = await adapter.GetTaskAsync(new A2A.GetTaskRequest { Id = "task-1", HistoryLength = historyLength });

        Assert.Equal("task-1", task.Id);
        Assert.NotNull(capturedRequest);
        using var document = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("tasks/get", document.RootElement.GetProperty("method").GetString());
        var parameters = document.RootElement.GetProperty("params");
        Assert.Equal("task-1", parameters.GetProperty("id").GetString());
        if (historyLength is null)
        {
            Assert.False(parameters.TryGetProperty("historyLength", out _));
        }
        else
        {
            Assert.Equal(historyLength, parameters.GetProperty("historyLength").GetInt32());
        }
    }

    [Fact]
    public async Task GetTaskAsync_TranslatesV03ExceptionToV1Exception()
    {
        using var client = CreateClient(
            new V03.A2AException("Task is missing.", V03.A2AErrorCode.TaskNotFound));
        var adapter = new V03ClientAdapter(new V03.A2AClient(new Uri("http://localhost"), client));

        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            adapter.GetTaskAsync(new A2A.GetTaskRequest { Id = "task-1" }));

        Assert.Equal("Task is missing.", exception.Message);
        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
        Assert.IsNotType<V03.A2AException>(exception);
    }

    [Fact]
    public async Task SendStreamingMessageAsync_TranslatesV03ExceptionToV1Exception()
    {
        using var client = CreateClient(
            new V03.A2AException("Streaming is unsupported.", V03.A2AErrorCode.UnsupportedOperation),
            streaming: true);
        var adapter = new V03ClientAdapter(new V03.A2AClient(new Uri("http://localhost"), client));

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var _ in adapter.SendStreamingMessageAsync(CreateRequest()))
            {
            }
        });

        Assert.Equal("Streaming is unsupported.", exception.Message);
        Assert.Equal(A2AErrorCode.UnsupportedOperation, exception.ErrorCode);
        Assert.IsNotType<V03.A2AException>(exception);
    }

    private static A2A.SendMessageRequest CreateRequest() => new()
    {
        Message = new A2A.Message
        {
            MessageId = "message-1",
            Role = A2A.Role.User,
            Parts = [A2A.Part.FromText("hello")],
        },
    };

    private static HttpClient CreateClient(V03.A2AException exception, bool streaming = false)
    {
        var response = new V03.JsonRpcResponse
        {
            Id = new V03.JsonRpcId("request-1"),
            Error = new V03.JsonRpcError
            {
                Code = (int)exception.ErrorCode,
                Message = exception.Message,
            },
        };
        var json = JsonSerializer.Serialize(response, V03.A2AJsonUtilities.DefaultOptions);
        var content = streaming ? $"event: message\ndata: {json}\n\n" : json;
        var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                streaming ? "text/event-stream" : "application/json"),
        };
        return new HttpClient(new MockHttpMessageHandler(message));
    }
}
