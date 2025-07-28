using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class A2AEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task MapA2A_RegistersJsonRpcEndpoint_WithoutWellKnownEndpoint()
    {
        // Arrange
        var host = CreateTestHost(app =>
        {
            var taskManager = new TaskManager();
            app.MapA2A(taskManager, "/agent");
        });

        var client = host.GetTestClient();

        // Act & Assert - JsonRPC endpoint should exist
        var jsonRpcResponse = await client.PostAsync("/agent", new StringContent(
            @"{""jsonrpc"":""2.0"",""method"":""message.send"",""id"":""test"",""params"":{""message"":{""messageId"":""test"",""role"":""user"",""parts"":[{""kind"":""text"",""text"":""hi""}]}}}",
            System.Text.Encoding.UTF8,
            "application/json"));

        Assert.Equal(HttpStatusCode.OK, jsonRpcResponse.StatusCode);

        // Act & Assert - Well-known endpoint should NOT exist
        var wellKnownResponse = await client.GetAsync("/.well-known/agent.json");
        Assert.Equal(HttpStatusCode.NotFound, wellKnownResponse.StatusCode);
    }

    [Fact]
    public async Task MapWellKnownAgentCard_RegistersWellKnownEndpoint_WithoutJsonRpcEndpoint()
    {
        // Arrange
        var host = CreateTestHost(app =>
        {
            var taskManager = new TaskManager();
            app.MapWellKnownAgentCard(taskManager, "/agent");
        });

        var client = host.GetTestClient();

        // Act & Assert - Well-known endpoint should exist
        var wellKnownResponse = await client.GetAsync("/.well-known/agent.json");
        Assert.Equal(HttpStatusCode.OK, wellKnownResponse.StatusCode);

        var content = await wellKnownResponse.Content.ReadAsStringAsync();
        var agentCard = JsonSerializer.Deserialize<AgentCard>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(agentCard);

        // Act & Assert - JsonRPC endpoint should NOT exist
        var jsonRpcResponse = await client.PostAsync("/agent", new StringContent(
            @"{""jsonrpc"":""2.0"",""method"":""message.send"",""id"":""test"",""params"":{""message"":{""messageId"":""test"",""role"":""user"",""parts"":[{""kind"":""text"",""text"":""hi""}]}}}",
            System.Text.Encoding.UTF8,
            "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, jsonRpcResponse.StatusCode);
    }

    [Fact]
    public async Task MapA2A_And_MapWellKnownAgentCard_Together_RegistersBothEndpoints()
    {
        // Arrange
        var host = CreateTestHost(app =>
        {
            var taskManager = new TaskManager();
            app.MapA2A(taskManager, "/agent");
            app.MapWellKnownAgentCard(taskManager, "/agent");
        });

        var client = host.GetTestClient();

        // Act & Assert - Both endpoints should exist
        var jsonRpcResponse = await client.PostAsync("/agent", new StringContent(
            @"{""jsonrpc"":""2.0"",""method"":""message.send"",""id"":""test"",""params"":{""message"":{""messageId"":""test"",""role"":""user"",""parts"":[{""kind"":""text"",""text"":""hi""}]}}}",
            System.Text.Encoding.UTF8,
            "application/json"));

        Assert.Equal(HttpStatusCode.OK, jsonRpcResponse.StatusCode);

        var wellKnownResponse = await client.GetAsync("/.well-known/agent.json");
        Assert.Equal(HttpStatusCode.OK, wellKnownResponse.StatusCode);

        var content = await wellKnownResponse.Content.ReadAsStringAsync();
        var agentCard = JsonSerializer.Deserialize<AgentCard>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(agentCard);
    }

    [Fact]
    public async Task MapWellKnownAgentCard_SetsCorrectAgentUrl()
    {
        // Arrange
        var host = CreateTestHost(app =>
        {
            var taskManager = new TaskManager();
            app.MapWellKnownAgentCard(taskManager, "/custom-agent");
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/.well-known/agent.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var agentCard = JsonSerializer.Deserialize<AgentCard>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        
        Assert.NotNull(agentCard);
        Assert.NotNull(agentCard.Transports);
        
        // Check that the agent URL contains the custom path
        var transport = agentCard.Transports.FirstOrDefault();
        Assert.NotNull(transport);
        Assert.Contains("/custom-agent", transport.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapA2A_ThrowsArgumentException_WhenPathIsNullOrEmpty(string? path)
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        var taskManager = new TaskManager();

        // Act & Assert
        if (path == null)
        {
            Assert.Throws<ArgumentNullException>(() => app.MapA2A(taskManager, path!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => app.MapA2A(taskManager, path));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapWellKnownAgentCard_ThrowsArgumentException_WhenAgentPathIsNullOrEmpty(string? agentPath)
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        var taskManager = new TaskManager();

        // Act & Assert
        if (agentPath == null)
        {
            Assert.Throws<ArgumentNullException>(() => app.MapWellKnownAgentCard(taskManager, agentPath!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => app.MapWellKnownAgentCard(taskManager, agentPath));
        }
    }

    private static IHost CreateTestHost(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        configure(app);

        app.Start();
        return app;
    }
}