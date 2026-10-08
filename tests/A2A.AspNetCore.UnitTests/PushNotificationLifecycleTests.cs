using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Net;

namespace A2A.AspNetCore.Tests;

public sealed class PushNotificationLifecycleTests
{
    private readonly AgentCard _card = new()
    {
        Capabilities = new AgentCapabilities { PushNotifications = true, Streaming = true },
    };
    private readonly string _messageId = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task RejectedBackgroundAdmission_CancelsAndJoinsTheProducer()
    {
        var tasks = new InMemoryTaskStore();
        var configs = new InMemoryPushNotificationStore();
        var agent = new BurstAgent();
        var logger = new LifecycleLogger();
        using var transport = new HoldingTransport();
        await using var sender = new HttpPushNotificationSender(configs, new LocalPolicy(), new ClientFactory(transport),
            new PushNotificationDeliveryOptions
            {
                QueueCapacity = 1,
                QueueFullBehavior = PushNotificationQueueFullBehavior.Reject,
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(30),
            }, NullLogger<HttpPushNotificationSender>.Instance);
        var gatedStore = new BlockedWriterTaskStore(tasks, agent.WriteBlocked.Task);
        await using var server = new A2AServer(agent, gatedStore, new ChannelEventNotifier(), logger, null,
            _card, configs, new LocalPolicy(), sender);
        try
        {
            var request = Request();
            request.Configuration!.ReturnImmediately = true;
            var result = await server.SendMessageAsync(request);
            var cancellationSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = agent.ExecutionToken.Register(() => cancellationSeen.TrySetResult());
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.Release.TrySetResult();
            await logger.AdmissionFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(agent.ExecutionToken.IsCancellationRequested);
            Assert.True(agent.WriteBlocked.Task.IsCompletedSuccessfully);
            Assert.InRange(agent.CompletedWrites, 16, 63);
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TaskState.Working, (await tasks.GetTaskAsync(result.Task!.Id))!.Status.State);
            Assert.Single(await configs.GetAllAsync(result.Task.Id));
        }
        finally
        {
            agent.Release.TrySetResult();
            await agent.ManualCleanup.CancelAsync();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ManualCleanup.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedStop_JoinsActiveAndDetachedStreamCleanup(bool detached)
    {
        var sender = new SelectedSender();
        var agent = new CleanupAgent();
        var services = Services(agent, sender);
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var server = provider.GetRequiredService<IA2ARequestHandler>();
        Assert.Same(sender, provider.GetRequiredService<IPushNotificationSender>());
        await hosted.StartAsync(CancellationToken.None);
        await using var stream = server.SendStreamingMessageAsync(Request()).GetAsyncEnumerator();
        Task? stopping = null;
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var taskId = stream.Current.Task!.Id;
            if (detached)
            {
                await stream.DisposeAsync();
            }
            stopping = hosted.StopAsync(CancellationToken.None);
            await agent.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stopping.IsCompleted, "Stop must join the producer, including its cooperative cleanup.");
            Assert.False(sender.Stopped);
            agent.ReleaseCleanup.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(agent.Finished.Task.IsCompleted);
            Assert.True(sender.Stopped);
            Assert.Equal(0, sender.LateEnqueues);
            Assert.Equal(66, sender.Events.Count); // Task, 64 cleanup updates, final cancellation.
            Assert.Equal(TaskState.Canceled,
                (await provider.GetRequiredService<ITaskStore>().GetTaskAsync(taskId))!.Status.State);
            if (!detached)
            {
                while (await stream.MoveNextAsync())
                {
                    Assert.NotEqual(StreamResponseCase.None, stream.Current.PayloadCase);
                }
            }
            Assert.Equal(0, sender.LateEnqueues);
        }
        finally
        {
            agent.ReleaseCleanup.TrySetResult();
            await agent.ManualCleanup.CancelAsync();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (stopping is not null)
            {
                await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            }
            agent.ManualCleanup.Dispose();
        }
    }

    [Fact]
    public async Task HostedStop_JoinsPublicationAfterTheProducerHasFinished()
    {
        var sender = new SelectedSender { HoldFinalPublication = true };
        var agent = new CleanupAgent();
        var services = Services(agent, sender);
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var server = provider.GetRequiredService<IA2ARequestHandler>();
        await hosted.StartAsync(CancellationToken.None);
        await using var stream = server.SendStreamingMessageAsync(Request()).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var stopping = hosted.StopAsync(CancellationToken.None);
            await agent.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ReleaseCleanup.TrySetResult();
            await sender.PublicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stopping.IsCompleted);
            Assert.False(sender.Stopped);
            sender.ReleasePublication.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(sender.Stopped);
            Assert.Equal(0, sender.LateEnqueues);
            Assert.Equal(66, sender.Events.Count);
        }
        finally
        {
            sender.ReleasePublication.TrySetResult();
            agent.ReleaseCleanup.TrySetResult();
            await agent.ManualCleanup.CancelAsync();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ManualCleanup.Dispose();
        }
    }

    [Fact]
    public async Task HostedStop_DeadlineCancelsPendingAdmissionWithoutLateEnqueue()
    {
        var sender = new SelectedSender { HoldFinalPublication = true };
        var agent = new CleanupAgent();
        var services = Services(agent, sender);
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var server = provider.GetRequiredService<IA2ARequestHandler>();
        await hosted.StartAsync(CancellationToken.None);
        await using var stream = server.SendStreamingMessageAsync(Request()).GetAsyncEnumerator();
        using var deadline = new CancellationTokenSource();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var stopping = hosted.StopAsync(deadline.Token);
            await agent.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ReleaseCleanup.TrySetResult();
            await sender.PublicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
            await Assert.IsType<A2AServer>(server).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(sender.Stopped);
            Assert.Equal(0, sender.LateEnqueues);
            Assert.Equal(65, sender.Events.Count);
        }
        finally
        {
            sender.ReleasePublication.TrySetResult();
            agent.ReleaseCleanup.TrySetResult();
            await agent.ManualCleanup.CancelAsync();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ManualCleanup.Dispose();
        }
    }

    [Fact]
    public async Task HostedStop_DeadlineIsNotReportedAsProducerQuiescence()
    {
        var sender = new SelectedSender();
        var agent = new CleanupAgent();
        var logger = new LifecycleLogger();
        var services = Services(agent, sender);
        services.AddSingleton<ILogger<A2AServer>>(logger);
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var server = provider.GetRequiredService<IA2ARequestHandler>();
        await hosted.StartAsync(CancellationToken.None);
        await using var stream = server.SendStreamingMessageAsync(Request()).GetAsyncEnumerator();
        using var deadline = new CancellationTokenSource();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var stopping = hosted.StopAsync(deadline.Token);
            await agent.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
            Assert.False(agent.Finished.Task.IsCompleted);
            Assert.True(sender.Stopped);
            Assert.True(sender.StopToken.IsCancellationRequested);

            agent.ReleaseCleanup.TrySetResult();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.IsType<A2AServer>(server).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, sender.LateEnqueues);
            Assert.Contains(logger.Messages, message => message.Contains("abandon", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            agent.ReleaseCleanup.TrySetResult();
            await agent.ManualCleanup.CancelAsync();
            await agent.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.ManualCleanup.Dispose();
        }
    }

    private ServiceCollection Services(CleanupAgent agent, SelectedSender sender)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddSingleton<IPushNotificationUrlValidator>(new LocalPolicy());
        services.AddA2AAgent<CleanupAgent>(_card);
        services.AddSingleton<IAgentHandler>(agent);
        return services;
    }

    private SendMessageRequest Request() => new()
    {
        Message = new Message { MessageId = _messageId, Role = Role.User, Parts = [Part.FromText("synthetic")] },
        Configuration = new SendMessageConfiguration
        {
            TaskPushNotificationConfig = new TaskPushNotificationConfig
            {
                Id = "config",
                Url = "https://owned.invalid/callback",
            },
        },
    };

    private sealed class BurstAgent : IAgentHandler
    {
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource WriteBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource ManualCleanup { get; } = new();
        internal CancellationToken ExecutionToken { get; private set; }
        internal int CompletedWrites { get; private set; }

        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            ExecutionToken = cancellationToken;
            using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ManualCleanup.Token);
            try
            {
                var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
                await updater.SubmitAsync(cancellationToken: cleanup.Token);
                await Release.Task.WaitAsync(cleanup.Token);
                for (int i = 0; i < 64; i++)
                {
                    var write = updater.StartWorkAsync(cancellationToken: cleanup.Token);
                    if (!write.IsCompletedSuccessfully)
                    {
                        WriteBlocked.TrySetResult();
                    }
                    await write;
                    CompletedWrites++;
                }
            }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
            {
            }
            finally
            {
                Finished.TrySetResult();
            }
        }
    }

    public sealed class CleanupAgent : IAgentHandler
    {
        internal TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource ManualCleanup { get; } = new();

        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            try
            {
                var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
                await updater.SubmitAsync(cancellationToken: cancellationToken);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CancellationSeen.TrySetResult();
                    await ReleaseCleanup.Task.WaitAsync(ManualCleanup.Token);
                    for (int i = 0; i < 64; i++)
                    {
                        await updater.StartWorkAsync(cancellationToken: ManualCleanup.Token);
                    }
                    await updater.CancelAsync(cancellationToken: ManualCleanup.Token);
                }
            }
            catch (OperationCanceledException) when (ManualCleanup.IsCancellationRequested)
            {
            }
            finally
            {
                Finished.TrySetResult();
            }
        }
    }

    private sealed class LocalPolicy : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
        }
    }

    private sealed class SelectedSender : IPushNotificationSender, IHostedService
    {
        internal bool HoldFinalPublication { get; init; }
        internal TaskCompletionSource PublicationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleasePublication { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<StreamResponse> Events { get; } = new();
        internal bool Stopped { get; private set; }
        internal int LateEnqueues { get; private set; }
        internal CancellationToken StopToken { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopToken = cancellationToken;
            Stopped = true;
            return Task.CompletedTask;
        }
        public async Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopped)
            {
                LateEnqueues++;
                throw new InvalidOperationException("Selected sender is stopped.");
            }
            if (HoldFinalPublication &&
                (response.StatusUpdate?.Status.State).GetValueOrDefault() == TaskState.Canceled)
            {
                PublicationEntered.TrySetResult();
                await ReleasePublication.Task.WaitAsync(cancellationToken);
            }
            Events.Enqueue(response);
        }
    }

    private sealed class BlockedWriterTaskStore(InMemoryTaskStore inner, Task writeBlocked) : ITaskStore
    {
        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.GetTaskAsync(taskId, cancellationToken);
        public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
        {
            if (task.Status.State == TaskState.Working)
            {
                await writeBlocked.WaitAsync(cancellationToken);
            }
            await inner.SaveTaskAsync(taskId, task, cancellationToken);
        }
        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.DeleteTaskAsync(taskId, cancellationToken);
        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            inner.ListTasksAsync(request, cancellationToken);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class HoldingTransport : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class LifecycleLogger : ILogger<A2AServer>
    {
        internal TaskCompletionSource AdmissionFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<string> Messages { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception));
            if (exception is PushNotificationAdmissionException)
            {
                AdmissionFailed.TrySetResult();
            }
        }
    }
}
