using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace A2A.AspNetCore.Tests;

public sealed class IndependentPushDeliveryTests : IDisposable
{
    private readonly InMemoryPushNotificationStore _store = new();
    private readonly Transport _transport = new();
    private readonly string _contextId = Guid.NewGuid().ToString("N");

    public void Dispose() => _transport.Dispose();

    [Fact]
    public async Task FullQueue_DoesNotEraseANewWebhookWithoutItsRequiredFirstAttempt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.BeforeResponse = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        var busy = await RegistrationAsync("busy");
        var other = await RegistrationAsync("other");
        await using var sender = CreateSender(_store, new() { QueueCapacity = 1 });
        await sender.EnqueueAsync(busy, Event("busy"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.EnqueueAsync(busy, Event("busy"));
        var terminal = sender.EnqueueAsync(other, Event("other", terminal: true));
        try
        {
            Assert.False(terminal.IsFaulted, "Queue pressure must not discard a different task's only configured notification.");
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await terminal;
            }
            catch (InvalidOperationException)
            {
                // Preserve the failing admission assertion above, then drain the owned worker.
            }
            await sender.StopAsync(CancellationToken.None);
        }
        Assert.Contains(_transport.Destinations, uri => uri.AbsolutePath == "/other");
    }

    [Fact]
    public async Task CompletedDelivery_DoesNotImplicitlyDeleteTheClientsConfiguration()
    {
        var registration = await RegistrationAsync("task");
        await using var sender = CreateSender(_store);
        await sender.EnqueueAsync(registration, Event("task", terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Fact]
    public async Task DeletionCompletedBeforeDispatch_PreventsANewRequestEvenWithAnOlderRead()
    {
        var gated = new GatedReadStore(_store);
        var registration = await RegistrationAsync("task");
        await using var sender = CreateSender(gated);
        await sender.EnqueueAsync(registration, Event("task"));
        await gated.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _store.DeleteAsync("task", "config");
        gated.Release.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Empty(_transport.Destinations);
    }

    private HttpPushNotificationSender CreateSender(IPushNotificationStore store, PushNotificationDeliveryOptions? options = null) =>
        new(store, new Validator(), new Factory(_transport), options ?? new PushNotificationDeliveryOptions(),
            NullLogger<HttpPushNotificationSender>.Instance);

    private Task<PushNotificationConfigSnapshot> RegistrationAsync(string taskId) =>
        _store.SaveAsync(new TaskPushNotificationConfig { TaskId = taskId, Id = "config", Url = $"https://callback.example/{taskId}" });

    private StreamResponse Event(string taskId, bool terminal = false) => new()
    {
        StatusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = _contextId,
            Status = new A2A.TaskStatus { State = terminal ? TaskState.Completed : TaskState.Working },
        },
    };

    private sealed class Validator : IPushNotificationUrlValidator
    {
        public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }

    private sealed class Factory(Transport transport) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(transport, disposeHandler: false);
    }

    private sealed class Transport : HttpMessageHandler
    {
        internal List<Uri> Destinations { get; } = [];
        internal Func<CancellationToken, Task>? BeforeResponse { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Destinations.Add(request.RequestUri!);
            if (BeforeResponse is not null)
            {
                await BeforeResponse(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class GatedReadStore(IPushNotificationStore inner) : IPushNotificationStore
    {
        private int _reads;
        internal TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(config, cancellationToken);
        public async Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default)
        {
            var captured = await inner.GetAsync(taskId, configId, cancellationToken);
            if (Interlocked.Increment(ref _reads) == 2)
            {
                Captured.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return captured;
        }
        public Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.GetAllAsync(taskId, cancellationToken);
        public Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) =>
            inner.ListAsync(request, cancellationToken);
        public Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(taskId, configId, version, cancellationToken);
    }
}
