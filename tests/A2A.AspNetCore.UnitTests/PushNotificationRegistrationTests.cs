using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace A2A.AspNetCore.Tests;

public sealed class PushNotificationRegistrationTests
{
    private readonly AgentCard _card = new() { Capabilities = new AgentCapabilities { PushNotifications = true } };

    [Fact]
    public async Task Defaults_AreSingletons_AndConfigurationDoesNotEnableCapability()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _card.Capabilities.PushNotifications = false;
        services.AddA2AAgent<Agent>(_card);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IPushNotificationStore>();
        Assert.IsType<InMemoryPushNotificationStore>(store);
        Assert.Same(store, provider.GetRequiredService<IPushNotificationStore>());
        Assert.IsType<DefaultPushNotificationUrlValidator>(provider.GetRequiredService<IPushNotificationUrlValidator>());
        Assert.IsType<HttpPushNotificationSender>(provider.GetRequiredService<IPushNotificationSender>());
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            provider.GetRequiredService<IA2ARequestHandler>().CreateTaskPushNotificationConfigAsync(new()));
        Assert.Equal(A2AErrorCode.PushNotificationNotSupported, exception.ErrorCode);
    }

    [Fact]
    public async Task PreRegisteredServices_Win_AndHostedLifecycleIsForwardedToCustomSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var store = new InMemoryPushNotificationStore();
        var validator = new CustomValidator();
        var sender = new CustomSender();
        services.AddSingleton<IPushNotificationStore>(store);
        services.AddSingleton<IPushNotificationUrlValidator>(validator);
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddA2AAgent<Agent>(_card);
        await using var provider = services.BuildServiceProvider();
        Assert.Same(store, provider.GetRequiredService<IPushNotificationStore>());
        Assert.Same(validator, provider.GetRequiredService<IPushNotificationUrlValidator>());
        Assert.Same(sender, provider.GetRequiredService<IPushNotificationSender>());
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);
        Assert.Equal(1, sender.Starts);
        Assert.Equal(1, sender.Stops);
    }

    public sealed class Agent : IAgentHandler
    {
        public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CustomValidator : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }

    private sealed class CustomSender : IPushNotificationSender, IHostedService
    {
        internal int Starts { get; private set; }
        internal int Stops { get; private set; }
        public Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) { Starts++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { Stops++; return Task.CompletedTask; }
    }
}
