using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base JSON converter for A2A enums that accepts both proto3-style SCREAMING_SNAKE_CASE
/// names (e.g., "ROLE_USER") and spec-compliant lowercase names (e.g., "user") during
/// deserialization. Serialization always produces proto3-style names.
/// </summary>
internal abstract class A2AEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private readonly Dictionary<string, TEnum> _readMap;
    private readonly Dictionary<TEnum, string> _writeMap;

    protected A2AEnumConverter((TEnum Value, string Proto3Name, string SpecName)[] mappings)
    {
        _readMap = new(mappings.Length * 2, StringComparer.OrdinalIgnoreCase);
        _writeMap = new(mappings.Length);
        foreach (var (value, proto3Name, specName) in mappings)
        {
            _readMap[proto3Name] = value;
            _readMap[specName] = value;
            _writeMap[value] = proto3Name;
        }
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string for {typeof(TEnum).Name}, got {reader.TokenType}.");
        }

        var value = reader.GetString()!;
        if (_readMap.TryGetValue(value, out var result))
        {
            return result;
        }

        throw new JsonException($"Unable to convert \"{value}\" to {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (_writeMap.TryGetValue(value, out var name))
        {
            writer.WriteStringValue(name);
            return;
        }

        throw new JsonException($"Unable to convert {typeof(TEnum).Name}.{value} to JSON.");
    }
}

/// <summary>JSON converter for <see cref="Role"/> that accepts both proto3 and spec-compliant names.</summary>
internal sealed class RoleConverter() : A2AEnumConverter<Role>(
[
    (Role.Unspecified, "ROLE_UNSPECIFIED", "unspecified"),
    (Role.User, "ROLE_USER", "user"),
    (Role.Agent, "ROLE_AGENT", "agent"),
]);

/// <summary>JSON converter for <see cref="TaskState"/> that accepts both proto3 and spec-compliant names.</summary>
internal sealed class TaskStateConverter() : A2AEnumConverter<TaskState>(
[
    (TaskState.Unspecified, "TASK_STATE_UNSPECIFIED", "unspecified"),
    (TaskState.Submitted, "TASK_STATE_SUBMITTED", "submitted"),
    (TaskState.Working, "TASK_STATE_WORKING", "working"),
    (TaskState.Completed, "TASK_STATE_COMPLETED", "completed"),
    (TaskState.Failed, "TASK_STATE_FAILED", "failed"),
    (TaskState.Canceled, "TASK_STATE_CANCELED", "canceled"),
    (TaskState.InputRequired, "TASK_STATE_INPUT_REQUIRED", "input-required"),
    (TaskState.Rejected, "TASK_STATE_REJECTED", "rejected"),
    (TaskState.AuthRequired, "TASK_STATE_AUTH_REQUIRED", "auth-required"),
]);
