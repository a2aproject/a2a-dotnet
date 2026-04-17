namespace MultiAgentHost;

/// <summary>
/// Scoped service that holds the resolved subdomain (tenant) for the current request.
/// Set by <see cref="SubdomainMiddleware"/>, consumed by <see cref="MultiAgentHandler"/>.
/// </summary>
public sealed class SubdomainContext
{
    public string? Subdomain { get; set; }
}
