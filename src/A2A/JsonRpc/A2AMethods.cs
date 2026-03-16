namespace A2A;

/// <summary>Defines the A2A protocol method name constants.</summary>
public static class A2AMethods
{
    /// <summary>Send a message to an agent.</summary>
    public const string SendMessage = "SendMessage";

    /// <summary>Send a streaming message to an agent.</summary>
    public const string SendStreamingMessage = "SendStreamingMessage";

    /// <summary>Get a task by ID.</summary>
    public const string GetTask = "GetTask";

    /// <summary>List tasks with pagination.</summary>
    public const string ListTasks = "ListTasks";

    /// <summary>Cancel a task.</summary>
    public const string CancelTask = "CancelTask";

    /// <summary>Subscribe to task updates.</summary>
    public const string SubscribeToTask = "SubscribeToTask";

    /// <summary>Create a push notification configuration.</summary>
    public const string CreateTaskPushNotificationConfig = "CreateTaskPushNotificationConfig";

    /// <summary>Get a push notification configuration.</summary>
    public const string GetTaskPushNotificationConfig = "GetTaskPushNotificationConfig";

    /// <summary>List push notification configurations.</summary>
    public const string ListTaskPushNotificationConfig = "ListTaskPushNotificationConfig";

    /// <summary>Delete a push notification configuration.</summary>
    public const string DeleteTaskPushNotificationConfig = "DeleteTaskPushNotificationConfig";

    /// <summary>Get extended agent card.</summary>
    public const string GetExtendedAgentCard = "GetExtendedAgentCard";

    /// <summary>
    /// Determines if a method requires streaming response handling.
    /// </summary>
    /// <param name="method">The method name to check.</param>
    /// <returns>True if the method requires streaming, false otherwise.</returns>
    public static bool IsStreamingMethod(string method) => method is SendStreamingMessage or SubscribeToTask;

    /// <summary>
    /// Determines if a method is a push notification method.
    /// </summary>
    /// <param name="method">The method name to check.</param>
    /// <returns>True if the method is a push notification method, false otherwise.</returns>
    public static bool IsPushNotificationMethod(string method) => method is CreateTaskPushNotificationConfig or GetTaskPushNotificationConfig or ListTaskPushNotificationConfig or DeleteTaskPushNotificationConfig;

    /// <summary>
    /// Determines if a method name is valid for A2A JSON-RPC.
    /// </summary>
    /// <param name="method">The method name to validate.</param>
    /// <returns>True if the method is valid, false otherwise.</returns>
    public static bool IsValidMethod(string method) =>
        method is SendMessage
            or GetTask
            or ListTasks
            or CancelTask
            or GetExtendedAgentCard ||
        IsStreamingMethod(method) ||
        IsPushNotificationMethod(method);
}