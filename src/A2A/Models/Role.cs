namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the role of a message sender in the A2A protocol.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
public enum Role
{
    /// <summary>Unspecified role.</summary>
    [JsonStringEnumMemberName("ROLE_UNSPECIFIED")]
    Unspecified = 0,

    /// <summary>User role.</summary>
    [JsonStringEnumMemberName("ROLE_USER")]
    User = 1,

    /// <summary>Agent role.</summary>
    [JsonStringEnumMemberName("ROLE_AGENT")]
    Agent = 2,
}
