using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace A2A.AspNetCore;

internal static class AgentCardHttpResult
{
    public static IResult Create(HttpResponse response, string json, string lastModified, string cacheControl, bool varyByVersion = false)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(jsonBytes))}\"");
        response.Headers.CacheControl = cacheControl;
        response.Headers.ETag = etag.ToString();
        response.Headers.LastModified = lastModified;
        if (varyByVersion)
        {
            response.Headers.Append("Vary", "A2A-Version");
        }

        if (IsNotModified(response.HttpContext.Request, etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(jsonBytes, "application/json");
    }

    private static bool IsNotModified(HttpRequest request, EntityTagHeaderValue etag)
    {
        // Do not evaluate a later precondition when an earlier one is present;
        // this endpoint does not yet implement If-Match or If-Unmodified-Since.
        if (request.Headers.ContainsKey(HeaderNames.IfMatch) || request.Headers.ContainsKey(HeaderNames.IfUnmodifiedSince))
        {
            return false;
        }

        var values = request.Headers.IfNoneMatch;
        if (values.Count == 0)
        {
            return false;
        }

        // Multiple header fields are equivalent to a comma-separated field value.
        // Entity tags are not quoted-strings: '\\' is ordinary etagc, not an escape.
        var field = string.Join(",", values.Select(static value => value ?? ""));
        var header = field.AsSpan();
        var position = 0;
        SkipWhitespace(header, ref position);

        if (position < header.Length && header[position] == '*')
        {
            position++;
            SkipWhitespace(header, ref position);
            return position == header.Length;
        }

        var matched = false;
        while (position < header.Length)
        {
            SkipWhitespace(header, ref position);
            if (position == header.Length)
            {
                break;
            }

            // Recipients tolerate empty list elements, including between fields.
            if (header[position] == ',')
            {
                position++;
                continue;
            }

            if (header[position] == 'W' && position + 1 < header.Length && header[position + 1] == '/')
            {
                position += 2;
            }

            if (position == header.Length || header[position] != '"')
            {
                return false;
            }

            var start = position++;
            while (position < header.Length && header[position] != '"')
            {
                var ch = header[position++];
                if (ch != 0x21 && (ch < 0x23 || ch > 0x7e) && (ch < 0x80 || ch > 0xff))
                {
                    return false;
                }
            }

            if (position == header.Length)
            {
                return false;
            }

            position++;
            matched |= header[start..position].Equals(etag.Tag.AsSpan(), StringComparison.Ordinal);
            SkipWhitespace(header, ref position);
            if (position < header.Length && header[position++] != ',')
            {
                return false;
            }
        }

        // Validate the entire field before honoring even an earlier matching tag.
        return matched;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        while (position < value.Length && (value[position] == ' ' || value[position] == '\t'))
        {
            position++;
        }
    }
}
