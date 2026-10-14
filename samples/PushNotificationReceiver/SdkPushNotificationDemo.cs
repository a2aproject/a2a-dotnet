using A2A;
using A2A.AspNetCore;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace PushNotificationReceiver;

internal sealed class SdkPushNotificationDemo
{
    private readonly LocalReceiverUrlValidator _policy = new();
    private readonly string _credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly string _contextId = Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<StreamResponse> _received = new();

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var agent = CreateAgent();
        await agent.StartAsync(cancellationToken);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var client = new A2AClient(new Uri(new Uri(agent.Urls.Single()), "/agent"), http);
        var task = (await client.SendMessageAsync(Request(), cancellationToken)).Task
            ?? throw new InvalidOperationException("The demo agent did not create a task.");
        await using var receiver = new WebhookReceiver(task.Id, _credential, notification =>
        {
            _received.Enqueue(notification);
            Console.WriteLine($"SDK webhook received: {notification.PayloadCase}");
        }).CreateApplication();
        await receiver.StartAsync(cancellationToken);
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
        _policy.SetReceiver(callback);

        var config = await client.CreateTaskPushNotificationConfigAsync(new TaskPushNotificationConfig
        {
            TaskId = task.Id,
            Id = "demo",
            Url = callback.AbsoluteUri,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = _credential },
        }, cancellationToken);
        var redacted = await client.GetTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = config.Id! }, cancellationToken);
        if (redacted.Authentication?.Credentials is not null)
        {
            throw new InvalidOperationException("The configuration read exposed credentials.");
        }
        var completed = await client.SendMessageAsync(Request(task.Id), cancellationToken);
        var sender = (HttpPushNotificationSender)agent.Services.GetRequiredService<IPushNotificationSender>();
        await sender.StopAsync(cancellationToken);
        var remaining = await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id }, cancellationToken);
        bool succeeded = (completed.Task?.Status.State).GetValueOrDefault() == TaskState.Completed &&
            _received.Count == 3 &&
            remaining.Configs!.Count == 1;
        if (!succeeded)
        {
            throw new InvalidOperationException("SDK delivery or configuration retention did not match the contract.");
        }
        await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = config.Id! }, cancellationToken);
        if ((await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id }, cancellationToken)).Configs!.Count != 0)
        {
            throw new InvalidOperationException("Explicit configuration deletion did not complete.");
        }
        Console.WriteLine("SDK push demo passed: 3 webhook events, task Completed, configuration explicitly deleted.");
        await receiver.StopAsync(cancellationToken);
        await agent.StopAsync(cancellationToken);
    }

    private WebApplication CreateAgent()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IPushNotificationUrlValidator>(_policy);
        builder.Services.AddA2AAgent<DemoAgent>(new AgentCard
        {
            Name = "Local push demo",
            Capabilities = new AgentCapabilities { PushNotifications = true },
        });
        var app = builder.Build();
        app.MapA2A("/agent");
        return app;
    }

    private SendMessageRequest Request(string? taskId = null) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = _contextId,
            TaskId = taskId,
            Role = Role.User,
            Parts = [Part.FromText(taskId is null ? "start" : "finish")],
        },
    };

    private sealed class DemoAgent : IAgentHandler
    {
        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            if (context.Task is null)
            {
                await updater.SubmitAsync(cancellationToken: cancellationToken);
                await updater.RequireInputAsync(new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = Role.Agent,
                    ContextId = context.ContextId,
                    Parts = [Part.FromText("Ready for the next message.")],
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await updater.AddArtifactAsync([Part.FromText("Demo result")], artifactId: "result", cancellationToken: cancellationToken);
                await updater.CompleteAsync(cancellationToken: cancellationToken);
            }
        }

        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
            new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();
    }
}
