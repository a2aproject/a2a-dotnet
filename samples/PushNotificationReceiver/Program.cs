using PushNotificationReceiver;

if (args.Contains("--demo", StringComparer.Ordinal))
{
    await new SdkPushNotificationDemo().RunAsync(CancellationToken.None);
    return;
}

var taskId = Environment.GetEnvironmentVariable("A2A_PUSH_TASK_ID")
    ?? throw new InvalidOperationException("Set A2A_PUSH_TASK_ID to the task to receive.");
var credential = Environment.GetEnvironmentVariable("A2A_PUSH_BEARER_TOKEN")
    ?? throw new InvalidOperationException("Set A2A_PUSH_BEARER_TOKEN to a fresh, local test credential.");
var legacyToken = Environment.GetEnvironmentVariable("A2A_PUSH_NOTIFICATION_TOKEN");

var receiver = new WebhookReceiver(taskId, credential, notification =>
{
    var state = notification.PayloadCase switch
    {
        A2A.StreamResponseCase.Task => notification.Task!.Status.State.ToString(),
        A2A.StreamResponseCase.StatusUpdate => notification.StatusUpdate!.Status.State.ToString(),
        _ => "n/a",
    };
    Console.WriteLine($"Received {notification.PayloadCase}: state={state}");
}, legacyToken);

await using var app = receiver.CreateApplication();
await app.StartAsync();
Console.WriteLine($"Receiver only: POST notifications to {app.Urls.Single()}/notifications");
await app.WaitForShutdownAsync();
