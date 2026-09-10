using Microsoft.Extensions.DependencyInjection;

namespace A2A.AspNetCore.Tests;

public class A2AServiceCollectionExtensionsTests
{
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
