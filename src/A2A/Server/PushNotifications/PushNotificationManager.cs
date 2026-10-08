using Microsoft.Extensions.Logging;
using A2A.Extensions;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

internal sealed class PushNotificationManager(
    AgentCard card, ITaskStore tasks, IPushNotificationStore store,
    IPushNotificationUrlValidator validator, IPushNotificationSender sender,
    ChannelEventNotifier notifier, ILogger logger) : IAsyncDisposable
{
    private readonly bool _enabled = card.Capabilities.PushNotifications.GetValueOrDefault();
    private readonly PushNotificationDiagnostics _diagnostics = new(logger);
    private readonly ConcurrentDictionary<string, Task> _enqueueTails = new(StringComparer.Ordinal);
    private readonly object _admissionGate = new();
    private readonly CancellationTokenSource _admissionStop = new();
    private Task? _stopCallbacks;
    private bool _abandoned;
    private bool _disposed;
    private readonly JsonTypeInfo<StreamResponse> _streamTypeInfo =
        (JsonTypeInfo<StreamResponse>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(StreamResponse));

    internal void EnsureEnabled()
    {
        if (!_enabled)
        {
            throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);
        }
    }

    internal async Task<TaskPushNotificationConfig> PrepareAsync(TaskPushNotificationConfig config, string taskId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        bool missingRequiredValue = string.IsNullOrWhiteSpace(config.Url) ||
            (config.Authentication is { } authentication && string.IsNullOrWhiteSpace(authentication.Scheme));
        if (missingRequiredValue)
        {
            throw new A2AException("Push destination and authentication scheme must be provided.", A2AErrorCode.InvalidParams);
        }
        var prepared = new PushNotificationConfigSnapshot(config, Guid.NewGuid().ToString("N")).Configuration;
        prepared.TaskId = taskId;
        if (string.IsNullOrEmpty(prepared.Id))
        {
            prepared.Id = Guid.NewGuid().ToString("N");
        }
        await ValidateIdentifiersAsync(taskId, prepared.Id, requireTask: false, cancellationToken).ConfigureAwait(false);
        try
        {
            if (prepared.Authentication is { } auth)
            {
                _ = new AuthenticationHeaderValue(auth.Scheme, string.IsNullOrEmpty(auth.Credentials) ? null : auth.Credentials);
            }
            bool illegalHeader =
                (prepared.Token?.Any(ch => ch < 32 || ch > 126)).GetValueOrDefault() ||
                (prepared.Authentication?.Credentials?.Any(ch => ch < 32 || ch > 126)).GetValueOrDefault();
            if (illegalHeader)
            {
                throw new FormatException();
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new A2AException("Invalid push notification authentication/header value.", A2AErrorCode.InvalidParams);
        }
        await validator.ValidateAsync(prepared.Url, cancellationToken).ConfigureAwait(false);
        return prepared;
    }

    internal async Task<TaskPushNotificationConfig> CreateAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await ValidateIdentifiersAsync(config.TaskId!, null, requireTask: true, cancellationToken).ConfigureAwait(false);
        var prepared = await PrepareAsync(config, config.TaskId!, cancellationToken).ConfigureAwait(false);
        PushNotificationBatch? initial = null;
        PushNotificationConfigSnapshot saved;
        using (await notifier.AcquireTaskLockAsync(prepared.TaskId!, cancellationToken).ConfigureAwait(false))
        {
            await ValidateIdentifiersAsync(prepared.TaskId!, prepared.Id, requireTask: true, cancellationToken).ConfigureAwait(false);
            var task = await tasks.GetTaskAsync(prepared.TaskId!, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
            saved = await store.SaveAsync(prepared, cancellationToken).ConfigureAwait(false);
            _diagnostics.Record("config_create", prepared.TaskId, prepared.Id);
            if (task.Status.State.IsTerminal())
            {
                initial = CreateBatch(prepared.TaskId!, [saved], new StreamResponse { Task = task });
            }
        }
        await EnqueueAsync(initial, CancellationToken.None).ConfigureAwait(false);
        return saved.Configuration;
    }

    internal async Task<TaskPushNotificationConfig> GetAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new A2AException("Invalid push configuration identifier.", A2AErrorCode.InvalidParams);
        }
        await ValidateIdentifiersAsync(request.TaskId, request.Id, true, cancellationToken).ConfigureAwait(false);
        var result = await store.GetAsync(request.TaskId, request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new A2AException("Push configuration not found.", A2AErrorCode.TaskNotFound);
        return Redact(result.Configuration, "config_get");
    }

    internal async Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await ValidateIdentifiersAsync(request.TaskId, null, true, cancellationToken).ConfigureAwait(false);
        var result = await store.ListAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var config in result.Configs!)
        {
            Redact(config, null);
        }
        _diagnostics.Record("config_list", request.TaskId);
        return result;
    }

    internal async Task DeleteAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new A2AException("Invalid push configuration identifier.", A2AErrorCode.InvalidParams);
        }
        await ValidateIdentifiersAsync(request.TaskId, request.Id, true, cancellationToken).ConfigureAwait(false);
        using (await notifier.AcquireTaskLockAsync(request.TaskId, cancellationToken).ConfigureAwait(false))
        {
            await store.DeleteAsync(request.TaskId, request.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        _diagnostics.Record("config_delete", request.TaskId, request.Id);
    }

    internal async Task<PushNotificationRequestState> PrepareInlineAsync(TaskPushNotificationConfig config, string taskId, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(config, taskId, cancellationToken).ConfigureAwait(false);
        if (await store.GetAsync(taskId, prepared.Id!, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new A2AException("Push configuration ID already exists.", A2AErrorCode.InvalidParams);
        }
        return new PushNotificationRequestState { Pending = prepared };
    }

    internal Task<IReadOnlyList<PushNotificationConfigSnapshot>> ReadConfigurationsAsync(string taskId, CancellationToken cancellationToken)
    {
        return _enabled
            ? store.GetAllAsync(taskId, cancellationToken)
            : Task.FromResult<IReadOnlyList<PushNotificationConfigSnapshot>>([]);
    }

    internal async Task<PushNotificationBatch?> SnapshotAsync(RequestContext context, StreamResponse response,
        IReadOnlyList<PushNotificationConfigSnapshot> configs, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return null;
        }
        if (context.PushNotifications is { Pending: { } config } state)
        {
            var created = await store.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            configs = [.. configs, created];
            state.Pending = null;
            _diagnostics.Record("config_create", context.TaskId, config.Id);
        }
        if (configs.Count == 0)
        {
            return null;
        }
        return CreateBatch(context.TaskId, configs, response);
    }

    private PushNotificationBatch CreateBatch(string taskId, IReadOnlyList<PushNotificationConfigSnapshot> configs, StreamResponse response)
    {
        // Called under the task lock: freeze the event before publishing it to SSE subscribers,
        // and reserve its enqueue position without doing delivery work while locked.
        var previous = _enqueueTails.GetValueOrDefault(taskId) ?? Task.CompletedTask;
        var batch = new PushNotificationBatch(taskId, configs,
            JsonSerializer.SerializeToUtf8Bytes(response, _streamTypeInfo), previous);
        _enqueueTails[taskId] = batch.Completed.Task;
        return batch;
    }

    internal async Task EnqueueAsync(PushNotificationBatch? batch, CancellationToken cancellationToken)
    {
        if (batch is null)
        {
            return;
        }
        try
        {
            CancellationTokenSource admission;
            lock (_admissionGate)
            {
                if (_abandoned)
                {
                    _diagnostics.Record("abandoned", batch.TaskId, failure: "shutdown");
                    throw new PushNotificationAdmissionException();
                }
                admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _admissionStop.Token);
            }
            using var admissionLifetime = admission;
            await batch.Previous.WaitAsync(admission.Token).ConfigureAwait(false);
            foreach (var config in batch.Configs)
            {
                try
                {
                    var response = JsonSerializer.Deserialize(batch.Payload, _streamTypeInfo)!;
                    Task enqueue;
                    lock (_admissionGate)
                    {
                        admission.Token.ThrowIfCancellationRequested();
                        enqueue = sender.EnqueueAsync(config, response, admission.Token);
                    }
                    await enqueue.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Admission failure is not a successful accepted notification.
                    var value = config.Configuration;
                    _diagnostics.Record("admission_failed", value.TaskId, value.Id, exception.GetType().Name);
                    throw new PushNotificationAdmissionException();
                }
            }
        }
        finally
        {
            batch.Completed.TrySetResult();
            using (await notifier.AcquireTaskLockAsync(batch.TaskId, CancellationToken.None).ConfigureAwait(false))
            {
                if (_enqueueTails.TryGetValue(batch.TaskId, out var tail) &&
                    ReferenceEquals(tail, batch.Completed.Task))
                {
                    _enqueueTails.TryRemove(batch.TaskId, out _);
                }
            }
        }
    }

    internal void Abandon()
    {
        lock (_admissionGate)
        {
            _abandoned = true;
            _stopCallbacks ??= _admissionStop.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Abandon();
        Task callbacks;
        lock (_admissionGate)
        {
            callbacks = _stopCallbacks!;
        }
        await callbacks.ConfigureAwait(false);
        lock (_admissionGate)
        {
            if (!_disposed)
            {
                _admissionStop.Dispose();
                _disposed = true;
            }
        }
    }

    internal void RecordInline(string action, RequestContext context)
    {
        _diagnostics.Record(action, context.TaskId, context.PushNotifications?.Pending?.Id);
    }

    private async Task ValidateIdentifiersAsync(string taskId, string? configId, bool requireTask, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool invalid = string.IsNullOrWhiteSpace(taskId) ||
            taskId.Any(char.IsControl) ||
            (configId is not null &&
                (string.IsNullOrWhiteSpace(configId) ||
                 configId is "." or ".." ||
                 configId.Any(ch => char.IsControl(ch) || ch is '/' or '\\' or '%')));
        if (invalid)
        {
            throw new A2AException("Invalid push configuration identifier.", A2AErrorCode.InvalidParams);
        }
        if (requireTask &&
            await tasks.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
        }
    }

    private TaskPushNotificationConfig Redact(TaskPushNotificationConfig config, string? action)
    {
        config.Token = null;
        if (config.Authentication is not null)
        {
            config.Authentication.Credentials = null;
        }
        if (action is not null)
        {
            _diagnostics.Record(action, config.TaskId, config.Id);
        }
        return config;
    }
}

internal sealed class PushNotificationRequestState
{
    internal TaskPushNotificationConfig? Pending { get; set; }
}

internal sealed class PushNotificationBatch(
    string taskId, IReadOnlyList<PushNotificationConfigSnapshot> configs, byte[] payload, Task previous)
{
    internal string TaskId { get; } = taskId;
    internal IReadOnlyList<PushNotificationConfigSnapshot> Configs { get; } = configs;
    internal byte[] Payload { get; } = payload;
    internal Task Previous { get; } = previous;
    internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
