namespace A2A.UnitTests.Operations;

public class A2AStandardOperationsTests
{
    [Fact]
    public void AddStandardA2AOperations_RegistersEveryStandardMethod()
    {
        var builder = new A2AOperationCatalogBuilder();

        var standard = builder.AddStandardA2AOperations();
        var catalog = builder.Build();

        Assert.Same(standard.SendMessage, catalog.GetRequired(standard.SendMessage.Id));
        Assert.Same(
            standard.SendStreamingMessage,
            catalog.GetRequiredStreaming(standard.SendStreamingMessage.Id));
        Assert.Same(standard.GetTask, catalog.GetRequired(standard.GetTask.Id));
        Assert.Same(standard.ListTasks, catalog.GetRequired(standard.ListTasks.Id));
        Assert.Same(standard.CancelTask, catalog.GetRequired(standard.CancelTask.Id));
        Assert.Same(
            standard.SubscribeToTask,
            catalog.GetRequiredStreaming(standard.SubscribeToTask.Id));
        Assert.Same(
            standard.CreateTaskPushNotificationConfig,
            catalog.GetRequired(standard.CreateTaskPushNotificationConfig.Id));
        Assert.Same(
            standard.GetTaskPushNotificationConfig,
            catalog.GetRequired(standard.GetTaskPushNotificationConfig.Id));
        Assert.Same(
            standard.ListTaskPushNotificationConfigs,
            catalog.GetRequired(standard.ListTaskPushNotificationConfigs.Id));
        Assert.Same(
            standard.DeleteTaskPushNotificationConfig,
            catalog.GetRequired(standard.DeleteTaskPushNotificationConfig.Id));
        Assert.Same(
            standard.GetExtendedAgentCard,
            catalog.GetRequired(standard.GetExtendedAgentCard.Id));
    }

    [Fact]
    public void AddStandardA2AOperations_UsesStableLibraryOperationIds()
    {
        var standard = new A2AOperationCatalogBuilder().AddStandardA2AOperations();

        Assert.Equal(
            "https://a2a-protocol.org/operations/send-message",
            standard.SendMessage.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/send-message-stream",
            standard.SendStreamingMessage.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/get-task",
            standard.GetTask.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/list-tasks",
            standard.ListTasks.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/cancel-task",
            standard.CancelTask.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/subscribe-to-task",
            standard.SubscribeToTask.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/create-task-push-notification-config",
            standard.CreateTaskPushNotificationConfig.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/get-task-push-notification-config",
            standard.GetTaskPushNotificationConfig.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/list-task-push-notification-configs",
            standard.ListTaskPushNotificationConfigs.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/delete-task-push-notification-config",
            standard.DeleteTaskPushNotificationConfig.Id.Value);
        Assert.Equal(
            "https://a2a-protocol.org/operations/get-extended-agent-card",
            standard.GetExtendedAgentCard.Id.Value);
    }

    [Fact]
    public void AddStandardA2AOperations_MarksStandardRegistrationsForDiagnostics()
    {
        var builder = new A2AOperationCatalogBuilder();
        var extension = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test"));
        var standard = builder.AddStandardA2AOperations();
        var catalog = builder.Build();

        Assert.Equal(A2AOperationSource.Extension, catalog.GetSource(extension.Id));
        Assert.Equal(A2AOperationSource.Standard, catalog.GetSource(standard.SendMessage.Id));
    }

    private sealed record TestRequest(string Value);

    private sealed record TestResult(string Value);
}
