namespace A2A;

/// <summary>Accepts immutable, process-local or durable delivery work independently of agent execution.</summary>
/// <remarks>
/// Implementations must recheck registration versions before attempts and honor generation-scoped deletion.
/// A successful enqueue is acceptance for delivery, not remote acknowledgement. Delivery does not
/// own configuration retention. Admission failures must be surfaced rather than counted as accepted work.
/// </remarks>
public interface IPushNotificationSender
{
    /// <summary>Accepts a snapshot of the payload; throws if work could not be accepted.</summary>
    /// <param name="registration">Immutable registration generation.</param>
    /// <param name="response">Event to snapshot before returning.</param>
    /// <param name="cancellationToken">Enqueue cancellation token.</param>
    Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default);
}
