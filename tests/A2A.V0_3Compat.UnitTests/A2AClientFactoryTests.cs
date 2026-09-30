namespace A2A.V0_3Compat.UnitTests;

public class V03CompatClientFactoryTests
{
    [Fact]
    public void Create_WithUrl_ReturnsV03Adapter()
    {
        var client = V03CompatClientFactory.Create(new Uri("http://localhost/a2a"));

        Assert.IsAssignableFrom<IA2AClient>(client);
        Assert.IsNotType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithJson_UsesUrlFromCard()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "url": "http://agent-host/a2a",
            "protocolVersion": "0.3"
        }
        """;

        var client = V03CompatClientFactory.Create(cardJson, new Uri("http://fallback-host"));

        Assert.IsAssignableFrom<IA2AClient>(client);
        Assert.IsNotType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithJson_NoUrlInCard_UsesBaseUrl()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "protocolVersion": "0.3"
        }
        """;

        var client = V03CompatClientFactory.Create(cardJson, new Uri("http://fallback-host/a2a"));

        Assert.IsAssignableFrom<IA2AClient>(client);
    }

    [Fact]
    public async Task InvokeAsync_CustomOperationIsNotSupported()
    {
        var operation = new A2AOperationCatalogBuilder()
            .DefineUnary<GetTaskRequest, AgentTask>(
                new A2AOperationId("test.custom"));
        var client = CreateClient();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.InvokeAsync(
                operation,
                new GetTaskRequest { Id = "task-1" }));

        Assert.Contains("custom operations", exception.Message);
    }

    [Fact]
    public async Task InvokeStreamingAsync_CustomOperationFailsDuringEnumeration()
    {
        var operation = new A2AOperationCatalogBuilder()
            .DefineStreaming<SubscribeToTaskRequest, StreamResponse>(
                new A2AOperationId("test.custom-stream"));
        var client = CreateClient();
        var events = client.InvokeStreamingAsync(
            operation,
            new SubscribeToTaskRequest { Id = "task-1" });

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () =>
            {
                await foreach (var _ in events)
                {
                }
            });

        Assert.Contains("custom operations", exception.Message);
    }

    private static IA2AClient CreateClient() =>
        V03CompatClientFactory.Create(
            new Uri("http://localhost/a2a"));
}
