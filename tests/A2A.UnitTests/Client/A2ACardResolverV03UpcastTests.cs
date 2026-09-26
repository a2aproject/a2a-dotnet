using System.Net;
using System.Text;

namespace A2A.UnitTests.Client;

/// <summary>
/// Tests for the v0.3 → v1.0 agent card upcast fallback in <see cref="A2ACardResolver"/>.
/// v0.3 cards use a top-level <c>url</c> + <c>preferredTransport</c> and an
/// <c>additionalInterfaces</c> array of <c>{ transport, url }</c> objects, none of which
/// match the v1.0 <c>supportedInterfaces</c> shape of <c>{ url, protocolBinding, protocolVersion }</c>.
/// </summary>
public class A2ACardResolverV03UpcastTests
{
    private static A2ACardResolver CreateResolver(string cardJson)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(cardJson, Encoding.UTF8, "application/json"),
        };
        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        return new A2ACardResolver(new Uri("http://localhost"), httpClient);
    }

    [Fact]
    public async Task UpcastsV03Card_MapsAdditionalInterfacesTransportAndProtocolVersion()
    {
        // A minimal but realistic v0.3 card with a JSONRPC primary interface and
        // additional HTTP+JSON and GRPC interfaces. A v1.0 client that naively deserializes
        // each entry as an AgentInterface would drop the "transport" field and default
        // ProtocolBinding to JSONRPC, silently misrouting requests.
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "preferredTransport": "JSONRPC",
          "additionalInterfaces": [
            { "transport": "HTTP+JSON", "url": "http://localhost/http" },
            { "transport": "GRPC",      "url": "http://localhost/grpc" }
          ],
          "capabilities": { "streaming": true, "pushNotifications": false },
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": []
        }
        """;

        var resolver = CreateResolver(cardJson);

        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.Equal(3, card.SupportedInterfaces.Count);

        var primary = card.SupportedInterfaces[0];
        Assert.Equal("http://localhost/rpc", primary.Url);
        Assert.Equal(ProtocolBindingNames.JsonRpc, primary.ProtocolBinding);
        Assert.Equal("0.3", primary.ProtocolVersion);

        var http = card.SupportedInterfaces[1];
        Assert.Equal("http://localhost/http", http.Url);
        Assert.Equal(ProtocolBindingNames.HttpJson, http.ProtocolBinding);
        Assert.Equal("0.3", http.ProtocolVersion);

        var grpc = card.SupportedInterfaces[2];
        Assert.Equal("http://localhost/grpc", grpc.Url);
        Assert.Equal(ProtocolBindingNames.Grpc, grpc.ProtocolBinding);
        Assert.Equal("0.3", grpc.ProtocolVersion);
    }

    [Fact]
    public async Task UpcastsV03Card_UnknownTransportPassesThrough()
    {
        // Custom / unknown transport strings should round-trip so custom protocol bindings
        // are preserved rather than swallowed.
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "additionalInterfaces": [
            { "transport": "WEBSOCKET", "url": "ws://localhost/ws" }
          ],
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": []
        }
        """;

        var resolver = CreateResolver(cardJson);

        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.Equal(2, card.SupportedInterfaces.Count);
        Assert.Equal("WEBSOCKET", card.SupportedInterfaces[1].ProtocolBinding);
        Assert.Equal("ws://localhost/ws", card.SupportedInterfaces[1].Url);
    }

    [Fact]
    public async Task UpcastsV03Card_MapsProvider()
    {
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": [],
          "provider": {
            "organization": "Contoso",
            "url": "https://contoso.com"
          }
        }
        """;

        var resolver = CreateResolver(cardJson);
        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.NotNull(card.Provider);
        Assert.Equal("Contoso", card.Provider.Organization);
        Assert.Equal("https://contoso.com", card.Provider.Url);
    }

    [Fact]
    public async Task UpcastsV03Card_MapsSignatures()
    {
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": [],
          "signatures": [
            {
              "protected": "eyJhbGciOiJSUzI1NiJ9",
              "signature": "abc123"
            }
          ]
        }
        """;

        var resolver = CreateResolver(cardJson);
        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.NotNull(card.Signatures);
        Assert.Single(card.Signatures);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9", card.Signatures[0].Protected);
        Assert.Equal("abc123", card.Signatures[0].Signature);
    }

    [Fact]
    public async Task UpcastsV03Card_MapsSecuritySchemes()
    {
        // v0.3 securitySchemes use polymorphic discriminator on "type".
        // v1 uses a flat container with optional sub-scheme properties.
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": [],
          "securitySchemes": {
            "apiKeyAuth": {
              "type": "apiKey",
              "name": "X-API-Key",
              "in": "header",
              "description": "API key auth"
            },
            "bearerAuth": {
              "type": "http",
              "scheme": "bearer",
              "bearerFormat": "JWT"
            },
            "oidcAuth": {
              "type": "openIdConnect",
              "openIdConnectUrl": "https://auth.example.com/.well-known/openid-configuration"
            },
            "mtlsAuth": {
              "type": "mutualTLS",
              "description": "mTLS"
            }
          }
        }
        """;

        var resolver = CreateResolver(cardJson);
        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.NotNull(card.SecuritySchemes);
        Assert.Equal(4, card.SecuritySchemes.Count);

        // API Key
        var apiKey = card.SecuritySchemes["apiKeyAuth"];
        Assert.NotNull(apiKey.ApiKeySecurityScheme);
        Assert.Equal("X-API-Key", apiKey.ApiKeySecurityScheme.Name);
        Assert.Equal("header", apiKey.ApiKeySecurityScheme.Location);
        Assert.Equal("API key auth", apiKey.ApiKeySecurityScheme.Description);

        // HTTP Bearer
        var bearer = card.SecuritySchemes["bearerAuth"];
        Assert.NotNull(bearer.HttpAuthSecurityScheme);
        Assert.Equal("bearer", bearer.HttpAuthSecurityScheme.Scheme);
        Assert.Equal("JWT", bearer.HttpAuthSecurityScheme.BearerFormat);

        // OpenID Connect
        var oidc = card.SecuritySchemes["oidcAuth"];
        Assert.NotNull(oidc.OpenIdConnectSecurityScheme);
        Assert.Equal("https://auth.example.com/.well-known/openid-configuration", oidc.OpenIdConnectSecurityScheme.OpenIdConnectUrl);

        // Mutual TLS
        var mtls = card.SecuritySchemes["mtlsAuth"];
        Assert.NotNull(mtls.MtlsSecurityScheme);
        Assert.Equal("mTLS", mtls.MtlsSecurityScheme.Description);
    }

    [Fact]
    public async Task UpcastsV03Card_MapsSecurity()
    {
        // v0.3 security is List<Dictionary<string, string[]>>,
        // v1 is List<SecurityRequirement> with Schemes dictionary.
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": [],
          "security": [
            {
              "bearerAuth": ["read", "write"],
              "apiKeyAuth": []
            }
          ]
        }
        """;

        var resolver = CreateResolver(cardJson);
        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.NotNull(card.SecurityRequirements);
        Assert.Single(card.SecurityRequirements);

        var req = card.SecurityRequirements[0];
        Assert.NotNull(req.Schemes);
        Assert.Equal(2, req.Schemes.Count);
        Assert.Equal(["read", "write"], req.Schemes["bearerAuth"].List);
        Assert.Empty(req.Schemes["apiKeyAuth"].List);
    }

    [Fact]
    public async Task UpcastsV03Card_SkipsMalformedAdditionalInterfaceEntries()
    {
        // Entries without a string url, or that aren't objects, must be skipped
        // rather than throwing or producing invalid AgentInterface instances.
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "additionalInterfaces": [
            { "transport": "HTTP+JSON", "url": "http://localhost/http" },
            { "transport": "GRPC" },
            "not-an-object",
            { "transport": "HTTP+JSON", "url": 42 }
          ],
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": []
        }
        """;

        var resolver = CreateResolver(cardJson);

        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        // primary + one valid additional
        Assert.Equal(2, card.SupportedInterfaces.Count);
        Assert.Equal("http://localhost/http", card.SupportedInterfaces[1].Url);
        Assert.Equal(ProtocolBindingNames.HttpJson, card.SupportedInterfaces[1].ProtocolBinding);
    }
}
