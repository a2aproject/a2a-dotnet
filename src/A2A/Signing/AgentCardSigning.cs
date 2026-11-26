using System.Buffers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>
/// Signs and verifies Agent Cards with JSON Web Signatures, as A2A specification Section 8.4 defines.
/// </summary>
/// <remarks>
/// <para>
/// The signed payload is the RFC 8785 canonical form of the card, without its <c>signatures</c> member and
/// with field presence applied as Section 8.4.1 rule 1 requires. The JWS payload is detached: only the
/// protected header and the signature value are stored in <see cref="AgentCardSignature"/>.
/// </para>
/// <para>
/// Supported algorithms are ES256, ES384 and ES512 (ECDSA on P-256, P-384 and P-521) and RS256, RS384,
/// RS512, PS256, PS384 and PS512 (RSA keys of at least 2048 bits).
/// </para>
/// </remarks>
public static class AgentCardSigning
{
    private const int MinimumRsaKeySize = 2048;

    private static readonly SearchValues<char> s_base64UrlAlphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    private static readonly string[] s_supportedAlgorithms =
        ["ES256", "ES384", "ES512", "RS256", "RS384", "RS512", "PS256", "PS384", "PS512"];

    private static readonly ReadOnlyCollection<string> s_supportedAlgorithmsView = Array.AsReadOnly(s_supportedAlgorithms);

    // Struct-typed fields such as extension params can nest deeper than the serializer's default of 64;
    // the canonicalizer enforces its own bound of 128 containers.
    private static readonly JsonTypeInfo<AgentCard> s_deepAgentCard = (JsonTypeInfo<AgentCard>)new JsonSerializerOptions(A2AJsonUtilities.DefaultOptions)
    {
        MaxDepth = 4 * JsonCanonicalizer.MaxDepth,
    }.GetTypeInfo(typeof(AgentCard));

    /// <summary>Gets the JWS algorithms this SDK can sign and verify.</summary>
    public static IReadOnlyList<string> SupportedAlgorithms => s_supportedAlgorithmsView;

    /// <summary>
    /// Signs <paramref name="agentCard"/>, adds the signature to <see cref="AgentCard.Signatures"/> and returns it.
    /// </summary>
    /// <param name="agentCard">The card to sign. Existing signatures are not covered by the new one.</param>
    /// <param name="signingKey">An ECDSA or RSA private key that matches the algorithm.</param>
    /// <param name="keyId">The <c>kid</c> a verifier uses to select the public key.</param>
    /// <param name="options">The algorithm and optional header parameters. ES256 is used when omitted.</param>
    /// <returns>The signature that was added to the card.</returns>
    /// <exception cref="ArgumentException">The key does not match the algorithm, or the card cannot be canonicalized.</exception>
    public static AgentCardSignature Sign(AgentCard agentCard, AsymmetricAlgorithm signingKey, string keyId, AgentCardSigningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentCard);
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentException.ThrowIfNullOrEmpty(keyId);
        if (!JsonCanonicalizer.IsWellFormed(keyId))
        {
            throw new ArgumentException("The key ID contains an unpaired surrogate.", nameof(keyId));
        }

        var algorithm = options?.Algorithm ?? "ES256";
        if (!IsSupported(algorithm))
        {
            throw new ArgumentException($"Algorithm '{algorithm}' is not supported.", nameof(options));
        }

        if (!KeyMatches(algorithm, signingKey, out var keyError))
        {
            throw new ArgumentException(keyError, nameof(signingKey));
        }

        byte[] payload;
        try
        {
            payload = GetCanonicalPayload(agentCard);
        }
        catch (JsonCanonicalizationException e)
        {
            throw new ArgumentException($"The Agent Card cannot be canonicalized: {e.Message}", nameof(agentCard), e);
        }

        var protectedHeader = Base64UrlEncode(ProtectedHeader(algorithm, keyId, options?.JwksUrl));
        var signingInput = Encoding.ASCII.GetBytes(protectedHeader + "." + Base64UrlEncode(payload));

        var signature = new AgentCardSignature
        {
            Protected = protectedHeader,
            Signature = Base64UrlEncode(SignData(algorithm, signingKey, signingInput)),
            Header = options?.Header is { Count: > 0 } header ? new Dictionary<string, JsonElement>(header) : null,
        };

        (agentCard.Signatures ??= []).Add(signature);
        return signature;
    }

    /// <summary>
    /// Verifies the signatures on <paramref name="agentCard"/>. Verification succeeds when at least one signature
    /// is valid under a key that <paramref name="keyResolver"/> returns.
    /// </summary>
    /// <remarks>
    /// The card is canonicalized from the SDK model, so members the model does not define are not covered.
    /// To verify a card exactly as it was served, use the <see cref="JsonElement"/> overload.
    /// </remarks>
    /// <param name="agentCard">The card to verify.</param>
    /// <param name="keyResolver">Returns the trusted public key for a signature, or <see langword="null"/> when there is none.</param>
    /// <param name="options">Restricts the accepted algorithms. All supported algorithms are accepted when omitted.</param>
    /// <param name="cancellationToken">A token to cancel key resolution.</param>
    /// <returns>The outcome, including the signature that verified or the reason each one did not.</returns>
    public static ValueTask<AgentCardSignatureVerificationResult> VerifyAsync(
        AgentCard agentCard,
        AgentCardKeyResolver keyResolver,
        AgentCardVerificationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentCard);
        ArgumentNullException.ThrowIfNull(keyResolver);

        return VerifyCoreAsync(
            [.. agentCard.Signatures ?? []],
            () => GetCanonicalPayload(agentCard),
            keyResolver,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Verifies the signatures on an Agent Card in its JSON form, as it was served. Verification succeeds when
    /// at least one signature is valid under a key that <paramref name="keyResolver"/> returns.
    /// </summary>
    /// <param name="agentCardJson">The Agent Card JSON object, including its <c>signatures</c> member.</param>
    /// <param name="keyResolver">Returns the trusted public key for a signature, or <see langword="null"/> when there is none.</param>
    /// <param name="options">Restricts the accepted algorithms. All supported algorithms are accepted when omitted.</param>
    /// <param name="cancellationToken">A token to cancel key resolution.</param>
    /// <returns>The outcome, including the signature that verified or the reason each one did not.</returns>
    /// <remarks>
    /// Parse the card with a <see cref="JsonDocumentOptions.MaxDepth"/> of at least 128 if its extension
    /// parameters may nest deeply; the canonical form allows 128 levels.
    /// </remarks>
    public static ValueTask<AgentCardSignatureVerificationResult> VerifyAsync(
        JsonElement agentCardJson,
        AgentCardKeyResolver keyResolver,
        AgentCardVerificationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyResolver);
        if (agentCardJson.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The Agent Card JSON must be an object.", nameof(agentCardJson));
        }

        List<AgentCardSignature?> signatures;
        try
        {
            signatures = agentCardJson.TryGetProperty("signatures", out var value) && value.ValueKind != JsonValueKind.Null
                ? [.. value.Deserialize(A2AJsonUtilities.JsonContext.Default.ListAgentCardSignature) ?? []]
                : [];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return ValueTask.FromResult(AgentCardSignatureVerificationResult.Failed(["The signatures member is malformed."]));
        }

        return VerifyCoreAsync(
            signatures,
            () => JsonCanonicalizer.Canonicalize(agentCardJson, CanonicalSchema.AgentCard),
            keyResolver,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Imports the public part of a JSON Web Key (RFC 7517) with <c>kty</c> <c>EC</c> (P-256, P-384, P-521) or <c>RSA</c>.
    /// </summary>
    /// <param name="jsonWebKey">The JWK object. Private members such as <c>d</c> are ignored.</param>
    /// <returns>An <see cref="ECDsa"/> or <see cref="RSA"/> instance holding only the public key.</returns>
    /// <exception cref="ArgumentException">The key type or curve is not supported, or a member is missing.</exception>
    public static AsymmetricAlgorithm ImportPublicJsonWebKey(JsonElement jsonWebKey)
    {
        if (jsonWebKey.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The JWK must be an object.", nameof(jsonWebKey));
        }

        AsymmetricAlgorithm? key = null;
        try
        {
            switch (Member(jsonWebKey, "kty"))
            {
                case "EC":
                    var (curve, size) = Member(jsonWebKey, "crv") switch
                    {
                        "P-256" => (ECCurve.NamedCurves.nistP256, 32),
                        "P-384" => (ECCurve.NamedCurves.nistP384, 48),
                        "P-521" => (ECCurve.NamedCurves.nistP521, 66),
                        _ => throw new ArgumentException("The JWK curve is not supported.", nameof(jsonWebKey)),
                    };
                    var x = Base64UrlMember(jsonWebKey, "x");
                    var y = Base64UrlMember(jsonWebKey, "y");
                    if (x.Length != size || y.Length != size)
                    {
                        throw new ArgumentException("The JWK point coordinates have the wrong length for the curve.", nameof(jsonWebKey));
                    }

                    key = ECDsa.Create();
                    ((ECDsa)key).ImportParameters(new ECParameters { Curve = curve, Q = new ECPoint { X = x, Y = y } });
                    return key;
                case "RSA":
                    var modulus = Base64UrlMember(jsonWebKey, "n");
                    var exponent = Base64UrlMember(jsonWebKey, "e");
                    if (modulus.Length == 0 || exponent.Length == 0)
                    {
                        throw new ArgumentException("The JWK RSA modulus or exponent is empty.", nameof(jsonWebKey));
                    }

                    key = RSA.Create();
                    ((RSA)key).ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
                    return key;
                default:
                    throw new ArgumentException("The JWK key type is not supported.", nameof(jsonWebKey));
            }
        }
        catch (Exception e) when (e is CryptographicException or IndexOutOfRangeException)
        {
            key?.Dispose();
            throw new ArgumentException("The JWK is not a valid public key.", nameof(jsonWebKey), e);
        }
        catch (ArgumentException)
        {
            key?.Dispose();
            throw;
        }
    }

    /// <summary>Returns the canonical payload a signature over <paramref name="agentCard"/> covers.</summary>
    /// <param name="agentCard">The card.</param>
    /// <returns>The canonical payload, encoded as UTF-8.</returns>
    internal static byte[] GetCanonicalPayload(AgentCard agentCard)
    {
        JsonElement json;
        try
        {
            json = JsonSerializer.SerializeToElement(agentCard, s_deepAgentCard);
        }
        catch (JsonException)
        {
            throw new JsonCanonicalizationException("The Agent Card nests too deeply to serialize.");
        }

        return JsonCanonicalizer.Canonicalize(json, CanonicalSchema.AgentCard);
    }

    private static async ValueTask<AgentCardSignatureVerificationResult> VerifyCoreAsync(
        List<AgentCardSignature?> signatures,
        Func<byte[]> canonicalPayload,
        AgentCardKeyResolver keyResolver,
        AgentCardVerificationOptions? options,
        CancellationToken cancellationToken)
    {
        if (signatures.Count == 0)
        {
            return AgentCardSignatureVerificationResult.Failed(["The Agent Card has no signatures."]);
        }

        string encodedPayload;
        try
        {
            encodedPayload = Base64UrlEncode(canonicalPayload());
        }
        catch (JsonCanonicalizationException e)
        {
            return AgentCardSignatureVerificationResult.Failed([$"The Agent Card cannot be canonicalized: {e.Message}"]);
        }

        var allowed = options?.AllowedAlgorithms ?? s_supportedAlgorithms;
        var errors = new List<string>();
        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            var error = signature is null
                ? "the signature entry is null."
                : await VerifyOneAsync(signature, encodedPayload, allowed, keyResolver, cancellationToken).ConfigureAwait(false);
            if (error is null)
            {
                var header = ReadProtectedHeader(signature!.Protected)!.Value;
                return AgentCardSignatureVerificationResult.Verified(signature, header.KeyId, header.Algorithm, errors);
            }

            errors.Add($"Signature {i}: {error}");
        }

        return AgentCardSignatureVerificationResult.Failed(errors);
    }

    private static async ValueTask<string?> VerifyOneAsync(
        AgentCardSignature signature,
        string encodedPayload,
        IReadOnlyCollection<string> allowedAlgorithms,
        AgentCardKeyResolver keyResolver,
        CancellationToken cancellationToken)
    {
        if (signature.Protected is null || signature.Signature is null)
        {
            return "the signature has no protected header or no signature value.";
        }

        if (ReadProtectedHeader(signature.Protected) is not { } header)
        {
            return "the protected header is not a base64url-encoded JSON object.";
        }

        if (header.Error is not null)
        {
            return header.Error;
        }

        if (!IsSupported(header.Algorithm) || !allowedAlgorithms.Contains(header.Algorithm, StringComparer.Ordinal))
        {
            return $"algorithm {Describe(header.Algorithm)} is not accepted.";
        }

        if (!TryBase64UrlDecode(signature.Signature, out var signatureBytes))
        {
            return "the signature is not base64url-encoded.";
        }

        var key = await keyResolver(
            new AgentCardSignatureKeyRequest(signature, header.KeyId, header.JwksUrl, header.Algorithm),
            cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return $"no trusted key for kid {Describe(header.KeyId)}.";
        }

        if (!KeyMatches(header.Algorithm, key, out var keyError))
        {
            return keyError;
        }

        var signingInput = Encoding.ASCII.GetBytes(signature.Protected + "." + encodedPayload);
        return VerifyData(header.Algorithm, key, signingInput, signatureBytes) ? null : "the signature does not verify.";
    }

    private readonly record struct ProtectedHeaderValues(string Algorithm, string? KeyId, string? JwksUrl, string? Error);

    private static ProtectedHeaderValues? ReadProtectedHeader(string encoded)
    {
        if (!TryBase64UrlDecode(encoded, out var bytes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var algorithm = root.TryGetProperty("alg", out var alg) && alg.ValueKind == JsonValueKind.String ? alg.GetString()! : string.Empty;
            var keyId = root.TryGetProperty("kid", out var kid) && kid.ValueKind == JsonValueKind.String ? kid.GetString() : null;
            var jwksUrl = root.TryGetProperty("jku", out var jku) && jku.ValueKind == JsonValueKind.String ? jku.GetString() : null;

            string? error = null;
            if (algorithm.Length == 0)
            {
                error = "the protected header has no alg.";
            }
            else if (root.TryGetProperty("crit", out _))
            {
                // RFC 7515 Section 4.1.11: a recipient that does not understand a critical extension must reject.
                error = "the protected header lists critical extensions, which are not supported.";
            }
            else if (root.TryGetProperty("b64", out _))
            {
                error = "the protected header sets b64 (RFC 7797), which is not supported.";
            }

            return new ProtectedHeaderValues(algorithm, keyId, jwksUrl, error);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Quotes an attacker-chosen header value for an error message, without control characters.</summary>
    /// <param name="value">The header value.</param>
    /// <returns>The value quoted and truncated to 64 characters.</returns>
    private static string Describe(string? value)
    {
        if (value is null)
        {
            return "(none)";
        }

        var printable = new StringBuilder(Math.Min(value.Length, 64) + 2).Append('\'');
        foreach (var c in value.Length > 64 ? value[..64] : value)
        {
            printable.Append(char.IsControl(c) || char.IsSurrogate(c) ? '?' : c);
        }

        return printable.Append(value.Length > 64 ? "...'" : "'").ToString();
    }

    private static byte[] ProtectedHeader(string algorithm, string keyId, Uri? jwksUrl)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", algorithm);
            writer.WriteString("typ", "JOSE");
            writer.WriteString("kid", keyId);
            if (jwksUrl is not null)
            {
                writer.WriteString("jku", jwksUrl.AbsoluteUri);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsSupported(string algorithm) => Array.IndexOf(s_supportedAlgorithms, algorithm) >= 0;

    private static HashAlgorithmName Hash(string algorithm) => algorithm[2..] switch
    {
        "256" => HashAlgorithmName.SHA256,
        "384" => HashAlgorithmName.SHA384,
        _ => HashAlgorithmName.SHA512,
    };

    private static RSASignaturePadding Padding(string algorithm) =>
        algorithm.StartsWith("PS", StringComparison.Ordinal) ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1;

    private static bool KeyMatches(string algorithm, AsymmetricAlgorithm key, out string error)
    {
        switch (key)
        {
            case ECDsa ecdsa when algorithm.StartsWith("ES", StringComparison.Ordinal):
                var (expected, oid) = algorithm switch
                {
                    "ES256" => (256, "1.2.840.10045.3.1.7"),
                    "ES384" => (384, "1.3.132.0.34"),
                    _ => (521, "1.3.132.0.35"),
                };
                error = ecdsa.KeySize == expected && IsNamedCurve(ecdsa, oid, expected)
                    ? string.Empty
                    : $"{algorithm} needs a P-{expected} key.";
                return error.Length == 0;
            case RSA rsa when algorithm[0] is 'R' or 'P':
                error = rsa.KeySize >= MinimumRsaKeySize ? string.Empty : $"{algorithm} needs an RSA key of at least {MinimumRsaKeySize} bits.";
                return error.Length == 0;
            default:
                error = $"the key type {key.GetType().Name} does not match {algorithm}.";
                return false;
        }
    }

    private static bool IsNamedCurve(ECDsa key, string oid, int size)
    {
        ECCurve curve;
        try
        {
            curve = key.ExportParameters(includePrivateParameters: false).Curve;
        }
        catch (CryptographicException)
        {
            return false;
        }

        // Platforms report a named curve by OID value, by friendly name, or both.
        return curve.IsNamed &&
            (curve.Oid.Value == oid ||
             curve.Oid.FriendlyName is { } name && (name == $"nistP{size}" || name == $"ECDSA_P{size}" || name == (size == 256 ? "secp256r1" : $"secp{size}r1")));
    }

    private static byte[] SignData(string algorithm, AsymmetricAlgorithm key, byte[] data) => key switch
    {
        // JWS uses the fixed-size R || S form (RFC 7518 Section 3.4), which is .NET's IEEE P1363 format.
        ECDsa ecdsa => ecdsa.SignData(data, Hash(algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
        RSA rsa => rsa.SignData(data, Hash(algorithm), Padding(algorithm)),
        _ => throw new ArgumentException($"The key type {key.GetType().Name} is not supported."),
    };

    private static bool VerifyData(string algorithm, AsymmetricAlgorithm key, byte[] data, byte[] signature)
    {
        try
        {
            return key switch
            {
                ECDsa ecdsa => ecdsa.VerifyData(data, signature, Hash(algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
                RSA rsa => rsa.VerifyData(data, signature, Hash(algorithm), Padding(algorithm)),
                _ => false,
            };
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Member(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ArgumentException($"The JWK has no '{name}' member.", nameof(element));

    private static byte[] Base64UrlMember(JsonElement element, string name) =>
        TryBase64UrlDecode(Member(element, name), out var bytes)
            ? bytes
            : throw new ArgumentException($"The JWK '{name}' member is not base64url-encoded.", nameof(element));

    internal static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static bool TryBase64UrlDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length % 4 == 1 || text.AsSpan().IndexOfAnyExcept(s_base64UrlAlphabet) >= 0)
        {
            return false;
        }

        var padded = text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - (text.Length % 4)) % 4);
        try
        {
            bytes = Convert.FromBase64String(padded);

            // Reject non-zero padding bits, so each value has one encoding (RFC 4648 Section 3.5).
            return Base64UrlEncode(bytes) == text;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
