namespace A2A;

/// <summary>Committed continuation progress. Acceptance does not imply remote webhook receipt.</summary>
/// <param name="Task">The last successfully persisted task, or the initial snapshot if no event was persisted.</param>
/// <param name="PersistedEventCount">Number of successfully persisted producer events.</param>
/// <param name="AcceptedPushNotificationCount">Number of event/registration pairs accepted by the sender.</param>
public sealed record TaskContinuationResult(
    AgentTask Task, int PersistedEventCount, int AcceptedPushNotificationCount);
