using System.Net;
using System.Net.Sockets;

namespace A2A;

/// <summary>Resolves HTTPS destinations to approved public addresses for use by the outbound connector.</summary>
public sealed class DefaultPushNotificationUrlValidator : IPushNotificationUrlValidator
{
    private readonly string[] _allowedHosts;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly (byte[] Network, int Bits)[] _blockedV4 =
    [
        ([0, 0, 0, 0], 8), ([10, 0, 0, 0], 8), ([100, 64, 0, 0], 10),
        ([127, 0, 0, 0], 8), ([169, 254, 0, 0], 16), ([172, 16, 0, 0], 12),
        ([192, 0, 0, 0], 24), ([192, 0, 2, 0], 24), ([192, 88, 99, 0], 24), ([192, 168, 0, 0], 16),
        ([198, 18, 0, 0], 15), ([198, 51, 100, 0], 24), ([203, 0, 113, 0], 24),
        ([224, 0, 0, 0], 3),
    ];

    /// <summary>Creates a validator with a startup snapshot of host policy.</summary>
    /// <param name="options">Host filtering policy.</param>
    public DefaultPushNotificationUrlValidator(PushNotificationUrlOptions? options = null)
        : this(options, Dns.GetHostAddressesAsync)
    {
    }

    internal DefaultPushNotificationUrlValidator(
        PushNotificationUrlOptions? options, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
        _allowedHosts = (options?.AllowedHosts ?? []).Select(host => host.TrimEnd('.').ToLowerInvariant()).ToArray();
        foreach (var entry in _allowedHosts)
        {
            var host = entry.StartsWith("*.", StringComparison.Ordinal) ? entry[2..] : entry;
            if (entry != "*" &&
                Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new ArgumentException("AllowedHosts must contain hosts, *.host entries, or *.", nameof(options));
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool validShape = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 &&
            uri.Fragment.Length == 0 &&
            uri.Port > 0;
        if (!validShape)
        {
            throw new A2AException("Push destination must be an absolute HTTPS URL without userinfo or fragment.", A2AErrorCode.InvalidParams);
        }
        var host = uri!.IdnHost.TrimEnd('.');
        bool allowed = _allowedHosts.Length == 0 ||
            _allowedHosts.Any(entry => entry == "*" ||
                host.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                (entry.StartsWith("*.", StringComparison.Ordinal) &&
                 host.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase)));
        if (!allowed)
        {
            throw new A2AException("Push destination host is not allowed.", A2AErrorCode.InvalidParams);
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _resolve(host, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                throw new A2AException("Push destination could not be resolved.", A2AErrorCode.InvalidParams);
            }
        }

        if (addresses.Length == 0 ||
            addresses.Any(address => !IsPublic(address)))
        {
            throw new A2AException("Push destination must resolve only to public addresses.", A2AErrorCode.InvalidParams);
        }
        return addresses;
    }

    private bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            foreach (var (network, bits) in _blockedV4)
            {
                uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                uint prefix = ((uint)network[0] << 24) | ((uint)network[1] << 16) | ((uint)network[2] << 8) | network[3];
                uint mask = uint.MaxValue << (32 - bits);
                if ((value & mask) == (prefix & mask))
                {
                    return false;
                }
            }
            return true;
        }

        // Global IPv6 unicast only; exclude special-use, documentation and IPv4 tunneling ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] & 0xfe) == 0) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
            !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0);
    }
}
