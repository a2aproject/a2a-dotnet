using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Server;

public sealed class A2ACustomOperationRegistryTests
{
    [Fact]
    public async Task InvokeAsync_InvokesRegisteredHandler()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#echo"),
            (request, _) => ValueTask.FromResult(new TestResult(request.Value.ToUpperInvariant())),
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        var result = await registry.InvokeAsync(
            operation,
            new TestRequest("hello"),
            CancellationToken.None);

        Assert.Equal("HELLO", result.Value);
    }

    [Fact]
    public async Task InvokeAsync_ProvidesRequestScopedContext()
    {
        var feature = new TestFeature("request-feature");
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#context"),
            (context, request, _) => ValueTask.FromResult(
                new TestResult(
                    $"{context.GetRequiredFeature<TestFeature>().Value}:{request.Value}")),
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        var result = await registry.InvokeAsync(
            operation,
            new A2ACustomOperationContext(features: [feature]),
            new TestRequest("payload"),
            CancellationToken.None);

        Assert.Equal("request-feature:payload", result.Value);
    }

    [Fact]
    public async Task InvokeAsync_ValidatesBeforeInvokingHandler()
    {
        var handlerInvoked = false;
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#validated"),
            (request, _) =>
            {
                handlerInvoked = true;
                return ValueTask.FromResult(new TestResult(request.Value));
            },
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult,
            request =>
            {
                if (string.IsNullOrWhiteSpace(request.Value))
                {
                    throw new A2AException("Value is required.", A2AErrorCode.InvalidParams);
                }
            });
        var registry = builder.Build();

        var exception = await Assert.ThrowsAsync<A2AException>(async () =>
            await registry.InvokeAsync(
                operation,
                new TestRequest(""),
                CancellationToken.None));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.False(handlerInvoked);
    }

    [Fact]
    public async Task InvokeStreamingAsync_InvokesRegisteredHandler()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.MapStreaming<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#watch"),
            StreamResults,
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        var results = new List<TestResult>();
        await foreach (var result in registry.InvokeStreamingAsync(
            operation,
            new TestRequest("event"),
            CancellationToken.None))
        {
            results.Add(result);
        }

        Assert.Equal(
            [new TestResult("event-1"), new TestResult("event-2")],
            results);
    }

    [Fact]
    public void Map_RejectsEmptyOperationId()
    {
        var builder = new A2ACustomOperationRegistryBuilder();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Map<TestRequest, TestResult>(
                new A2AOperationId(""),
                (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
                CustomOperationJsonContext.Default.TestRequest,
                CustomOperationJsonContext.Default.TestResult));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void MapStreaming_RejectsDuplicateOperationId()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var id = new A2AOperationId("https://example.test/operations#duplicate");
        builder.Map<TestRequest, TestResult>(
            id,
            (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.MapStreaming<TestRequest, TestResult>(
                id,
                StreamResults,
                CustomOperationJsonContext.Default.TestRequest,
                CustomOperationJsonContext.Default.TestResult));

        Assert.Contains(id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RejectsNullRegistrationValues()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var id = new A2AOperationId("https://example.test/operations#nulls");
        A2ACustomOperationHandler<TestRequest, TestResult>? handler = null;

        Assert.Throws<ArgumentNullException>(() =>
            builder.Map<TestRequest, TestResult>(
                id,
                handler!,
                CustomOperationJsonContext.Default.TestRequest,
                CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Map<TestRequest, TestResult>(
                id,
                (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
                null!,
                CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Map<TestRequest, TestResult>(
                id,
                (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
                CustomOperationJsonContext.Default.TestRequest,
                null!));
    }

    [Fact]
    public async Task InvokeAsync_RejectsHandleFromAnotherRegistry()
    {
        var firstBuilder = new A2ACustomOperationRegistryBuilder();
        var foreignOperation = firstBuilder.Map<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#foreign"),
            (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);
        _ = firstBuilder.Build();

        var secondRegistry = new A2ACustomOperationRegistryBuilder().Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await secondRegistry.InvokeAsync(
                foreignOperation,
                new TestRequest("value"),
                CancellationToken.None));

        Assert.Contains("does not belong", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PreventsFurtherRegistrations()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        _ = builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Map<TestRequest, TestResult>(
                new A2AOperationId("https://example.test/operations#late"),
                (request, _) => ValueTask.FromResult(new TestResult(request.Value)),
                CustomOperationJsonContext.Default.TestRequest,
                CustomOperationJsonContext.Default.TestResult));
    }

    [Fact]
    public async Task InvokeAsync_IsSafeForConcurrentInvocation()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<TestRequest, TestResult>(
            new A2AOperationId("https://example.test/operations#concurrent"),
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new TestResult(request.Value));
            },
            CustomOperationJsonContext.Default.TestRequest,
            CustomOperationJsonContext.Default.TestResult);
        var registry = builder.Build();

        var invocations = Enumerable.Range(0, 100)
            .Select(index => registry.InvokeAsync(
                operation,
                new TestRequest(index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                CancellationToken.None).AsTask());

        var results = await Task.WhenAll(invocations);

        Assert.Equal(100, results.Length);
        Assert.Equal("99", results[99].Value);
    }

    private static async IAsyncEnumerable<TestResult> StreamResults(
        TestRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TestResult($"{request.Value}-1");
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TestResult($"{request.Value}-2");
    }
}

internal sealed record TestRequest(string Value);

internal sealed record TestResult(string Value);

internal sealed record TestFeature(string Value);

[JsonSerializable(typeof(TestRequest))]
[JsonSerializable(typeof(TestResult))]
internal sealed partial class CustomOperationJsonContext : JsonSerializerContext;
