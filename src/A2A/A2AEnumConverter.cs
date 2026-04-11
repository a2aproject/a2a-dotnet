using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Base JSON converter for A2A enums that accepts both proto3-style SCREAMING_SNAKE_CASE
/// names (e.g., "ROLE_USER") and spec-compliant lowercase names (e.g., "user") during
/// deserialization. Serialization always produces proto3-style names.
/// </summary>
/// <remarks>
/// Proto3 names are read from <see cref="JsonStringEnumMemberNameAttribute"/> on each enum member.
/// Spec-compliant names are derived by stripping the proto3 prefix from the proto3
/// name, converting to lowercase, and replacing underscores with hyphens.
/// </remarks>
internal abstract class A2AEnumConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private readonly Dictionary<string, TEnum> _readMap;
    private readonly Dictionary<TEnum, string> _writeMap;

    protected A2AEnumConverter(string proto3Prefix)
    {
        var fields = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);
        _readMap = new(fields.Length * 2, StringComparer.OrdinalIgnoreCase);
        _writeMap = new(fields.Length);

        foreach (var field in fields)
        {
            var value = (TEnum)field.GetValue(null)!;
            var attr = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
            var proto3Name = attr?.Name ?? field.Name;

            _readMap[proto3Name] = value;
            _writeMap[value] = proto3Name;

            // Derive spec name: strip prefix, lowercase, replace _ with -
            var specName = proto3Name.StartsWith(proto3Prefix, StringComparison.Ordinal)
                ? proto3Name[proto3Prefix.Length..]
                : proto3Name;
            specName = specName.ToLowerInvariant().Replace('_', '-');
            _readMap[specName] = value;
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
internal sealed class RoleConverter() : A2AEnumConverter<Role>("ROLE_");

/// <summary>JSON converter for <see cref="TaskState"/> that accepts both proto3 and spec-compliant names.</summary>
internal sealed class TaskStateConverter() : A2AEnumConverter<TaskState>("TASK_STATE_");
