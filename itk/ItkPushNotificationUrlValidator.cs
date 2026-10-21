using System.Net;

namespace A2A.Itk;

internal sealed class ItkPushNotificationUrlValidator : IPushNotificationUrlValidator
{
    public Task<IReadOnlyList<IPAddress>> ValidateAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool valid = Uri.TryCreate(url, UriKind.Absolute, out var receiver) &&
            receiver.Scheme == Uri.UriSchemeHttp &&
            receiver.Host == "127.0.0.1" &&
            receiver.Port > 0 &&
            receiver.UserInfo.Length == 0 &&
            receiver.Fragment.Length == 0;

        if (!valid)
        {
            throw new A2AException(
                "The ITK push notification receiver must use HTTP IPv4 loopback.",
                A2AErrorCode.InvalidParams);
        }

        return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }
}
