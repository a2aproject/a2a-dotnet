namespace A2A.V0_3Compat.UnitTests;

using System.Net;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

public sealed class AgentCardConditionalGetTests
{
    [Theory]
    [InlineData("native", null, "strong")]
    [InlineData("native", null, "weak")]
    [InlineData("native", null, "list")]
    [InlineData("native", null, "wildcard")]
    [InlineData("compat-root", "1.0", "strong")]
    [InlineData("compat-root", "0.3", "weak")]
    [InlineData("compat-root", null, "list")]
    [InlineData("compat-root", null, "wildcard")]
    [InlineData("compat-wellknown", "1.0", "weak")]
    [InlineData("compat-wellknown", "0.3", "list")]
    [InlineData("compat-wellknown", null, "strong")]
    [InlineData("compat-wellknown", "0.3", "wildcard")]
    public async Task MatchingValidator_Returns304WithCacheMetadataAndNoBody(string route, string? version, string form)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var tag = Assert.IsType<System.Net.Http.Headers.EntityTagHeaderValue>(first.Headers.ETag).ToString();
        var validator = form switch
        {
            "strong" => tag,
            "weak" => $"W/{tag}",
            "list" => $"\"opaque,W/value\", \"trail\\\", W/{tag}, \"another\"",
            "wildcard" => "*",
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        using var result = await host.GetAsync(route, version, validator);

        Assert.Equal(HttpStatusCode.NotModified, result.StatusCode);
        Assert.Empty(await result.Content.ReadAsByteArrayAsync());
        Assert.Equal(first.Headers.ETag, result.Headers.ETag);
        Assert.Equal(first.Headers.CacheControl?.ToString(), result.Headers.CacheControl?.ToString());
        Assert.Equal(first.Content.Headers.LastModified, result.Content.Headers.LastModified);
        if (route != "native")
        {
            Assert.Contains("A2A-Version", result.Headers.Vary);
            Assert.Contains("Accept-Encoding", result.Headers.Vary);
        }
    }

    [Theory]
    [InlineData("native", null)]
    [InlineData("compat-root", "1.0")]
    [InlineData("compat-wellknown", "0.3")]
    [InlineData("compat-wellknown", null)]
    public async Task NonMatchingValidators_ReturnFullRepresentation(string route, string? version)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        var tag = first.Headers.ETag!.Tag.Trim('"');
        var original = await first.Content.ReadAsByteArrayAsync();
        foreach (var validator in new[] { "\"opaque,comma\", \"different\"", $"\"prefix{tag}\"", $"\"{tag}suffix\"" })
        {
            using var result = await host.GetAsync(route, version, validator);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(first.Headers.ETag, result.Headers.ETag);
            Assert.Equal(original, await result.Content.ReadAsByteArrayAsync());
        }
    }

    [Theory]
    [InlineData("native", null)]
    [InlineData("compat-root", "1.0")]
    [InlineData("compat-wellknown", "0.3")]
    [InlineData("compat-wellknown", null)]
    public async Task MalformedValidatorList_DoesNotProduceFalse304(string route, string? version)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        var tag = first.Headers.ETag!.ToString();
        var original = await first.Content.ReadAsByteArrayAsync();
        // Conservative policy: a malformed If-None-Match header is ignored as a whole.
        // Never turn an invalid wildcard combination or a malformed member into a match.
        foreach (var malformed in new[] { $"bad-token, {tag}", $"*, {tag}", "*, \"other\"", ", *,", $"W/ {tag}", $"W/\t{tag}", $"w/{tag}", $"{tag}, w/\"other\"", $"{tag}, \"has space\"", $"{tag}, \"has\ttab\"", $"{tag}, \"escaped\\\"quote\"", $"\"unterminated, {tag}" })
        {
            using var result = await host.GetAsync(route, version, malformed);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(original, await result.Content.ReadAsByteArrayAsync());
        }
    }

    [Theory]
    [InlineData("native", null)]
    [InlineData("compat-root", "1.0")]
    [InlineData("compat-wellknown", "0.3")]
    [InlineData("compat-wellknown", null)]
    public async Task OtherPreconditions_DisableNotModifiedResponse(string route, string? version)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        var tag = first.Headers.ETag!.ToString();
        // Conservative policy until the other preconditions are implemented: retain the
        // existing 200 behavior instead of returning a 304 without evaluating them.
        using var ifMatch = await host.GetAsync(route, version, tag, ifMatch: tag);
        using var ifUnmodified = await host.GetAsync(route, version, tag, ifUnmodifiedSince: "Wed, 21 Oct 2015 07:28:00 GMT");
        using var ifModified = await host.GetAsync(route, version, "\"not-the-current-tag\"", ifModifiedSince: "Wed, 21 Oct 2099 07:28:00 GMT");
        Assert.Equal(HttpStatusCode.OK, ifMatch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ifUnmodified.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ifModified.StatusCode);
        Assert.NotEmpty(await ifMatch.Content.ReadAsByteArrayAsync());
        Assert.NotEmpty(await ifUnmodified.Content.ReadAsByteArrayAsync());
        Assert.NotEmpty(await ifModified.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("native", null)]
    [InlineData("compat-root", "1.0")]
    [InlineData("compat-wellknown", "0.3")]
    [InlineData("compat-wellknown", null)]
    public async Task RepeatedIfNoneMatchValues_AreMatchedAsAList(string route, string? version)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        var tag = first.Headers.ETag!.ToString();
        using var matched = await host.GetAsync(route, version, ifNoneMatchValues: ["\"w/inside,tag\"", "\"trail\\\"", $"W/{tag},,"]);
        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());

        using var emptyMembers = await host.GetAsync(route, version, $", , W/{tag}, ,");
        Assert.Equal(HttpStatusCode.NotModified, emptyMembers.StatusCode);
        using var wildcardMixed = await host.GetAsync(route, version, ifNoneMatchValues: ["*", tag]);
        Assert.Equal(HttpStatusCode.OK, wildcardMixed.StatusCode);
    }

    [Theory]
    [InlineData("native", null)]
    [InlineData("compat-root", "1.0")]
    [InlineData("compat-wellknown", "0.3")]
    [InlineData("compat-wellknown", null)]
    public async Task CardChange_OldTagReturnsNewRepresentation(string route, string? version)
    {
        await using var host = await TestHost.StartAsync();
        using var first = await host.GetAsync(route, version);
        var oldTag = first.Headers.ETag!.ToString();
        var oldBody = await first.Content.ReadAsStringAsync();
        host.Card.Name = "Changed Agent";

        using var changed = await host.GetAsync(route, version, oldTag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(oldTag, changed.Headers.ETag!.ToString());
        var newBody = await changed.Content.ReadAsStringAsync();
        Assert.NotEqual(oldBody, newBody);
        Assert.Contains("Changed Agent", newBody);

        using var fresh = await host.GetAsync(route, version, changed.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.NotModified, fresh.StatusCode);
        Assert.Empty(await fresh.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("compat-root")]
    [InlineData("compat-wellknown")]
    public async Task DifferentNegotiatedRepresentation_DoesNotReuseAnotherVersionTag(string route)
    {
        await using var host = await TestHost.StartAsync();
        using var v03 = await host.GetAsync(route, "0.3");
        using var v1 = await host.GetAsync(route, "1.0", v03.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.NotEqual(v03.Headers.ETag, v1.Headers.ETag);
        Assert.NotEmpty(await v1.Content.ReadAsByteArrayAsync());
        Assert.Contains("A2A-Version", v1.Headers.Vary);
    }

    private sealed class TestHost(WebApplication app, HttpClient client, AgentCard card) : IAsyncDisposable
    {
        public AgentCard Card { get; } = card;

        public static async Task<TestHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Vary = "Accept-Encoding";
                await next();
            });
            var card = new AgentCard
            {
                Name = "Agent",
                Description = "Agent card caching regression",
                Version = "1.0",
                SupportedInterfaces = [new AgentInterface { Url = "http://localhost/agent", ProtocolBinding = "JSONRPC" }],
                Capabilities = new AgentCapabilities { Streaming = true },
                Skills = [],
            };
            app.MapWellKnownAgentCard(card, "/native");
            app.MapAgentCardGetWithV03Compat(() => Task.FromResult(card), "/compat", blendedCard: true);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            return new TestHost(app, client, card);
        }

        public async Task<HttpResponseMessage> GetAsync(
            string route, string? version, string? ifNoneMatch = null,
            string? ifMatch = null, string? ifUnmodifiedSince = null, string? ifModifiedSince = null,
            string[]? ifNoneMatchValues = null)
        {
            var path = route switch
            {
                "native" => "/native/.well-known/agent-card.json",
                "compat-root" => "/compat",
                "compat-wellknown" => "/compat/.well-known/agent-card.json",
                _ => throw new ArgumentOutOfRangeException(nameof(route)),
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (version is not null) request.Headers.TryAddWithoutValidation("A2A-Version", version);
            if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            if (ifUnmodifiedSince is not null) request.Headers.TryAddWithoutValidation("If-Unmodified-Since", ifUnmodifiedSince);
            if (ifModifiedSince is not null) request.Headers.TryAddWithoutValidation("If-Modified-Since", ifModifiedSince);
            if (ifNoneMatchValues is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatchValues);
            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
