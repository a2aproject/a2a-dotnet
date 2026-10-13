using System.Net;

namespace A2A;

/// <summary>Validates a destination before storage and each delivery attempt. Invalid URLs must fail closed.</summary>
public interface IPushNotificationUrlValidator
{
    /// <summary>Resolves and validates a destination, returning only addresses approved for the connection.</summary>
    /// <param name="url">Destination URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default);
}
