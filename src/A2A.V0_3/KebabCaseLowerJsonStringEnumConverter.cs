using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.V0_3;

/// <summary>
/// A JSON string enum converter that converts enum values to kebab-case lower strings.
/// </summary>
/// <typeparam name="TEnum">The type of the enum to convert.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KebabCaseLowerJsonStringEnumConverter<TEnum>() :
    JsonStringEnumConverter<TEnum>(JsonNamingPolicy.KebabCaseLower)
    where TEnum : struct, Enum;
