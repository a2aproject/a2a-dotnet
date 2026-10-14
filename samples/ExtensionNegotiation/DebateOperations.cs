using A2A;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtensionNegotiation;

internal sealed record DebateOperations(
    A2ACustomOperationRegistry Registry,
    A2ACustomOperation<ResumeDebateRequest, ResumeDebateResult> Resume,
    A2AStreamingCustomOperation<ResumeDebateRequest, DraftChunk> Watch)
{
    internal const string ExtensionUri = "https://example.org/extensions/debate/v1";
    internal const string ResumeId = "https://example.org/operations#resume-debate";
    internal const string WatchId = "https://example.org/operations#watch-debate";

    internal static DebateOperations Create(DemoAvailability availability)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        using var parameters = JsonDocument.Parse("{\"format\":\"product-draft\"}");
        var debate = builder.RegisterExtension(new AgentExtension
        {
            Uri = ExtensionUri,
            Description = "Demo operations for continuing a debate",
            Required = false,
            Params = parameters.RootElement,
        });
        var resume = builder.Map<ResumeDebateRequest, ResumeDebateResult>(
            debate, new A2AOperationId(ResumeId),
            (context, request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                availability.RecordInvocation();
                var activation = context.GetRequiredFeature<A2ARequestContext>();
                return ValueTask.FromResult(new ResumeDebateResult(request.DebateId,
                    activation.ActivatedExtensions.Contains(debate.Uri)));
            },
            DebateJsonContext.Default.ResumeDebateRequest, DebateJsonContext.Default.ResumeDebateResult);
        var watch = builder.MapStreaming<ResumeDebateRequest, DraftChunk>(
            debate, new A2AOperationId(WatchId),
            (context, request, ct) => WatchAsync(context, request, availability, ct),
            DebateJsonContext.Default.ResumeDebateRequest, DebateJsonContext.Default.DraftChunk);
        return new DebateOperations(builder.Build(), resume, watch);
    }

    private static async IAsyncEnumerable<DraftChunk> WatchAsync(
        A2ACustomOperationContext context, ResumeDebateRequest request, DemoAvailability availability,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activation = context.GetRequiredFeature<A2ARequestContext>();
        yield return new DraftChunk(request.DebateId, 1,
            activation.ActivatedExtensions.Contains(ExtensionUri), availability.IsAvailable, "Product requirements");

        // Simulate a host change after negotiation. This demo can finish using in-memory
        // data; a real dependency failure should surface as an execution error.
        availability.IsAvailable = false;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new DraftChunk(request.DebateId, 2,
            activation.ActivatedExtensions.Contains(ExtensionUri), availability.IsAvailable, "Acceptance criteria");
    }
}

internal sealed class DemoAvailability
{
    private int _available = 1;
    private int _policyCalls;
    private int _invocations;
    internal bool IsAvailable { get => Volatile.Read(ref _available) != 0; set => Volatile.Write(ref _available, value ? 1 : 0); }
    internal int PolicyCalls => Volatile.Read(ref _policyCalls);
    internal int Invocations => Volatile.Read(ref _invocations);

    internal ValueTask<bool> CanActivate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _policyCalls);
        return ValueTask.FromResult(IsAvailable);
    }

    internal void RecordInvocation() => Interlocked.Increment(ref _invocations);
}

internal sealed record ResumeDebateRequest(string DebateId);
internal sealed record ResumeDebateResult(string DebateId, bool IsActive);
internal sealed record DraftChunk(string DebateId, int Sequence, bool IsActive, bool AvailableForNewRequests, string Text);

[JsonSerializable(typeof(ResumeDebateRequest))]
[JsonSerializable(typeof(ResumeDebateResult))]
[JsonSerializable(typeof(DraftChunk))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DebateJsonContext : JsonSerializerContext;
