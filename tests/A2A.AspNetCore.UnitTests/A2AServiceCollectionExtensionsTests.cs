using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace A2A.AspNetCore.Tests;

public class A2AServiceCollectionExtensionsTests
{
    private sealed class TestAgentHandler : IAgentHandler
    {
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            eventQueue.Complete();
            return Task.CompletedTask;
        }

        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            eventQueue.Complete();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddA2AAgent_ConfiguresExplicitRequestBodySizeLimit()
    {
        // Arrange — the SDK must not rely on the host's framework default
        var services = new ServiceCollection();

        // Act
        services.AddA2AAgent<TestAgentHandler>(new AgentCard { Name = "test", Description = "test agent" });

        // Assert — Kestrel is explicitly configured to the 10 MB SDK limit
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.Equal(10 * 1024 * 1024, options.Limits.MaxRequestBodySize);
    }
}
