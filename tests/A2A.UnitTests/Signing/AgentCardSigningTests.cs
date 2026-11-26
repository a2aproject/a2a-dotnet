using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace A2A.UnitTests.Signing;

/// <summary>
/// Agent Card signatures (A2A specification Section 8.4), checked against signing_interop_vectors.json:
/// one card signed by a2a-python, @a2a-js/sdk, a2a-go and an independent reference signer.
/// </summary>
public class AgentCardSigningTests
{
    private static readonly JsonDocument s_interop = JsonCanonicalizerTests.Load("signing_interop_vectors.json");

    private static JsonElement TestKey => s_interop.RootElement.GetProperty("test_key");

    private static JsonElement InteropVector(string id) =>
        s_interop.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    public static TheoryData<string> AcceptIds() => new(
        s_interop.RootElement.GetProperty("vectors").EnumerateArray()
            .Where(v => v.GetProperty("disposition").GetString() == "MUST-ACCEPT")
            .Select(v => v.GetProperty("id").GetString()!));

    private static readonly AsymmetricAlgorithm s_testPublicKey = AgentCardSigning.ImportPublicJsonWebKey(TestKey.GetProperty("jwk"));

    // The resolver keeps ownership of the keys it returns, so it hands out one cached instance.
    private static AgentCardKeyResolver TrustedTestKey(List<AgentCardSignatureKeyRequest>? requests = null) =>
        (request, _) =>
        {
            requests?.Add(request);
            return ValueTask.FromResult(request.KeyId == TestKey.GetProperty("kid").GetString() ? s_testPublicKey : null);
        };

    /// <summary>The published test key's private half, derived as the vector file describes.</summary>
    private static ECDsa DerivedTestPrivateKey()
    {
        var n = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TestKey.GetProperty("phrase").GetString()!));
        var d = (new BigInteger(hash, isUnsigned: true, isBigEndian: true) % (n - 1)) + 1;
        var dBytes = d.ToByteArray(isUnsigned: true, isBigEndian: true);
        var jwk = TestKey.GetProperty("jwk");
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = [.. new byte[32 - dBytes.Length], .. dBytes],
            Q = new ECPoint
            {
                X = Base64Url(jwk.GetProperty("x").GetString()!),
                Y = Base64Url(jwk.GetProperty("y").GetString()!),
            },
        });
    }

    private static byte[] Base64Url(string text) =>
        AgentCardSigning.TryBase64UrlDecode(text, out var bytes) ? bytes : throw new FormatException(text);

    /// <summary>The interop card without its signatures.</summary>
    internal static AgentCard ProbeCard()
    {
        var card = InteropVector("S0-001").GetProperty("served_card").Deserialize<AgentCard>(A2AJsonUtilities.DefaultOptions)!;
        card.Signatures = null;
        return card;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void Corpus_IsComplete()
    {
        var counts = s_interop.RootElement.GetProperty("counts");
        Assert.Equal(4, counts.GetProperty("accept").GetInt32());
        Assert.Equal(1, counts.GetProperty("reject").GetInt32());
        Assert.Equal(5, s_interop.RootElement.GetProperty("vectors").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(AcceptIds))]
    public async Task InteropVector_VerifiesAsServed(string id)
    {
        var served = InteropVector(id).GetProperty("served_card");

        var result = await AgentCardSigning.VerifyAsync(served, TrustedTestKey(), new() { AllowedAlgorithms = ["ES256"] });

        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
        Assert.Equal("hs-interop-test-v1", result.KeyId);
        Assert.Equal("ES256", result.Algorithm);
    }

    [Theory]
    [MemberData(nameof(AcceptIds))]
    public async Task InteropVector_VerifiesFromTheModel_AndCanonicalizesToTheCorpusBytes(string id)
    {
        var vector = InteropVector(id);
        var card = vector.GetProperty("served_card").Deserialize<AgentCard>(A2AJsonUtilities.DefaultOptions)!;

        Assert.Equal(vector.GetProperty("canonical_utf8_hex").GetString(), Hex(AgentCardSigning.GetCanonicalPayload(card)));

        var result = await AgentCardSigning.VerifyAsync(card, TrustedTestKey());
        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task InteropRejectVector_DoesNotVerify()
    {
        var served = InteropVector("S0-REJECT-005").GetProperty("served_card");

        var result = await AgentCardSigning.VerifyAsync(served, TrustedTestKey());

        Assert.False(result.IsVerified);
        Assert.Contains(result.Errors, e => e.Contains("does not verify", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sign_WithTheDerivedTestKey_ProducesAPayloadOtherSdksSign()
    {
        using var key = DerivedTestPrivateKey();
        var card = ProbeCard();

        var signature = AgentCardSigning.Sign(card, key, "hs-interop-test-v1", new() { JwksUrl = new("https://example.com/jwks.json") });

        Assert.Same(signature, Assert.Single(card.Signatures!));
        Assert.Equal(InteropVector("S0-001").GetProperty("canonical_utf8_hex").GetString(), Hex(AgentCardSigning.GetCanonicalPayload(card)));
        Assert.Equal(
            "{\"alg\":\"ES256\",\"typ\":\"JOSE\",\"kid\":\"hs-interop-test-v1\",\"jku\":\"https://example.com/jwks.json\"}",
            Encoding.UTF8.GetString(Base64Url(signature.Protected)));
        Assert.Equal(64, Base64Url(signature.Signature).Length);

        var requests = new List<AgentCardSignatureKeyRequest>();
        var result = await AgentCardSigning.VerifyAsync(card, TrustedTestKey(requests));
        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
        Assert.Equal("https://example.com/jwks.json", Assert.Single(requests).JwksUrl);
    }

    [Fact]
    public async Task Verify_FailsWhenTheCardChangesAfterSigning()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, key, "k");

        card.Skills[0].Description = "Answers something else";

        var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key));
        Assert.False(result.IsVerified);
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("RS384")]
    [InlineData("RS512")]
    [InlineData("PS256")]
    [InlineData("PS384")]
    [InlineData("PS512")]
    public async Task Rsa_RoundTrips(string algorithm)
    {
        using var key = RSA.Create(2048);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, key, "rsa-1", new() { Algorithm = algorithm });

        var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key));

        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
        Assert.Equal(algorithm, result.Algorithm);
    }

    [Theory]
    [InlineData("ES384", 384)]
    [InlineData("ES512", 521)]
    public async Task Ecdsa_RoundTripsOnEachCurve(string algorithm, int keySize)
    {
        using var key = ECDsa.Create(keySize == 384 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP521);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, key, "ec", new() { Algorithm = algorithm });

        var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key));

        Assert.True(result.IsVerified, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Sign_RefusesAKeyThatDoesNotMatchTheAlgorithm()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var rsa = RSA.Create(1024);

        Assert.Throws<ArgumentException>(() => AgentCardSigning.Sign(ProbeCard(), p384, "k"));
        Assert.Throws<ArgumentException>(() => AgentCardSigning.Sign(ProbeCard(), rsa, "k", new() { Algorithm = "RS256" }));
        Assert.Throws<ArgumentException>(() => AgentCardSigning.Sign(ProbeCard(), p384, "k", new() { Algorithm = "HS256" }));
    }

    [Fact]
    public async Task Verify_RejectsAlgorithmsOutsideTheAllowList()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, key, "k");

        var result = await AgentCardSigning.VerifyAsync(
            card,
            (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key),
            new() { AllowedAlgorithms = ["PS256"] });

        Assert.False(result.IsVerified);
        Assert.Contains("'ES256' is not accepted", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_RejectsAKeyOfTheWrongTypeForTheHeaderAlgorithm()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var rsa = RSA.Create(2048);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, signingKey, "k");

        foreach (AsymmetricAlgorithm key in new AsymmetricAlgorithm[] { otherCurve, rsa })
        {
            var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key));
            Assert.False(result.IsVerified);
        }
    }

    [Theory]
    [InlineData("{\"alg\":\"none\",\"kid\":\"k\"}", "'none' is not accepted")]
    [InlineData("{\"alg\":\"HS256\",\"kid\":\"k\"}", "'HS256' is not accepted")]
    [InlineData("{\"kid\":\"k\"}", "has no alg")]
    [InlineData("{\"alg\":\"ES256\",\"kid\":\"k\",\"crit\":[\"exp\"],\"exp\":1}", "critical extensions")]
    [InlineData("{\"alg\":\"ES256\",\"kid\":\"k\",\"b64\":false}", "b64")]
    [InlineData("[\"ES256\"]", "not a base64url-encoded JSON object")]
    public async Task Verify_RejectsUnsafeProtectedHeaders(string header, string reason)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var card = ProbeCard();
        var signed = AgentCardSigning.Sign(card, key, "k");
        signed.Protected = AgentCardSigning.Base64UrlEncode(Encoding.UTF8.GetBytes(header));

        var result = await AgentCardSigning.VerifyAsync(card, (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key));

        Assert.False(result.IsVerified);
        Assert.Contains(reason, Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_AcceptsWhenAnyOneSignatureVerifies_AndReportsTheOthers()
    {
        using var unknown = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var card = ProbeCard();
        AgentCardSigning.Sign(card, unknown, "retired");
        AgentCardSigning.Sign(card, trusted, "current");

        var result = await AgentCardSigning.VerifyAsync(
            card,
            (request, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(request.KeyId == "current" ? trusted : null));

        Assert.True(result.IsVerified);
        Assert.Equal("current", result.KeyId);
        Assert.Contains("no trusted key for kid 'retired'", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_WithoutSignatures_Fails()
    {
        var result = await AgentCardSigning.VerifyAsync(ProbeCard(), (_, _) => throw new InvalidOperationException("not called"));

        Assert.False(result.IsVerified);
        Assert.Equal("The Agent Card has no signatures.", Assert.Single(result.Errors));
    }

    [Fact]
    public void Canonicalization_AppliesProtoFieldPresence()
    {
        var card = ProbeCard();
        card.Capabilities.Streaming = false;
        card.Capabilities.PushNotifications = null;
        card.Capabilities.Extensions =
        [
            // `required` and `description` are plain proto3 fields, so their defaults are dropped.
            new AgentExtension { Uri = "https://example.com/ext/a", Required = false, Description = "" },
            new AgentExtension { Uri = "https://example.com/ext/b", Required = true, Params = JsonDocument.Parse("{\"keep\":\"\",\"list\":[]}").RootElement },
        ];
        card.Skills[0].Examples = [];
        card.Provider = new AgentProvider { Organization = "Example", Url = "https://example.com" };

        using var payload = JsonDocument.Parse(AgentCardSigning.GetCanonicalPayload(card));
        var root = payload.RootElement;
        var capabilities = root.GetProperty("capabilities");
        var extensions = capabilities.GetProperty("extensions");

        // An `optional` field explicitly set to its default is kept; an unset one is omitted.
        Assert.False(capabilities.GetProperty("streaming").GetBoolean());
        Assert.False(capabilities.TryGetProperty("pushNotifications", out _));
        Assert.Equal("{\"uri\":\"https://example.com/ext/a\"}", extensions[0].GetRawText());
        Assert.True(extensions[1].GetProperty("required").GetBoolean());

        // google.protobuf.Struct contents are data and are not pruned.
        Assert.Equal("{\"keep\":\"\",\"list\":[]}", extensions[1].GetProperty("params").GetRawText());
        Assert.False(root.GetProperty("skills")[0].TryGetProperty("examples", out _));
        Assert.Equal("Example", root.GetProperty("provider").GetProperty("organization").GetString());
    }

    [Fact]
    public void Canonicalization_KeepsRequiredFieldsEvenWhenEmpty()
    {
        var card = ProbeCard();
        card.Skills[0].Description = string.Empty;

        using var payload = JsonDocument.Parse(AgentCardSigning.GetCanonicalPayload(card));

        Assert.Equal(string.Empty, payload.RootElement.GetProperty("skills")[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task VerifyJson_CoversMembersTheModelDoesNotDefine()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var served = JsonSerializer.SerializeToNode(ProbeCard(), A2AJsonUtilities.DefaultOptions)!.AsObject();
        served["futureField"] = "from a newer protocol version";

        // Sign the JSON form directly, as a signer on a newer SDK would.
        var payload = JsonCanonicalizer.Canonicalize(JsonSerializer.SerializeToElement(served, A2AJsonUtilities.DefaultOptions), CanonicalSchema.AgentCard);
        var header = AgentCardSigning.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"kid\":\"k\"}"));
        var signature = key.SignData(Encoding.ASCII.GetBytes(header + "." + AgentCardSigning.Base64UrlEncode(payload)), HashAlgorithmName.SHA256);
        served["signatures"] = JsonSerializer.SerializeToNode(
            new List<AgentCardSignature> { new() { Protected = header, Signature = AgentCardSigning.Base64UrlEncode(signature) } },
            A2AJsonUtilities.DefaultOptions);
        var json = JsonSerializer.SerializeToElement(served, A2AJsonUtilities.DefaultOptions);
        AgentCardKeyResolver resolver = (_, _) => ValueTask.FromResult<AsymmetricAlgorithm?>(key);

        Assert.True((await AgentCardSigning.VerifyAsync(json, resolver)).IsVerified);

        // The model drops the unknown member, so the model overload cannot reproduce the signed bytes.
        var card = json.Deserialize<AgentCard>(A2AJsonUtilities.DefaultOptions)!;
        Assert.False((await AgentCardSigning.VerifyAsync(card, resolver)).IsVerified);
    }

    [Fact]
    public void ImportPublicJsonWebKey_ReadsEcAndRsaKeys()
    {
        using var ec = AgentCardSigning.ImportPublicJsonWebKey(TestKey.GetProperty("jwk"));
        Assert.Equal(256, Assert.IsAssignableFrom<ECDsa>(ec).KeySize);

        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        using var jwk = JsonDocument.Parse(
            $"{{\"kty\":\"RSA\",\"n\":\"{AgentCardSigning.Base64UrlEncode(parameters.Modulus!)}\",\"e\":\"{AgentCardSigning.Base64UrlEncode(parameters.Exponent!)}\"}}");
        using var imported = AgentCardSigning.ImportPublicJsonWebKey(jwk.RootElement);
        Assert.Equal(parameters.Modulus, Assert.IsAssignableFrom<RSA>(imported).ExportParameters(false).Modulus);

        using var okp = JsonDocument.Parse("{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"AA\"}");
        Assert.Throws<ArgumentException>(() => AgentCardSigning.ImportPublicJsonWebKey(okp.RootElement));
    }
}
