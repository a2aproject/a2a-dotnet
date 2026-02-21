using System.Text.Json;

namespace A2A;

/// <summary>
/// Provides the agent with pre-resolved context for the current request.
/// IDs are pre-generated, existing task is pre-fetched from store.
/// </summary>
public sealed class AgentContext
{
    /// <summary>The incoming client message.</summary>
    public required Message Message { get; init; }

    /// <summary>Existing task if continuing, null for new conversations.</summary>
    public AgentTask? Task { get; init; }

    /// <summary>The task ID — existing or newly generated.</summary>
    public required string TaskId { get; init; }

    /// <summary>The context ID — client-provided or newly generated.</summary>
    public required string ContextId { get; init; }

    /// <summary>Whether this is a streaming request (vs synchronous send).</summary>
    public required bool IsStreaming { get; init; }

    /// <summary>Original request metadata.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>First text content from message parts, or null.</summary>
    public string? UserText => Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text;

    /// <summary>Whether this continues an existing task.</summary>
    public bool IsContinuation => Task is not null;
}
