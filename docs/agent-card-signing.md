# Agent Card Signing

An Agent Card can carry JSON Web Signatures so that a client can check who published it and that nobody changed it on the way. The A2A specification defines this in [Section 8.4](https://a2a-protocol.org/latest/specification/#84-agent-card-signing). `AgentCardSigning` implements it.

## What is signed

A signature covers the card's canonical form: the card without its `signatures` member, with Protocol Buffers field presence applied, serialized with the JSON Canonicalization Scheme ([RFC 8785](https://www.rfc-editor.org/rfc/rfc8785)). Field presence follows Section 8.4.1:

- `REQUIRED` fields are always included, even when empty.
- Fields with the `optional` keyword are included whenever they are set, even to a default value. For example, `capabilities.streaming: false` is signed.
- Other fields are left out when they hold their default value: an empty string, `false`, zero, an empty list or an empty map. For example, an extension's `required: false` is not signed.
- The contents of `google.protobuf.Struct` fields, such as an extension's `params`, are data and are signed as they are.
- A JSON `null` on a field the card defines means the field is not set, as in ProtoJSON.
- Members the card does not define are always signed, whatever their value, so nothing can be added to a signed card without breaking its signature.
- Two members of one object whose names differ only by case are rejected, because the SDK reads Agent Card JSON case-insensitively. Map keys, such as security scheme names, are exempt.

The signature is a detached JWS: `protected` holds the base64url protected header and `signature` the signature value. The SDK signs and verifies ES256, ES384, ES512, RS256, RS384, RS512, PS256, PS384 and PS512. RSA keys must have at least 2048 bits.

The canonical bytes match the ones a2a-python, @a2a-js/sdk and a2a-go produce for the shared interop vectors in `tests/A2A.UnitTests/Signing/Vectors`.

## Sign a card

```csharp
using System.Security.Cryptography;
using A2A;

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

var card = new AgentCard { /* ... */ };
AgentCardSigning.Sign(card, key, keyId: "agent-key-2026", new AgentCardSigningOptions
{
    Algorithm = "ES256",
    JwksUrl = new Uri("https://agent.example.com/.well-known/jwks.json"),
});
```

`Sign` adds the signature to `card.Signatures` and returns it. Sign the card after every other change: any later edit to a signed field invalidates the signature. To rotate keys, add a signature with the new key next to the old one.

## Verify a card

Verification succeeds when at least one signature is valid under a key your resolver returns.

```csharp
AgentCardKeyResolver resolver = (request, cancellationToken) =>
    ValueTask.FromResult<AsymmetricAlgorithm?>(trustedKeys.Find(provider, request.KeyId));

var result = await AgentCardSigning.VerifyAsync(card, resolver, new AgentCardVerificationOptions
{
    AllowedAlgorithms = ["ES256"],
});

if (!result.IsVerified)
{
    // result.Errors says why each signature was rejected.
}
```

To verify a card exactly as it was served, pass the JSON instead of the model. Members the SDK model does not define, for example fields from a newer protocol version, are then covered too:

```csharp
using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
var result = await AgentCardSigning.VerifyAsync(document.RootElement, resolver);
```

`AgentCardSigning.ImportPublicJsonWebKey` turns an `EC` or `RSA` JSON Web Key into an `ECDsa` or `RSA` instance for the resolver. The SDK does not dispose keys a resolver returns, so keep them in a cache you own rather than creating one per call.

When a card object or its signatures are malformed, verification does not throw: it returns a result with `IsVerified` set to `false` and the reasons in `Errors`. Errors quote header values with control characters removed, but they still contain text from the card, so treat them as untrusted when you log them.

## Choosing keys

The `kid` and `jku` in a signature come from the card, so whoever served the card chose them. The resolver decides which keys are trusted:

- Map the `kid` to a key you already trust for that agent's provider, for example one pinned in configuration or published by a registry you trust.
- The SDK never fetches `jku`. Fetch it only if you have decided to trust that URL for that provider, and only over HTTPS.
- Do not return expired or revoked keys (Section 8.4.3).
- Restrict `AllowedAlgorithms` to the algorithms your providers use.

When a JWK Set is consumed by other SDKs, include each key's `alg`. Some JOSE libraries assume RS256 for an RSA key that has no `alg`, and then reject PS256 signatures made with it.
