namespace A2A;

public static class A2AMethods
{
    public const string MessageSend = "message/send";
    public const string MessageStream = "message/stream";
    public const string TaskGet = "tasks/get";
    public const string TaskCancel = "tasks/cancel";
    public const string TaskResubscribe = "tasks/resubscribe";
    public const string TaskPushNotificationConfigSet = "tasks/pushnotificationconfig/set";
    public const string TaskPushNotificationConfigGet = "tasks/pushnotificationconfig/get";


    public static bool IsStreamingMethod(string method)
    {
        return method == MessageStream || method == TaskResubscribe;
    }
}

