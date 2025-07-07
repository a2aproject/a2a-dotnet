using System.Text.Json.Serialization;

namespace A2A;

[JsonConverter(typeof(KebabCaseLowerJsonStringEnumConverter<TaskState>))]
public enum TaskState
{
    Submitted,
    Working,
    InputRequired,
    Completed,
    Canceled,
    Failed,
    Rejected,
    AuthRequired,
    Unknown
}