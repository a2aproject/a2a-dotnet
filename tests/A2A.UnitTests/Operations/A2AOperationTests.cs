using System.Reflection;
using System.Runtime.CompilerServices;

namespace A2A.UnitTests.Operations;

public class A2AOperationTests
{
    [Theory]
    [InlineData(nameof(A2AFeatureCollection.Set))]
    [InlineData(nameof(A2AFeatureCollection.Get))]
    [InlineData(nameof(A2AFeatureCollection.GetRequired))]
    public void FeatureMethods_RequireReferenceTypes(string methodName)
    {
        var method = typeof(A2AFeatureCollection)
            .GetMethod(methodName)!;
        var attributes = method
            .GetGenericArguments()[0]
            .GenericParameterAttributes;

        Assert.True(
            attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint));
    }

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
    public void DefineUnary_WhenRequestTypeIsOpenGeneric_ReflectionRejectsInvocationBeforeRegistration()
    {
        var builder = new A2AOperationCatalogBuilder();
        var method = typeof(A2AOperationCatalogBuilder)
            .GetMethod(nameof(A2AOperationCatalogBuilder.DefineUnary))!
            .MakeGenericMethod(typeof(OpenGeneric<>), typeof(TestResult));

        Assert.Throws<InvalidOperationException>(() =>
        {
            method.Invoke(builder, [new A2AOperationId("open.request"), null]);
        });

        var catalog = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.GetRequired(new A2AOperationId("open.request"));
        });

        Assert.Contains(
            "open.request",
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
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var operationCatalog = operationBuilder.Build();
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
            .Build(operationCatalog);

        var result = await handlers.InvokeAsync(
            operation,
            context,
            new TestRequest("request-1"),
            CancellationToken.None);

        Assert.Equal("tenant-1:request-1", result.Value);
    }

    [Fact]
    public async Task HandlerCatalog_InvokesStreamingHandler()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("https://example.com/extensions/test#stream"));
        var operationCatalog = operationBuilder.Build();
        var features = new A2AFeatureCollection();
        features.Set(new TestFeature("tenant-1"));
        var context = new A2AOperationContext(new TestRequestHandler(), features);
        var expected = new TestEvent("tenant-1:request-1");
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (operationContext, request, cancellationToken) =>
                {
                    Assert.Equal(expected.Value, $"{operationContext.Features.GetRequired<TestFeature>().Value}:{request.Value}");
                    return Yield(expected, cancellationToken);
                })
            .Build(operationCatalog);

        var result = await ToListAsync(handlers.InvokeStreamingAsync(
            operation,
            context,
            new TestRequest("request-1"),
            CancellationToken.None));

        Assert.Same(expected, Assert.Single(result));
    }

    [Fact]
    public async Task HandlerCatalog_DoesNotInvokeOperationValidatorImplicitly()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#validated"),
            static request =>
            {
                if (request.Value == "invalid")
                {
                    throw new InvalidOperationException("invalid");
                }
            });
        var operationCatalog = operationBuilder.Build();
        var handlerInvoked = false;
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                (_, _, _) =>
                {
                    handlerInvoked = true;
                    return ValueTask.FromResult(new TestResult("handled"));
                })
            .Build(operationCatalog);

        var result = await handlers.InvokeAsync(
            operation,
            new A2AOperationContext(new TestRequestHandler()),
            new TestRequest("invalid"),
            CancellationToken.None);

        Assert.True(handlerInvoked);
        Assert.Equal("handled", result.Value);
    }

    [Fact]
    public async Task HandlerCatalog_DoesNotInvokeStreamingOperationValidatorImplicitly()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("https://example.com/extensions/test#validated-stream"),
            static request =>
            {
                if (request.Value == "invalid")
                {
                    throw new InvalidOperationException("invalid");
                }
            });
        var operationCatalog = operationBuilder.Build();
        var handlerInvoked = false;
        var expected = new TestEvent("handled");
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, _, cancellationToken) =>
                {
                    handlerInvoked = true;
                    return Yield(expected, cancellationToken);
                })
            .Build(operationCatalog);

        var result = await ToListAsync(handlers.InvokeStreamingAsync(
            operation,
            new A2AOperationContext(new TestRequestHandler()),
            new TestRequest("invalid"),
            CancellationToken.None));

        Assert.True(handlerInvoked);
        Assert.Same(expected, Assert.Single(result));
    }

    [Fact]
    public async Task HandlerCatalog_PropagatesCancellationToStreamingHandler()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var operation = operationBuilder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("https://example.com/extensions/test#cancellable-stream"));
        var operationCatalog = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .MapStreaming(
                operation,
                (_, _, cancellationToken) => Yield(new TestEvent("ignored"), cancellationToken))
            .Build(operationCatalog);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ToListAsync(
                handlers.InvokeStreamingAsync(
                    operation,
                    new A2AOperationContext(new TestRequestHandler()),
                    new TestRequest("request-1"),
                    cts.Token),
                cts.Token));
    }

    [Fact]
    public void HandlerCatalog_BuildRejectsHandlersForOperationsAbsentFromCatalog()
    {
        var operationCatalog = new A2AOperationCatalogBuilder().Build();
        var operation = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#missing"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new A2AOperationHandlerCatalogBuilder()
                .Map(
                    operation,
                    static (_, _, _) => ValueTask.FromResult(new TestResult("result")))
                .Build(operationCatalog);
        });

        Assert.Contains(operation.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerCatalog_BuildRejectsHandlersWhenOperationKindDiffers()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        _ = operationBuilder.DefineStreaming<TestRequest, TestEvent>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var operationCatalog = operationBuilder.Build();
        var unaryOperation = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new A2AOperationHandlerCatalogBuilder()
                .Map(
                    unaryOperation,
                    static (_, _, _) => ValueTask.FromResult(new TestResult("result")))
                .Build(operationCatalog);
        });

        Assert.Contains(unaryOperation.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerCatalog_BuildRejectsHandlersWhenOperationTypesDiffer()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        _ = operationBuilder.DefineUnary<OtherRequest, OtherResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));
        var operationCatalog = operationBuilder.Build();
        var typedOperation = new A2AOperation<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#execute"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new A2AOperationHandlerCatalogBuilder()
                .Map(
                    typedOperation,
                    static (_, _, _) => ValueTask.FromResult(new TestResult("result")))
                .Build(operationCatalog);
        });

        Assert.Contains(typedOperation.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenOperationComesFromAnotherCatalog_Throws()
    {
        var firstBuilder = new A2AOperationCatalogBuilder();
        var firstOperation = firstBuilder.DefineUnary<TestRequest, TestResult>(
            new A2AOperationId("https://example.com/extensions/test#shared"));
        var firstCatalog = firstBuilder.Build();

        var secondBuilder = new A2AOperationCatalogBuilder();
        var secondOperation = secondBuilder.DefineUnary<TestRequest, TestResult>(
            firstOperation.Id);
        _ = secondBuilder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            firstCatalog.Validate(secondOperation, new TestRequest("value"));
        });

        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateStreaming_WhenOperationTypesDoNotMatch_Throws()
    {
        var builder = new A2AOperationCatalogBuilder();
        var registeredOperation = builder.DefineStreaming<OtherRequest, OtherResult>(
            new A2AOperationId("https://example.com/extensions/test#stream-shared"));
        var catalog = builder.Build();
        var mismatchedOperation = new A2AStreamingOperation<TestRequest, TestEvent>(
            registeredOperation.Id);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            catalog.ValidateStreaming(
                mismatchedOperation,
                new TestRequest("value"));
        });

        Assert.Contains(mismatchedOperation.Id.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains("incompatible", exception.Message, StringComparison.Ordinal);
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
    public void FeatureCollection_GetReturnsRegisteredFeature()
    {
        var features = new A2AFeatureCollection();
        var feature = new TestFeature("value");
        features.Set(feature);

        Assert.Same(feature, features.Get<TestFeature>());
    }

    [Fact]
    public void FeatureCollection_GetReturnsNullForMissingFeature()
    {
        var features = new A2AFeatureCollection();

        Assert.Null(features.Get<TestFeature>());
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

    private static async Task<List<T>> ToListAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            results.Add(item);
        }

        return results;
    }

    private static async IAsyncEnumerable<TestEvent> Yield(
        TestEvent value,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return value;
        await Task.CompletedTask;
    }

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
