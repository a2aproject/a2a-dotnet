using System.Net;
using System.Net.Sockets;

namespace A2A.AspNetCore;

internal sealed class PushNotificationHttpConnector
{
    private readonly IPushNotificationUrlValidator _validator;
    private readonly Func<IPEndPoint, CancellationToken, ValueTask<Stream>> _connect;

    internal PushNotificationHttpConnector(IPushNotificationUrlValidator validator,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>>? connect = null)
    {
        _validator = validator;
        _connect = connect ?? (async (endpoint, cancellationToken) =>
        {
            var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        });
    }

    internal ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        ConnectAsync(context.InitialRequestMessage.RequestUri!, context.DnsEndPoint, cancellationToken);

    internal async ValueTask<Stream> ConnectAsync(Uri uri, DnsEndPoint destination, CancellationToken cancellationToken)
    {
        bool sameDestination = uri.Port == destination.Port &&
            string.Equals(uri.IdnHost.Trim('[', ']').TrimEnd('.'),
                destination.Host.Trim('[', ']').TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        if (!sameDestination)
        {
            throw new HttpRequestException("Unexpected push connection destination.");
        }
        var addresses = (await _validator.ValidateAsync(uri.AbsoluteUri, cancellationToken).ConfigureAwait(false)).ToArray();
        foreach (var address in addresses)
        {
            try
            {
                // Use the approved IP directly: do not perform a second, unchecked DNS lookup.
                // SocketsHttpHandler retains the original URI for normal TLS/SNI and certificate checks.
                return await _connect(new IPEndPoint(address, destination.Port), cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // Try only the remaining addresses from the same validated resolution.
            }
        }
        throw new HttpRequestException("No approved push destination could be connected.");
    }
}
