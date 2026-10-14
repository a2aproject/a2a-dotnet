namespace A2A.V0_3Compat;

/// <summary>
/// Declares whether a custom request handler implements A2A v0.3 push notification semantics.
/// </summary>
/// <remarks>
/// Custom handlers retain their existing behavior unless they implement this interface.
/// Set <see cref="SupportsV03PushNotifications"/> to <see langword="false"/> when the handler's
/// push implementation only supports the v1 wire contract.
/// </remarks>
public interface IV03PushNotificationCompatibility
{
    /// <summary>
    /// Gets a value indicating whether the handler supports A2A v0.3 push notification semantics.
    /// </summary>
    bool SupportsV03PushNotifications { get; }
}

internal static class V03PushNotificationCompatibility
{
    internal static bool IsV1Only(IA2ARequestHandler requestHandler) =>
        requestHandler.GetType() == typeof(A2AServer) ||
        requestHandler is IV03PushNotificationCompatibility { SupportsV03PushNotifications: false };
}
