using System.Text.Json.Serialization;

namespace A2A;

[JsonConverter(typeof(TaskStateJsonConverter))]
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