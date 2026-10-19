namespace A2A.UnitTests.Server;

public sealed partial class TaskContinuationTests
{
    [Fact]
    public async Task PausedStreamingResponse_CanBeCanceledWithoutResumingOrDisposingTheStream()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producerStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.OnEnqueue = (_, response, _) =>
        {
            if (response.Message?.MessageId == "blocked-response")
            {
                blockedResponse.TrySetResult();
            }
            return Task.CompletedTask;
        };
        var handler = new Handler
        {
            Execute = async (_, queue, cancellationToken) =>
            {
                try
                {
                    await release.Task.WaitAsync(cancellationToken);
                    await queue.EnqueueMessageAsync(Message("buffered-response"), cancellationToken);
                    await queue.EnqueueMessageAsync(Message("blocked-response"), cancellationToken);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    producerStopped.TrySetResult();
                }
            },
        };
        await using var server = Server(handler);
        await using var stream = server.SendStreamingMessageAsync(new() { Message = Message("inbound") }, null).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(stream.Current.Task);
            release.TrySetResult();
            await blockedResponse.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var canceled = await server.CancelTaskAsync(new() { Id = "task" }, null).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(TaskState.Canceled, canceled.Status.State);
            Assert.True(producerStopped.Task.IsCompleted);
            Assert.Contains(canceled.History!, message => message.MessageId == "blocked-response");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InitialHistoryBlockedInAdmission_CanBeCanceledOrAbandonedWithoutInvokingHandler(bool streaming, bool shutdown)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool handlerCalled = false;
        _sender.OnEnqueue = async (_, response, cancellationToken) =>
        {
            if (response.Message?.MessageId == "inbound")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var handler = new Handler
        {
            Execute = (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            },
        };
        await using var server = Server(handler);
        var request = new SendMessageRequest
        {
            Message = Message("inbound"),
            Configuration = new() { ReturnImmediately = true },
        };
        await using var stream = server.SendStreamingMessageAsync(request, null).GetAsyncEnumerator();
        Task pendingRequest = streaming ? stream.MoveNextAsync().AsTask() : server.SendMessageAsync(request, null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (shutdown)
            {
                using var deadline = new CancellationTokenSource();
                var stopping = server.StopAsync(deadline.Token);
                Assert.False(stopping.IsCompleted);
                await deadline.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
                await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                var canceled = await server.CancelTaskAsync(new() { Id = "task" }, null).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(TaskState.Canceled, canceled.Status.State);
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRequest.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(handlerCalled);
            Assert.DoesNotContain("inbound", _sender.Events);
            Assert.Contains((await _tasks.GetTaskAsync("task"))!.History!, message => message.MessageId == "inbound");
        }
        finally
        {
            release.TrySetResult();
            if (streaming)
            {
                try
                {
                    await pendingRequest.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    // Join the pending MoveNext before disposing the stream.
                }
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DetachedInboundBlockedInAdmission_CanBeCanceledOrAbandonedAndJoined(bool streaming, bool shutdown)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producerStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.OnEnqueue = async (_, response, cancellationToken) =>
        {
            if (response.Message?.MessageId == "blocked")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var handler = new Handler
        {
            Execute = async (_, queue, cancellationToken) =>
            {
                try
                {
                    await queue.EnqueueTaskAsync((await _tasks.GetTaskAsync("task", cancellationToken))!, cancellationToken);
                    await queue.EnqueueMessageAsync(Message("blocked"), cancellationToken);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    producerStopped.TrySetResult();
                }
            },
        };
        await using var server = Server(handler);
        var request = new SendMessageRequest
        {
            Message = Message("inbound"),
            Configuration = new() { ReturnImmediately = true },
        };
        try
        {
            if (streaming)
            {
                await using var stream = server.SendStreamingMessageAsync(request, null).GetAsyncEnumerator();
                Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.NotNull((await server.SendMessageAsync(request, null).WaitAsync(TimeSpan.FromSeconds(5))).Task);
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (shutdown)
            {
                using var deadline = new CancellationTokenSource();
                var stopping = server.StopAsync(deadline.Token);
                await producerStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(stopping.IsCompleted);
                await deadline.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
                await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                var canceled = await server.CancelTaskAsync(new() { Id = "task" }, null).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(TaskState.Canceled, canceled.Status.State);
            }
            Assert.True(producerStopped.Task.IsCompleted);
            Assert.DoesNotContain("blocked", _sender.Events);
            Assert.Contains((await _tasks.GetTaskAsync("task"))!.History!, message => message.MessageId == "blocked");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InboundAndProactiveExecutions_SerializeAndRefreshTheTask(bool inboundFirst)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler
        {
            Execute = async (context, queue, cancellationToken) =>
            {
                if (inboundFirst)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    Assert.Contains(context.Task!.History!, message => message.MessageId == "proactive");
                }
                await queue.EnqueueMessageAsync(Message("inbound-reply"), cancellationToken);
                await queue.EnqueueStatusUpdateAsync(new()
                {
                    TaskId = context.TaskId,
                    ContextId = context.ContextId,
                    Status = new() { State = TaskState.InputRequired },
                }, cancellationToken);
            },
        };
        await using var server = Server(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new SendMessageRequest { Message = Message("inbound") };
        bool proactiveEntered = false;
        Task<TaskContinuationResult> ContinueAsync() => server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            proactiveEntered = true;
            if (!inboundFirst)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                Assert.Contains(context.Task.History!, message => message.MessageId == "inbound-reply");
                Assert.Equal(TaskState.InputRequired, context.Task.Status.State);
            }
            await context.EventQueue.EnqueueMessageAsync(Message("proactive"), cancellationToken);
        }, timeout.Token);

        Task first = inboundFirst ? server.SendMessageAsync(request, null, timeout.Token) : ContinueAsync();
        Task? second = null;
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            second = inboundFirst ? ContinueAsync() : server.SendMessageAsync(request, null, timeout.Token);
            Assert.False(second.IsCompleted);
            if (inboundFirst)
            {
                Assert.False(proactiveEntered);
            }
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(first, second!).WaitAsync(timeout.Token);
        var task = (await _tasks.GetTaskAsync("task"))!;
        Assert.Equal(4, task.History!.Count);
        Assert.Equal(4, task.History.Select(message => message.MessageId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(task.History, message => message.MessageId == "original");
        Assert.Contains(task.History, message => message.MessageId == "inbound");
        Assert.Contains(task.History, message => message.MessageId == "inbound-reply");
        Assert.Contains(task.History, message => message.MessageId == "proactive");
    }

    [Fact]
    public async Task ConcurrentContinuations_DoNotInterleavePersistedEventsOrPushAdmission()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.OnEnqueue = async (_, response, cancellationToken) =>
        {
            if (response.Message?.MessageId == "first-start")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        await using var server = Server();
        await using var secondServer = Server();
        var first = server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            await context.EventQueue.EnqueueMessageAsync(Message("first-start"), cancellationToken);
            await context.EventQueue.EnqueueMessageAsync(Message("first-end"), cancellationToken);
        });
        Task<TaskContinuationResult>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = secondServer.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                Assert.Equal(["original", "first-start", "first-end"], context.Task.History!.Select(message => message.MessageId));
                await context.EventQueue.EnqueueMessageAsync(Message("second"), cancellationToken);
            });
            Assert.False(second.IsCompleted);
            Assert.Equal(["original", "first-start"], (await _tasks.GetTaskAsync("task"))!.History!.Select(message => message.MessageId));
            // The execution gate is not the persistence/subscriber lock.
            using var lease = await _notifier.AcquireTaskLockAsync("task");
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["first-start", "first-end", "second"], _sender.Events);
        Assert.Equal(4, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedInboundExecution_HoldsGateUntilProductionAndPublicationEnd(bool streaming)
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler
        {
            Execute = async (context, queue, cancellationToken) =>
            {
                await queue.EnqueueTaskAsync((await _tasks.GetTaskAsync("task", cancellationToken))!, cancellationToken);
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                await queue.EnqueueMessageAsync(Message("late-inbound"), cancellationToken);
                await queue.EnqueueStatusUpdateAsync(new()
                {
                    TaskId = context.TaskId,
                    ContextId = context.ContextId,
                    Status = new() { State = TaskState.InputRequired },
                }, cancellationToken);
            },
        };
        await using var server = Server(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new SendMessageRequest
        {
            Message = Message("inbound"),
            Configuration = new() { ReturnImmediately = true },
        };
        Task<TaskContinuationResult>? continuation = null;
        try
        {
            if (streaming)
            {
                await using var stream = server.SendStreamingMessageAsync(request, null, timeout.Token).GetAsyncEnumerator(timeout.Token);
                Assert.True(await stream.MoveNextAsync());
            }
            else
            {
                Assert.NotNull((await server.SendMessageAsync(request, null, timeout.Token)).Task);
            }
            await entered.Task.WaitAsync(timeout.Token);
            continuation = server.ContinueTaskAsync("task", async (context, cancellationToken) =>
            {
                Assert.Contains(context.Task.History!, message => message.MessageId == "late-inbound");
                await context.EventQueue.EnqueueMessageAsync(Message("proactive"), cancellationToken);
            }, timeout.Token);
            Assert.False(continuation.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await continuation!.WaitAsync(timeout.Token);
        Assert.Equal(["original", "inbound", "late-inbound", "proactive"], result.Task.History!.Select(message => message.MessageId));
        Assert.Equal("proactive", _sender.Events[^1]);
    }

    [Fact]
    public async Task DifferentTasks_CanProduceWhileAnotherTaskIsBlocked()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        await _tasks.SaveTaskAsync("other", new() { Id = "other", ContextId = "other-context", Status = new() { State = TaskState.Working } });
        var registration = Config();
        registration.TaskId = "other";
        await _registrations.SaveAsync(registration);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = Server();
        var first = server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await context.EventQueue.EnqueueMessageAsync(Message("first"), cancellationToken);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var other = await server.ContinueTaskAsync("other", (context, cancellationToken) =>
                context.EventQueue.EnqueueMessageAsync(Message("independent", "other", "other-context"), cancellationToken).AsTask())
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, other.PersistedEventCount);
            Assert.False(first.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["independent", "first"], _sender.Events);
    }

    [Fact]
    public async Task CancellationWhileWaitingForExecutionGate_DoesNotInvokeProducer()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = Server();
        using var cancellation = new CancellationTokenSource();
        var first = server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await context.EventQueue.EnqueueMessageAsync(Message("first"), cancellationToken);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = server.ContinueTaskAsync("task",
                (_, _) => throw new InvalidOperationException("must not run"), cancellation.Token);
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsAsync<TaskContinuationException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(TaskContinuationFailureKind.Cancellation, error.FailureKind);
            Assert.Null(error.Progress);
        }
        finally
        {
            release.TrySetResult();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(_sender.Events);
    }

    [Fact]
    public async Task WaitingContinuation_RechecksTerminalStateWithoutRunningProducer()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = Server();
        var first = server.ContinueTaskAsync("task", async (context, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await new TaskUpdater(context.EventQueue, context.TaskId, context.ContextId).CompleteAsync(cancellationToken: cancellationToken);
        });
        Task<TaskContinuationResult>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = server.ContinueTaskAsync("task", (_, _) => throw new InvalidOperationException("must not run"));
        }
        finally
        {
            release.TrySetResult();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() => second!);
        Assert.Equal(TaskContinuationFailureKind.NonContinuableTask, error.FailureKind);
    }

    [Fact]
    public async Task Cancellation_ClosesBlockedWritersAndJoinsProducerWithCommittedProgress()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool exited = false;
        _sender.OnEnqueue = async (_, _, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        };
        await using var server = Server();
        using var cancellation = new CancellationTokenSource();
        var continuation = server.ContinueTaskAsync("task", async (context, _) =>
        {
            try
            {
                for (int index = 0; index < 100; index++)
                {
                    var write = context.EventQueue.EnqueueMessageAsync(Message($"message-{index}"), CancellationToken.None);
                    if (!write.IsCompleted)
                    {
                        blockedWriter.TrySetResult();
                    }
                    await write;
                }
            }
            finally
            {
                exited = true;
            }
        }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await blockedWriter.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() => continuation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(TaskContinuationFailureKind.Cancellation, error.FailureKind);
        Assert.True(exited);
        Assert.Equal(1, error.Progress!.PersistedEventCount);
        Assert.Equal(0, error.Progress.AcceptedPushNotificationCount);
        Assert.Equal(2, (await _tasks.GetTaskAsync("task"))!.History!.Count);
    }

    [Fact]
    public async Task Shutdown_CancelsAndJoinsProactiveExecution()
    {
        await SeedAsync();
        await _registrations.SaveAsync(Config());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool exited = false;
        await using var server = Server();
        var continuation = server.ContinueTaskAsync("task", async (_, cancellationToken) =>
        {
            try
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                exited = true;
            }
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<TaskContinuationException>(() => continuation);
        Assert.Equal(TaskContinuationFailureKind.Cancellation, error.FailureKind);
        Assert.True(exited);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            server.ContinueTaskAsync("task", (_, _) => Task.CompletedTask));
    }
}
