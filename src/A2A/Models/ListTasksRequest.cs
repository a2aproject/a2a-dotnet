namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to list tasks with pagination.</summary>
public sealed class ListTasksRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the context identifier filter.</summary>
    public string? ContextId { get; set; }

    /// <summary>Gets or sets the status filter.</summary>
    public TaskState? Status { get; set; }

    /// <summary>Gets or sets the page size.</summary>
    public int? PageSize { get; set; }

    /// <summary>Gets or sets the page token for cursor-based pagination.</summary>
    public string? PageToken { get; set; }

    /// <summary>Gets or sets the history length to include.</summary>
    public int? HistoryLength { get; set; }

    /// <summary>Gets or sets a filter for tasks with status timestamps after this value.</summary>
    public DateTimeOffset? StatusTimestampAfter { get; set; }

    /// <summary>Gets or sets whether to include artifacts in the response.</summary>
    public bool? IncludeArtifacts { get; set; }
}
