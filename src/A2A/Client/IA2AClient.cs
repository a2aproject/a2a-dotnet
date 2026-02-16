namespace A2A;

/// <summary>Defines the client interface for communicating with an A2A agent.</summary>
public interface IA2AClient
{
    /// <summary>Sends a message to the agent.</summary>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send message response.</returns>
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a streaming message to the agent.</summary>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous enumerable of streaming responses.</returns>
    IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a task by ID.</summary>
    /// <param name="request">The get task request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The agent task.</returns>
    Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists tasks with pagination.</summary>
    /// <param name="request">The list tasks request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The list tasks response.</returns>
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels a task.</summary>
    /// <param name="request">The cancel task request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The canceled agent task.</returns>
    Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to task updates.</summary>
    /// <param name="request">The subscribe to task request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous enumerable of streaming responses.</returns>
    IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates a push notification configuration.</summary>
    /// <param name="request">The create push notification config request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created push notification configuration.</returns>
    Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a push notification configuration.</summary>
    /// <param name="request">The get push notification config request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The push notification configuration.</returns>
    Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists push notification configurations.</summary>
    /// <param name="request">The list push notification configs request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The list push notification config response.</returns>
    Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a push notification configuration.</summary>
    /// <param name="request">The delete push notification config request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets the extended agent card.</summary>
    /// <param name="request">The get extended agent card request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The extended agent card.</returns>
    Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default);
}
