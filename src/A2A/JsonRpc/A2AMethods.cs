namespace A2A;

/// <summary>
/// Constants for A2A JSON-RPC method names.
/// </summary>
public static class A2AMethods
{
    /// <summary>
    /// Method for sending messages to agents.
    /// </summary>
    public const string MessageSend = "message/send";

    /// <summary>
    /// Method for streaming messages from agents.
    /// </summary>
    public const string MessageStream = "message/stream";

    /// <summary>
    /// Method for retrieving task information.
    /// </summary>
    public const string TaskGet = "tasks/get";

    /// <summary>
    /// Method for canceling tasks.
    /// </summary>
    public const string TaskCancel = "tasks/cancel";

    /// <summary>
    /// Method for resubscribing to task updates.
    /// </summary>
    public const string TaskResubscribe = "tasks/resubscribe";

    /// <summary>
    /// Method for setting push notification configuration.
    /// </summary>
    public const string TaskPushNotificationConfigSet = "tasks/pushNotificationConfig/set";

    /// <summary>
    /// Method for getting push notification configuration.
    /// </summary>
    public const string TaskPushNotificationConfigGet = "tasks/pushNotificationConfig/get";

    /// <summary>
    /// Determines if a method requires streaming response handling.
    /// </summary>
    /// <param name="method">The method name to check.</param>
    /// <returns>True if the method requires streaming, false otherwise.</returns>
    public static bool IsStreamingMethod(string method) => method is MessageStream or TaskResubscribe;

    /// <summary>
    /// Determines if a method name is valid for A2A JSON-RPC.
    /// </summary>
    /// <param name="method">The method name to validate.</param>
    /// <returns>True if the method is valid, false otherwise.</returns>
    public static bool IsValidMethod(string method) => method is MessageSend or MessageStream or TaskGet or TaskCancel or TaskResubscribe or TaskPushNotificationConfigSet or TaskPushNotificationConfigGet;
}