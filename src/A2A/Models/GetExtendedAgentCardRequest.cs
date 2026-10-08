namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents a request to get an extended agent card.</summary>
public sealed class GetExtendedAgentCardRequest
{
    /// <summary>Gets or sets the tenant routing identifier for the selected agent interface.</summary>
    public string? Tenant { get; set; }
}
