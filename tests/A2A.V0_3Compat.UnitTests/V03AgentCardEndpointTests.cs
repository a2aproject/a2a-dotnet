using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace A2A.V0_3Compat.UnitTests;

/// <summary>
/// Tests for <see cref="V03ServerCompatEndpointExtensions.MapAgentCardGetWithV03Compat"/>:
/// - No A2A-Version header → blended card (blendedCard=true default) or strict v0.3 (blendedCard=false)
/// - A2A-Version: 0.3 → strict v0.3 card (has top-level url, no supportedInterfaces)
/// - A2A-Version: 1.0 → v1.0 card (has supportedInterfaces, no top-level url)
/// Both GET / and GET /.well-known/agent-card.json are exercised.
/// </summary>
public class V03AgentCardEndpointTests
{
    private static AgentCard CreateTestCard() => new()
    {
        Name = "TestAgent",
        Description = "Test",
        Version = "1.0",
        SupportedInterfaces =
        [
            new AgentInterface { Url = "http://localhost/agent", ProtocolBinding = "JSONRPC" }
        ],
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills = [],
    };

    private static async Task<HttpClient> CreateClientAsync(
        bool blendedCard = true,
        AgentCardCacheOptions? cacheOptions = null,
        string? existingVary = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        if (existingVary is not null)
        {
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Vary = existingVary;
                await next();
            });
        }

        var card = CreateTestCard();
        app.MapAgentCardGetWithV03Compat(
            () => Task.FromResult(card),
            "/agent",
            blendedCard,
            cacheOptions);
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Theory]
    [InlineData("/agent")]
    [InlineData("/agent/.well-known/agent-card.json")]
    public async Task AgentCardRoutes_IncludeCacheControlMaxAge(string path)
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AgentCardRoutes_WithCacheOptions_UseConfiguredMaxAge()
    {
        using var client = await CreateClientAsync(
            cacheOptions: new AgentCardCacheOptions { MaxAge = TimeSpan.FromMinutes(15) });

        var response = await client.GetAsync("/agent/.well-known/agent-card.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=900", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void MapAgentCardGetWithV03Compat_WithNegativeMaxAge_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => app.MapAgentCardGetWithV03Compat(
            () => Task.FromResult(CreateTestCard()),
            cacheOptions: new AgentCardCacheOptions { MaxAge = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task WellKnownRoute_ETagVariesByRepresentation()
    {
        using var client = await CreateClientAsync();
        var blendedResponse = await client.GetAsync("/agent/.well-known/agent-card.json");
        var v03Request = new HttpRequestMessage(HttpMethod.Get, "/agent/.well-known/agent-card.json");
        v03Request.Headers.Add("A2A-Version", "0.3");
        var v10Request = new HttpRequestMessage(HttpMethod.Get, "/agent/.well-known/agent-card.json");
        v10Request.Headers.Add("A2A-Version", "1.0");

        var v03Response = await client.SendAsync(v03Request);
        var v10Response = await client.SendAsync(v10Request);

        blendedResponse.EnsureSuccessStatusCode();
        v03Response.EnsureSuccessStatusCode();
        v10Response.EnsureSuccessStatusCode();
        await AssertETagMatchesContentAsync(blendedResponse);
        await AssertETagMatchesContentAsync(v03Response);
        await AssertETagMatchesContentAsync(v10Response);
        Assert.NotEqual(v03Response.Headers.ETag, v10Response.Headers.ETag);
    }

    [Theory]
    [InlineData("/agent")]
    [InlineData("/agent/.well-known/agent-card.json")]
    public async Task AgentCardRoutes_VaryByA2AVersion(string path)
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Contains("A2A-Version", response.Headers.Vary);
    }

    [Theory]
    [InlineData("/agent")]
    [InlineData("/agent/.well-known/agent-card.json")]
    public async Task AgentCardRoutes_PreserveExistingVaryHeader(string path)
    {
        using var client = await CreateClientAsync(existingVary: "Accept-Encoding");

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Contains("A2A-Version", response.Headers.Vary);
    }

    [Fact]
    public async Task WellKnownRoute_LastModifiedIsStable()
    {
        using var client = await CreateClientAsync();

        var firstResponse = await client.GetAsync("/agent/.well-known/agent-card.json");
        var secondResponse = await client.GetAsync("/agent/.well-known/agent-card.json");

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal(firstResponse.Content.Headers.LastModified, secondResponse.Content.Headers.LastModified);
        Assert.True(DateTimeOffset.TryParseExact(
            firstResponse.Content.Headers.LastModified?.ToString("R"),
            "R",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _));
    }

    // ── GET /agent (root route) ────────────────────────────────────────────

    [Fact]
    public async Task RootRoute_NoHeader_BlendedTrue_ReturnsBlendedCard()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var response = await client.GetAsync("/agent");
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        // Blended card has both v0.3 top-level url AND v1.0 supportedInterfaces
        Assert.True(json.ContainsKey("url"), "Blended card must have v0.3 'url' field");
        Assert.True(json.ContainsKey("supportedInterfaces"), "Blended card must have v1.0 'supportedInterfaces'");
    }

    [Fact]
    public async Task RootRoute_NoHeader_BlendedFalse_ReturnsStrictV03Card()
    {
        using var client = await CreateClientAsync(blendedCard: false);
        var response = await client.GetAsync("/agent");
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("url"), "v0.3 card must have top-level 'url'");
        Assert.False(json.ContainsKey("supportedInterfaces"), "Strict v0.3 card must not have 'supportedInterfaces'");
    }

    [Fact]
    public async Task RootRoute_V03Header_ReturnsStrictV03Card()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/agent");
        request.Headers.Add("A2A-Version", "0.3");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("url"), "Explicit v0.3 request must get top-level 'url'");
        Assert.False(json.ContainsKey("supportedInterfaces"), "Explicit v0.3 request must not get 'supportedInterfaces'");
    }

    [Fact]
    public async Task RootRoute_V10Header_ReturnsV10Card()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/agent");
        request.Headers.Add("A2A-Version", "1.0");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("supportedInterfaces"), "v1.0 card must have 'supportedInterfaces'");
        Assert.False(json.ContainsKey("url"), "v1.0 card must not have top-level 'url'");
    }

    // ── GET /agent/.well-known/agent-card.json ─────────────────────────────

    [Fact]
    public async Task WellKnownRoute_NoHeader_BlendedTrue_ReturnsBlendedCard()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var response = await client.GetAsync("/agent/.well-known/agent-card.json");
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("url"), "Blended card must have v0.3 'url' field");
        Assert.True(json.ContainsKey("supportedInterfaces"), "Blended card must have v1.0 'supportedInterfaces'");
    }

    [Fact]
    public async Task WellKnownRoute_V03Header_ReturnsStrictV03Card()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/agent/.well-known/agent-card.json");
        request.Headers.Add("A2A-Version", "0.3");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("url"));
        Assert.False(json.ContainsKey("supportedInterfaces"));
    }

    [Fact]
    public async Task WellKnownRoute_V10Header_ReturnsV10Card()
    {
        using var client = await CreateClientAsync(blendedCard: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/agent/.well-known/agent-card.json");
        request.Headers.Add("A2A-Version", "1.0");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(json.ContainsKey("supportedInterfaces"));
        Assert.False(json.ContainsKey("url"));
    }

    private static async Task AssertETagMatchesContentAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(
            $"\"{Convert.ToHexString(SHA256.HashData(body))}\"",
            response.Headers.ETag?.ToString());
    }
}
