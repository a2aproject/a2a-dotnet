using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public sealed class PushNotificationSenderTests : IDisposable
{
    private readonly InMemoryPushNotificationStore _store = new();
    private readonly ProbeHandler _transport = new();
    private readonly Validator _validator = new();
    private readonly CaptureLogger _logger = new();
    private readonly string _credential = Guid.NewGuid().ToString("N");
    private readonly string _contextId = Guid.NewGuid().ToString("N");

    public void Dispose() => _transport.Dispose();

    [Theory]
    [InlineData(200, 1)]
    [InlineData(204, 1)]
    [InlineData(400, 1)]
    [InlineData(401, 1)]
    [InlineData(403, 1)]
    [InlineData(404, 1)]
    [InlineData(302, 1)]
    [InlineData(408, 3)]
    [InlineData(429, 3)]
    [InlineData(500, 3)]
    [InlineData(503, 3)]
    [InlineData(599, 3)]
    [InlineData(600, 1)]
    public async Task StatusPolicy_IsBoundedWithoutChangingRegistrationRetention(int status, int expected)
    {
        _transport.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, Event(terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(expected, _transport.Requests.Count);
        Assert.Equal(expected, _validator.Calls);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkAndTimeoutFailures_RetryWithoutLeakingExceptionText(bool timeout)
    {
        _transport.Respond = async (_, cancellationToken) =>
        {
            if (timeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            throw new HttpRequestException("secret exception " + _credential);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender(new() { RequestTimeout = TimeSpan.FromMilliseconds(20), InitialRetryDelay = TimeSpan.Zero });
        await sender.EnqueueAsync(registration, Event(terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(3, _transport.Requests.Count);
        Assert.NotNull(await _store.GetAsync("task", "config"));
        Assert.DoesNotContain(_credential, string.Join('\n', _logger.Messages), StringComparison.Ordinal);
        Assert.Contains(_logger.Messages, entry => entry.Contains("failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X-A2A-Notification-Token")]
    [InlineData("X-Custom-Notification")]
    public async Task PayloadAndAuthentication_UseV1AndConfigurableLegacyHeader(string? header)
    {
        var config = Config();
        var registration = await _store.SaveAsync(config);
        var response = Event();
        var expected = JsonSerializer.Serialize(response, A2AJsonUtilities.DefaultOptions);
        await using var sender = Sender(new() { LegacyTokenHeaderName = header });
        await sender.EnqueueAsync(registration, response);
        response.StatusUpdate!.Status.State = TaskState.Failed;
        config.Authentication!.Credentials = "mutated";
        await sender.StopAsync(CancellationToken.None);
        var request = Assert.Single(_transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("Bearer " + _credential, request.Authorization);
        Assert.Equal(expected, request.Body);
        if (string.IsNullOrEmpty(header))
        {
            Assert.DoesNotContain(request.Headers, pair => pair.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Equal("legacy-token", request.Headers[header]);
        }
        Assert.NotNull(await _store.GetAsync("task", "config")); // non-terminal registrations remain
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalAuthentication_HandlesNoAuthAndSchemeWithoutCredentials(bool schemeOnly)
    {
        var config = Config();
        config.Authentication = schemeOnly ? new AuthenticationInfo { Scheme = "Custom" } : null;
        var registration = await _store.SaveAsync(config);
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, Event());
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(schemeOnly ? "Custom" : null, Assert.Single(_transport.Requests).Authorization);
    }

    [Fact]
    public async Task RetryDelays_AreExponentialAndCapped_AndTelemetryIsSecretSafe()
    {
        var config = Config();
        config.Url = "https://callback.example/private-path?private-query=secret";
        var activities = new ConcurrentQueue<string>();
        var measurements = new ConcurrentQueue<long>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "A2A",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(string.Join(";", activity.TagObjects.Select(pair => $"{pair.Key}={pair.Value}"))),
        };
        ActivitySource.AddActivityListener(listener);
        using var meter = new MeterListener
        {
            InstrumentPublished = (instrument, observer) =>
            {
                if (instrument.Name == "a2a.server.push.count")
                {
                    observer.EnableMeasurementEvents(instrument);
                }
            },
        };
        meter.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Enqueue(value));
        meter.Start();
        _transport.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var registration = await _store.SaveAsync(config);
        await using var sender = Sender(new()
        {
            MaximumAttempts = 4,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            BackoffFactor = 2,
            MaximumRetryDelay = TimeSpan.FromMilliseconds(3),
        });
        await sender.EnqueueAsync(registration, Event(terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(4, _transport.Requests.Count);
        Assert.Contains(_logger.Messages, item => item.Contains("retry delay 1ms", StringComparison.Ordinal));
        Assert.Contains(_logger.Messages, item => item.Contains("retry delay 2ms", StringComparison.Ordinal));
        Assert.Contains(_logger.Messages, item => item.Contains("retry delay 3ms", StringComparison.Ordinal));
        Assert.NotEmpty(measurements);
        var telemetry = string.Join('\n', _logger.Messages.Concat(activities));
        Assert.DoesNotContain(_credential, telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-token", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("private-path", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("private-query", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-secret", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestCancellationAfterAdmission_DoesNotRevokeThePublication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _validator.Check = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(cancellationToken);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender();
        using var request = new CancellationTokenSource();
        await sender.EnqueueAsync(registration, Event(), request.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await request.CancelAsync();
        }
        finally
        {
            resume.TrySetResult();
        }
        await sender.StopAsync(CancellationToken.None);
        Assert.Single(_transport.Requests);
        Assert.Contains(_logger.Messages, item => item.Contains("succeeded", StringComparison.Ordinal));
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Fact]
    public async Task DeletionWhileValidationIsPending_PreventsTheAttempt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _validator.Check = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(cancellationToken);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, Event(terminal: true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _store.DeleteAsync("task", "config");
        resume.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Empty(_transport.Requests);
        Assert.Contains(_logger.Messages, item => item.Contains("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteRecreate_SuppressesRetryAndOldTerminalCleanupDoesNotDeleteNewGeneration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Respond = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };
        var old = await _store.SaveAsync(Config());
        await using var sender = Sender();
        await sender.EnqueueAsync(old, Event(terminal: true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _store.DeleteAsync("task", "config");
        var current = await _store.SaveAsync(Config());
        resume.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Single(_transport.Requests); // already in flight before deletion; no later attempt
        Assert.Equal(current.Version, (await _store.GetAsync("task", "config"))!.Version);
    }

    [Fact]
    public async Task RevalidationFailure_SuppressesLaterAttemptsAndRetainsConfiguration()
    {
        _transport.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        _validator.Check = (_, _) => _validator.Calls == 2
            ? throw new A2AException("Destination no longer public.", A2AErrorCode.InvalidParams)
            : Task.CompletedTask;
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, Event(terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.Single(_transport.Requests);
        Assert.Equal(2, _validator.Calls);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Theory]
    [InlineData(PushNotificationQueueFullBehavior.Reject)]
    [InlineData(PushNotificationQueueFullBehavior.Wait)]
    public async Task FullQueue_IsBoundedAndRejectsOrWaitsObservably(PushNotificationQueueFullBehavior behavior)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Respond = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender(new() { QueueCapacity = 1, QueueFullBehavior = behavior });
        await sender.EnqueueAsync(registration, Event());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.EnqueueAsync(registration, Event());
        if (behavior == PushNotificationQueueFullBehavior.Reject)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => sender.EnqueueAsync(registration, Event()));
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = sender.EnqueueAsync(registration, Event(), cancellation.Token);
            Assert.False(waiting.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        resume.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(2, _transport.Requests.Count);
        Assert.Contains(_logger.Messages, item => item.Contains("queue_full", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.EnqueueAsync(registration, Event()));
    }

    [Fact]
    public async Task ShutdownDeadline_CancelsActiveAndBufferedWorkAndDisposalIsIdempotent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Respond = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var registration = await _store.SaveAsync(Config());
        var sender = Sender(new() { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(20) });
        try
        {
            await sender.StartAsync(CancellationToken.None);
            await sender.EnqueueAsync(registration, Event());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await sender.EnqueueAsync(registration, Event(terminal: true));
            await sender.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(_transport.Requests);
            Assert.NotNull(await _store.GetAsync("task", "config"));
            Assert.Contains(_logger.Messages, item => item.Contains("shutdown", StringComparison.Ordinal));
        }
        finally
        {
            await sender.DisposeAsync();
            await sender.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Host")]
    [InlineData("Content-Length")]
    [InlineData("bad header")]
    [InlineData("X-Test\r\n")]
    public void UnsafeLegacyHeaderNames_AreRejectedAtStartup(string header)
    {
        Assert.Throws<ArgumentException>(() => Sender(new() { LegacyTokenHeaderName = header }));
    }

    [Fact]
    public async Task BufferedPayload_IsFrozenBeforeTheWorkerCanReadIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Respond = async (_, cancellationToken) =>
        {
            if (_transport.Requests.Count == 1)
            {
                entered.TrySetResult();
                await resume.Task.WaitAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, Event());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var buffered = Event();
        var expected = JsonSerializer.Serialize(buffered, A2AJsonUtilities.DefaultOptions);
        await sender.EnqueueAsync(registration, buffered);
        buffered.StatusUpdate!.Status.State = TaskState.Failed;
        buffered.StatusUpdate.Status.Message!.Parts.Clear();
        resume.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(2, _transport.Requests.Count);
        Assert.Equal(expected, _transport.Requests.Last().Body);
    }

    [Fact]
    public async Task ExplicitAdmissionRejection_DoesNotDeleteTheConfiguration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Respond = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var registration = await _store.SaveAsync(Config());
        await using var sender = Sender(new() { QueueCapacity = 1, QueueFullBehavior = PushNotificationQueueFullBehavior.Reject });
        await sender.EnqueueAsync(registration, Event());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.EnqueueAsync(registration, Event());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.EnqueueAsync(registration, Event(terminal: true)));
        Assert.NotNull(await _store.GetAsync("task", "config"));
        resume.TrySetResult();
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(2, _transport.Requests.Count);
        Assert.Contains(_logger.Messages, entry => entry.Contains("queue_full", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("capacity")]
    [InlineData("timeout")]
    [InlineData("delay")]
    [InlineData("factor")]
    [InlineData("shutdown")]
    [InlineData("queue-policy")]
    public void InvalidDeliveryBounds_AreRejected(string setting)
    {
        var options = new PushNotificationDeliveryOptions();
        switch (setting)
        {
            case "attempts": options.MaximumAttempts = 0; break;
            case "capacity": options.QueueCapacity = 0; break;
            case "timeout": options.RequestTimeout = TimeSpan.Zero; break;
            case "delay": options.InitialRetryDelay = TimeSpan.FromMilliseconds(-1); break;
            case "factor": options.BackoffFactor = double.NaN; break;
            case "shutdown": options.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(-1); break;
            case "queue-policy": options.QueueFullBehavior = (PushNotificationQueueFullBehavior)42; break;
        }
        Assert.Throws<ArgumentException>(() => Sender(options));
    }

    [Fact]
    public async Task Options_AreCopiedAtConstruction()
    {
        var options = new PushNotificationDeliveryOptions { MaximumAttempts = 1 };
        await using var sender = Sender(options);
        options.MaximumAttempts = 10;
        options.LegacyTokenHeaderName = "Authorization";
        _transport.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await sender.EnqueueAsync(await _store.SaveAsync(Config()), Event(terminal: true));
        await sender.StopAsync(CancellationToken.None);
        Assert.Single(_transport.Requests);
        Assert.Equal("legacy-token", _transport.Requests.Single().Headers["X-A2A-Notification-Token"]);
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Canceled)]
    [InlineData(TaskState.Rejected)]
    [InlineData(TaskState.InputRequired)]
    [InlineData(TaskState.AuthRequired)]
    [InlineData(TaskState.Working)]
    public async Task DeliveryState_DoesNotDecideConfigurationRetention(TaskState state)
    {
        var registration = await _store.SaveAsync(Config());
        var response = Event();
        response.StatusUpdate!.Status.State = state;
        await using var sender = Sender();
        await sender.EnqueueAsync(registration, response);
        await sender.StopAsync(CancellationToken.None);
        Assert.Single(_transport.Requests);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Fact]
    public async Task Delivery_DoesNotWriteToTheConfigurationStore()
    {
        var slow = new SlowCleanupStore(_store);
        var options = new PushNotificationDeliveryOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(30),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(20),
        };
        await using var sender = new HttpPushNotificationSender(slow, _validator, new Factory(_transport), options, _logger);
        await sender.EnqueueAsync(await _store.SaveAsync(Config()), Event(terminal: true));
        await sender.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(slow.Entered.Task.IsCompleted);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("multiple")]
    [InlineData("wrong-task")]
    [InlineData("missing-task")]
    public async Task InvalidEnvelopeOrTaskIdentity_IsRejectedBeforeAdmission(string kind)
    {
        var notification = Event();
        switch (kind)
        {
            case "empty": notification = new StreamResponse(); break;
            case "multiple": notification.Task = new AgentTask { Id = "task" }; break;
            case "wrong-task": notification.StatusUpdate!.TaskId = "other"; break;
            case "missing-task": notification.StatusUpdate!.TaskId = ""; break;
        }
        await using var sender = Sender();
        var config = await _store.SaveAsync(Config());
        await Assert.ThrowsAsync<ArgumentException>(() => sender.EnqueueAsync(config, notification));
        Assert.Empty(_transport.Requests);
        Assert.NotNull(await _store.GetAsync("task", "config"));
    }

    [Fact]
    public async Task CallerControlledIdentifiers_AreHashedInPushTelemetry()
    {
        var config = Config();
        config.TaskId = "private-user@example.invalid";
        config.Id = "customer-name";
        var notification = Event();
        notification.StatusUpdate!.TaskId = config.TaskId;
        await using var sender = Sender();
        await sender.EnqueueAsync(await _store.SaveAsync(config), notification);
        await sender.StopAsync(CancellationToken.None);
        var logs = string.Join('\n', _logger.Messages);
        Assert.DoesNotContain(config.TaskId, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(config.Id, logs, StringComparison.Ordinal);
        Assert.Contains("succeeded", logs, StringComparison.Ordinal);
    }

    private HttpPushNotificationSender Sender(PushNotificationDeliveryOptions? options = null)
    {
        return new HttpPushNotificationSender(_store, _validator, new Factory(_transport),
            options ?? new PushNotificationDeliveryOptions { InitialRetryDelay = TimeSpan.Zero }, _logger);
    }

    private TaskPushNotificationConfig Config() => new()
    {
        TaskId = "task",
        Id = "config",
        Url = "https://callback.example/notify",
        Token = "legacy-token",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = _credential },
    };

    private StreamResponse Event(bool terminal = false) => new()
    {
        StatusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task",
            ContextId = _contextId,
            Status = new A2A.TaskStatus
            {
                State = terminal ? TaskState.Completed : TaskState.Working,
                Message = new Message { MessageId = "message", Parts = [Part.FromText("payload-secret")] },
            },
        },
    };

    private sealed class SlowCleanupStore(IPushNotificationStore inner) : IPushNotificationStore
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(config, cancellationToken);
        public Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(taskId, configId, cancellationToken);
        public Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.GetAllAsync(taskId, cancellationToken);
        public Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default) =>
            inner.ListAsync(request, cancellationToken);
        public async Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class Factory(ProbeHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpPushNotificationSender.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class Validator : IPushNotificationUrlValidator
    {
        internal int Calls { get; private set; }
        internal Func<string, CancellationToken, Task>? Check { get; set; }
        public async Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Check is not null)
            {
                await Check(url, cancellationToken);
            }
            return [IPAddress.Loopback];
        }
    }

    private sealed class ProbeHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<Request> Requests { get; } = new();
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new Request(request.Method.Method, request.Content.Headers.ContentType!.ToString(),
                request.Headers.Authorization?.ToString(), request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value)), body));
            return await Respond(request, cancellationToken);
        }
    }

    private sealed record Request(string Method, string ContentType, string? Authorization, Dictionary<string, string> Headers, string Body);

    private sealed class CaptureLogger : ILogger<HttpPushNotificationSender>
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }
}
