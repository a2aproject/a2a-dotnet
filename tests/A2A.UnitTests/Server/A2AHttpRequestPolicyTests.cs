using System.Diagnostics;

namespace A2A.UnitTests.Server;

public class A2AHttpRequestPolicyTests
{
    [Fact]
    public void NormalizeIncomingRequest_ClearsTenantFromEveryStandardRequestType()
    {
        var requests = CreateTenantBearingRequests();

        foreach (var (request, getTenant) in requests)
        {
            A2AHttpRequestPolicy.NormalizeIncomingRequest(request, "JSONRPC", "test");

            Assert.Null(getTenant());
        }
    }

    [Fact]
    public void NormalizeIncomingRequest_WithNoTenant_DoesNotRecordDiagnosticEvent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "tenant-policy-tests",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("tenant-policy-tests");
        using var activity = source.StartActivity("request");
        var request = new GetTaskRequest { Id = "task-1" };

        A2AHttpRequestPolicy.NormalizeIncomingRequest(request, "JSONRPC", A2AMethods.GetTask);

        Assert.NotNull(activity);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public void SendMessagePolicy_HandlesTopLevelAndEmbeddedTenantsAsOneRequest()
    {
        var request = new SendMessageRequest
        {
            Tenant = "top-level",
            Message = new Message { MessageId = "message-1", Role = Role.User, Parts = [Part.FromText("hi")] },
            Configuration = new SendMessageConfiguration
            {
                TaskPushNotificationConfig = new TaskPushNotificationConfig
                {
                    Url = "https://push.example",
                    Tenant = "embedded",
                },
            },
        };
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "tenant-policy-tests",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("tenant-policy-tests");
        using var activity = source.StartActivity("request");

        A2AHttpRequestPolicy.NormalizeIncomingRequest(request, "HTTP+JSON", "SendMessage");

        Assert.Null(request.Tenant);
        Assert.Null(request.Configuration.TaskPushNotificationConfig.Tenant);
        var tenantEvent = Assert.Single(activity!.Events);
        Assert.Equal("a2a.tenant.ignored", tenantEvent.Name);
        Assert.DoesNotContain(tenantEvent.Tags, tag =>
            tag.Value?.ToString()?.Contains("top-level", StringComparison.Ordinal) == true ||
            tag.Value?.ToString()?.Contains("embedded", StringComparison.Ordinal) == true);
    }

    private static (object Request, Func<string?> GetTenant)[] CreateTenantBearingRequests()
    {
        var sendMessage = new SendMessageRequest
        {
            Tenant = "tenant-a",
            Message = new Message { MessageId = "message-1", Role = Role.User, Parts = [Part.FromText("hi")] },
        };
        var getTask = new GetTaskRequest { Id = "task-1", Tenant = "tenant-a" };
        var listTasks = new ListTasksRequest { Tenant = "tenant-a" };
        var cancelTask = new CancelTaskRequest { Id = "task-1", Tenant = "tenant-a" };
        var subscribe = new SubscribeToTaskRequest { Id = "task-1", Tenant = "tenant-a" };
        var config = new TaskPushNotificationConfig { Url = "https://push.example", Tenant = "tenant-a" };
        var getConfig = new GetTaskPushNotificationConfigRequest
        {
            TaskId = "task-1",
            Id = "config-1",
            Tenant = "tenant-a",
        };
        var listConfigs = new ListTaskPushNotificationConfigsRequest { TaskId = "task-1", Tenant = "tenant-a" };
        var deleteConfig = new DeleteTaskPushNotificationConfigRequest
        {
            TaskId = "task-1",
            Id = "config-1",
            Tenant = "tenant-a",
        };
        var getCard = new GetExtendedAgentCardRequest { Tenant = "tenant-a" };

        return
        [
            (sendMessage, () => sendMessage.Tenant),
            (getTask, () => getTask.Tenant),
            (listTasks, () => listTasks.Tenant),
            (cancelTask, () => cancelTask.Tenant),
            (subscribe, () => subscribe.Tenant),
            (config, () => config.Tenant),
            (getConfig, () => getConfig.Tenant),
            (listConfigs, () => listConfigs.Tenant),
            (deleteConfig, () => deleteConfig.Tenant),
            (getCard, () => getCard.Tenant),
        ];
    }
}
