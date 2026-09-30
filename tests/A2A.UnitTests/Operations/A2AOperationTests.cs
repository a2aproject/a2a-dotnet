using System.Reflection;

namespace A2A.UnitTests.Operations;

public class A2AOperationTests
{
    [Fact]
    public void Build_WhenOperationIdsAreUnique_ReturnsImmutableCatalog()
    {
        var builder = new A2AOperationCatalogBuilder();
        var get = builder.DefineUnary<GetTaskRequest, AgentTask>(
            new A2AOperationId("a2a:get-task"));
        var stream = builder.DefineStreaming<SendMessageRequest, StreamResponse>(
            new A2AOperationId("a2a:send-message-stream"));

        var catalog = builder.Build();

        Assert.Same(get, catalog.GetRequired<GetTaskRequest, AgentTask>(get.Id));
        Assert.Same(
            stream,
            catalog.GetRequiredStreaming<SendMessageRequest, StreamResponse>(
                stream.Id));
    }

    [Fact]
    public void DefineUnary_WhenIdAlreadyExists_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var id = new A2AOperationId("duplicate");
        builder.DefineUnary<GetTaskRequest, AgentTask>(id);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.DefineUnary<CancelTaskRequest, AgentTask>(id);
        });

        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefineStreaming_WhenUnaryIdAlreadyExists_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var id = new A2AOperationId("duplicate");
        builder.DefineUnary<GetTaskRequest, AgentTask>(id);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.DefineStreaming<SendMessageRequest, StreamResponse>(id);
        });

        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefineUnary_WhenIdIsDefault_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();

        Assert.Throws<ArgumentException>(() =>
        {
            builder.DefineUnary<TestRequest, TestResult>(default);
        });
    }

    [Fact]
    public void GetRequired_WhenRegistrationTypesDoNotMatch_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.execute"));
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequired<OtherRequest, OtherResult>(operation.Id);
        });

        Assert.Contains(operation.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredStreaming_WhenRegistrationTypesDoNotMatch_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("test.stream"));
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequiredStreaming<OtherRequest, OtherResult>(operation.Id);
        });

        Assert.Contains(operation.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefineUnary_WhenRequestTypeIsOpenGeneric_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var method = typeof(A2AOperationCatalogBuilder)
            .GetMethod(nameof(A2AOperationCatalogBuilder.DefineUnary))!
            .MakeGenericMethod(typeof(OpenGeneric<>), typeof(TestResult));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            method.Invoke(builder, [new A2AOperationId("open.request"), null]);
        });

        Assert.Contains(
            "ContainsGenericParameters",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefineUnary_WhenResultTypeIsOpenGeneric_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var method = typeof(A2AOperationCatalogBuilder)
            .GetMethod(nameof(A2AOperationCatalogBuilder.DefineUnary))!
            .MakeGenericMethod(typeof(TestRequest), typeof(OpenGeneric<>));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            method.Invoke(builder, [new A2AOperationId("open.result"), null]);
        });

        Assert.Contains(
            "ContainsGenericParameters",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenValidatorRegistered_InvokesValidator()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.validate"),
            static request =>
            {
                if (request.Value == "invalid")
                {
                    throw new InvalidOperationException("invalid");
                }
            });
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.Validate(operation, new TestRequest("invalid"));
        });

        Assert.Equal("invalid", exception.Message);
    }

    [Fact]
    public void ValidateStreaming_WhenValidatorRegistered_InvokesValidator()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("test.stream.validate"),
            static request =>
            {
                if (request.Value == "invalid")
                {
                    throw new InvalidOperationException("invalid");
                }
            });
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.ValidateStreaming(operation, new TestRequest("invalid"));
        });

        Assert.Equal("invalid", exception.Message);
    }

    [Fact]
    public void Build_WhenBuilderMutatesLater_KeepsCatalogImmutable()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.immutable"));
        var catalog = builder.Build();

        builder.DefineUnary<OtherRequest, OtherResult>(
            new A2AOperationId("test.later"));

        Assert.Same(operation, catalog.GetRequired<TestRequest, TestResult>(operation.Id));
        Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequired<OtherRequest, OtherResult>(
                new A2AOperationId("test.later"));
        });
    }

    [Fact]
    public void DeclareError_WhenErrorIdIsEmpty_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.error"));

        Assert.Throws<ArgumentException>(() =>
        {
            builder.DeclareError<TestRequest, TestResult, TestErrorDetails>(
                operation,
                string.Empty);
        });
    }

    [Fact]
    public void DeclareError_WhenOperationWasNotDefinedByBuilder_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var undeclared = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("test.missing"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.DeclareError<TestRequest, TestResult, TestErrorDetails>(
                undeclared,
                "test-error");
        });

        Assert.Contains(undeclared.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclareError_WhenErrorIdAlreadyExists_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.error"));
        builder.DeclareError<TestRequest, TestResult, TestErrorDetails>(
            operation,
            "test-error");

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.DeclareError<TestRequest, TestResult, OtherErrorDetails>(
                operation,
                "test-error");
        });

        Assert.Contains("test-error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredError_WhenDetailsTypeDoesNotMatch_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.error"));
        builder.DeclareError<TestRequest, TestResult, TestErrorDetails>(
            operation,
            "test-error");
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequiredError<TestRequest, TestResult, OtherErrorDetails>(
                operation,
                "test-error");
        });

        Assert.Contains("test-error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredStreamingError_WhenDetailsTypeDoesNotMatch_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("test.stream.error"));
        builder.DeclareError<TestRequest, TestEvent, TestErrorDetails>(
            operation,
            "stream-error");
        var catalog = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequiredStreamingError<TestRequest, TestEvent, OtherErrorDetails>(
                operation,
                "stream-error");
        });

        Assert.Contains("stream-error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationException_PreservesDeclaredErrorIdentityAndDetails()
    {
        var builder = new A2AOperationCatalogBuilder();
        var operation = builder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("test.error"));
        var error = builder.DeclareError<TestRequest, TestResult, TestErrorDetails>(
            operation,
            "test-error");

        var exception = new A2AOperationException<TestErrorDetails>(
            error,
            "failed",
            new TestErrorDetails("details"));

        Assert.Equal("test-error", exception.ErrorId);
        Assert.Same(error, exception.Error);
        Assert.Equal("details", exception.Details.Value);
    }

    [Fact]
    public async Task HandlerCatalog_InvokesTypedHandlerWithOperationContext()
    {
        var operation = new A2AOperationCatalogBuilder()
            .DefineUnary<TestRequest, TestResult>(
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

    private sealed record TestEvent(string Value);

    private sealed record TestErrorDetails(string Value);

    private sealed record OtherErrorDetails(string Value);

    private sealed record TestFeature(string Value);

    private sealed class OpenGeneric<T>;

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
