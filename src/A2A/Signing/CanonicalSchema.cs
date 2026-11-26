using System.Text.Json;

namespace A2A;

/// <summary>
/// Field presence for one Protocol Buffers message, used to drop members before canonicalization
/// as A2A specification Section 8.4.1 rule 1 requires.
/// </summary>
/// <remarks>
/// <para>
/// The rules follow ProtoJSON field presence for the fields the message defines. A <c>REQUIRED</c> field is
/// always kept. A field with the <c>optional</c> keyword is kept whenever it is set, even to its default
/// value. A message-typed field is kept whenever it is set. Any other field is dropped when it holds its
/// default value: an empty string, <c>false</c>, zero, an empty list or an empty map. A JSON <c>null</c> on a
/// defined field means the field is not set, as in ProtoJSON, so it is dropped.
/// </para>
/// <para>
/// Members the message does not define are always kept, whatever their value, so nothing can be added to a
/// signed card without changing the signed bytes.
/// </para>
/// </remarks>
internal sealed class CanonicalSchema
{
    private readonly HashSet<string> _required;
    private readonly HashSet<string> _optional;
    private readonly HashSet<string> _plain;
    private readonly HashSet<string> _messages;
    private readonly HashSet<string> _excluded;
    private readonly Dictionary<string, Func<CanonicalSchema?>> _children = new(StringComparer.Ordinal);
    private readonly Func<CanonicalSchema?>? _mapValues;

    private CanonicalSchema(
        string[]? required = null,
        string[]? optional = null,
        string[]? plain = null,
        string[]? excluded = null,
        (string Name, Func<CanonicalSchema?> Schema, bool IsMessage)[]? children = null,
        Func<CanonicalSchema?>? mapValues = null)
    {
        _required = new(required ?? [], StringComparer.Ordinal);
        _optional = new(optional ?? [], StringComparer.Ordinal);
        _plain = new(plain ?? [], StringComparer.Ordinal);
        _messages = new(StringComparer.Ordinal);
        _excluded = new(excluded ?? [], StringComparer.Ordinal);
        _mapValues = mapValues;
        foreach (var (name, schema, isMessage) in children ?? [])
        {
            _children[name] = schema;

            // A repeated or map field without REQUIRED is a plain field: dropped when empty.
            (isMessage ? _messages : _required.Contains(name) ? null : _plain)?.Add(name);
        }
    }

    /// <summary>Gets the schema for an Agent Card, with the <c>signatures</c> member excluded (rule 3).</summary>
    internal static CanonicalSchema AgentCard { get; } = new(
        required: ["name", "description", "supportedInterfaces", "version", "capabilities", "defaultInputModes", "defaultOutputModes", "skills"],
        optional: ["documentationUrl", "iconUrl"],
        excluded: ["signatures"],
        children:
        [
            Repeated("supportedInterfaces", () => AgentInterface),
            Message("provider", () => AgentProvider),
            Message("capabilities", () => AgentCapabilities),
            Repeated("securitySchemes", () => SecuritySchemeMap),
            Repeated("securityRequirements", () => SecurityRequirement),
            Repeated("skills", () => AgentSkill),
        ]);

    private static CanonicalSchema AgentInterface { get; } = new(
        required: ["url", "protocolBinding", "protocolVersion"],
        plain: ["tenant"]);

    private static CanonicalSchema AgentProvider { get; } = new(required: ["url", "organization"]);

    private static CanonicalSchema AgentCapabilities { get; } = new(
        optional: ["streaming", "pushNotifications", "extendedAgentCard"],
        children: [Repeated("extensions", () => AgentExtension)]);

    // `params` is a google.protobuf.Struct: its contents are data, so they are not pruned.
    private static CanonicalSchema AgentExtension { get; } = new(
        plain: ["uri", "description", "required"],
        children: [Message("params", () => null)]);

    private static CanonicalSchema AgentSkill { get; } = new(
        required: ["id", "name", "description", "tags"],
        plain: ["examples", "inputModes", "outputModes"],
        children: [Repeated("securityRequirements", () => SecurityRequirement)]);

    private static CanonicalSchema SecurityRequirement { get; } = new(children: [Repeated("schemes", () => StringListMap)]);

    private static CanonicalSchema StringListMap { get; } = MapOf(() => StringList);

    private static CanonicalSchema StringList { get; } = new(plain: ["list"]);

    private static CanonicalSchema SecuritySchemeMap { get; } = MapOf(() => SecurityScheme);

    private static CanonicalSchema SecurityScheme { get; } = new(children:
    [
        Message("apiKeySecurityScheme", () => ApiKeySecurityScheme),
        Message("httpAuthSecurityScheme", () => HttpAuthSecurityScheme),
        Message("oauth2SecurityScheme", () => OAuth2SecurityScheme),
        Message("openIdConnectSecurityScheme", () => OpenIdConnectSecurityScheme),
        Message("mtlsSecurityScheme", () => MutualTlsSecurityScheme),
    ]);

    private static CanonicalSchema ApiKeySecurityScheme { get; } = new(required: ["location", "name"], plain: ["description"]);

    private static CanonicalSchema HttpAuthSecurityScheme { get; } = new(required: ["scheme"], plain: ["description", "bearerFormat"]);

    private static CanonicalSchema OAuth2SecurityScheme { get; } = new(
        required: ["flows"],
        plain: ["description", "oauth2MetadataUrl"],
        children: [Message("flows", () => OAuthFlows)]);

    private static CanonicalSchema OpenIdConnectSecurityScheme { get; } = new(required: ["openIdConnectUrl"], plain: ["description"]);

    private static CanonicalSchema MutualTlsSecurityScheme { get; } = new(plain: ["description"]);

    private static CanonicalSchema OAuthFlows { get; } = new(children:
    [
        Message("authorizationCode", () => AuthorizationCodeOAuthFlow),
        Message("clientCredentials", () => ClientCredentialsOAuthFlow),
        Message("implicit", () => ImplicitOAuthFlow),
        Message("password", () => PasswordOAuthFlow),
        Message("deviceCode", () => DeviceCodeOAuthFlow),
    ]);

    private static CanonicalSchema AuthorizationCodeOAuthFlow { get; } = new(
        required: ["authorizationUrl", "tokenUrl", "scopes"],
        plain: ["refreshUrl", "pkceRequired"]);

    private static CanonicalSchema ClientCredentialsOAuthFlow { get; } = new(required: ["tokenUrl", "scopes"], plain: ["refreshUrl"]);

    private static CanonicalSchema ImplicitOAuthFlow { get; } = new(plain: ["authorizationUrl", "refreshUrl", "scopes"]);

    private static CanonicalSchema PasswordOAuthFlow { get; } = new(plain: ["tokenUrl", "refreshUrl", "scopes"]);

    private static CanonicalSchema DeviceCodeOAuthFlow { get; } = new(
        required: ["deviceAuthorizationUrl", "tokenUrl", "scopes"],
        plain: ["refreshUrl"]);

    /// <summary>
    /// Gets a value indicating whether the schema describes a map. A map's keys are data: every entry is kept,
    /// and keys that differ only by case are distinct.
    /// </summary>
    internal bool IsMap => _mapValues is not null;

    private static CanonicalSchema MapOf(Func<CanonicalSchema?> values) => new(mapValues: values);

    private static (string, Func<CanonicalSchema?>, bool) Message(string name, Func<CanonicalSchema?> schema) => (name, schema, true);

    private static (string, Func<CanonicalSchema?>, bool) Repeated(string name, Func<CanonicalSchema?> schema) => (name, schema, false);

    /// <summary>Gets a value indicating whether the member is part of the canonical payload.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member value.</param>
    /// <returns><see langword="true"/> when the member is written.</returns>
    internal bool Includes(string name, JsonElement value)
    {
        if (IsMap)
        {
            return true;
        }

        if (_excluded.Contains(name))
        {
            return false;
        }

        var defined = _required.Contains(name) || _optional.Contains(name) || _messages.Contains(name) || _plain.Contains(name);
        if (!defined)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return !_plain.Contains(name) || !IsDefault(value);
    }

    /// <summary>Gets the schema that applies to the value of the member, or <see langword="null"/> to write it unchanged.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The schema for the member value.</returns>
    internal CanonicalSchema? ChildSchema(string name)
    {
        if (_mapValues is not null)
        {
            return _mapValues();
        }

        return _children.TryGetValue(name, out var schema) ? schema() : null;
    }

    private static bool IsDefault(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.False => true,
        JsonValueKind.String => value.GetString()?.Length == 0,
        JsonValueKind.Number => value.TryGetDouble(out var number) && number == 0,
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false,
    };
}
