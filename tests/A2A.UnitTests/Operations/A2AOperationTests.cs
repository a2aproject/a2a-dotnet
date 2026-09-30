namespace A2A.UnitTests.Operations;

public class A2AOperationTests
{
    [Fact]
    public async Task HandlerCatalog_InvokesTypedHandlerWithOperationContext()
    {
        var operation = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var features = new A2AFeatureCollection();
        features.Set(new TestFeature("tenant-1"));
        var context = new A2AOperationContext(new TestRequestHandler(), features);
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (operationContext, request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var feature = operationContext.Features.GetRequired<TestFeature>();
                    return ValueTask.FromResult(
                        new TestResult($"{feature.Value}:{request.Value}"));
                })
            .Build();

        var result = await handlers.InvokeAsync(
            operation,
            context,
            new TestRequest("request-1"),
            CancellationToken.None);

        Assert.Equal("tenant-1:request-1", result.Value);
    }

    [Fact]
    public void HandlerCatalog_RejectsDuplicateOperationIds()
    {
        var first = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var duplicate = new A2AOperation<OtherRequest, OtherResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var builder = new A2AOperationHandlerCatalogBuilder()
            .Map(
                first,
                static (_, _, _) => ValueTask.FromResult(new TestResult("first")));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.Map(
                duplicate,
                static (_, _, _) => ValueTask.FromResult(new OtherResult("duplicate")));
        });

        Assert.Contains(first.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureCollection_GetRequiredThrowsForMissingFeature()
    {
        var features = new A2AFeatureCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            features.GetRequired<TestFeature>();
        });

        Assert.Contains(typeof(TestFeature).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestScope_DisposesCleanupOnlyOnce()
    {
        var disposeCount = 0;
        var scope = new A2ARequestScope(
            new A2AOperationContext(new TestRequestHandler()),
            () =>
            {
                disposeCount++;
                return ValueTask.CompletedTask;
            });

        await scope.DisposeAsync();
        await scope.DisposeAsync();

        Assert.Equal(1, disposeCount);
    }

    private sealed record TestRequest(string Value);

    private sealed record TestResult(string Value);

    private sealed record OtherRequest(string Value);

    private sealed record OtherResult(string Value);

    private sealed record TestFeature(string Value);

    private sealed class TestRequestHandler : IA2ARequestHandler
    {
        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
