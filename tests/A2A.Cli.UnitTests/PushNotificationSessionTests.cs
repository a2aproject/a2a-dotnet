using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace A2A.Cli.Tests;

public sealed class PushNotificationSessionTests
{
    [Fact]
    public async Task RefreshAsync_ReplacesRegistrationBeforeExpiry_AndKeepsOldCredentialUntilExpiry()
    {
        var clock = new TestClock();
        using var handler = new PushHandler();
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var original = session.CreateConfiguration(new Uri("http://localhost/notify"));
        original.TaskId = "server-assigned-task";
        handler.Configurations.Add(original.Id!, original);
        session.TrackTask(original.Id!, WorkingTask("server-assigned-task"));

        clock.Advance(TimeSpan.FromMinutes(3));
        await session.RefreshAsync(CancellationToken.None);
        Assert.Empty(handler.Operations);

        clock.Advance(TimeSpan.FromMinutes(1));
        await session.RefreshAsync(CancellationToken.None);

        var replacement = Assert.Single(handler.Configurations.Values);
        Assert.NotEqual(original.Id, replacement.Id);
        Assert.NotEqual(original.Authentication!.Credentials, replacement.Authentication!.Credentials);
        Assert.Equal("server-assigned-task", replacement.TaskId);
        Assert.Equal("http://localhost/notify", replacement.Url);
        Assert.Equal(["create", "delete"], handler.Operations);
        Assert.True(session.TryAuthenticate($"Bearer {original.Authentication.Credentials}", out _));
        Assert.True(session.TryAuthenticate($"Bearer {replacement.Authentication.Credentials}", out _));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(session.TryAuthenticate($"Bearer {original.Authentication.Credentials}", out _));
        Assert.True(session.TryAuthenticate($"Bearer {replacement.Authentication.Credentials}", out _));
    }

    [Fact]
    public async Task RefreshAsync_CreateFailure_PreservesOldRegistration_AndRetriesWithoutLoggingSecrets()
    {
        var clock = new TestClock();
        var logger = new RecordingLogger();
        using var handler = new PushHandler { FailCreate = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, logger, clock);
        var original = session.CreateConfiguration(new Uri("http://localhost/notify"));
        original.TaskId = "task";
        handler.Configurations.Add(original.Id!, original);
        session.TrackTask(original.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        await session.RefreshAsync(CancellationToken.None);

        Assert.Same(original, Assert.Single(handler.Configurations.Values));
        Assert.True(session.TryAuthenticate($"Bearer {original.Authentication!.Credentials}", out _));
        Assert.NotEmpty(logger.Messages);
        Assert.All(handler.IssuedCredentials, secret =>
            Assert.DoesNotContain(secret, string.Join("\n", logger.Messages)));

        handler.FailCreate = false;
        await session.RefreshAsync(CancellationToken.None);
        Assert.NotEqual(original.Id, Assert.Single(handler.Configurations.Values).Id);
    }

    [Fact]
    public async Task RefreshAsync_DeleteFailure_RetriesCleanupWithoutCreatingAnotherReplacement()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { FailDelete = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var original = session.CreateConfiguration(new Uri("http://localhost/notify"));
        original.TaskId = "task";
        handler.Configurations.Add(original.Id!, original);
        session.TrackTask(original.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        await session.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, handler.Configurations.Count);
        handler.FailDelete = false;
        await session.RefreshAsync(CancellationToken.None);

        Assert.Single(handler.Configurations);
        Assert.Equal(["create", "delete", "delete"], handler.Operations);
    }

    [Fact]
    public async Task ObserveNotification_LearnsTaskBeforeSendReturns_AndStopsRefreshAtTerminalState()
    {
        var clock = new TestClock();
        using var handler = new PushHandler();
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        config.TaskId = "actual-task";
        handler.Configurations.Add(config.Id!, config);
        session.ObserveNotification(config.Id!, new StreamResponse { Task = WorkingTask("actual-task") });
        clock.Advance(TimeSpan.FromMinutes(4));

        await session.RefreshAsync(CancellationToken.None);

        var replacement = Assert.Single(handler.Configurations.Values);
        Assert.Equal("actual-task", replacement.TaskId);
        session.ObserveNotification(replacement.Id!, new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "actual-task",
                Status = new TaskStatus { State = TaskState.Completed }
            }
        });
        clock.Advance(TimeSpan.FromMinutes(4));
        await session.RefreshAsync(CancellationToken.None);
        Assert.Equal(["create", "delete"], handler.Operations);
    }

    [Fact]
    public async Task Start_RefreshesInBackground_AndDisposalCancelsPendingRequest()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { BlockCreate = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        session.TrackTask(config.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        session.Start();
        await handler.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(handler.CreateCancelled);
    }

    [Fact]
    public async Task Start_RefreshesOnLaterTimerTicks_WithoutCallerActivity()
    {
        var clock = new TestClock();
        using var handler = new PushHandler();
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        config.TaskId = "task";
        handler.Configurations.Add(config.Id!, config);
        session.TrackTask(config.Id!, WorkingTask("task"));

        session.Start();
        Assert.Empty(handler.Operations);
        clock.Advance(TimeSpan.FromMinutes(4));
        await handler.RotationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(config.Id, Assert.Single(handler.Configurations.Values).Id);
    }

    [Fact]
    public async Task RefreshAsync_LostCreateResponse_CleansUpAttemptedRegistration()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { LoseCreateResponse = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        config.TaskId = "task";
        handler.Configurations.Add(config.Id!, config);
        session.TrackTask(config.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        await session.RefreshAsync(CancellationToken.None);

        Assert.Same(config, Assert.Single(handler.Configurations.Values));
        Assert.Equal(["create", "delete"], handler.Operations);
    }

    [Fact]
    public async Task RefreshAsync_MalformedCreateResponse_PreservesOriginalAndCleansUpAttempt()
    {
        var clock = new TestClock();
        var logger = new RecordingLogger();
        using var handler = new PushHandler { MalformedCreateResponse = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, logger, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        config.TaskId = "task";
        handler.Configurations.Add(config.Id!, config);
        session.TrackTask(config.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        await session.RefreshAsync(CancellationToken.None);

        Assert.Same(config, Assert.Single(handler.Configurations.Values));
        Assert.Equal(["create", "delete"], handler.Operations);
        Assert.NotEmpty(logger.Messages);
        handler.MalformedCreateResponse = false;
        await session.RefreshAsync(CancellationToken.None);
        Assert.NotEqual(config.Id, Assert.Single(handler.Configurations.Values).Id);
    }

    [Fact]
    public async Task RefreshAsync_CleanupBacklog_DoesNotPerformUnboundedDeletesBeforeNextRenewal()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { FailDelete = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        config.TaskId = "task";
        handler.Configurations.Add(config.Id!, config);
        session.TrackTask(config.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));
        await session.RefreshAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(4));
        handler.Operations.Clear();

        await session.RefreshAsync(CancellationToken.None);

        Assert.Equal(["create", "delete"], handler.Operations);
        Assert.Equal(3, handler.Configurations.Count);
        handler.FailDelete = false;
        await session.RefreshAsync(CancellationToken.None);
        await session.RefreshAsync(CancellationToken.None);
        Assert.Single(handler.Configurations);
    }

    [Fact]
    public async Task Start_ParentCancellation_StopsPendingRefresh()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { BlockCreate = true };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        using var stop = new CancellationTokenSource();
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock, stop.Token);
        var config = session.CreateConfiguration(new Uri("http://localhost/notify"));
        session.TrackTask(config.Id!, WorkingTask("task"));
        clock.Advance(TimeSpan.FromMinutes(4));

        session.Start();
        await handler.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await handler.CreateCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(handler.CreateCancelled);
    }

    [Fact]
    public async Task Start_SlowTaskRefresh_DoesNotBlockOtherTasks()
    {
        var clock = new TestClock();
        using var handler = new PushHandler { BlockTaskId = "slow" };
        using var http = new HttpClient(handler);
        using var client = new A2AClient(new Uri("https://agent.example"), http);
        await using var session = new PushNotificationSession(client, NullLogger.Instance, clock);
        var slow = session.CreateConfiguration(new Uri("http://localhost/notify"));
        slow.TaskId = "slow";
        handler.Configurations.Add(slow.Id!, slow);
        session.TrackTask(slow.Id!, WorkingTask("slow"));
        var healthy = session.CreateConfiguration(new Uri("http://localhost/notify"));
        healthy.TaskId = "healthy";
        handler.Configurations.Add(healthy.Id!, healthy);
        session.TrackTask(healthy.Id!, WorkingTask("healthy"));
        clock.Advance(TimeSpan.FromMinutes(4));

        session.Start();
        await handler.RotationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(slow.Id!, handler.Configurations.Keys);
        Assert.DoesNotContain(healthy.Id!, handler.Configurations.Keys);
        Assert.Contains(handler.Configurations.Values, config => config.TaskId == "healthy");
    }

    private static AgentTask WorkingTask(string id) => new()
    {
        Id = id,
        ContextId = "context",
        Status = new TaskStatus { State = TaskState.Working }
    };

    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly object _gate = new();
        private readonly List<TestTimer> _timers = [];

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan duration)
        {
            TestTimer[] due;
            lock (_gate)
            {
                _now += duration;
                due = _timers.Where(timer => timer.DueAt <= _now).ToArray();
            }
            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_gate)
            {
                _timers.Add(timer);
            }
            return timer;
        }

        private sealed class TestTimer(TestClock clock, TimerCallback callback, object? state) : ITimer
        {
            internal DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                lock (clock._gate)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue : clock._now + dueTime;
                }
                return true;
            }

            internal void Fire()
            {
                lock (clock._gate)
                {
                    DueAt = DateTimeOffset.MaxValue;
                }
                callback(state);
            }

            public void Dispose()
            {
                lock (clock._gate)
                {
                    clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }

    private sealed class PushHandler : HttpMessageHandler
    {
        public Dictionary<string, TaskPushNotificationConfig> Configurations { get; } = [];
        public List<string> Operations { get; } = [];
        public List<string> IssuedCredentials { get; } = [];
        public bool FailCreate { get; set; }
        public bool FailDelete { get; set; }
        public bool BlockCreate { get; set; }
        public string? BlockTaskId { get; set; }
        public bool LoseCreateResponse { get; set; }
        public bool MalformedCreateResponse { get; set; }
        public bool CreateCancelled { get; private set; }
        public TaskCompletionSource CreateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CreateCancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RotationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            object? result = null;
            if (root.GetProperty("method").GetString() == "CreateTaskPushNotificationConfig")
            {
                Operations.Add("create");
                var config = root.GetProperty("params").Deserialize<TaskPushNotificationConfig>(A2AJsonUtilities.DefaultOptions)!;
                IssuedCredentials.Add(config.Authentication!.Credentials!);
                CreateStarted.TrySetResult();
                if (BlockCreate || config.TaskId == BlockTaskId)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        CreateCancelled = true;
                        CreateCancellationObserved.TrySetResult();
                        throw;
                    }
                }
                if (FailCreate)
                {
                    throw new HttpRequestException($"Untrusted server error containing {config.Authentication.Credentials}");
                }
                Configurations.Add(config.Id!, config);
                if (LoseCreateResponse)
                {
                    throw new HttpRequestException("Connection closed after committing registration");
                }
                if (MalformedCreateResponse)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{", Encoding.UTF8, "application/json")
                    };
                }
                result = config;
            }
            else
            {
                Assert.Equal("DeleteTaskPushNotificationConfig", root.GetProperty("method").GetString());
                Operations.Add("delete");
                if (FailDelete)
                {
                    throw new HttpRequestException("Deletion failed");
                }
                var parameters = root.GetProperty("params");
                string id = parameters.GetProperty("id").GetString()!;
                if (Configurations.TryGetValue(id, out var config))
                {
                    Assert.Equal(config.TaskId, parameters.GetProperty("taskId").GetString());
                }
                Configurations.Remove(id);
                RotationCompleted.TrySetResult();
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = root.GetProperty("id"),
                    result
                }, A2AJsonUtilities.DefaultOptions), Encoding.UTF8, "application/json")
            };
        }
    }
}
