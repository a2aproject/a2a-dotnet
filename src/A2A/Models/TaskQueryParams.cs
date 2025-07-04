using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents parameters for querying task information, extending task identification with additional query options.
/// </summary>
public class TaskQueryParams : TaskIdParams
{
    /// <summary>
    /// Gets or sets the maximum number of history entries to retrieve for the task.
    /// </summary>
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }
}