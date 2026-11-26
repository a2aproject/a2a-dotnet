using System.Security.Cryptography;
using System.Text.Json;

namespace A2A;

/// <summary>Options for <see cref="AgentCardSigning.Sign"/>.</summary>
public sealed class AgentCardSigningOptions
{
    /// <summary>Gets or sets the JWS algorithm. Defaults to ES256.</summary>
    public string? Algorithm { get; set; }

    /// <summary>Gets or sets the JWK Set URL written as <c>jku</c> in the protected header.</summary>
    public Uri? JwksUrl { get; set; }

    /// <summary>Gets or sets unprotected header parameters. They are not covered by the signature.</summary>
    public IDictionary<string, JsonElement>? Header { get; set; }
}

/// <summary>Options for <see cref="AgentCardSigning.VerifyAsync(AgentCard, AgentCardKeyResolver, AgentCardVerificationOptions?, CancellationToken)"/>.</summary>
public sealed class AgentCardVerificationOptions
{
    /// <summary>
    /// Gets or sets the JWS algorithms to accept. Defaults to <see cref="AgentCardSigning.SupportedAlgorithms"/>.
    /// Algorithms this SDK does not support are never accepted.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedAlgorithms { get; set; }
}

/// <summary>
/// Returns the trusted public key for an Agent Card signature, or <see langword="null"/> when there is none.
/// </summary>
/// <remarks>
/// The <c>kid</c> and <c>jku</c> come from the card and are chosen by whoever signed or served it. A resolver
/// should map them to keys it already trusts for the agent's provider, and should only fetch a <c>jku</c> it
/// has decided to trust. Expired or revoked keys must not be returned (A2A specification Section 8.4.3).
/// The SDK does not dispose the returned key: the resolver keeps ownership, so return cached instances
/// rather than creating one per call.
/// </remarks>
/// <param name="request">The signature and the key parameters from its protected header.</param>
/// <param name="cancellationToken">A token to cancel the lookup.</param>
public delegate ValueTask<AsymmetricAlgorithm?> AgentCardKeyResolver(AgentCardSignatureKeyRequest request, CancellationToken cancellationToken);

/// <summary>The key parameters of an Agent Card signature, read from its protected header.</summary>
/// <param name="Signature">The signature being verified.</param>
/// <param name="KeyId">The <c>kid</c> header parameter, if present.</param>
/// <param name="JwksUrl">The <c>jku</c> header parameter, if present. It is not fetched by the SDK.</param>
/// <param name="Algorithm">The <c>alg</c> header parameter.</param>
public sealed record AgentCardSignatureKeyRequest(AgentCardSignature Signature, string? KeyId, string? JwksUrl, string Algorithm);

/// <summary>The outcome of verifying the signatures on an Agent Card.</summary>
public sealed class AgentCardSignatureVerificationResult
{
    private AgentCardSignatureVerificationResult(bool isVerified, AgentCardSignature? signature, string? keyId, string? algorithm, IReadOnlyList<string> errors)
    {
        IsVerified = isVerified;
        Signature = signature;
        KeyId = keyId;
        Algorithm = algorithm;
        Errors = errors;
    }

    /// <summary>Gets a value indicating whether at least one signature verified.</summary>
    public bool IsVerified { get; }

    /// <summary>Gets the signature that verified.</summary>
    public AgentCardSignature? Signature { get; }

    /// <summary>Gets the <c>kid</c> of the signature that verified.</summary>
    public string? KeyId { get; }

    /// <summary>Gets the algorithm of the signature that verified.</summary>
    public string? Algorithm { get; }

    /// <summary>Gets why each signature checked before the verifying one, or every signature, did not verify.</summary>
    public IReadOnlyList<string> Errors { get; }

    internal static AgentCardSignatureVerificationResult Verified(AgentCardSignature signature, string? keyId, string algorithm, IReadOnlyList<string> errors) =>
        new(true, signature, keyId, algorithm, errors);

    internal static AgentCardSignatureVerificationResult Failed(IReadOnlyList<string> errors) =>
        new(false, null, null, null, errors);
}
