namespace A2A;

/// <summary>Host filtering in addition to mandatory HTTPS and public-address checks.</summary>
public sealed class PushNotificationUrlOptions
{
    /// <summary>Exact hosts or explicit *.example.com subdomains. Empty or * disables only host filtering.</summary>
    public IList<string> AllowedHosts { get; set; } = [];
}
