using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace A2A.V0_3Compat.UnitTests;

public class A2AClientVersionHeaderTests
{
    [Fact]
    public async Task SendMessageAsync_SendsVersionHeader()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;

        var responseResult = new A2A.SendMessageResponse
        {
            Message = new A2A.Message { MessageId = "id-1", Role = A2A.Role.User, Parts = [] },
        };
        var jsonResponse = new A2A.JsonRpcResponse
        {
            Id = new A2A.JsonRpcId("test-id"),
            Result = JsonSerializer.SerializeToNode(responseResult, A2A.A2AJsonUtilities.DefaultOptions),
        };
        var responseContent = JsonSerializer.Serialize(jsonResponse, A2A.A2AJsonUtilities.DefaultOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json"),
        };

        var handler = new MockHttpMessageHandler(response, req => capturedRequest = req);
        var httpClient = new HttpClient(handler);
        var client = new A2A.A2AClient(new Uri("http://localhost"), httpClient);

        // Act
        await client.SendMessageAsync(new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                MessageId = "m1",
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText("hi")],
            },
        });

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.Contains("A2A-Version"));
        Assert.Equal("1.0", capturedRequest.Headers.GetValues("A2A-Version").Single());
    }

    [Fact]
    public async Task SendStreamingMessageAsync_SendsVersionHeader()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;

        var streamResult = new A2A.StreamResponse
        {
            Message = new A2A.Message { MessageId = "id-1", Role = A2A.Role.Agent, Parts = [A2A.Part.FromText("hello")] },
        };
        var jsonResponse = new A2A.JsonRpcResponse
        {
            Id = new A2A.JsonRpcId("test-id"),
            Result = JsonSerializer.SerializeToNode(streamResult, A2A.A2AJsonUtilities.DefaultOptions),
        };
        var rpcJson = JsonSerializer.Serialize(jsonResponse, A2A.A2AJsonUtilities.DefaultOptions);
        var ssePayload = $"event: message\ndata: {rpcJson}\n\n";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream"),
        };

        var handler = new MockHttpMessageHandler(response, req => capturedRequest = req);
        var httpClient = new HttpClient(handler);
        var client = new A2A.A2AClient(new Uri("http://localhost"), httpClient);

        // Act — consume the stream
        await foreach (var _ in client.SendStreamingMessageAsync(new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                MessageId = "m2",
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText("stream me")],
            },
        }))
        {
            // drain
        }

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.Contains("A2A-Version"));
        Assert.Equal("1.0", capturedRequest.Headers.GetValues("A2A-Version").Single());
    }
}
