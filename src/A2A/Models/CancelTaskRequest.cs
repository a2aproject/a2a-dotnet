namespace A2A;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Represents a request to cancel a task.</summary>
public sealed class CancelTaskRequest
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the task identifier.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the metadata associated with this request.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
