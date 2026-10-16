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
}
