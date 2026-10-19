using System.Text.Json;

namespace A2A;

/// <summary>
/// An immutable declaration and registry-owned handle for a supported A2A extension.
/// </summary>
/// <remarks>
/// Registration records support; it does not activate the extension for a request.
/// </remarks>
public sealed class A2AExtensionRegistration
{
    internal A2AExtensionRegistration(AgentExtension extension, object registryToken)
    {
        Uri = extension.Uri;
        Description = extension.Description;
        Required = extension.Required;
        Params = extension.Params?.Clone();
        RegistryToken = registryToken;
    }

    /// <summary>Gets the opaque, case-sensitive extension identifier.</summary>
    public string Uri { get; }

    /// <summary>Gets the extension description.</summary>
    public string? Description { get; }

    /// <summary>Gets whether the extension declaration marks support as required.</summary>
    public bool? Required { get; }

    /// <summary>Gets a snapshot of the extension parameters.</summary>
    public JsonElement? Params { get; }

    internal object RegistryToken { get; }

    /// <summary>
    /// Creates an independent Agent Card extension declaration from this registration.
    /// </summary>
    /// <returns>A detached declaration that does not mutate this registration.</returns>
    public AgentExtension ToAgentExtension() => new()
    {
        Uri = Uri,
        Description = Description,
        Required = Required,
        Params = Params?.Clone(),
    };

    /// <summary>Creates a detached Agent Card containing a declaration consistent with this registration.</summary>
    /// <param name="agentCard">The host-provided Agent Card.</param>
    /// <returns>A detached copy; the original card is never modified.</returns>
    /// <remarks>
    /// Adds a missing declaration to an unsigned card, or validates an existing declaration.
    /// Matching declarations retain their supplied representation. Duplicate or conflicting
    /// declarations for this extension are rejected. Signed cards must already contain a
    /// matching declaration; otherwise update and re-sign the card before hosting.
    /// Other extensions are preserved, not validated against a registry. This does not
    /// change runtime support or activation, validate the complete card, or verify signatures.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="agentCard"/> is null.</exception>
    /// <exception cref="ArgumentException">The card's capabilities are null.</exception>
    /// <exception cref="InvalidOperationException">The declaration conflicts, is duplicated, or is missing from a signed card.</exception>
    public AgentCard EnsureAgentCardDeclaration(AgentCard agentCard)
        => A2AExtensionAgentCards.EnsureDeclaration(this, agentCard);
}
