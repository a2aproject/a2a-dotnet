using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Server;

public sealed class A2AExtensionRegistrationTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Fact]
    public void UsageExample_RegistersExtensionAndItsOperation()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var debate = builder.RegisterExtension(new AgentExtension
        {
            Uri = ExtensionUri,
            Description = "Operations for controlling a debate",
        });
        var resume = builder.Map(
            debate,
            new A2AOperationId("https://example.org/operations#resume"),
            ResumeDebateAsync,
            DebateJsonContext.Default.ResumeDebateRequest,
            DebateJsonContext.Default.ResumeDebateResult);
        var registry = builder.Build();

        Assert.Same(debate, resume.Extension);
        Assert.Same(debate, registry.Extensions[ExtensionUri]);
        Assert.Equal(ExtensionUri, debate.ToAgentExtension().Uri);
    }

    [Fact]
    public void RegisterExtension_SupportsExtensionsWithoutOperations()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });

        var registry = builder.Build();

        Assert.Same(extension, Assert.Single(registry.Extensions).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://example.org/ext,v1")]
    [InlineData("https://example.org/ext\r\nInjected: value")]
    [InlineData("https://example.org/ext\tvalue")]
    [InlineData("https://example.org/ext value")]
    [InlineData("https://example.org/ext\0")]
    public void RegisterExtension_RejectsInvalidIdentifiers(string? uri)
    {
        var builder = new A2ACustomOperationRegistryBuilder();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.RegisterExtension(new AgentExtension { Uri = uri! }));

        Assert.Equal("extension", exception.ParamName);
    }

    [Fact]
    public void RegisterExtension_RejectsNullDeclaration()
    {
        var builder = new A2ACustomOperationRegistryBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.RegisterExtension(null!));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"not an object\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void RegisterExtension_RejectsNonObjectParametersWithoutAddingARegistration(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new A2ACustomOperationRegistryBuilder();
        var error = Assert.Throws<ArgumentException>(() =>
            builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Params = document.RootElement }));

        Assert.Equal("extension", error.ParamName);
        Assert.Empty(builder.Build().Extensions);
    }

    [Fact]
    public void RegisterExtension_RejectsUndefinedParameters()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        Assert.Throws<ArgumentException>(() =>
            builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Params = default(JsonElement) }));
        Assert.Empty(builder.Build().Extensions);
    }

    [Fact]
    public void RegisterExtension_RejectsDuplicateIdentifier()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri }));
    }

    [Fact]
    public void RegisterExtension_PreservesOpaqueCaseSensitiveIdentifiers()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var first = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var second = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri.ToUpperInvariant() });
        var registry = builder.Build();

        Assert.Equal(2, registry.Extensions.Count);
        Assert.Same(first, registry.Extensions[ExtensionUri]);
        Assert.Same(second, registry.Extensions[ExtensionUri.ToUpperInvariant()]);
    }

    [Fact]
    public void RegisterExtension_KeepsDifferentVersionsSeparate()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var first = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var second = builder.RegisterExtension(new AgentExtension
        {
            Uri = "https://example.org/extensions/debate/v2",
        });
        var registry = builder.Build();

        Assert.Equal(2, registry.Extensions.Count);
        Assert.Same(first, registry.Extensions[first.Uri]);
        Assert.Same(second, registry.Extensions[second.Uri]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisterExtension_SnapshotsTheCompleteDeclaration(bool? required)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        A2AExtensionRegistration extension;
        using (var document = JsonDocument.Parse("{\"version\":1}"))
        {
            var declaration = new AgentExtension
            {
                Uri = ExtensionUri,
                Description = "Original description",
                Required = required,
                Params = document.RootElement,
            };
            extension = builder.RegisterExtension(declaration);
            declaration.Uri = "https://example.org/changed";
            declaration.Description = "Changed description";
            declaration.Required = !required;
            declaration.Params = null;
        }

        var registered = Assert.Single(builder.Build().Extensions).Value;
        Assert.Same(extension, registered);
        Assert.Equal(ExtensionUri, registered.Uri);
        Assert.Equal("Original description", registered.Description);
        Assert.Equal(required, registered.Required);
        Assert.Equal(1, registered.Params!.Value.GetProperty("version").GetInt32());
    }

    [Fact]
    public void ToAgentExtension_ReturnsAnIndependentDeclaration()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension
        {
            Uri = ExtensionUri,
            Description = "Original",
            Required = true,
            Params = JsonSerializer.SerializeToElement(new { version = 1 }),
        });
        var exported = extension.ToAgentExtension();
        exported.Uri = "https://example.org/changed";
        exported.Description = "Changed";
        exported.Required = false;
        exported.Params = null;

        var fresh = extension.ToAgentExtension();

        Assert.NotSame(exported, fresh);
        Assert.Equal(ExtensionUri, fresh.Uri);
        Assert.Equal("Original", fresh.Description);
        Assert.True(fresh.Required);
        Assert.Equal(1, fresh.Params!.Value.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Build_FreezesExtensionRegistrationAndExposesReadOnlyEntries()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var registry = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.RegisterExtension(
            new AgentExtension { Uri = "https://example.org/other" }));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, A2AExtensionRegistration>>(registry.Extensions);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Same(extension, registry.Extensions[ExtensionUri]);
    }

    [Fact]
    public void MapOverloads_PreserveExtensionOwnership()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var unary = builder.Map<TestRequest, TestResult>(
            extension, new A2AOperationId("unary"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var contextualUnary = builder.Map<TestRequest, TestResult>(
            extension, new A2AOperationId("contextual-unary"), (_, request, _) => Echo(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var streaming = builder.MapStreaming<TestRequest, TestResult>(
            extension, new A2AOperationId("streaming"), Stream,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var contextualStreaming = builder.MapStreaming<TestRequest, TestResult>(
            extension, new A2AOperationId("contextual-streaming"), (_, request, _) => Stream(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        _ = builder.Build();

        Assert.Same(extension, unary.Extension);
        Assert.Same(extension, contextualUnary.Extension);
        Assert.Same(extension, streaming.Extension);
        Assert.Same(extension, contextualStreaming.Extension);
    }

    [Fact]
    public void MapOverloads_RejectForeignExtensionEvenWithMatchingUri()
    {
        var first = new A2ACustomOperationRegistryBuilder();
        var foreign = first.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        _ = first.Build();
        var second = new A2ACustomOperationRegistryBuilder();
        var local = second.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var id = new A2AOperationId("operation");

        Assert.Throws<InvalidOperationException>(() => second.Map<TestRequest, TestResult>(
            foreign, id, Echo, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<InvalidOperationException>(() => second.Map<TestRequest, TestResult>(
            foreign, id, (_, request, _) => Echo(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<InvalidOperationException>(() => second.MapStreaming<TestRequest, TestResult>(
            foreign, id, Stream, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<InvalidOperationException>(() => second.MapStreaming<TestRequest, TestResult>(
            foreign, id, (_, request, _) => Stream(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));

        var operation = second.Map<TestRequest, TestResult>(
            local, id, Echo, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        Assert.Same(local, operation.Extension);
        Assert.Single(second.Build().Extensions);
    }

    [Fact]
    public void MapOverloads_RejectNullExtension()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var id = new A2AOperationId("operation");

        Assert.Throws<ArgumentNullException>(() => builder.Map<TestRequest, TestResult>(
            null!, id, Echo, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.Map<TestRequest, TestResult>(
            null!, id, (_, request, _) => Echo(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.MapStreaming<TestRequest, TestResult>(
            null!, id, Stream, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.MapStreaming<TestRequest, TestResult>(
            null!, id, (_, request, _) => Stream(request, default),
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
    }

    [Fact]
    public void Map_RejectsDuplicateOperationIdsAcrossExtensions()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var first = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var second = builder.RegisterExtension(new AgentExtension { Uri = "https://example.org/other" });
        var id = new A2AOperationId("operation");
        builder.Map<TestRequest, TestResult>(first, id, Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);

        Assert.Throws<InvalidOperationException>(() => builder.MapStreaming<TestRequest, TestResult>(
            second, id, Stream, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
    }

    [Fact]
    public void Map_RejectsInvalidExtensionOwnedOperationInputs()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var id = new A2AOperationId("operation");
        A2ACustomOperationHandler<TestRequest, TestResult>? handler = null;
        A2AStreamingCustomOperationHandler<TestRequest, TestResult>? streamingHandler = null;

        Assert.Throws<ArgumentNullException>(() => builder.Map<TestRequest, TestResult>(
            extension, id, handler!, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.MapStreaming<TestRequest, TestResult>(
            extension, id, streamingHandler!, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.Map<TestRequest, TestResult>(
            extension, id, Echo, null!, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentNullException>(() => builder.MapStreaming<TestRequest, TestResult>(
            extension, id, Stream, CustomOperationJsonContext.Default.TestRequest, null!));
        Assert.Throws<ArgumentException>(() => builder.Map<TestRequest, TestResult>(
            extension, default, Echo, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<ArgumentException>(() => builder.MapStreaming<TestRequest, TestResult>(
            extension, default, Stream, CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));

        _ = builder.Map<TestRequest, TestResult>(extension, id, Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
    }

    [Fact]
    public void Build_RejectsFurtherExtensionOwnedOperationRegistration()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        _ = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Map<TestRequest, TestResult>(
            extension, new A2AOperationId("unary"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
        Assert.Throws<InvalidOperationException>(() => builder.MapStreaming<TestRequest, TestResult>(
            extension, new A2AOperationId("streaming"), Stream,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult));
    }

    [Fact]
    public void ExistingMapOverloads_KeepOperationsUnowned()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var operation = builder.Map<TestRequest, TestResult>(new A2AOperationId("unary"), Echo,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);
        var streaming = builder.MapStreaming<TestRequest, TestResult>(new A2AOperationId("streaming"), Stream,
            CustomOperationJsonContext.Default.TestRequest, CustomOperationJsonContext.Default.TestResult);

        Assert.Null(operation.Extension);
        Assert.Null(streaming.Extension);
        Assert.Empty(builder.Build().Extensions);
    }

    private static ValueTask<TestResult> Echo(TestRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new TestResult(request.Value));
    }

    private static async IAsyncEnumerable<TestResult> Stream(
        TestRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new TestResult(request.Value);
    }

    private static ValueTask<ResumeDebateResult> ResumeDebateAsync(
        ResumeDebateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ResumeDebateResult(request.DebateId, "resumed"));
    }
}

internal sealed record ResumeDebateRequest(string DebateId);

internal sealed record ResumeDebateResult(string DebateId, string Status);

[JsonSerializable(typeof(ResumeDebateRequest))]
[JsonSerializable(typeof(ResumeDebateResult))]
internal sealed partial class DebateJsonContext : JsonSerializerContext;
