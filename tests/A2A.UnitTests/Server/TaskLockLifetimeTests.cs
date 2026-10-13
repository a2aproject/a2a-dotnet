namespace A2A.UnitTests.Server;

public sealed class TaskLockLifetimeTests
{
    private readonly ChannelEventNotifier _notifier = new();

    [Fact]
    public async Task RemovingLastSubscriber_DoesNotRetireAHeldTaskLock()
    {
        var channel = _notifier.CreateChannel("task");
        var first = await _notifier.AcquireTaskLockAsync("task");
        Task<IDisposable>? second = null;
        try
        {
            _notifier.RemoveChannel("task", channel);
            second = _notifier.AcquireTaskLockAsync("task");
            Assert.False(second.IsCompleted, "A second lease must not enter while the first is held.");
        }
        finally
        {
            first.Dispose();
            if (second is not null)
            {
                using var released = await second.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public async Task WaitingLeaseAndNewSubscriber_KeepTheSameMutualExclusionDomain()
    {
        var old = _notifier.CreateChannel("task");
        var first = await _notifier.AcquireTaskLockAsync("task");
        var second = _notifier.AcquireTaskLockAsync("task");
        _notifier.RemoveChannel("task", old);
        var current = _notifier.CreateChannel("task");
        var third = _notifier.AcquireTaskLockAsync("task");
        try
        {
            Assert.False(second.IsCompleted);
            Assert.False(third.IsCompleted);
            first.Dispose();
            using var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(third.IsCompleted);
        }
        finally
        {
            first.Dispose();
            if (second.IsCompletedSuccessfully)
            {
                (await second).Dispose();
            }
            using var thirdLease = await third.WaitAsync(TimeSpan.FromSeconds(5));
            _notifier.RemoveChannel("task", current);
        }
    }
}
