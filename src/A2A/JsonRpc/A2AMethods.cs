namespace A2A;

/// <summary>
/// Provides constants for A2A JSON-RPC method names and utility methods for method classification.
/// </summary>
public static class A2AMethods
{
    /// <summary>
    /// The method name for sending a message to an agent.
    /// </summary>
    public const string MessageSend = "message/send";

    /// <summary>
    /// The method name for sending a message and receiving a stream of events.
    /// </summary>
    public const string MessageStream = "message/stream";

    /// <summary>
    /// The method name for retrieving a task by its identifier.
    /// </summary>
    public const string TaskGet = "tasks/get";

    /// <summary>
    /// The method name for cancelling a task.
    /// </summary>
    public const string TaskCancel = "tasks/cancel";

    /// <summary>
    /// The method name for resubscribing to a task's event stream.
    /// </summary>
    public const string TaskResubscribe = "tasks/resubscribe";

    /// <summary>
    /// The method name for setting push notification configuration for a task.
    /// </summary>
    public const string TaskPushNotificationConfigSet = "tasks/pushnotificationconfig/set";

    /// <summary>
    /// The method name for getting push notification configuration for a task.
    /// </summary>
    public const string TaskPushNotificationConfigGet = "tasks/pushnotificationconfig/get";

    /// <summary>
    /// Determines whether the specified method returns a stream of events.
    /// </summary>
    /// <param name="method">The method name to check.</param>
    /// <returns>True if the method is a streaming method; otherwise, false.</returns>
    public static bool IsStreamingMethod(string method) => method is MessageStream or TaskResubscribe;
}