namespace A2A.Grpc;

using System.Globalization;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Registers the gRPC binding with <see cref="A2AClientFactory"/> so that
/// <see cref="A2AClientFactory.Create(AgentCard, HttpClient?, A2AClientOptions?)"/> can resolve a
/// <see cref="A2AGrpcClient"/> for agent interfaces advertising <see cref="ProtocolBindingNames.Grpc"/>.
/// </summary>
public static class A2AGrpcClientRegistration
{
    private static readonly IdnMapping s_idnMapping = new();

    /// <summary>
    /// Registers the <see cref="ProtocolBindingNames.Grpc"/> binding with <see cref="A2AClientFactory"/>.
    /// Call this once during startup before resolving clients via
    /// <see cref="A2AClientFactory.Create(AgentCard, HttpClient?, A2AClientOptions?)"/>.
    /// </summary>
    public static void Register() => Register(useTlsForSchemeLessAddresses: true);

    /// <summary>
    /// Registers the <see cref="ProtocolBindingNames.Grpc"/> binding with <see cref="A2AClientFactory"/>.
    /// </summary>
    /// <param name="useTlsForSchemeLessAddresses">
    /// <see langword="true"/> to use HTTPS for gRPC addresses in the specification's <c>host:port</c> format;
    /// <see langword="false"/> to use HTTP for plaintext HTTP/2. Explicit <c>http://</c> and <c>https://</c>
    /// addresses retain their declared scheme.
    /// </param>
    public static void Register(bool useTlsForSchemeLessAddresses) =>
        A2AClientFactory.RegisterAddress(
            ProtocolBindingNames.Grpc,
            (address, httpClient) => new A2AGrpcClient(
                ResolveAddress(address, useTlsForSchemeLessAddresses),
                httpClient));

    internal static Uri ResolveAddress(string address, bool useTlsForSchemeLessAddresses)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var absoluteAddress)
            && (absoluteAddress.Scheme == Uri.UriSchemeHttp || absoluteAddress.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(absoluteAddress.Host))
        {
            return absoluteAddress;
        }

        if (!IsValidSchemeLessAddress(address))
        {
            throw InvalidAddress(address);
        }

        var scheme = useTlsForSchemeLessAddresses ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        if (!Uri.TryCreate($"{scheme}://{address}", UriKind.Absolute, out var resolvedAddress)
            || string.IsNullOrEmpty(resolvedAddress.Host))
        {
            throw InvalidAddress(address);
        }

        return resolvedAddress;
    }

    private static bool IsValidSchemeLessAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || address.Contains('/')
            || address.Contains('?')
            || address.Contains('#')
            || address.Contains('@'))
        {
            return false;
        }

        var portSeparator = address.LastIndexOf(':');
        if (portSeparator <= 0 || portSeparator == address.Length - 1)
        {
            return false;
        }

        if (address[0] == '[')
        {
            var closingBracket = address.IndexOf(']');
            if (closingBracket <= 1
                || portSeparator != closingBracket + 1
                || !IPAddress.TryParse(address.AsSpan(1, closingBracket - 1), out var ipAddress)
                || ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }
        }
        else
        {
            var host = address[..portSeparator];
            if (host.Contains(':') || !IsValidHostName(host))
            {
                return false;
            }
        }

        return int.TryParse(
            address.AsSpan(portSeparator + 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port)
            && port is > 0 and <= 65535;
    }

    private static bool IsValidHostName(string host)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return ipAddress.AddressFamily == AddressFamily.InterNetwork;
        }

        if (host.Length > 0 && host[^1] == '.')
        {
            host = host[..^1];
        }

        string asciiHost;
        try
        {
            asciiHost = s_idnMapping.GetAscii(host);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (asciiHost.Length is 0 or > 253)
        {
            return false;
        }

        var remaining = asciiHost.AsSpan();
        while (true)
        {
            var separator = remaining.IndexOf('.');
            var label = separator >= 0 ? remaining[..separator] : remaining;
            if (!IsValidDnsLabel(label))
            {
                return false;
            }

            if (separator < 0)
            {
                return true;
            }

            remaining = remaining[(separator + 1)..];
        }
    }

    private static bool IsValidDnsLabel(ReadOnlySpan<char> label)
    {
        if (label.Length is 0 or > 63
            || !IsAsciiLetterOrDigit(label[0])
            || !IsAsciiLetterOrDigit(label[^1]))
        {
            return false;
        }

        foreach (var character in label)
        {
            if (!IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9';

    private static A2AException InvalidAddress(string address) =>
        new(
            $"Invalid gRPC interface address: '{address}'. Expected 'host:port' or an absolute HTTP(S) URL.",
            A2AErrorCode.InvalidRequest);
}
