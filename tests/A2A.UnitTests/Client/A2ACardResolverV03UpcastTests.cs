using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace A2A.UnitTests.Client;

/// <summary>
/// Tests for the v0.3 → v1.0 agent card upcast fallback in <see cref="A2ACardResolver"/>.
/// v0.3 cards use a top-level <c>url</c> + <c>preferredTransport</c> and an
/// <c>additionalInterfaces</c> array of <c>{ transport, url }</c> objects, none of which
/// match the v1.0 <c>supportedInterfaces</c> shape of <c>{ url, protocolBinding, protocolVersion }</c>.
/// </summary>
public class A2ACardResolverV03UpcastTests
{
    private static A2ACardResolver CreateResolver(string cardJson, ILogger? logger = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(cardJson, Encoding.UTF8, "application/json"),
        };
        var handler = new MockHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        return new A2ACardResolver(new Uri("http://localhost"), httpClient, logger: logger);
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

        var logger = new RecordingLogger();
        var resolver = CreateResolver(cardJson, logger);

        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.Equal(3, card.SupportedInterfaces.Count);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries, entry => entry.EventId.Id == 5).Level);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);

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
    public async Task UpcastsV03Card_DropsSignaturesBoundToOriginalPayload()
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
        Assert.Null(card.Signatures);
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
    public async Task UpcastsV03Card_MapsSkillSecurityAndAuthenticatedExtendedCardCapability()
    {
        const string cardJson = """
        {
          "protocolVersion": "0.3",
          "name": "Test Agent",
          "description": "A v0.3 test agent",
          "version": "1.0.0",
          "url": "http://localhost/rpc",
          "supportsAuthenticatedExtendedCard": true,
          "capabilities": {},
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": [
            {
              "id": "secure-skill",
              "name": "Secure Skill",
              "description": "A skill with security requirements",
              "tags": ["secure"],
              "security": [
                { "oauth": ["read", "write"] },
                { "apiKey": [] }
              ]
            }
          ]
        }
        """;

        var resolver = CreateResolver(cardJson);
        var card = await resolver.GetAgentCardAsync();

        Assert.NotNull(card);
        Assert.True(card.Capabilities.ExtendedAgentCard);
        var skill = Assert.Single(card.Skills);
        Assert.NotNull(skill.SecurityRequirements);
        Assert.Equal(2, skill.SecurityRequirements.Count);
        Assert.Equal(["read", "write"], skill.SecurityRequirements[0].Schemes!["oauth"].List);
        Assert.Empty(skill.SecurityRequirements[1].Schemes!["apiKey"].List);
    }

    [Fact]
    public async Task GetAgentCardAsync_ThrowsA2AExceptionWhenV03FallbackJsonIsMalformed()
    {
        var logger = new RecordingLogger();
        var resolver = CreateResolver("""{"protocolVersion":"0.3","url":""", logger);

        var exception = await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());

        Assert.Contains("Failed to parse JSON", exception.Message);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(exception.InnerException, error.Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
    }

    [Fact]
    public async Task GetAgentCardAsync_WhenNotV03_PreservesOriginalV1ParsingFailure()
    {
        const string json = """{"name":"Invalid v1 card"}""";
        var expected = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, A2AJsonUtilities.JsonContext.Default.AgentCard));
        var logger = new RecordingLogger();

        var exception = await Assert.ThrowsAsync<A2AException>(() => CreateResolver(json, logger).GetAgentCardAsync());

        var original = Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Equal(expected.Message, original.Message);
        Assert.Contains("JsonSerializer", original.StackTrace);
        Assert.Same(original, Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
    }

    [Theory]
    [InlineData("apiKey", "name", "42")]
    [InlineData("apiKey", "in", "true")]
    [InlineData("apiKey", "description", "{}")]
    [InlineData("http", "scheme", "[]")]
    [InlineData("http", "bearerFormat", "42")]
    [InlineData("http", "description", "false")]
    [InlineData("openIdConnect", "openIdConnectUrl", "{}")]
    [InlineData("openIdConnect", "description", "42")]
    [InlineData("mutualTLS", "description", "[]")]
    public async Task UpcastsV03Card_MalformedSecuritySchemeString_LogsAndThrowsA2AException(
        string type, string property, string value)
    {
        var json = $$"""
        {
          "protocolVersion": "0.3",
          "url": "http://localhost/rpc",
          "securitySchemes": { "auth": { "type": "{{type}}", "{{property}}": {{value}} } }
        }
        """;
        var logger = new RecordingLogger();

        var exception = await Assert.ThrowsAsync<A2AException>(() => CreateResolver(json, logger).GetAgentCardAsync());

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Contains(property, exception.Message);
        Assert.Same(exception.InnerException, Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
    }

    [Fact]
    public async Task GetAgentCardAsync_V1Card_PreservesSignaturesAndDoesNotUpcast()
    {
        const string json = """
        {
          "name": "V1 agent",
          "description": "A v1 agent",
          "version": "1.0",
          "supportedInterfaces": [{ "url": "http://localhost", "protocolBinding": "JSONRPC", "protocolVersion": "1.0" }],
          "capabilities": { "extendedAgentCard": true },
          "defaultInputModes": [],
          "defaultOutputModes": [],
          "skills": [],
          "signatures": [{ "protected": "header", "signature": "abc123" }]
        }
        """;
        var logger = new RecordingLogger();

        var card = await CreateResolver(json, logger).GetAgentCardAsync();

        var signature = Assert.Single(card.Signatures!);
        Assert.Equal("header", signature.Protected);
        Assert.Equal("abc123", signature.Signature);
        Assert.True(card.Capabilities.ExtendedAgentCard);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5 || entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task GetAgentCardAsync_CanceledToken_PropagatesCancellation()
    {
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateResolver("not-json", logger).GetAgentCardAsync(cancellation.Token));

        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData("\"0.3\"", true)]
    [InlineData("\"0.3.0\"", true)]
    [InlineData("\"\"", false)]
    [InlineData("\"1.0\"", false)]
    [InlineData("\"0.2\"", false)]
    [InlineData("\"0.3.1\"", false)]
    [InlineData("\" 0.3\"", false)]
    [InlineData("null", false)]
    [InlineData("3", false)]
    public async Task UpcastsV03Card_RequiresSupportedProtocolVersion(string versionJson, bool supported)
    {
        var json = $$"""{"url":"http://localhost/rpc","protocolVersion":{{versionJson}}}""";
        var logger = new RecordingLogger();
        var resolver = CreateResolver(json, logger);

        if (supported)
        {
            var card = await resolver.GetAgentCardAsync();
            Assert.Equal(JsonSerializer.Deserialize<string>(versionJson), Assert.Single(card.SupportedInterfaces).ProtocolVersion);
            Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries, entry => entry.EventId.Id == 5).Level);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());
            Assert.IsAssignableFrom<JsonException>(exception.InnerException);
            Assert.Contains("supportedInterfaces", exception.Message);
            Assert.Same(exception.InnerException, Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception);
            Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
        }
    }

    [Fact]
    public async Task UpcastsV03Card_MapsOAuth2AuthorizationCodeFlow()
    {
        const string json = """
        {
          "protocolVersion": "0.3",
          "url": "http://localhost/rpc",
          "securitySchemes": {
            "oauth": {
              "type": "oauth2",
              "description": "OAuth authorization",
              "flows": {
                "authorizationCode": {
                  "authorizationUrl": "https://auth.example.com/authorize",
                  "tokenUrl": "https://auth.example.com/token",
                  "refreshUrl": "https://auth.example.com/refresh",
                  "scopes": { "read": "Read access", "write": "Write access" }
                }
              }
            }
          }
        }
        """;

        var card = await CreateResolver(json).GetAgentCardAsync();

        var oauth = card.SecuritySchemes!["oauth"].OAuth2SecurityScheme;
        Assert.NotNull(oauth);
        Assert.Equal("OAuth authorization", oauth.Description);
        var flow = oauth.Flows.AuthorizationCode;
        Assert.NotNull(flow);
        Assert.Equal("https://auth.example.com/authorize", flow.AuthorizationUrl);
        Assert.Equal("https://auth.example.com/token", flow.TokenUrl);
        Assert.Equal("https://auth.example.com/refresh", flow.RefreshUrl);
        Assert.Equal(2, flow.Scopes.Count);
        Assert.Equal("Read access", flow.Scopes["read"]);
        Assert.Equal("Write access", flow.Scopes["write"]);
    }

    [Fact]
    public async Task UpcastsV03Card_MalformedOAuth2Flow_LogsAndThrowsA2AException()
    {
        const string json = """
        {
          "protocolVersion": "0.3",
          "url": "http://localhost/rpc",
          "securitySchemes": {
            "oauth": { "type": "oauth2", "flows": { "authorizationCode": { "authorizationUrl": 42 } } }
          }
        }
        """;
        var logger = new RecordingLogger();

        var exception = await Assert.ThrowsAsync<A2AException>(() => CreateResolver(json, logger).GetAgentCardAsync());

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Same(exception.InnerException, Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
    }

    [Theory]
    [InlineData(false, 1, "known")]
    [InlineData(false, 0, "known")]
    [InlineData(false, -1, "known")]
    [InlineData(true, 1, "known")]
    [InlineData(true, 0, "known")]
    [InlineData(true, -1, "known")]
    [InlineData(false, 1, "absent")]
    [InlineData(false, 0, "absent")]
    [InlineData(false, -1, "absent")]
    [InlineData(true, 1, "absent")]
    [InlineData(true, 0, "absent")]
    [InlineData(true, -1, "absent")]
    [InlineData(false, 1, "underreported")]
    [InlineData(false, 0, "underreported")]
    [InlineData(false, -1, "underreported")]
    [InlineData(true, 1, "underreported")]
    [InlineData(true, 0, "underreported")]
    [InlineData(true, -1, "underreported")]
    public async Task GetAgentCardAsync_EnforcesActualByteLimit(bool v03, int limitOffset, string lengthHeader)
    {
        var json = GetSizeTestCardJson(v03);
        var size = Encoding.UTF8.GetByteCount(json);
        long? reportedLength = lengthHeader switch { "known" => size, "underreported" => 1, _ => null };
        using var content = new StreamingContent(json, reportedLength);
        using var client = new HttpClient(new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var logger = new RecordingLogger();
        var resolver = new A2ACardResolver(new Uri("http://localhost"), maxAgentCardSize: size + limitOffset, httpClient: client, logger: logger);

        if (limitOffset >= 0)
        {
            var card = await resolver.GetAgentCardAsync();
            Assert.Equal("Test Agent", card.Name);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());
            var httpException = Assert.IsType<HttpRequestException>(exception.InnerException);
            Assert.Contains((size + limitOffset).ToString(System.Globalization.CultureInfo.InvariantCulture), httpException.Message);
            Assert.Same(httpException, Assert.Single(logger.Entries, entry => entry.EventId.Id == 2).Exception);
            Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAgentCardAsync_DefaultLimitIsOneMiB_AndCanBeIncreased(bool v03)
    {
        var json = GetSizeTestCardJson(v03);
        json += new string(' ', 1048577 - Encoding.UTF8.GetByteCount(json));
        using var content = new StreamingContent(json, null);
        using var client = new HttpClient(new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var defaultResolver = new A2ACardResolver(new Uri("http://localhost"), client);

        var exception = await Assert.ThrowsAsync<A2AException>(() => defaultResolver.GetAgentCardAsync());
        Assert.IsType<HttpRequestException>(exception.InnerException);

        using var largerContent = new StreamingContent(json, null);
        using var largerClient = new HttpClient(new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = largerContent }));
        var configuredResolver = new A2ACardResolver(new Uri("http://localhost"), maxAgentCardSize: 1048577, httpClient: largerClient);
        Assert.Equal("Test Agent", (await configuredResolver.GetAgentCardAsync()).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAgentCardAsync_PreBufferedContent_StillEnforcesByteLimit(bool v03)
    {
        var json = GetSizeTestCardJson(v03);
        using var content = new StreamingContent(json, 1);
        await content.LoadIntoBufferAsync();
        using var client = new HttpClient(new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var logger = new RecordingLogger();
        var resolver = new A2ACardResolver(new Uri("http://localhost"), maxAgentCardSize: 1, httpClient: client, logger: logger);

        var exception = await Assert.ThrowsAsync<A2AException>(() => resolver.GetAgentCardAsync());

        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Same(exception.InnerException, Assert.Single(logger.Entries, entry => entry.EventId.Id == 2).Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 5);
    }

    [Fact]
    public void Constructor_PreservesOriginalBinarySignatureAndOptionalDefaults()
    {
        var constructor = typeof(A2ACardResolver).GetConstructor(
            [typeof(Uri), typeof(HttpClient), typeof(string), typeof(ILogger)]);

        Assert.NotNull(constructor);
        var parameters = constructor.GetParameters();
        Assert.False(parameters[0].IsOptional);
        Assert.True(parameters[1].IsOptional);
        Assert.Null(parameters[1].DefaultValue);
        Assert.True(parameters[2].IsOptional);
        Assert.Equal("/.well-known/agent-card.json", parameters[2].DefaultValue);
        Assert.True(parameters[3].IsOptional);
        Assert.Null(parameters[3].DefaultValue);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void Constructor_RejectsInvalidSizeLimit(long limit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new A2ACardResolver(new Uri("http://localhost"), maxAgentCardSize: limit));
        Assert.Equal("maxAgentCardSize", exception.ParamName);
    }

    [Fact]
    public async Task GetAgentCardAsync_CancellationDuringBuffering_PropagatesWithoutParsing()
    {
        using var cancellation = new CancellationTokenSource();
        using var content = new StreamingContent(GetSizeTestCardJson(false), null, cancellation);
        using var client = new HttpClient(new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var logger = new RecordingLogger();
        var resolver = new A2ACardResolver(new Uri("http://localhost"), client, logger: logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.GetAgentCardAsync(cancellation.Token));

        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id is 1 or 2 or 5);
    }

    private static string GetSizeTestCardJson(bool v03) => v03
        ? """{"protocolVersion":"0.3","url":"http://localhost/rpc","name":"Test Agent"}"""
        : """
        {
          "name":"Test Agent","description":"A v1 agent","version":"1.0",
          "supportedInterfaces":[{"url":"http://localhost/rpc","protocolBinding":"JSONRPC","protocolVersion":"1.0"}],
          "capabilities":{},"defaultInputModes":[],"defaultOutputModes":[],"skills":[]
        }
        """;

    private sealed class StreamingContent(string json, long? reportedLength, CancellationTokenSource? cancelOnWrite = null) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(json);

        protected override bool TryComputeLength(out long length)
        {
            length = reportedLength ?? 0;
            return reportedLength.HasValue;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            if (cancelOnWrite is not null)
            {
                await cancelOnWrite.CancelAsync();
            }

            await stream.WriteAsync(_bytes, cancellationToken);
        }
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
            { "transport": "HTTP+JSON", "url": 42 },
            { "url": "http://localhost/missing-transport" },
            { "transport": null, "url": "http://localhost/null-transport" },
            { "transport": 42, "url": "http://localhost/numeric-transport" },
            { "transport": "", "url": "http://localhost/empty-transport" },
            { "transport": { "value": "GRPC" }, "url": "http://localhost/object-transport" },
            { "transport": true, "url": "http://localhost/boolean-transport" },
            { "transport": [], "url": "http://localhost/array-transport" }
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

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, EventId EventId, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, exception));
    }
}
