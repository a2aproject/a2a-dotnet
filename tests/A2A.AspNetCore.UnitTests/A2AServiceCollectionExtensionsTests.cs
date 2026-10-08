using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace A2A.AspNetCore.Tests;

public class A2AServiceCollectionExtensionsTests
{
    [Fact]
    public void AddA2AAgent_ConfiguresExplicitRequestBodySizeLimit()
    {
        var services = new ServiceCollection();

        services.AddA2AAgent<TestAgentHandler>(
            new AgentCard { Name = "test", Description = "test agent" });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.Equal(10 * 1024 * 1024, options.Limits.MaxRequestBodySize);
    }

    [Theory]
    [InlineData(null, A2AErrorCode.UnsupportedOperation)]
    [InlineData(false, A2AErrorCode.UnsupportedOperation)]
    [InlineData(true, A2AErrorCode.ExtendedAgentCardNotConfigured)]
    public async Task AddA2AAgent_SynchronizesExtendedCardCapability(
        bool? extendedAgentCard, A2AErrorCode expectedErrorCode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AAgent<TestAgentHandler>(
            new AgentCard
            {
                Capabilities = new AgentCapabilities { ExtendedAgentCard = extendedAgentCard },
            },
            options => options.SupportsExtendedAgentCard = extendedAgentCard != true);

        await using var serviceProvider = services.BuildServiceProvider();
        var server = serviceProvider.GetRequiredService<IA2ARequestHandler>();

        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            server.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest()));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task AddA2AAgent_SynchronizesStreamingAndInputModeCapabilities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AAgent<TestAgentHandler>(new AgentCard
        {
            Capabilities = new AgentCapabilities { Streaming = false },
            DefaultInputModes = ["text/plain"],
            Skills =
            [
                new AgentSkill
                {
                    Id = "image",
                    Name = "Image",
                    Description = "Accepts images.",
                    Tags = ["image"],
                    InputModes = ["image/png"],
                },
            ],
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<A2AServerOptions>();

        Assert.False(options.SupportsStreaming);
        Assert.Equal(["text/plain", "image/png"], options.SupportedInputModes);
    }

    private sealed class TestAgentHandler : IAgentHandler
    {
        public Task ExecuteAsync(
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CancelAsync(
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
