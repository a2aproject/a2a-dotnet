namespace A2A.AspNetCore;

/// <summary>Behavior when the bounded process-local delivery queue is full.</summary>
public enum PushNotificationQueueFullBehavior
{
    /// <summary>Reject admission with an error. Callers must not treat rejected work as accepted.</summary>
    Reject,
    /// <summary>Wait outside the task lock for capacity, subject to cancellation and shutdown.</summary>
    Wait,
}

/// <summary>Bounded process-local delivery policy. Changes after construction do not affect a running sender.</summary>
public sealed class PushNotificationDeliveryOptions
{
    /// <summary>Per-attempt deadline, default 15 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Total attempts, including the first, default three.</summary>
    public int MaximumAttempts { get; set; } = 3;
    /// <summary>First retry delay, default one second.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Retry delay multiplier, default two.</summary>
    public double BackoffFactor { get; set; } = 2;
    /// <summary>Maximum individual retry delay, default 30 seconds.</summary>
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum buffered items, excluding the active delivery, default 256.</summary>
    public int QueueCapacity { get; set; } = 256;
    /// <summary>Full-queue policy, default wait. No item is silently dropped as successful delivery.</summary>
    public PushNotificationQueueFullBehavior QueueFullBehavior { get; set; } = PushNotificationQueueFullBehavior.Wait;
    /// <summary>Graceful drain period before canceling remaining work, default five seconds.</summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Compatibility token header. Null or empty disables it.</summary>
    public string? LegacyTokenHeaderName { get; set; } = "X-A2A-Notification-Token";

    internal PushNotificationDeliveryOptions Snapshot()
    {
        bool invalid = RequestTimeout <= TimeSpan.Zero ||
            RequestTimeout.TotalMilliseconds > uint.MaxValue - 1 ||
            MaximumAttempts < 1 ||
            InitialRetryDelay < TimeSpan.Zero ||
            MaximumRetryDelay < TimeSpan.Zero ||
            MaximumRetryDelay.TotalMilliseconds > uint.MaxValue - 1 ||
            !double.IsFinite(BackoffFactor) ||
            BackoffFactor < 1 ||
            QueueCapacity < 1 ||
            ShutdownDrainTimeout < TimeSpan.Zero ||
            ShutdownDrainTimeout.TotalMilliseconds > uint.MaxValue - 1 ||
            !Enum.IsDefined(QueueFullBehavior);
        if (invalid)
        {
            throw new ArgumentException("Invalid push notification delivery policy.");
        }
        if (!string.IsNullOrEmpty(LegacyTokenHeaderName))
        {
            var reserved = new[] { "Authorization", "Host", "Content-Type", "Content-Length", "Transfer-Encoding",
                "Connection", "Proxy-Authorization", "Cookie", "Expect", "Upgrade", "Trailer", "TE" };
            bool invalidHeader = LegacyTokenHeaderName.Any(ch =>
                !char.IsAsciiLetterOrDigit(ch) && !"!#$%&'*+-.^_`|~".Contains(ch)) ||
                reserved.Contains(LegacyTokenHeaderName, StringComparer.OrdinalIgnoreCase);
            if (invalidHeader)
            {
                throw new ArgumentException("Invalid compatibility token header name.");
            }
        }
        return new PushNotificationDeliveryOptions
        {
            RequestTimeout = RequestTimeout,
            MaximumAttempts = MaximumAttempts,
            InitialRetryDelay = InitialRetryDelay,
            BackoffFactor = BackoffFactor,
            MaximumRetryDelay = MaximumRetryDelay,
            QueueCapacity = QueueCapacity,
            QueueFullBehavior = QueueFullBehavior,
            ShutdownDrainTimeout = ShutdownDrainTimeout,
            LegacyTokenHeaderName = LegacyTokenHeaderName,
        };
    }
}
