using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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

    private static async Task<HttpClient> CreateClientAsync(bool blendedCard = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var card = CreateTestCard();
        app.MapAgentCardGetWithV03Compat(() => Task.FromResult(card), "/agent", blendedCard);
        await app.StartAsync();
        return app.GetTestClient();
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
}
