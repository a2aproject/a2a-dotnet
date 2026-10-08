using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace A2A.AspNetCore;

/// <summary>Single-worker, bounded, process-local HTTP delivery with retry and generation-scoped cancellation.</summary>
/// <remarks>Accepted work is lost on process failure. Replace with a durable sender if restart loss is unacceptable.</remarks>
public sealed class HttpPushNotificationSender : IPushNotificationSender, IHostedService, IAsyncDisposable
{
    /// <summary>Named factory client. Custom handlers must preserve redirect/cookie protections and honor cancellation.</summary>
    public const string HttpClientName = "A2A.PushNotifications";
    private readonly IPushNotificationStore _store;
    private readonly IPushNotificationUrlValidator _validator;
    private readonly IHttpClientFactory _clients;
    private readonly PushNotificationDeliveryOptions _options;
    private readonly PushNotificationDiagnostics _diagnostics;
    private readonly TimeProvider _time;
    private readonly Channel<Delivery> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly JsonTypeInfo<StreamResponse> _typeInfo =
        (JsonTypeInfo<StreamResponse>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(StreamResponse));
    private Task? _worker;
    private Task? _shutdown;
    private bool _stopping;
    private bool _disposed;

    /// <summary>Creates a sender using immutable startup options and replaceable transport/policy services.</summary>
    /// <param name="store">Registration store.</param>
    /// <param name="validator">Destination validator.</param>
    /// <param name="clients">Named HTTP client factory.</param>
    /// <param name="options">Delivery policy.</param>
    /// <param name="logger">Secret-safe delivery logger.</param>
    /// <param name="timeProvider">Clock for attempt deadlines and retry delays.</param>
    public HttpPushNotificationSender(IPushNotificationStore store, IPushNotificationUrlValidator validator,
        IHttpClientFactory clients, PushNotificationDeliveryOptions options, ILogger<HttpPushNotificationSender> logger,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
        _diagnostics = new PushNotificationDiagnostics(logger ?? throw new ArgumentNullException(nameof(logger)));
        _time = timeProvider ?? TimeProvider.System;
        _queue = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();
        var config = registration.Configuration;
        int payloadCount = (response.Task is null ? 0 : 1) +
            (response.Message is null ? 0 : 1) +
            (response.StatusUpdate is null ? 0 : 1) +
            (response.ArtifactUpdate is null ? 0 : 1);
        var taskId = response.Task?.Id ?? response.Message?.TaskId ??
            response.StatusUpdate?.TaskId ?? response.ArtifactUpdate?.TaskId;
        if (payloadCount != 1 ||
            !string.Equals(taskId, config.TaskId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Push payload must contain exactly one event for the registered task.", nameof(response));
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, _typeInfo);
        var delivery = new Delivery(registration, config, payload);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, registration.RemovalToken);
        try
        {
            registration.RemovalToken.ThrowIfCancellationRequested();
            EnsureStarted();
            if (!_queue.Writer.TryWrite(delivery))
            {
                _diagnostics.Record("queue_full", config.TaskId, config.Id, "capacity");
                if (_options.QueueFullBehavior == PushNotificationQueueFullBehavior.Reject)
                {
                    throw new InvalidOperationException("Push notification queue cannot accept work.");
                }
                await _queue.Writer.WriteAsync(delivery, admission.Token).ConfigureAwait(false);
            }
            _diagnostics.Record("accepted", config.TaskId, config.Id);
        }
        catch (OperationCanceledException) when (registration.RemovalToken.IsCancellationRequested)
        {
            _diagnostics.Record("skipped", config.TaskId, config.Id);
        }
        catch (Exception exception)
        {
            _diagnostics.Record("admission_failed", config.TaskId, config.Id, exception.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _stopping = true;
            _queue.Writer.TryComplete();
            return _shutdown ??= DrainAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_disposed)
            {
                _stop.Dispose();
                _disposed = true;
            }
        }
    }

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                throw new InvalidOperationException("Push notification sender is stopping.");
            }
            _worker ??= Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (_worker is null)
        {
            return;
        }
        try
        {
            await _worker.WaitAsync(_options.ShutdownDrainTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            _diagnostics.Record("shutdown_cancelled", failure: exception.GetType().Name);
            await _stop.CancelAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // This sender owns the Task.Run worker and must join it after cancellation.
            await _worker.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var delivery in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await DeliverAsync(delivery, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Shutdown cancellation is reported below for every buffered item.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var pending))
            {
                _diagnostics.Record("abandoned", pending.Config.TaskId, pending.Config.Id, "shutdown");
            }
        }
    }

    private async Task DeliverAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var config = delivery.Config;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, delivery.Registration.RemovalToken);
        cancellationToken = lifetime.Token;
        try
        {
            using var client = _clients.CreateClient(HttpClientName);
            for (int attempt = 1; attempt <= _options.MaximumAttempts; attempt++)
            {
                if (!await IsActiveAsync(delivery, cancellationToken).ConfigureAwait(false))
                {
                    _diagnostics.Record("skipped", config.TaskId, config.Id, attempt: attempt);
                    return;
                }
                await _validator.ValidateAsync(config.Url, cancellationToken).ConfigureAwait(false);
                // Deletion while DNS validation was pending must also suppress this attempt.
                if (!await IsActiveAsync(delivery, cancellationToken).ConfigureAwait(false))
                {
                    _diagnostics.Record("skipped", config.TaskId, config.Id, attempt: attempt);
                    return;
                }
                cancellationToken.ThrowIfCancellationRequested();
                bool retry;
                int status = 0;
                string? failure;
                using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                _diagnostics.Record("attempted", config.TaskId, config.Id, attempt: attempt);
                try
                {
                    using var request = CreateRequest(delivery);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        _diagnostics.Record("succeeded", config.TaskId, config.Id, attempt: attempt, statusCode: status);
                        return;
                    }
                    retry = status is 408 or 429 || (status >= 500 && status <= 599);
                    failure = "http";
                }
                catch (OperationCanceledException) when (delivery.Registration.RemovalToken.IsCancellationRequested)
                {
                    _diagnostics.Record("skipped", config.TaskId, config.Id, attempt: attempt);
                    return;
                }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                {
                    retry = true;
                    failure = "timeout";
                }
                catch (HttpRequestException)
                {
                    retry = true;
                    failure = "network";
                }
                if (!retry ||
                    attempt == _options.MaximumAttempts)
                {
                    _diagnostics.Record("failed", config.TaskId, config.Id, failure, attempt, status);
                    return;
                }
                double delay = _options.InitialRetryDelay == TimeSpan.Zero ? 0 : Math.Min(_options.MaximumRetryDelay.TotalMilliseconds,
                    _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(_options.BackoffFactor, attempt - 1));
                _diagnostics.Record("retried", config.TaskId, config.Id, attempt: attempt, statusCode: status, delayMilliseconds: delay);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (delivery.Registration.RemovalToken.IsCancellationRequested)
        {
            _diagnostics.Record("skipped", config.TaskId, config.Id);
        }
        catch (Exception exception)
        {
            _diagnostics.Record("failed", config.TaskId, config.Id,
                _stop.IsCancellationRequested ? "shutdown" : exception.GetType().Name);
        }
    }

    private HttpRequestMessage CreateRequest(Delivery delivery)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, delivery.Config.Url)
        {
            Content = new ByteArrayContent(delivery.Payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (delivery.Config.Authentication is { } auth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                auth.Scheme, string.IsNullOrEmpty(auth.Credentials) ? null : auth.Credentials);
        }
        if (!string.IsNullOrEmpty(_options.LegacyTokenHeaderName) &&
            !string.IsNullOrEmpty(delivery.Config.Token))
        {
            request.Headers.Add(_options.LegacyTokenHeaderName, delivery.Config.Token);
        }
        return request;
    }

    private async Task<bool> IsActiveAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var active = await _store.GetAsync(delivery.Config.TaskId!, delivery.Config.Id!, cancellationToken).ConfigureAwait(false);
        return active?.Version == delivery.Registration.Version &&
            !delivery.Registration.RemovalToken.IsCancellationRequested;
    }

    private sealed record Delivery(PushNotificationConfigSnapshot Registration, TaskPushNotificationConfig Config, byte[] Payload);
}
