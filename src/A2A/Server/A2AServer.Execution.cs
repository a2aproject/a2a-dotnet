using A2A.Extensions;
using System.Threading.Channels;

namespace A2A;

public partial class A2AServer
{
    private readonly object _executionGate = new();
    private readonly HashSet<ServerExecution> _executions = [];
    private Task? _shutdown;

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        Task shutdown;
        lock (_executionGate)
        {
            _shutdown ??= Task.WhenAll(_executions.Select(item => item.StopAsync(CancellationToken.None)));
            shutdown = _shutdown;
        }
        try
        {
            await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false);
            _pushNotifications?.Abandon();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _pushNotifications?.Abandon();
            lock (_executionGate)
            {
                _logger.ServerShutdownAbandoned(_executions.Count);
                foreach (var execution in _executions)
                {
                    execution.Abandon();
                }
            }
            throw;
        }
    }

    private ServerExecution StartExecution(RequestContext context, bool detachedLifetime, bool cancelHandler, CancellationToken cancellationToken)
    {
        lock (_executionGate)
        {
            ObjectDisposedException.ThrowIf(_shutdown is not null, this);
            var execution = new ServerExecution(this, context, detachedLifetime, cancelHandler, cancellationToken);
            _executions.Add(execution);
            execution.Start();
            return execution;
        }
    }

    private void CompleteExecution(ServerExecution execution)
    {
        lock (_executionGate)
        {
            _executions.Remove(execution);
        }
    }

    private sealed class ServerExecution
    {
        private readonly A2AServer _server;
        private readonly bool _cancelHandler;
        private readonly CancellationToken _requestToken;
        private readonly AgentEventQueue _events = new();
        private readonly Channel<StreamResponse> _responses = Channel.CreateBounded<StreamResponse>(
            new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenSource _responseStop = new();
        private readonly CancellationToken _executionToken;
        private readonly CancellationToken _responseToken;
        private readonly object _gate = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _cancelCallbacks;
        private bool _finished;
        private bool _abandoned;

        internal ServerExecution(A2AServer server, RequestContext context, bool detachedLifetime,
            bool cancelHandler, CancellationToken requestToken)
        {
            _server = server;
            Context = context;
            DetachedLifetime = detachedLifetime;
            _cancelHandler = cancelHandler;
            _requestToken = requestToken;
            _cancellation = detachedLifetime
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            _executionToken = _cancellation.Token;
            _responseToken = _responseStop.Token;
        }

        internal RequestContext Context { get; }
        internal bool DetachedLifetime { get; }

        internal void Start() => _ = Task.Run(() => RunAsync(CancellationToken.None), CancellationToken.None);

        internal IAsyncEnumerable<StreamResponse> ReadAllAsync(CancellationToken cancellationToken) =>
            _responses.Reader.ReadAllAsync(cancellationToken);

        internal void Detach()
        {
            lock (_gate)
            {
                if (!_finished)
                {
                    // Only the response-channel writer observes this token, not agent code.
                    _responseStop.Cancel();
                }
            }
        }

        internal Task CancelAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var callbacks = _finished ? Task.CompletedTask : _cancelCallbacks ??= _cancellation.CancelAsync();
                return callbacks.WaitAsync(cancellationToken);
            }
        }

        internal async Task StopAsync(CancellationToken cancellationToken)
        {
            Detach();
            try
            {
                await CancelAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        internal void Abandon()
        {
            Volatile.Write(ref _abandoned, true);
            _events.Complete();
            Detach();
        }

        internal async Task AbortAsync(CancellationToken cancellationToken)
        {
            Abandon();
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            Task? producer = null;
            Exception? failure = null;
            using var initialization = CancellationTokenSource.CreateLinkedTokenSource(_requestToken, _executionToken);
            try
            {
                if (!_cancelHandler &&
                    Context.IsContinuation &&
                    _server._options.AutoAppendHistory)
                {
                    await _server.ApplyEventAsync(new StreamResponse { Message = Context.Message },
                        Context, initialization.Token).ConfigureAwait(false);
                }

                producer = Task.Run(() => ExecuteHandlerAsync(_executionToken), CancellationToken.None);
                if (Context.StreamingResponse &&
                    Context.Task is not null)
                {
                    var initial = await _server._taskStore.GetTaskAsync(Context.TaskId, initialization.Token).ConfigureAwait(false)
                        ?? throw new A2AException("Task not found.", A2AErrorCode.TaskNotFound);
                    await WriteResponseAsync(new StreamResponse { Task = initial }, _responseToken).ConfigureAwait(false);
                    if (initial.Status.State.IsTerminal())
                    {
                        Detach();
                        _responses.Writer.TryComplete();
                    }
                }

                // One reader owns persistence/publication for the entire execution. A paused
                // or disconnected SDK enumerator never owns this producer's bounded queue.
                await foreach (var response in _events.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (Volatile.Read(ref _abandoned))
                    {
                        throw new OperationCanceledException("Agent event publication was abandoned.", _executionToken);
                    }
                    var persistenceToken = DetachedLifetime ? CancellationToken.None : _executionToken;
                    await _server.ApplyEventAsync(response, Context, persistenceToken).ConfigureAwait(false);
                    await WriteResponseAsync(response, _responseToken).ConfigureAwait(false);
                }
                await producer.ConfigureAwait(false);
                _server.RecordUnusedInlinePush(Context);
            }
            catch (Exception exception)
            {
                failure = exception;
                if (exception is not OperationCanceledException ||
                    !_executionToken.IsCancellationRequested)
                {
                    _server._logger.BackgroundEventProcessingFailed(exception, Context.TaskId);
                }

                // A failed consumer must release blocked writers, cancel and join the handler
                // before its CTS or tracking entry can be retired.
                _events.Complete();
                try
                {
                    await CancelAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cancellationError)
                {
                    _server._logger.BackgroundEventProcessingFailed(cancellationError, Context.TaskId);
                }
                if (producer is not null)
                {
                    try
                    {
                        await producer.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_executionToken.IsCancellationRequested)
                    {
                        // The producer observed the cancellation requested above.
                    }
                    catch (ChannelClosedException)
                    {
                        // Closing the failed consumer's queue releases cleanup writes too.
                    }
                    catch (Exception producerError)
                    {
                        if (!ReferenceEquals(exception, producerError))
                        {
                            _server._logger.BackgroundEventProcessingFailed(producerError, Context.TaskId);
                        }
                    }
                }
                bool markFailed = DetachedLifetime &&
                    producer is not null &&
                    exception is not (PushNotificationAdmissionException or OperationCanceledException) &&
                    !Volatile.Read(ref _abandoned);
                if (markFailed)
                {
                    await _server.TryTransitionToFailedAsync(Context, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Task callbacks;
                lock (_gate)
                {
                    _finished = true;
                    callbacks = _cancelCallbacks ?? Task.CompletedTask;
                }
                try
                {
                    await callbacks.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                    _server._logger.BackgroundEventProcessingFailed(exception, Context.TaskId);
                }
                _cancellation.Dispose();
                _responseStop.Dispose();
                _responses.Writer.TryComplete(failure);
                _server.CompleteExecution(this);
                _completion.TrySetResult();
            }
        }

        private async Task ExecuteHandlerAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_cancelHandler)
                {
                    await _server._handler.CancelAsync(Context, _events, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _server._handler.ExecuteAsync(Context, _events, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _events.Complete();
            }
        }

        private async Task WriteResponseAsync(StreamResponse response, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                await _responses.Writer.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Detached consumers do not receive more responses. Persistence and push continue.
            }
        }
    }
}
