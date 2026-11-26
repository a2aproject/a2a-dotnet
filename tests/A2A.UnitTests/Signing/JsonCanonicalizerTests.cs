using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Signing;

/// <summary>
/// RFC 8785 conformance against the language-neutral corpora a2a-python also runs
/// (jcs_vectors.json from a2aproject/a2a-tck#228 and jcs_depth_vectors.json from a2aproject/A2A#2246).
/// </summary>
public class JsonCanonicalizerTests
{
    private static readonly JsonDocument s_vectors = Load("jcs_vectors.json");
    private static readonly JsonDocument s_depthVectors = Load("jcs_depth_vectors.json");

    internal static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Signing", "Vectors", name)));

    private static IEnumerable<JsonElement> Vectors(string disposition) =>
        s_vectors.RootElement.GetProperty("vectors").EnumerateArray()
            .Where(v => v.GetProperty("disposition").GetString() == disposition);

    public static TheoryData<string> AcceptIds() => new(Vectors("MUST-ACCEPT").Select(v => v.GetProperty("id").GetString()!));

    public static TheoryData<string> RejectIds() => new(Vectors("MUST-REJECT").Select(v => v.GetProperty("id").GetString()!));

    public static TheoryData<string> DepthIds() => new(s_depthVectors.RootElement.GetProperty("vectors").EnumerateArray().Select(v => v.GetProperty("vector_id").GetString()!));

    private static JsonElement Vector(string id) =>
        s_vectors.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    [Fact]
    public void Corpus_IsComplete()
    {
        var counts = s_vectors.RootElement.GetProperty("counts");
        Assert.Equal(counts.GetProperty("accept").GetInt32(), Vectors("MUST-ACCEPT").Count());
        Assert.Equal(counts.GetProperty("reject").GetInt32(), Vectors("MUST-REJECT").Count());
        Assert.Equal(57, s_vectors.RootElement.GetProperty("vectors").GetArrayLength());
        Assert.Equal(8, s_depthVectors.RootElement.GetProperty("vectors").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(AcceptIds))]
    public void MustAccept_MatchesCorpusBytes(string id)
    {
        var vector = Vector(id);
        var input = vector.GetProperty("input");

        // The signatures-exclusion group describes Agent Card payloads, so the card schema applies (rule 3).
        var schema = vector.GetProperty("group").GetString() == "a2-signatures-exclusion" ? CanonicalSchema.AgentCard : null;

        var actual = JsonCanonicalizer.Canonicalize(input, schema);

        Assert.Equal(vector.GetProperty("canonical_utf8_hex").GetString(), Convert.ToHexString(actual).ToLowerInvariant());
    }

    [Theory]
    [MemberData(nameof(RejectIds))]
    public void MustReject_IsRefused(string id)
    {
        var vector = Vector(id);
        if (vector.GetProperty("group").GetString() == "a2-signatures-exclusion")
        {
            // These reject claimed-canonical output that still carries `signatures`, a property of the
            // producer. Assert the producer side: a signed card's payload has no signatures member.
            using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var card = AgentCardSigningTests.ProbeCard();
            AgentCardSigning.Sign(card, key, "k");
            using var payload = JsonDocument.Parse(AgentCardSigning.GetCanonicalPayload(card));
            Assert.False(payload.RootElement.TryGetProperty("signatures", out _));
            return;
        }

        var raw = vector.GetProperty("input_raw").GetString()!;
        var refused = Record.Exception(() =>
        {
            using var document = JsonDocument.Parse(raw);
            JsonCanonicalizer.Canonicalize(document.RootElement);
        });

        // NaN and Infinity are not JSON, so the parser refuses them before the canonicalizer runs.
        Assert.True(refused is JsonCanonicalizationException or JsonException, $"{id}: {refused?.GetType().Name ?? "accepted"}");
    }

    [Theory]
    [MemberData(nameof(DepthIds))]
    public void DepthBound_IsExact(string id)
    {
        var vector = s_depthVectors.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("vector_id").GetString() == id);
        var rule = vector.GetProperty("preimage_rule");
        var containers = rule.GetProperty("containers").EnumerateArray().Select(c => c.GetString()!).ToArray();
        var count = rule.GetProperty("count").GetInt32();

        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            text.Append(containers[i % containers.Length] == "object" ? "{\"a\":" : "[");
        }

        text.Append(rule.GetProperty("leaf").GetString());
        for (var i = count - 1; i >= 0; i--)
        {
            text.Append(containers[i % containers.Length] == "object" ? "}" : "]");
        }

        using var document = JsonDocument.Parse(text.ToString(), new JsonDocumentOptions { MaxDepth = 1024 });
        if (vector.GetProperty("outcome").GetString() == "accept")
        {
            var actual = JsonCanonicalizer.Canonicalize(document.RootElement);
            Assert.Equal(vector.GetProperty("expected_jcs_bytes_b64").GetString(), Convert.ToBase64String(actual));
        }
        else
        {
            Assert.Throws<JsonCanonicalizationException>(() => JsonCanonicalizer.Canonicalize(document.RootElement));
        }
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(1d, "1")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(123456789012345680000d, "123456789012345680000")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1.5e-7, "1.5e-7")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(9007199254740993d, "9007199254740992")]
    public void FormatNumber_FollowsEcmaScript(double value, string expected) =>
        Assert.Equal(expected, JsonCanonicalizer.FormatNumber(value));

    [Fact]
    public void FormatNumber_WritesNegativeZeroAsZero() =>
        Assert.Equal("0", JsonCanonicalizer.FormatNumber(-0d));

    [Fact]
    public void Strings_KeepNonAsciiLiteral_AndEscapeOnlyWhatRfc8785Requires()
    {
        using var document = JsonDocument.Parse("{\"k\":\"Caf\\u00e9 \\u2028 \\ud83d\\ude00 a\\\"b\\\\c\\nd\\te\\u0000f\\u001f\"}");

        var actual = Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(document.RootElement));

        Assert.Equal("{\"k\":\"Café \u2028 \U0001F600 a\\\"b\\\\c\\nd\\te\\u0000f\\u001f\"}", actual);
    }

    [Fact]
    public void Keys_SortByUtf16CodeUnits()
    {
        // U+1F600's leading surrogate 0xD83D sorts below U+FF01, although its code point sorts above it.
        using var document = JsonDocument.Parse("{\"！\":2,\"\U0001F600\":1,\"ab\":3,\"a\":4}");

        var actual = Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(document.RootElement));

        Assert.Equal("{\"a\":4,\"ab\":3,\"\U0001F600\":1,\"！\":2}", actual);
    }

    [Fact]
    public void DuplicateMemberNames_AreRefused()
    {
        using var document = JsonDocument.Parse("{\"a\":1,\"a\":2}");

        Assert.Throws<JsonCanonicalizationException>(() => JsonCanonicalizer.Canonicalize(document.RootElement));
    }

    [Fact]
    public void NumbersOutsideDoubleRange_AreRefused()
    {
        using var document = JsonDocument.Parse("[1e400]");

        Assert.Throws<JsonCanonicalizationException>(() => JsonCanonicalizer.Canonicalize(document.RootElement));
    }
}
