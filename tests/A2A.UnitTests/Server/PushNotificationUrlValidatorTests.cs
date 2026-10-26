using System.Net;
using System.Net.Sockets;

namespace A2A.UnitTests.Server;

public sealed class PushNotificationUrlValidatorTests
{
    private int _resolutions;
    private IPAddress[] _addresses = [IPAddress.Parse("93.184.216.34")];

    [Theory]
    [InlineData("http://example.com/notify")]
    [InlineData("/notify")]
    [InlineData("https://user:password@example.com/notify")]
    [InlineData("https://example.com/notify#fragment")]
    [InlineData("https://127.0.0.1/notify")]
    [InlineData("https://127.255.1.2/")]
    [InlineData("https://2130706433/")]
    [InlineData("https://0.0.0.0/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://192.88.99.1/")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://100.64.0.1/")]
    [InlineData("https://224.0.0.1/")]
    [InlineData("https://255.255.255.255/")]
    [InlineData("https://[::]/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    [InlineData("https://[fc00::1]/")]
    [InlineData("https://[fe80::1]/")]
    [InlineData("https://[fec0::1]/")]
    [InlineData("https://[ff02::1]/")]
    [InlineData("https://[2001:db8::1]/")]
    [InlineData("https://[2001:2::1]/")]
    [InlineData("https://[2001:20::1]/")]
    [InlineData("https://[3fff::1]/")]
    [InlineData("https://[2002:7f00:1::]/")]
    public async Task ProhibitedDestinations_AreRejectedWithoutNetwork(string url)
    {
        var validator = Validator("*");
        var exception = await Assert.ThrowsAsync<A2AException>(() => validator.ValidateAsync(url));
        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.DoesNotContain(url, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, _resolutions);
    }

    [Theory]
    [InlineData("https://93.184.216.34/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    [InlineData("https://[::ffff:93.184.216.34]/")]
    public async Task PublicLiterals_AreValidatedWithoutContactingThem(string url)
    {
        await Validator().ValidateAsync(url);
        Assert.Equal(0, _resolutions);
    }

    [Fact]
    public async Task Dns_IsRevalidatedAndMixedOrEmptyAnswersFailClosed()
    {
        var validator = Validator();
        await validator.ValidateAsync("https://callback.example/path?secret=query");
        _addresses = [IPAddress.Parse("93.184.216.34"), IPAddress.Loopback];
        await Assert.ThrowsAsync<A2AException>(() => validator.ValidateAsync("https://callback.example/path"));
        _addresses = [];
        await Assert.ThrowsAsync<A2AException>(() => validator.ValidateAsync("https://callback.example/path"));
        Assert.Equal(3, _resolutions);
    }

    [Theory]
    [InlineData("example.com", "https://EXAMPLE.COM./notify", true)]
    [InlineData("example.com", "https://sub.example.com/notify", false)]
    [InlineData("*.example.com", "https://sub.example.com/notify", true)]
    [InlineData("*.example.com", "https://example.com/notify", false)]
    [InlineData("*.example.com", "https://badexample.com/notify", false)]
    [InlineData("*", "https://anything.example/notify", true)]
    public async Task Allowlist_UsesExactHostsAndExplicitSubdomains(string allowed, string url, bool expected)
    {
        var validator = Validator(allowed);
        if (expected)
        {
            await validator.ValidateAsync(url);
            Assert.Equal(1, _resolutions);
        }
        else
        {
            await Assert.ThrowsAsync<A2AException>(() => validator.ValidateAsync(url));
            Assert.Equal(0, _resolutions);
        }
    }

    [Fact]
    public async Task EmptyAndStarAllowlist_StillEnforcePublicAddresses()
    {
        _addresses = [IPAddress.Parse("192.168.0.1")];
        await Assert.ThrowsAsync<A2AException>(() => Validator().ValidateAsync("https://private.example/"));
        await Assert.ThrowsAsync<A2AException>(() => Validator("*").ValidateAsync("https://private.example/"));
    }

    [Theory]
    [InlineData("http://localhost:5000/notify")]
    [InlineData("https://localhost:5000/notify")]
    [InlineData("http://127.0.0.1:5000/notify")]
    [InlineData("https://127.0.0.1:5000/notify")]
    [InlineData("http://[::1]:5000/notify")]
    [InlineData("https://[::1]:5000/notify")]
    public async Task LoopbackDestinations_RequireExplicitOptIn(string url)
    {
        _addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];
        var defaultValidator = new DefaultPushNotificationUrlValidator(null,
            (_, _) => Task.FromResult(_addresses));

        await Assert.ThrowsAsync<A2AException>(() => defaultValidator.ValidateAsync(url));
        await Assert.ThrowsAsync<A2AException>(() => Validator().ValidateAsync(url));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/notify", "127.0.0.1")]
    [InlineData("https://127.0.0.1:5000/notify", "127.0.0.1")]
    [InlineData("http://127.255.1.2/notify", "127.255.1.2")]
    [InlineData("http://2130706433/notify", "127.0.0.1")]
    [InlineData("http://[::1]:5000/notify", "::1")]
    [InlineData("https://[::1]:5000/notify", "::1")]
    [InlineData("http://[::ffff:127.0.0.1]:5000/notify", "::ffff:127.0.0.1")]
    public async Task LoopbackOptIn_AllowsLoopbackLiteralsWithoutDns(string url, string address)
    {
        var addresses = await Validator(true).ValidateAsync(url);

        Assert.Equal(IPAddress.Parse(address), Assert.Single(addresses));
        Assert.Equal(0, _resolutions);
    }

    [Theory]
    [InlineData("http://localhost:5000/notify")]
    [InlineData("https://localhost:5000/notify")]
    [InlineData("http://LOCALHOST.:5000/notify")]
    public async Task LoopbackOptIn_AllowsLocalhostOnlyWhenAllAnswersAreLoopback(string url)
    {
        _addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("::ffff:127.0.0.1")];

        var addresses = await Validator(true).ValidateAsync(url);

        Assert.Collection(addresses,
            address => Assert.Equal(IPAddress.Loopback, address),
            address => Assert.Equal(IPAddress.IPv6Loopback, address),
            address => Assert.Equal(IPAddress.Parse("::ffff:127.0.0.1"), address));
        Assert.Equal(1, _resolutions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("93.184.216.34")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1,93.184.216.34")]
    [InlineData("::1,192.168.0.1")]
    public async Task LoopbackOptIn_RejectsEmptyMixedOrNonLoopbackLocalhostAnswers(string answers)
    {
        _addresses = answers.Length == 0 ? [] : answers.Split(',').Select(IPAddress.Parse).ToArray();
        var validator = Validator(true);

        foreach (var scheme in new[] { "http", "https" })
        {
            var exception = await Assert.ThrowsAsync<A2AException>(() =>
                validator.ValidateAsync($"{scheme}://localhost:5000/notify"));
            Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        }
    }

    [Theory]
    [InlineData("http://example.com/notify")]
    [InlineData("http://93.184.216.34/notify")]
    [InlineData("http://10.0.0.1/notify")]
    [InlineData("https://10.0.0.1/notify")]
    [InlineData("https://192.168.0.1/notify")]
    [InlineData("https://169.254.169.254/notify")]
    [InlineData("https://0.0.0.0/notify")]
    [InlineData("https://[::]/notify")]
    [InlineData("https://[fc00::1]/notify")]
    [InlineData("https://[fe80::1]/notify")]
    [InlineData("http://user@localhost:5000/notify")]
    [InlineData("https://localhost:5000/notify#fragment")]
    [InlineData("http://localhost:0/notify")]
    [InlineData("ftp://localhost/notify")]
    [InlineData("/notify")]
    public async Task LoopbackOptIn_DoesNotRelaxOtherDestinationRestrictions(string url)
    {
        var exception = await Assert.ThrowsAsync<A2AException>(() => Validator(true).ValidateAsync(url));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Equal(0, _resolutions);
    }

    [Theory]
    [InlineData("callback.example", "127.0.0.1")]
    [InlineData("localhost.example", "::1")]
    [InlineData("private.example", "192.168.0.1")]
    public async Task LoopbackOptIn_DoesNotAllowOtherHostnamesToResolveToNonPublicAddresses(string host, string address)
    {
        _addresses = [IPAddress.Parse(address)];

        await Assert.ThrowsAsync<A2AException>(() => Validator(true).ValidateAsync($"https://{host}/notify"));
    }

    [Theory]
    [InlineData("https://93.184.216.34/notify")]
    [InlineData("https://callback.example/notify")]
    public async Task LoopbackOptIn_StillAllowsPublicHttpsDestinations(string url)
    {
        var addresses = await Validator(true).ValidateAsync(url);

        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(addresses));
    }

    [Fact]
    public async Task LoopbackOptIn_StillEnforcesHostAllowlist()
    {
        await Assert.ThrowsAsync<A2AException>(() =>
            Validator(true, "callbacks.example.com").ValidateAsync("http://localhost:5000/notify"));
        Assert.Equal(0, _resolutions);

        _addresses = [IPAddress.Loopback];
        var addresses = await Validator(true, "localhost").ValidateAsync("http://localhost:5000/notify");
        Assert.Equal(IPAddress.Loopback, Assert.Single(addresses));
    }

    [Fact]
    public async Task LoopbackOptIn_IsSnapshottedWhenValidatorIsCreated()
    {
        var options = new PushNotificationUrlOptions { AllowLoopback = true };
        var enabled = new DefaultPushNotificationUrlValidator(options);
        options.AllowLoopback = false;
        var disabled = new DefaultPushNotificationUrlValidator(options);
        options.AllowLoopback = true;

        var addresses = await enabled.ValidateAsync("http://127.0.0.1:5000/notify");
        Assert.Equal(IPAddress.Loopback, Assert.Single(addresses));
        await Assert.ThrowsAsync<A2AException>(() => disabled.ValidateAsync("http://127.0.0.1:5000/notify"));
    }

    [Fact]
    public async Task ResolverFailureAndCancellation_AreNotTreatedAsSuccess()
    {
        var failed = new DefaultPushNotificationUrlValidator(null,
            (_, _) => throw new SocketException((int)SocketError.HostNotFound));
        await Assert.ThrowsAsync<A2AException>(() => failed.ValidateAsync("https://missing.example/"));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Validator().ValidateAsync("https://example.com/", canceled.Token));
        Assert.Equal(0, _resolutions);
    }

    private DefaultPushNotificationUrlValidator Validator(params string[] allowed) => Validator(false, allowed);

    private DefaultPushNotificationUrlValidator Validator(bool allowLoopback, params string[] allowed)
    {
        return new DefaultPushNotificationUrlValidator(
            new PushNotificationUrlOptions { AllowedHosts = allowed, AllowLoopback = allowLoopback }, (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _resolutions++;
                return Task.FromResult(_addresses);
            });
    }
}
