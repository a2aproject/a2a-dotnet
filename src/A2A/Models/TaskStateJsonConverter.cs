using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

public class TaskStateJsonConverter : JsonConverter<TaskState>
{
    public override TaskState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "submitted" => TaskState.Submitted,
            "working" => TaskState.Working,
            "input-required" => TaskState.InputRequired,
            "completed" => TaskState.Completed,
            "canceled" => TaskState.Canceled,
            "failed" => TaskState.Failed,
            "rejected" => TaskState.Rejected,
            "auth-required" => TaskState.AuthRequired,
            "unknown" => TaskState.Unknown,
            _ => throw new JsonException($"Unknown TaskState value: {value}")
        };
    }

    public override void Write(Utf8JsonWriter writer, TaskState value, JsonSerializerOptions options)
    {
        var stringValue = value switch
        {
            TaskState.Submitted => "submitted",
            TaskState.Working => "working",
            TaskState.InputRequired => "input-required",
            TaskState.Completed => "completed",
            TaskState.Canceled => "canceled",
            TaskState.Failed => "failed",
            TaskState.Rejected => "rejected",
            TaskState.AuthRequired => "auth-required",
            TaskState.Unknown => "unknown",
            _ => throw new JsonException($"Unknown TaskState value: {value}")
        };
        writer.WriteStringValue(stringValue);
    }
}
