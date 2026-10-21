using A2A.Extensions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace A2A;

internal sealed class PushNotificationSession(
    IA2AClient client,
    ILogger logger,
    TimeProvider? timeProvider = null,
    CancellationToken cancellationToken = default) : IAsyncDisposable
{
    private static readonly TimeSpan s_credentialLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_refreshBeforeExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan s_retryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];
    private readonly Dictionary<string, Credential> _credentials = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private Task? _worker;

    internal void Start()
    {
        _stop.Token.ThrowIfCancellationRequested();
        _worker ??= RunAsync(_stop.Token);
    }

    internal TaskPushNotificationConfig CreateConfiguration(Uri callbackUri)
    {
        lock (_gate)
        {
            var configuration = NewConfiguration(callbackUri.ToString());
            var expiresAt = _time.GetUtcNow() + s_credentialLifetime;
            var registration = new Registration(configuration, expiresAt);
            _credentials.Add(configuration.Authentication!.Credentials!, new Credential(registration, expiresAt));
            _registrations.Add(registration);
            return configuration;
        }
    }

    internal bool TryAuthenticate(string? authorization, out string registrationId)
    {
        registrationId = string.Empty;
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_credentials.TryGetValue(header.Parameter, out var credential) ||
                credential.ExpiresAt <= _time.GetUtcNow())
            {
                return false;
            }
            registrationId = credential.Registration.InitialId;
            return true;
        }
    }

    internal void TrackTask(string registrationId, AgentTask? task)
    {
        if (task is not null)
        {
            ObserveTask(registrationId, task.Id, task.Status.State.IsTerminal());
        }
        else
        {
            lock (_gate)
            {
                FindRegistration(registrationId).Finished = true;
            }
        }
    }

    internal void ObserveNotification(string registrationId, StreamResponse notification)
    {
        string? taskId = notification.Task?.Id ?? notification.StatusUpdate?.TaskId ??
            notification.ArtifactUpdate?.TaskId ?? notification.Message?.TaskId;
        if (taskId is not null)
        {
            var state = notification.Task?.Status.State ?? notification.StatusUpdate?.Status.State;
            ObserveTask(registrationId, taskId, state?.IsTerminal() == true);
        }
    }

    private void ObserveTask(string registrationId, string taskId, bool finished)
    {
        lock (_gate)
        {
            var registration = FindRegistration(registrationId);
            if (registration.TaskId is not null &&
                !string.Equals(registration.TaskId, taskId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A push registration cannot change its task association.");
            }
            registration.TaskId = taskId;
            registration.Finished |= finished;
        }
    }

    private Registration FindRegistration(string id) =>
        _registrations.Single(registration =>
            registration.InitialId == id || registration.Configuration.Id == id);

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Registration[] registrations;
        lock (_gate)
        {
            foreach (string expired in _credentials
                .Where(pair => pair.Value.ExpiresAt <= _time.GetUtcNow())
                .Select(pair => pair.Key).ToArray())
            {
                _credentials.Remove(expired);
            }
            registrations = _registrations.ToArray();
        }

        await Task.WhenAll(registrations.Select(registration =>
            RefreshRegistrationAsync(registration, cancellationToken))).ConfigureAwait(false);
    }

    private async Task RefreshRegistrationAsync(Registration registration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskPushNotificationConfig? replacement = null;
        string? taskId;
        lock (_gate)
        {
            taskId = registration.TaskId;
            if (taskId is not null && !registration.Finished &&
                registration.ExpiresAt - _time.GetUtcNow() <= s_refreshBeforeExpiry)
            {
                replacement = IssueConfiguration(registration, registration.Configuration.Url);
                replacement.TaskId = taskId;
            }
        }

        if (replacement is not null)
        {
            try
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                request.CancelAfter(s_requestTimeout);
                // Creation is not an upsert. Keep the old registration until its replacement exists.
                await client.CreateTaskPushNotificationConfigAsync(replacement, request.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    registration.RetiredIds.Add(registration.Configuration.Id!);
                    registration.Configuration = replacement;
                    registration.ExpiresAt = _credentials[replacement.Authentication!.Credentials!].ExpiresAt;
                }
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Refreshed push notification credentials for task {TaskId}.", taskId);
                }
            }
            catch (Exception exception) when (IsDeliveryFailure(exception, cancellationToken))
            {
                // A lost response can still mean creation succeeded; delete the attempted ID too.
                registration.RetiredIds.Add(replacement.Id!);
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Push credential refresh failed for task {TaskId} ({FailureType}); retrying.",
                        taskId, exception.GetType().Name);
                }
            }
        }

        // Bound each pass to one create and one delete, regardless of cleanup backlog.
        foreach (string id in registration.RetiredIds.Take(1).ToArray())
        {
            try
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                request.CancelAfter(s_requestTimeout);
                await client.DeleteTaskPushNotificationConfigAsync(new()
                {
                    TaskId = taskId!,
                    Id = id
                }, request.Token).ConfigureAwait(false);
                registration.RetiredIds.Remove(id);
            }
            catch (Exception exception) when (IsDeliveryFailure(exception, cancellationToken))
            {
                registration.RetiredIds.Remove(id);
                registration.RetiredIds.Add(id);
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Push registration cleanup failed for task {TaskId} ({FailureType}); retrying.",
                        taskId, exception.GetType().Name);
                }
            }
        }
    }

    private TaskPushNotificationConfig IssueConfiguration(Registration registration, string url)
    {
        var configuration = NewConfiguration(url);
        var expiresAt = _time.GetUtcNow() + s_credentialLifetime;
        _credentials.Add(configuration.Authentication!.Credentials!, new Credential(registration, expiresAt));
        return configuration;
    }

    private static TaskPushNotificationConfig NewConfiguration(string url)
    {
        return new TaskPushNotificationConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Url = url,
            Authentication = new AuthenticationInfo
            {
                Scheme = "Bearer",
                Credentials = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            }
        };
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(s_retryInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError("Push credential renewal stopped unexpectedly ({FailureType}).", exception.GetType().Name);
            }
            throw;
        }
    }

    private static bool IsDeliveryFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or A2AException or JsonException ||
        (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_worker is not null)
            {
                await _worker.ConfigureAwait(false);
            }
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private sealed class Registration(TaskPushNotificationConfig configuration, DateTimeOffset expiresAt)
    {
        internal string InitialId { get; } = configuration.Id!;
        internal TaskPushNotificationConfig Configuration { get; set; } = configuration;
        internal string? TaskId { get; set; }
        internal DateTimeOffset ExpiresAt { get; set; } = expiresAt;
        internal bool Finished { get; set; }
        internal List<string> RetiredIds { get; } = [];
    }

    private sealed record Credential(Registration Registration, DateTimeOffset ExpiresAt);
}
