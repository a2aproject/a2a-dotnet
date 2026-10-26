namespace A2A;

/// <summary>Host filtering and explicit loopback access for push notification destinations.</summary>
public sealed class PushNotificationUrlOptions
{
    /// <summary>Exact hosts or explicit *.example.com subdomains. Empty or * disables only host filtering.</summary>
    public IList<string> AllowedHosts { get; set; } = [];

    /// <summary>Allows HTTP or HTTPS callbacks to localhost and loopback IP literals for local development.</summary>
    /// <remarks>
    /// Defaults to false. All resolved addresses must be loopback, and host filtering still applies.
    /// Other destinations continue to require HTTPS and public addresses. Enabling this option allows
    /// access to local services; do not enable it for untrusted registration callers.
    /// </remarks>
    public bool AllowLoopback { get; set; }
}
