using A2A;
using System.Net;

namespace PushNotificationReceiver;

/// <summary>Demo-only policy allowing exactly one owned loopback receiver endpoint.</summary>
public sealed class LocalReceiverUrlValidator : IPushNotificationUrlValidator
{
    private Uri? _receiver;

    /// <summary>Sets the endpoint after the demo has started its own listener. Cannot be changed.</summary>
    /// <param name="receiver">Actual URL of the listener owned by this demo.</param>
    public void SetReceiver(Uri receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        bool valid = receiver.IsAbsoluteUri &&
            receiver.Scheme == "http" &&
            receiver.Host == "127.0.0.1" &&
            receiver.Port > 0 &&
            receiver.UserInfo.Length == 0 &&
            receiver.Fragment.Length == 0;
        if (!valid ||
            Interlocked.CompareExchange(ref _receiver, receiver, null) is not null)
        {
            throw new InvalidOperationException("The demo requires exactly one owned loopback receiver URL.");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_receiver is null ||
            !string.Equals(url, _receiver.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new A2AException("Only this demo's exact receiver URL is allowed.", A2AErrorCode.InvalidParams);
        }
        return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }
}
