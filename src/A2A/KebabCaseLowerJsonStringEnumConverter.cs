using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KebabCaseLowerJsonStringEnumConverter<TEnum>() :
    JsonStringEnumConverter<TEnum>(JsonNamingPolicy.KebabCaseLower)
    where TEnum : struct, Enum;