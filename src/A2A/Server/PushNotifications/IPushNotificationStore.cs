namespace A2A;

/// <summary>Stores task-scoped push registrations. Implementations must isolate tenants as appropriate.</summary>
public interface IPushNotificationStore
{
    /// <summary>Atomically creates a registration with a generation-scoped deletion signal, rejecting duplicate IDs.</summary>
    /// <param name="config">Identified secret-bearing configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PushNotificationConfigSnapshot> SaveAsync(TaskPushNotificationConfig config, CancellationToken cancellationToken = default);

    /// <summary>Gets an independent snapshot, or null if absent.</summary>
    /// <param name="taskId">Task identifier.</param>
    /// <param name="configId">Configuration identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PushNotificationConfigSnapshot?> GetAsync(string taskId, string configId, CancellationToken cancellationToken = default);

    /// <summary>Gets an independent, atomic snapshot of all active registrations for delivery.</summary>
    /// <param name="taskId">Task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PushNotificationConfigSnapshot>> GetAllAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>Lists independent configurations with opaque pagination cursors.</summary>
    /// <param name="request">Task and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListTaskPushNotificationConfigsResponse> ListAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Idempotently deletes a registration and signals its snapshots before returning. A supplied version deletes only that generation.</summary>
    /// <param name="taskId">Task identifier.</param>
    /// <param name="configId">Configuration identifier.</param>
    /// <param name="version">Expected generation, or null for unconditional deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string taskId, string configId, string? version = null, CancellationToken cancellationToken = default);
}
