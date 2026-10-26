using A2A;
using System.Net;

namespace AgentServer;

/// <summary>Sample-only policy permitting one explicitly configured IPv4 loopback callback.</summary>
internal sealed class LocalPushNotificationUrlValidator : IPushNotificationUrlValidator
{
    private readonly Uri _callback;

    public LocalPushNotificationUrlValidator(string callback)
    {
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" || uri.Port <= 0 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new ArgumentException(
                "The demo callback must be an HTTP URL at 127.0.0.1, without credentials, query, or fragment.", nameof(callback));
        }
        _callback = uri;
    }

    public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(url, _callback.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new A2AException("Only the proactive sample's configured callback URL is allowed.", A2AErrorCode.InvalidParams);
        }
        return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }
}
