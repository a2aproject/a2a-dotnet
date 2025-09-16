using System.Security.Claims;

namespace A2A;

/// <summary>
/// Represents authentication context for A2A requests.
/// </summary>
/// <remarks>
/// Provides information about the authenticated user and their permissions
/// for accessing extended agent card information.
/// </remarks>
public sealed class AuthenticationContext
{
    /// <summary>
    /// Gets or sets the authenticated user's principal.
    /// </summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Gets or sets the authentication scheme used.
    /// </summary>
    /// <remarks>
    /// Examples: "Bearer", "ApiKey", "OAuth2", "Basic", etc.
    /// </remarks>
    public string? Scheme { get; set; }

    /// <summary>
    /// Gets or sets additional authentication properties.
    /// </summary>
    /// <remarks>
    /// Contains additional metadata about the authentication context,
    /// such as token expiration, scopes, or other security-related information.
    /// </remarks>
    public IDictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Gets the user's identity name if available.
    /// </summary>
    public string? UserName => User?.Identity?.Name;

    /// <summary>
    /// Checks if the user has a specific claim.
    /// </summary>
    /// <param name="claimType">The type of claim to check for.</param>
    /// <param name="claimValue">The value of the claim to check for (optional).</param>
    /// <returns>True if the user has the specified claim, false otherwise.</returns>
    public bool HasClaim(string claimType, string? claimValue = null)
    {
        if (User == null) return false;

        return claimValue == null
            ? User.HasClaim(claimType, string.Empty) || User.Claims.Any(c => c.Type == claimType)
            : User.HasClaim(claimType, claimValue);
    }

    /// <summary>
    /// Gets all claims of a specific type.
    /// </summary>
    /// <param name="claimType">The type of claims to retrieve.</param>
    /// <returns>A collection of claim values for the specified type.</returns>
    public IEnumerable<string> GetClaimValues(string claimType)
    {
        return User?.Claims.Where(c => c.Type == claimType).Select(c => c.Value) ?? [];
    }
}