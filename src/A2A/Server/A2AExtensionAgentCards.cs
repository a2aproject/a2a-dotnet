using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>Derives or validates Agent Card declarations from the authoritative registry.</summary>
internal static class A2AExtensionAgentCards
{
    internal static AgentCard Prepare(A2ACustomOperationRegistry registry, AgentCard agentCard)
    {
        ValidateInput(agentCard);
        var signed = agentCard.Signatures is { Count: > 0 };
        var hasDeclarations = agentCard.Capabilities.Extensions is { Count: > 0 };
        if (signed || hasDeclarations) Validate(registry, agentCard);

        // Detach all mutable state, including nested parameters and signature headers.
        // Matching signed cards retain their content and declaration order unchanged.
        var copy = Clone(agentCard);
        if (!signed && !hasDeclarations)
        {
            copy.Capabilities.Extensions = registry.Extensions.Values.Select(extension => extension.ToAgentExtension()).ToList();
        }

        return copy;
    }

    internal static AgentCard EnsureDeclaration(A2AExtensionRegistration registration, AgentCard agentCard)
    {
        ValidateInput(agentCard);
        var found = false;
        foreach (var declaration in agentCard.Capabilities.Extensions ?? [])
        {
            if (declaration is null || !string.Equals(declaration.Uri, registration.Uri, StringComparison.Ordinal))
            {
                continue;
            }

            if (found || !DeclarationMatches(declaration, registration))
            {
                throw DeclarationMismatch(agentCard, registration.Uri);
            }

            found = true;
        }

        if (!found && agentCard.Signatures is { Count: > 0 })
        {
            throw DeclarationMismatch(agentCard, registration.Uri);
        }

        var copy = Clone(agentCard);
        if (!found)
        {
            copy.Capabilities.Extensions ??= [];
            copy.Capabilities.Extensions.Add(registration.ToAgentExtension());
        }

        return copy;
    }

    internal static void Validate(A2ACustomOperationRegistry registry, AgentCard agentCard)
    {
        ValidateInput(agentCard);
        var declarations = agentCard.Capabilities.Extensions;
        if ((declarations?.Count ?? 0) != registry.Extensions.Count) throw Mismatch(agentCard);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations ?? [])
        {
            if (declaration is null || declaration.Uri is null || !seen.Add(declaration.Uri) ||
                !registry.Extensions.TryGetValue(declaration.Uri, out var registration) ||
                !DeclarationMatches(declaration, registration))
            {
                throw Mismatch(agentCard);
            }
        }
    }

    private static AgentCard Clone(AgentCard agentCard)
    {
        var typeInfo = (JsonTypeInfo<AgentCard>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentCard));
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(agentCard, typeInfo), typeInfo)!;

        // A nullable JsonElement round-trip turns JSON null into an absent value.
        // Restore each parameter tree independently, preserving explicit nulls
        // without retaining the source JsonDocument's lifetime.
        if (agentCard.Capabilities.Extensions is { } declarations)
        {
            for (var index = 0; index < declarations.Count; index++)
            {
                if (declarations[index] is { } declaration)
                {
                    copy.Capabilities.Extensions![index].Params = declaration.Params?.Clone();
                }
            }
        }

        return copy;
    }

    // These protobuf scalar fields have implicit default semantics, not optional
    // presence. Accept equivalent declarations without rewriting the supplied card.
    private static bool DeclarationMatches(AgentExtension declaration, A2AExtensionRegistration registration)
        => string.Equals(declaration.Uri, registration.Uri, StringComparison.Ordinal)
            && (declaration.Description ?? "") == (registration.Description ?? "")
            && declaration.Required.GetValueOrDefault() == registration.Required.GetValueOrDefault()
            && ParametersMatch(declaration.Params, registration.Params);

    private static bool ParametersMatch(JsonElement? left, JsonElement? right)
        => left.HasValue == right.HasValue && (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));

    private static InvalidOperationException Mismatch(AgentCard card) => new(
        card.Signatures is { Count: > 0 }
            ? "Signed Agent Card extension declarations do not match the registry. Update the card and re-sign it before hosting."
            : "Agent Card extension declarations do not match the registry.");

    private static InvalidOperationException DeclarationMismatch(AgentCard card, string identifier) => new(
        card.Signatures is { Count: > 0 }
            ? $"Signed Agent Card must contain exactly one declaration matching registered extension '{identifier}'. Update the card and re-sign it before hosting."
            : $"Agent Card must contain exactly one declaration matching registered extension '{identifier}'.");

    private static void ValidateInput(AgentCard agentCard)
    {
        ArgumentNullException.ThrowIfNull(agentCard);
        if (agentCard.Capabilities is null)
        {
            throw new ArgumentException("Agent Card capabilities cannot be null.", nameof(agentCard));
        }
    }
}
