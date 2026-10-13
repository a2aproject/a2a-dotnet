# .NET Service Discovery

Use the existing `HttpClient` parameters to integrate the A2A HTTP clients with
[.NET Service Discovery](https://learn.microsoft.com/dotnet/core/extensions/service-discovery).
No additional A2A discovery API is required for this pattern.

`https+http://itservices-agent-api` is a logical service address, not a scheme
that a plain `HttpClient` can send. The Service Discovery message handler resolves
the service name and chooses an HTTPS or HTTP endpoint before sending the request.
Without that handler, the request fails with
`NotSupportedException: The 'https+http' scheme is not supported.`

## Configure and use the HTTP client

Add the host and Service Discovery packages to your client application:

```bash
dotnet add package A2A
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.ServiceDiscovery
```

This console example discovers an agent and sends a message using the HTTP binding
selected from its card:

```csharp
using A2A;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddServiceDiscovery();
builder.Services.AddHttpClient("a2a")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false })
    .AddServiceDiscovery();

using var host = builder.Build();
using var httpClient = host.Services.GetRequiredService<IHttpClientFactory>()
    .CreateClient("a2a");

// Configure discovery and invocation, not just one of them.
var resolver = new A2ACardResolver(
    new Uri("https+http://itservices-agent-api"), httpClient: httpClient);
var card = await resolver.GetAgentCardAsync();
var client = A2AClientFactory.Create(card, httpClient: httpClient,
    options: new A2AClientOptions
    {
        PreferredBindings = [ProtocolBindingNames.HttpJson, ProtocolBindingNames.JsonRpc]
    });

var response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.User,
        Parts = [Part.FromText("Hello!")]
    }
});
Console.WriteLine(response.PayloadCase);
```

The client application also needs endpoint configuration. Aspire can supply this
through a service reference. Without Aspire, configure the service through normal
.NET configuration, for example in `appsettings.json`:

```json
{
  "Services": {
    "itservices-agent-api": {
      "http": ["http://localhost:5000"]
    }
  }
}
```

Replace the endpoint with the address of your agent server. The server must expose
`/.well-known/agent-card.json`, and the card's `supportedInterfaces` must advertise
an invocation URL reachable by the client. Discovery does not rewrite the card's
URLs: the invocation client uses the advertised URL. If that URL is also logical,
its service name needs endpoint configuration as well.

## Microsoft Agent Framework

When using `Microsoft.Agents.AI.A2A`, replace the factory and invocation part of
the preceding example with:

```csharp
var agent = await resolver.GetAIAgentAsync(httpClient: httpClient);
Console.WriteLine(await agent.RunAsync("Hello!"));
```

The resolver must still be constructed with the configured `httpClient`.
`GetAIAgentAsync` uses the resolver's client to fetch the card; its own
`httpClient` argument configures subsequent agent calls. Passing the client only
to the extension method does not configure card discovery, and passing it only
to the resolver does not configure agent invocation.

This example concerns JSON-RPC and HTTP+JSON. It does not configure gRPC discovery.
Keep cookie handling aligned with the application's isolation requirements; see
[Client Cookie Isolation](security.md#2-client-cookie-isolation).
