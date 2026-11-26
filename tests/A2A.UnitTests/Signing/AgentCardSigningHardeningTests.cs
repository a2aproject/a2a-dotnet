using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace A2A.UnitTests.Signing;

/// <summary>
/// Tampering and hostile input: verification must fail closed, never throw and never accept a changed card.
/// </summary>
public sealed class AgentCardSigningHardeningTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => _key.Dispose();

    private AgentCardKeyResolver Resolver => (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(_key);

    private static AgentCard SecuredCard()
    {
        var card = AgentCardSigningTests.ProbeCard();
        card.SecuritySchemes = new()
        {
            ["key"] = new SecurityScheme { ApiKeySecurityScheme = new ApiKeySecurityScheme { Location = "header", Name = "X-Api-Key" } },
        };
        card.SecurityRequirements = [new SecurityRequirement { Schemes = new() { ["key"] = new StringList() } }];
        return card;
    }

    private JsonObject SignedJson(AgentCard card)
    {
        AgentCardSigning.Sign(card, _key, "k");

        // Parse case-sensitively, so a test adds a second member rather than overwriting the existing one.
        return JsonNode.Parse(JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions))!.AsObject();
    }

    private static JsonDocument Parse(JsonNode node) => JsonDocument.Parse(node.ToJsonString());

    [Fact]
    public async Task AddedEmptyMembers_UnderAnotherCase_DoNotVerify()
    {
        // System.Text.Json reads these case-insensitively, so ignoring them would let the model see
        // empty security, skills and interfaces under a valid signature.
        var served = SignedJson(SecuredCard());
        using (var untouched = Parse(served))
        {
            Assert.True((await AgentCardSigning.VerifyAsync(untouched.RootElement, Resolver)).IsVerified);
        }

        served["SecurityRequirements"] = new JsonArray();
        served["SecuritySchemes"] = new JsonObject();
        served["Skills"] = new JsonArray();
        served["SupportedInterfaces"] = new JsonArray();
        using var document = Parse(served);

        var result = await AgentCardSigning.VerifyAsync(document.RootElement, Resolver);

        Assert.False(result.IsVerified);
    }

    [Theory]
    [InlineData("futureFlag", "false")]
    [InlineData("futureList", "[]")]
    [InlineData("futureText", "\"\"")]
    [InlineData("Provider", "null")]
    [InlineData("Capabilities", "{}")]
    public async Task AnyAddedMember_ChangesTheSignedBytes(string name, string json)
    {
        var served = SignedJson(SecuredCard());
        using (var untouched = Parse(served))
        {
            Assert.True((await AgentCardSigning.VerifyAsync(untouched.RootElement, Resolver)).IsVerified);
        }

        served[name] = JsonNode.Parse(json);
        using var document = Parse(served);

        Assert.False((await AgentCardSigning.VerifyAsync(document.RootElement, Resolver)).IsVerified);
    }

    [Fact]
    public async Task NullOnADefinedField_MeansUnset_OnBothOverloads()
    {
        var served = SignedJson(SecuredCard());
        served["iconUrl"] = null;
        served["provider"] = null;
        using var document = Parse(served);

        Assert.True((await AgentCardSigning.VerifyAsync(document.RootElement, Resolver)).IsVerified);
        var card = document.RootElement.Deserialize<AgentCard>(A2AJsonUtilities.DefaultOptions)!;
        Assert.True((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Fact]
    public async Task EmptyStringListOfARequirement_IsSignedAsAnEmptyObject()
    {
        var card = SecuredCard();
        AgentCardSigning.Sign(card, _key, "k");

        using var payload = JsonDocument.Parse(AgentCardSigning.GetCanonicalPayload(card));
        Assert.Equal("{\"key\":{}}", payload.RootElement.GetProperty("securityRequirements")[0].GetProperty("schemes").GetRawText());
        Assert.True((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"protected\":null,\"signature\":null}]")]
    [InlineData("[{\"protected\":\"\",\"signature\":\"\"}]")]
    [InlineData("[{\"protected\":\"eyJhbGciOiJFUzI1NiJ9\"}]")]
    [InlineData("{\"not\":\"a list\"}")]
    public async Task MalformedSignatureEntries_FailInsteadOfThrowing(string signatures)
    {
        var served = SignedJson(SecuredCard());
        served["signatures"] = JsonNode.Parse(signatures);
        using var document = Parse(served);

        var result = await AgentCardSigning.VerifyAsync(document.RootElement, Resolver);

        Assert.False(result.IsVerified);
    }

    [Fact]
    public async Task ANullEntry_DoesNotHideAValidSignatureAfterIt()
    {
        var served = SignedJson(SecuredCard());
        served["signatures"]!.AsArray().Insert(0, null);
        using var document = Parse(served);

        var result = await AgentCardSigning.VerifyAsync(document.RootElement, Resolver);

        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
        Assert.Contains("null", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"alg\":\"ES256\",\"kid\":\"\\ud800\"}")]
    [InlineData("{\"alg\":\"\\ud800\",\"kid\":\"k\"}")]
    public async Task ProtectedHeadersWithInvalidStrings_FailInsteadOfThrowing(string header)
    {
        var card = SecuredCard();
        AgentCardSigning.Sign(card, _key, "k").Protected = AgentCardSigning.Base64UrlEncode(Encoding.UTF8.GetBytes(header));

        Assert.False((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Fact]
    public async Task ProtectedHeaderWithInvalidUtf8_FailsInsteadOfThrowing()
    {
        var card = SecuredCard();
        AgentCardSigning.Sign(card, _key, "k").Protected =
            AgentCardSigning.Base64UrlEncode([.. "{\"alg\":\"ES256\",\"kid\":\""u8, 0xFF, .. "\"}"u8]);

        Assert.False((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Fact]
    public async Task CardWithInvalidUtf8_FailsInsteadOfThrowing()
    {
        var text = SignedJson(SecuredCard()).ToJsonString().Replace("\"name\":\"Interop Probe\"", "\"name\":\"Interop Probe\",\"iconUrl\":\"#\"", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(text);
        bytes[Array.IndexOf(bytes, (byte)'#')] = 0xFF;
        using var document = await JsonDocument.ParseAsync(new MemoryStream(bytes));

        Assert.False((await AgentCardSigning.VerifyAsync(document.RootElement, Resolver)).IsVerified);
    }

    [Fact]
    public async Task DeeplyNestedParams_SignAndVerifyFromTheModel_UpToTheBound()
    {
        var card = SecuredCard();
        card.Capabilities.Extensions = [new AgentExtension { Uri = "https://example.com/deep", Params = Nest(100) }];
        AgentCardSigning.Sign(card, _key, "k");

        Assert.True((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);

        var tooDeep = SecuredCard();
        tooDeep.Capabilities.Extensions = [new AgentExtension { Uri = "https://example.com/deep", Params = Nest(200) }];
        Assert.Throws<ArgumentException>(() => AgentCardSigning.Sign(tooDeep, _key, "k"));
        tooDeep.Signatures = card.Signatures;
        Assert.False((await AgentCardSigning.VerifyAsync(tooDeep, Resolver)).IsVerified);
    }

    private static JsonElement Nest(int depth)
    {
        var text = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 });
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task MemberNamesDifferingOnlyByCase_AreRejected_ButMapKeysAreNot()
    {
        var served = SignedJson(SecuredCard());
        served["Name"] = "Other";
        using var collision = Parse(served);
        Assert.False((await AgentCardSigning.VerifyAsync(collision.RootElement, Resolver)).IsVerified);

        var card = SecuredCard();
        card.SecuritySchemes!["KEY"] = new SecurityScheme { ApiKeySecurityScheme = new ApiKeySecurityScheme { Location = "query", Name = "k" } };
        AgentCardSigning.Sign(card, _key, "k");
        Assert.True((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Fact]
    public async Task NonCanonicalBase64UrlSignature_IsRejected()
    {
        var card = SecuredCard();
        var signature = AgentCardSigning.Sign(card, _key, "k");

        // A 64-byte value leaves 4 unused bits in the last character; flipping one keeps the decoded bytes.
        var last = signature.Signature[^1];
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        signature.Signature = signature.Signature[..^1] + alphabet[alphabet.IndexOf(last, StringComparison.Ordinal) ^ 1];

        Assert.False((await AgentCardSigning.VerifyAsync(card, Resolver)).IsVerified);
    }

    [Fact]
    public void Sign_RejectsAKeyIdWithAnUnpairedSurrogate() =>
        Assert.Throws<ArgumentException>(() => AgentCardSigning.Sign(SecuredCard(), _key, "k\ud800"));

    [Fact]
    public void SupportedAlgorithms_CannotBeChanged()
    {
        Assert.False(AgentCardSigning.SupportedAlgorithms is string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)AgentCardSigning.SupportedAlgorithms)[0] = "none");
    }

    [Theory]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"AQ\",\"y\":\"AQ\"}")]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"y\":\"AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"secp256k1\",\"x\":\"AQ\",\"y\":\"AQ\"}")]
    [InlineData("{\"kty\":\"RSA\",\"n\":\"\",\"e\":\"AQAB\"}")]
    [InlineData("{\"kty\":\"RSA\",\"e\":\"AQAB\"}")]
    [InlineData("{\"kty\":\"oct\",\"k\":\"AQ\"}")]
    public void ImportPublicJsonWebKey_ThrowsArgumentExceptionForBadKeys(string jwk)
    {
        using var document = JsonDocument.Parse(jwk);

        Assert.ThrowsAny<ArgumentException>(() => AgentCardSigning.ImportPublicJsonWebKey(document.RootElement));
    }

    [Fact]
    public async Task ErrorMessages_DoNotEchoControlCharactersFromTheHeader()
    {
        var card = SecuredCard();
        AgentCardSigning.Sign(card, _key, "k").Protected =
            AgentCardSigning.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"kid\":\"a\\r\\nFAKE LOG LINE\"}"));

        var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(null));

        var error = Assert.Single(result.Errors);
        Assert.DoesNotContain('\n', error);
        Assert.DoesNotContain('\r', error);
    }
}
