using System.Globalization;
using System.Text;
using System.Text.Json;

namespace A2A;

/// <summary>
/// The value has no RFC 8785 canonical form, for example because it holds a lone surrogate,
/// a duplicate member name, a non-finite number, or nests deeper than <see cref="JsonCanonicalizer.MaxDepth"/>.
/// </summary>
/// <param name="message">Why the value has no canonical form.</param>
internal sealed class JsonCanonicalizationException(string message) : Exception(message);

/// <summary>
/// RFC 8785 JSON Canonicalization Scheme (JCS) over <see cref="JsonElement"/> values.
/// </summary>
/// <remarks>
/// Members are ordered by their UTF-16 code units, numbers are written with the ECMAScript
/// <c>Number.prototype.toString</c> rules, and strings are written as literal UTF-8 with only
/// the escapes RFC 8785 Section 3.2.2.2 requires. An optional <see cref="CanonicalSchema"/>
/// drops members before they are written, which is how Agent Card field presence is applied.
/// </remarks>
internal static class JsonCanonicalizer
{
    /// <summary>
    /// The deepest nesting accepted, counting open containers with the outermost one at depth 1.
    /// This matches the a2a-python SDK and the jcs_depth_v1 vectors.
    /// </summary>
    internal const int MaxDepth = 128;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Returns the canonical UTF-8 bytes of <paramref name="value"/>.</summary>
    /// <param name="value">The JSON value to canonicalize.</param>
    /// <param name="schema">Field presence to apply to objects, or <see langword="null"/> for plain RFC 8785.</param>
    /// <returns>The canonical form, encoded as UTF-8.</returns>
    internal static byte[] Canonicalize(JsonElement value, CanonicalSchema? schema = null)
    {
        EnsureStringsDecode(value);
        var builder = new StringBuilder();
        WriteValue(builder, value, schema, depth: 0);
        try
        {
            return StrictUtf8.GetBytes(builder.ToString());
        }
        catch (EncoderFallbackException)
        {
            throw new JsonCanonicalizationException("The value contains text that is not valid Unicode.");
        }
    }

    private static void WriteValue(StringBuilder builder, JsonElement value, CanonicalSchema? schema, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(builder, value, schema, depth + 1);
                break;
            case JsonValueKind.Array:
                EnsureDepth(depth + 1);
                builder.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteValue(builder, item, schema, depth + 1);
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(builder, ReadString(value));
                break;
            case JsonValueKind.Number:
                WriteNumber(builder, value);
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new JsonCanonicalizationException($"Unsupported JSON value kind {value.ValueKind}.");
        }
    }

    private static void WriteObject(StringBuilder builder, JsonElement value, CanonicalSchema? schema, int depth)
    {
        EnsureDepth(depth);
        var members = new List<KeyValuePair<string, JsonElement>>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        // The SDK reads Agent Cards with case-insensitive property matching, so two members of a message that
        // differ only by case could be signed as one value and read as the other. Map keys are data and exempt.
        var messageNames = schema is { IsMap: false } ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        foreach (var property in value.EnumerateObject())
        {
            var name = property.Name;
            EnsureWellFormed(name);
            if (!names.Add(name))
            {
                throw new JsonCanonicalizationException("An object has a duplicate member name.");
            }

            if (messageNames is not null && !messageNames.Add(name))
            {
                throw new JsonCanonicalizationException("An object has member names that differ only by case.");
            }

            if (schema is null || schema.Includes(name, property.Value))
            {
                members.Add(new(name, property.Value));
            }
        }

        // Ordinal comparison orders strings by UTF-16 code units (RFC 8785 Section 3.2.3).
        members.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        builder.Append('{');
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            WriteString(builder, members[i].Key);
            builder.Append(':');
            WriteValue(builder, members[i].Value, schema?.ChildSchema(members[i].Key), depth);
        }

        builder.Append('}');
    }

    /// <summary>
    /// Rejects escapes that decode to invalid UTF-16. <see cref="JsonProperty.Name"/> replaces such an escape in
    /// a member name instead of failing, so names and values are decoded once with the strict reader first.
    /// </summary>
    /// <param name="value">The value to check.</param>
    private static void EnsureStringsDecode(JsonElement value)
    {
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value.GetRawText()), new JsonReaderOptions { MaxDepth = 4 * MaxDepth });
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
                {
                    EnsureWellFormed(reader.GetString()!);
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            throw new JsonCanonicalizationException("A string is not valid Unicode or the value nests too deeply.");
        }
    }

    private static void EnsureDepth(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonCanonicalizationException($"Nesting exceeds the maximum depth of {MaxDepth}.");
        }
    }

    private static string ReadString(JsonElement value)
    {
        string text;
        try
        {
            text = value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new JsonCanonicalizationException("A string value is not valid Unicode.");
        }

        EnsureWellFormed(text);
        return text;
    }

    private static void EnsureWellFormed(string text)
    {
        if (!IsWellFormed(text))
        {
            throw new JsonCanonicalizationException("A string contains an unpaired surrogate.");
        }
    }

    /// <summary>Gets a value indicating whether every surrogate in <paramref name="text"/> is paired.</summary>
    /// <param name="text">The text to check.</param>
    /// <returns><see langword="true"/> when the text is valid UTF-16.</returns>
    internal static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u00").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void WriteNumber(StringBuilder builder, JsonElement value)
    {
        var number = double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            throw new JsonCanonicalizationException("A number is outside the range of an IEEE 754 double.");
        }

        builder.Append(FormatNumber(number));
    }

    /// <summary>
    /// Formats a finite double as ECMAScript <c>Number.prototype.toString</c> does (ECMA-262 Number::toString),
    /// which RFC 8785 Section 3.2.2.3 requires.
    /// </summary>
    /// <param name="number">A finite number.</param>
    /// <returns>The ECMAScript string form of the number.</returns>
    internal static string FormatNumber(double number)
    {
        if (number == 0)
        {
            return "0";
        }

        if (number < 0)
        {
            return "-" + FormatNumber(-number);
        }

        // "R" gives the shortest digits that round-trip, which is the digit string ECMAScript uses.
        var roundTrip = number.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = roundTrip.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex < 0 ? roundTrip : roundTrip[..exponentIndex];
        var exponent = exponentIndex < 0 ? 0 : int.Parse(roundTrip[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        var point = mantissa.IndexOf('.');
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var integerDigits = point < 0 ? mantissa.Length : point;

        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits.Trim('0');

        // The value is 0.digits * 10^n, so n is the position of the decimal point.
        var k = digits.Length;
        var n = integerDigits - leadingZeros + exponent;

        if (k <= n && n <= 21)
        {
            return digits + new string('0', n - k);
        }

        if (0 < n && n <= 21)
        {
            return digits[..n] + "." + digits[n..];
        }

        if (-6 < n && n <= 0)
        {
            return "0." + new string('0', -n) + digits;
        }

        var e = n - 1;
        var sign = e < 0 ? "-" : "+";
        var magnitude = Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        return k == 1
            ? digits + "e" + sign + magnitude
            : digits[..1] + "." + digits[1..] + "e" + sign + magnitude;
    }
}
