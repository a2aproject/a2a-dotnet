namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the response to a list tasks request with pagination support.</summary>
public sealed class ListTasksResponse
{
    /// <summary>List of tasks matching the query.</summary>
    [JsonRequired]
    public List<AgentTask> Tasks { get; set; } = [];

    /// <summary>Token for the next page. Empty string when no more results.</summary>
    [JsonRequired]
    public string NextPageToken { get; set; } = string.Empty;

    /// <summary>Number of tasks in this page.</summary>
    [JsonRequired]
    public int PageSize { get; set; }

    /// <summary>Total number of matching tasks across all pages.</summary>
    [JsonRequired]
    public int TotalSize { get; set; }
}
