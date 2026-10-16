using System.Runtime.CompilerServices;

namespace A2A.UnitTests.Server;

public sealed class A2AExtensionActivationTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Fact]
    public async Task UsageExample_NegotiatesAndInvokesAnExtensionOperation()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var debate = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var resume = builder.Map<ResumeDebateRequest, ResumeDebateResult>(
            debate, new A2AOperationId("https://example.org/operations#resume"),
            (request, _) => ValueTask.FromResult(new ResumeDebateResult(request.DebateId, "resumed")),
            DebateJsonContext.Default.ResumeDebateRequest, DebateJsonContext.Default.ResumeDebateResult);
        var registry = builder.Build();
        var hostAllowsDebate = true;
        Func<A2AExtensionRegistration, CancellationToken, ValueTask<bool>> canActivate = (_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(hostAllowsDebate);
        };

        var requestContext = await registry.NegotiateExtensionsAsync([debate.Uri], canActivate);
        var operationContext = new A2ACustomOperationContext(features: [requestContext]);
        var result = await registry.InvokeAsync(resume, operationContext, new ResumeDebateRequest("debate-42"));
        hostAllowsDebate = false;
        var nextRequest = await registry.NegotiateExtensionsAsync([debate.Uri], canActivate);

        Assert.Equal("resumed", result.Status);
        Assert.Contains(debate.Uri, requestContext.ActivatedExtensions);
        Assert.Empty(nextRequest.ActivatedExtensions);
    }

    [Fact]
    public async Task Negotiate_ActivatesOnlyRequestedSupportedExactIdentifiers()
    {
        var (registry, _, _) = CreateRegistry();
        var unknown = "https://example.org/extensions/unknown/v1";
        var differentVersion = "https://example.org/extensions/debate/v2";
        var context = await registry.NegotiateExtensionsAsync(
            [ExtensionUri, ExtensionUri, unknown, differentVersion, ExtensionUri.ToUpperInvariant()]);

        Assert.Equal(4, context.RequestedExtensions.Count);
        Assert.Contains(unknown, context.RequestedExtensions);
        Assert.Equal(ExtensionUri, Assert.Single(context.ActivatedExtensions));
    }

    [Fact]
    public async Task Negotiate_DoesNotActivateExtensionsByDefault()
    {
        var (registry, _, _) = CreateRegistry();

        var context = await registry.NegotiateExtensionsAsync([]);

        Assert.Empty(context.RequestedExtensions);
        Assert.Empty(context.ActivatedExtensions);
    }

    [Fact]
    public async Task Negotiate_ActivatesExtensionsWithoutCustomOperations()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });

        var context = await builder.Build().NegotiateExtensionsAsync([ExtensionUri]);

        Assert.Equal(ExtensionUri, Assert.Single(context.ActivatedExtensions));
    }

    [Fact]
    public async Task Negotiate_EvaluatesEachRequestedExtensionIndependently()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        const string unavailableUri = "https://example.org/extensions/usage/v1";
        builder.RegisterExtension(new AgentExtension { Uri = unavailableUri });
        var evaluated = new HashSet<string>(StringComparer.Ordinal);

        var context = await builder.Build().NegotiateExtensionsAsync(
            [ExtensionUri, unavailableUri], canActivate: (extension, _) =>
            {
                evaluated.Add(extension.Uri);
                return ValueTask.FromResult(extension.Uri != unavailableUri);
            });

        Assert.Equal(2, evaluated.Count);
        Assert.Equal(ExtensionUri, Assert.Single(context.ActivatedExtensions));
    }

    [Fact]
    public async Task Negotiate_CallsPolicyOnlyOnceForRequestedSupportedExtensions()
    {
        var (registry, extension, _) = CreateRegistry();
        var evaluated = new List<A2AExtensionRegistration>();

        var context = await registry.NegotiateExtensionsAsync(
            [ExtensionUri, ExtensionUri, "https://example.org/unknown"],
            canActivate: (candidate, _) =>
            {
                evaluated.Add(candidate);
                return ValueTask.FromResult(true);
            });

        Assert.Same(extension, Assert.Single(evaluated));
        Assert.Equal(ExtensionUri, Assert.Single(context.ActivatedExtensions));
    }

    [Fact]
    public async Task Negotiate_SkipsPolicyForUnrequestedExtensions()
    {
        var (registry, _, _) = CreateRegistry();
        var policyCalls = 0;

        _ = await registry.NegotiateExtensionsAsync([], canActivate: (_, _) =>
        {
            policyCalls++;
            return ValueTask.FromResult(true);
        });

        Assert.Equal(0, policyCalls);
    }

    [Fact]
    public async Task Negotiate_KeepsUnavailableOptionalExtensionInactive()
    {
        var (registry, _, operation) = CreateRegistry();
        var context = await registry.NegotiateExtensionsAsync(
            [ExtensionUri], canActivate: (_, _) => ValueTask.FromResult(false));

        Assert.Contains(ExtensionUri, context.RequestedExtensions);
        Assert.Empty(context.ActivatedExtensions);
        var exception = Assert.Throws<A2AException>(() =>
        {
            _ = registry.InvokeAsync(operation, FeatureContext(context), new TestRequest("value")).AsTask();
        });
        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
    }

    [Fact]
    public async Task Negotiate_RejectsMissingRequiredExtensionBeforeCallingPolicy()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = "https://example.org/optional" });
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = true });
        var registry = builder.Build();
        var policyCalls = 0;

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
            await registry.NegotiateExtensionsAsync(["https://example.org/optional"], canActivate: (_, _) =>
            {
                policyCalls++;
                return ValueTask.FromResult(true);
            }));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
        Assert.Equal(0, policyCalls);
    }

    [Fact]
    public async Task Negotiate_RejectsUnavailableRequiredExtension()
    {
        var (registry, _, _) = CreateRegistry(required: true);

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
            await registry.NegotiateExtensionsAsync(
                [ExtensionUri], canActivate: (_, _) => ValueTask.FromResult(false)));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
    }

    [Fact]
    public async Task Negotiate_RejectsDifferentVersionOfRequiredExtension()
    {
        var (registry, _, _) = CreateRegistry(required: true);

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
            await registry.NegotiateExtensionsAsync(["https://example.org/extensions/debate/v2"]));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
    }

    [Fact]
    public async Task Negotiate_ReturnsImmutableIndependentSnapshots()
    {
        var (registry, _, _) = CreateRegistry();
        var requested = new List<string> { ExtensionUri };
        var context = await registry.NegotiateExtensionsAsync(requested);
        requested.Clear();

        Assert.Contains(ExtensionUri, context.RequestedExtensions);
        Assert.Contains(ExtensionUri, context.ActivatedExtensions);
        var requestedSet = Assert.IsAssignableFrom<ISet<string>>(context.RequestedExtensions);
        var activatedSet = Assert.IsAssignableFrom<ISet<string>>(context.ActivatedExtensions);
        Assert.Throws<NotSupportedException>(() => requestedSet.Clear());
        Assert.Throws<NotSupportedException>(() => activatedSet.Clear());
    }

    [Fact]
    public async Task Negotiate_SnapshotsInputBeforeAwaitingPolicy()
    {
        var (registry, _, _) = CreateRegistry();
        var requested = new List<string> { ExtensionUri };
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var negotiation = registry.NegotiateExtensionsAsync(
            requested, canActivate: (_, _) => new ValueTask<bool>(decision.Task)).AsTask();
        requested.Clear();
        decision.SetResult(true);

        var context = await negotiation;

        Assert.Contains(ExtensionUri, context.RequestedExtensions);
        Assert.Contains(ExtensionUri, context.ActivatedExtensions);
    }

    [Fact]
    public async Task Negotiate_PropagatesPolicyFailuresWithoutTurningThemIntoUnsupportedErrors()
    {
        var (registry, _, _) = CreateRegistry();
        var expected = new InvalidOperationException("Dependency failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: (_, _) => throw expected));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task Negotiate_RejectsNullInput()
    {
        var (registry, _, _) = CreateRegistry();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await registry.NegotiateExtensionsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.NegotiateExtensionsAsync([null!]));
    }

    [Fact]
    public async Task Negotiate_HonorsCancellationBeforePolicyEvaluation()
    {
        var (registry, _, _) = CreateRegistry();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var policyCalls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: (_, _) =>
            {
                policyCalls++;
                return ValueTask.FromResult(true);
            }, cancellationToken: cancellation.Token));

        Assert.Equal(0, policyCalls);
    }

    [Fact]
    public async Task Negotiate_ForwardsTheRequestCancellationTokenToPolicy()
    {
        var (registry, _, _) = CreateRegistry();
        using var cancellation = new CancellationTokenSource();
        var observed = CancellationToken.None;

        _ = await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: (_, token) =>
        {
            observed = token;
            return ValueTask.FromResult(true);
        }, cancellationToken: cancellation.Token);

        Assert.Equal(cancellation.Token, observed);
    }

    [Fact]
    public async Task Negotiate_RechecksCancellationAfterAwaitingPolicy()
    {
        var (registry, _, _) = CreateRegistry();
        using var cancellation = new CancellationTokenSource();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var negotiation = registry.NegotiateExtensionsAsync(
            [ExtensionUri], canActivate: (_, _) => new ValueTask<bool>(decision.Task),
            cancellationToken: cancellation.Token).AsTask();
        await cancellation.CancelAsync();
        decision.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await negotiation);
    }

    [Fact]
    public async Task Negotiate_IsolatesConcurrentRequestsWithDifferentPolicyDecisions()
    {
        var (registry, _, _) = CreateRegistry();
        var negotiations = Enumerable.Range(0, 40).Select(async index =>
            await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                return index % 2 == 0;
            }));

        var contexts = await Task.WhenAll(negotiations);

        for (var index = 0; index < contexts.Length; index++)
        {
            Assert.Equal(index % 2 == 0, contexts[index].ActivatedExtensions.Contains(ExtensionUri));
        }
        Assert.NotSame(contexts[0], contexts[2]);
    }

    [Fact]
    public async Task Invoke_UsesTheSameSnapshotWithoutReevaluatingAvailability()
    {
        var (registry, _, operation) = CreateRegistry();
        var available = true;
        var policyCalls = 0;
        var activation = await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: (_, _) =>
        {
            policyCalls++;
            return ValueTask.FromResult(available);
        });
        available = false;

        var result = await registry.InvokeAsync(operation, FeatureContext(activation), new TestRequest("value"));

        Assert.Equal("value", result.Value);
        Assert.Contains(ExtensionUri, activation.ActivatedExtensions);
        Assert.Equal(1, policyCalls);
    }

    [Fact]
    public void Invoke_RejectsMissingActivationBeforeValidatorOrHandler()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var validatorCalled = false;
        var handlerCalled = false;
        var operation = builder.Map<TestRequest, TestResult>(extension, new A2AOperationId("owned"),
            (request, _) =>
            {
                handlerCalled = true;
                return ValueTask.FromResult(new TestResult(request.Value));
            }, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult,
            _ => validatorCalled = true);
        var registry = builder.Build();

        var exception = Assert.Throws<A2AException>(() =>
        {
            _ = registry.InvokeAsync(operation, new TestRequest("value")).AsTask();
        });

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
        Assert.False(validatorCalled);
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task Invoke_RejectsForeignContextEvenWithTheSameActivatedUri()
    {
        var (first, _, _) = CreateRegistry();
        var (second, _, operation) = CreateRegistry();
        var activation = await first.NegotiateExtensionsAsync([ExtensionUri]);

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = second.InvokeAsync(operation, FeatureContext(activation), new TestRequest("value")).AsTask();
        });
    }

    [Fact]
    public async Task Invoke_ProvidesTheNegotiatedContextToTheHandler()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        A2ARequestContext? observed = null;
        var operation = builder.Map<TestRequest, TestResult>(extension, new A2AOperationId("owned"),
            (context, request, _) =>
            {
                observed = context.GetRequiredFeature<A2ARequestContext>();
                return ValueTask.FromResult(new TestResult(request.Value));
            }, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();
        var activation = await registry.NegotiateExtensionsAsync([ExtensionUri]);

        _ = await registry.InvokeAsync(operation, FeatureContext(activation), new TestRequest("value"));

        Assert.Same(activation, observed);
    }

    [Fact]
    public async Task Invoke_PreservesActivationAndTheOriginalHandlerFailure()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var expected = new InvalidOperationException("Dependency went offline");
        var operation = builder.Map<TestRequest, TestResult>(extension, new A2AOperationId("owned"),
            (_, _) => throw expected,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();
        var activation = await registry.NegotiateExtensionsAsync([ExtensionUri]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.InvokeAsync(operation, FeatureContext(activation), new TestRequest("value")));

        Assert.Same(expected, exception);
        Assert.Contains(ExtensionUri, activation.ActivatedExtensions);
    }

    [Fact]
    public void Invoke_UnownedOperationsCannotBypassRequiredExtensions()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = true });
        var operation = builder.Map<TestRequest, TestResult>(new A2AOperationId("unowned"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        var exception = Assert.Throws<A2AException>(() =>
        {
            _ = registry.InvokeAsync(operation, new TestRequest("value")).AsTask();
        });
        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);
    }

    [Fact]
    public async Task Invoke_UnownedOperationsWorkWithRequiredExtensionsActivated()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = true });
        var operation = builder.Map<TestRequest, TestResult>(new A2AOperationId("unowned"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();
        var activation = await registry.NegotiateExtensionsAsync([ExtensionUri]);

        var result = await registry.InvokeAsync(operation, FeatureContext(activation), new TestRequest("value"));

        Assert.Equal("value", result.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UntypedDispatch_EnforcesTheSameActivationChecks(bool streaming)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var unary = builder.Map<TestRequest, TestResult>(extension, new A2AOperationId("unary"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var stream = builder.MapStreaming<TestRequest, TestResult>(extension, new A2AOperationId("stream"), Stream,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();
        var inactive = await registry.NegotiateExtensionsAsync([]);
        var active = await registry.NegotiateExtensionsAsync([ExtensionUri]);

        var exception = Assert.Throws<A2AException>(() =>
        {
            if (streaming)
            {
                _ = registry.InvokeStreamingAsync(stream.Registration, FeatureContext(inactive), new TestRequest("value"), default);
            }
            else
            {
                _ = registry.InvokeAsync(unary.Registration, FeatureContext(inactive), new TestRequest("value"), default).AsTask();
            }
        });
        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, exception.ErrorCode);

        if (streaming)
        {
            var count = 0;
            await foreach (var _ in registry.InvokeStreamingAsync(
                stream.Registration, FeatureContext(active), new TestRequest("value"), default))
            {
                count++;
            }
            Assert.Equal(2, count);
        }
        else
        {
            Assert.IsType<TestResult>(await registry.InvokeAsync(
                unary.Registration, FeatureContext(active), new TestRequest("value"), default));
        }
    }

    [Fact]
    public void InvokeStreaming_RejectsMissingActivationBeforeEnumeration()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var handlerCalled = false;
        var operation = builder.MapStreaming<TestRequest, TestResult>(extension, new A2AOperationId("stream"),
            (request, cancellationToken) =>
            {
                handlerCalled = true;
                return Stream(request, cancellationToken);
            }, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        Assert.Throws<A2AException>(() => registry.InvokeStreamingAsync(operation, new TestRequest("value")));
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task InvokeStreaming_PreservesActivationWhenTheHandlerFailsMidStream()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var expected = new InvalidOperationException("Dependency went offline");
        var disposed = false;
        var available = true;
        var policyCalls = 0;
        var operation = builder.MapStreaming<TestRequest, TestResult>(extension, new A2AOperationId("stream"),
            (_, cancellationToken) => FailingStream(cancellationToken),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();
        var activation = await registry.NegotiateExtensionsAsync([ExtensionUri], canActivate: (_, _) =>
        {
            policyCalls++;
            return ValueTask.FromResult(available);
        });
        await using var enumerator = registry.InvokeStreamingAsync(
            operation, FeatureContext(activation), new TestRequest("value")).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        available = false;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await enumerator.MoveNextAsync());

        Assert.Same(expected, exception);
        Assert.True(disposed);
        Assert.Contains(ExtensionUri, activation.ActivatedExtensions);
        Assert.Equal(1, policyCalls);

        async IAsyncEnumerable<TestResult> FailingStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new TestResult("first");
                await Task.Yield();
                if (!available)
                {
                    throw expected;
                }
                yield return new TestResult("second");
            }
            finally
            {
                disposed = true;
            }
        }
    }

    private static (A2ACustomOperationRegistry Registry, A2AExtensionRegistration Extension,
        A2ACustomOperation<TestRequest, TestResult> Operation) CreateRegistry(bool required = false)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Required = required });
        var operation = builder.Map<TestRequest, TestResult>(extension, new A2AOperationId("owned"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        return (builder.Build(), extension, operation);
    }

    private static A2ACustomOperationContext FeatureContext(A2ARequestContext activation) => new(features: [activation]);

    private static ValueTask<TestResult> Echo(TestRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new TestResult(request.Value));
    }

    private static async IAsyncEnumerable<TestResult> Stream(
        TestRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TestResult($"{request.Value}-first");
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TestResult($"{request.Value}-second");
    }
}
