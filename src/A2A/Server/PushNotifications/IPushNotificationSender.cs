namespace A2A;

/// <summary>Accepts immutable, process-local or durable delivery work independently of agent execution.</summary>
/// <remarks>
/// Implementations must recheck registration versions before attempts and honor generation-scoped deletion.
/// A successful enqueue is acceptance for delivery, not remote acknowledgement. Retention is sender policy:
/// the built-in HTTP sender removes terminal registrations after final delivery handling by default, while other
/// implementations may retain them. Admission failures must be surfaced rather than counted as accepted work.
/// If a registration is removed before admission, throw <see cref="System.OperationCanceledException"/>
/// using its removal token instead of completing successfully. Deletion after acceptance may suppress delivery.
/// Durable implementations must persist work before successful enqueue completion.
/// This does not make task persistence and notification admission atomic.
/// See <see href="https://github.com/a2aproject/a2a-dotnet/blob/main/docs/durable-push-notifications.md">durable sender implementation guidance</see>.
/// </remarks>
public interface IPushNotificationSender
{
    /// <summary>Accepts a snapshot of the payload; throws if work could not be accepted.</summary>
    /// <param name="registration">Immutable registration generation.</param>
    /// <param name="response">Event to snapshot before returning.</param>
    /// <param name="cancellationToken">Enqueue cancellation token.</param>
    Task EnqueueAsync(PushNotificationConfigSnapshot registration, StreamResponse response, CancellationToken cancellationToken = default);
}
