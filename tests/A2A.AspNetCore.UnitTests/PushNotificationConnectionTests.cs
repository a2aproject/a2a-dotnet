using System.Net;
using System.Net.Sockets;

namespace A2A.AspNetCore.Tests;

public sealed class PushNotificationConnectionTests
{
    private readonly List<IPEndPoint> _dialed = [];
    private readonly Uri _uri = new("https://callback.example/notify");

    [Fact]
    public async Task DnsChangesAfterPreflight_ConnectorNeverDialsPrivateAnswer()
    {
        int resolutions = 0;
        var validator = new DefaultPushNotificationUrlValidator(null, (_, _) =>
            Task.FromResult(++resolutions == 1 ? new[] { IPAddress.Parse("93.184.216.34") } : [IPAddress.Loopback]));
        await validator.ValidateAsync(_uri.AbsoluteUri);
        var connector = new PushNotificationHttpConnector(validator, ConnectAsync);
        await Assert.ThrowsAsync<A2AException>(() =>
            connector.ConnectAsync(_uri, new DnsEndPoint(_uri.Host, 443), CancellationToken.None).AsTask());
        Assert.Equal(2, resolutions);
        Assert.Empty(_dialed);
    }

    [Fact]
    public async Task Connection_UsesOnlyTheValidatedAddressWithoutDnsFallback()
    {
        int resolutions = 0;
        var address = IPAddress.Parse("93.184.216.34");
        var validator = new DefaultPushNotificationUrlValidator(null, (_, _) =>
        {
            resolutions++;
            return Task.FromResult(new[] { address });
        });
        var connector = new PushNotificationHttpConnector(validator, ConnectAsync);
        using var connection = await connector.ConnectAsync(_uri, new DnsEndPoint(_uri.Host, 443), CancellationToken.None);
        Assert.Equal(1, resolutions);
        var endpoint = Assert.Single(_dialed);
        Assert.Equal(address, endpoint.Address);
        Assert.Equal(443, endpoint.Port);
        Assert.Equal("callback.example", _uri.Host); // original URI is not rewritten, preserving TLS/SNI identity
    }

    [Fact]
    public async Task FailedApprovedAddress_DoesNotFallBackToUncheckedHostname()
    {
        int resolutions = 0;
        var validator = new DefaultPushNotificationUrlValidator(null, (_, _) =>
        {
            resolutions++;
            return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        });
        var connector = new PushNotificationHttpConnector(validator, (endpoint, _) =>
        {
            _dialed.Add(endpoint);
            throw new SocketException((int)SocketError.ConnectionRefused);
        });
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connector.ConnectAsync(_uri, new DnsEndPoint(_uri.Host, 443), CancellationToken.None).AsTask());
        Assert.Single(_dialed);
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task MismatchedConnectTarget_IsRejectedBeforeResolutionOrDial()
    {
        var validator = new DefaultPushNotificationUrlValidator(null, (_, _) => throw new InvalidOperationException("Must not resolve."));
        var connector = new PushNotificationHttpConnector(validator, ConnectAsync);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connector.ConnectAsync(_uri, new DnsEndPoint("different.example", 443), CancellationToken.None).AsTask());
        Assert.Empty(_dialed);
    }

    private ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dialed.Add(endpoint);
        return ValueTask.FromResult<Stream>(new MemoryStream());
    }
}
