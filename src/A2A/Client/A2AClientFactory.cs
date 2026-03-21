namespace A2A;

/// <summary>
/// Factory for creating <see cref="IA2AClient"/> instances from an <see cref="AgentCard"/>.
/// Selects the best protocol binding based on the agent's supported interfaces and the caller's preferences.
/// </summary>
public static class A2AClientFactory
{
    /// <summary>
    /// Creates an <see cref="IA2AClient"/> from an <see cref="AgentCard"/> by selecting the
    /// best matching protocol binding from the card's <see cref="AgentCard.SupportedInterfaces"/>.
    /// </summary>
    /// <param name="agentCard">The agent card describing the agent's supported interfaces.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="options">
    /// Optional client options controlling binding preference.
    /// Defaults to preferring HTTP+JSON first, with JSON-RPC as fallback.
    /// </param>
    /// <returns>An <see cref="IA2AClient"/> configured for the best available protocol binding.</returns>
    /// <exception cref="A2AException">
    /// Thrown when no supported interface in the agent card matches the preferred bindings.
    /// </exception>
    public static IA2AClient Create(AgentCard agentCard, HttpClient? httpClient = null, A2AClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentCard);

        options ??= new A2AClientOptions();

        foreach (var preferredBinding in options.PreferredBindings)
        {
            var matchingInterface = agentCard.SupportedInterfaces
                .FirstOrDefault(i => string.Equals(i.ProtocolBinding, preferredBinding, StringComparison.OrdinalIgnoreCase));

            if (matchingInterface is not null)
            {
                var url = new Uri(matchingInterface.Url);

                return preferredBinding.ToUpperInvariant() switch
                {
                    ProtocolBindingNames.HttpJson => new A2AHttpJsonClient(url, httpClient),
                    _ => new A2AClient(url, httpClient),
                };
            }
        }

        var available = agentCard.SupportedInterfaces.Count > 0
            ? string.Join(", ", agentCard.SupportedInterfaces.Select(i => i.ProtocolBinding))
            : "none";
        var requested = string.Join(", ", options.PreferredBindings);

        throw new A2AException(
            $"No supported interface matches the preferred protocol bindings. Requested: [{requested}]. Available: [{available}].",
            A2AErrorCode.InvalidRequest);
    }
}
