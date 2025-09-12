using A2A.AspNetCore;
using AgentServer;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text;
namespace A2A.Integration.Tests.Tck.Utils;

internal static class TransportHelpers
{
    public static WebApplicationFactory<AgentServer.SpecComplianceAgent> CreateTestApplication(
        Action<IServiceCollection>? configureServices = null,
        Action<TaskManager>? configureTaskManager = null)
    {
        return new WebApplicationFactory<AgentServer.SpecComplianceAgent>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => configureServices?.Invoke(services));

                builder.Configure((_, app) =>
                {
                    // Create and configure the TaskManager and SpecComplianceAgent exactly like Program.cs
                    var taskManager = new TaskManager();
                    var specComplianceAgent = new AgentServer.SpecComplianceAgent();
                    specComplianceAgent.Attach(taskManager);

                    configureTaskManager?.Invoke(taskManager);

                    // Use routing to enable endpoint mapping
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // Configure A2A endpoints exactly like Program.cs for speccompliance agent
                        endpoints.MapA2A(taskManager, "/speccompliance");
                        endpoints.MapWellKnownAgentCard(taskManager, "/speccompliance");
                    });
                });
            });
    }

    public static async Task<JsonDocument> TransportSendMessage(
        HttpClient client,
        string messageSendParamsJson,
        int requestId = 1)
    {
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.MessageSend}"",
            ""params"": {messageSendParamsJson},
            ""id"": {requestId}
        }}";

        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/speccompliance", content);
        var responseText = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(responseText);
    }

    public static async Task<JsonDocument> TransportGetTask(
        HttpClient client,
        string taskId,
        int? historyLength = null,
        int requestId = 1)
    {
        var paramsJson = historyLength.HasValue
            ? $@"{{""id"": ""{taskId}"", ""historyLength"": {historyLength.Value}}}"
            : $@"{{""id"": ""{taskId}""}}";

        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.TaskGet}"",
            ""params"": {paramsJson},
            ""id"": {requestId}
        }}";

        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/speccompliance", content);
        var responseText = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(responseText);
    }

    public static async Task<JsonDocument> TransportCancelTask(
        HttpClient client,
        string taskId,
        int requestId = 1)
    {
        var jsonRpcRequest = $@"{{
            ""jsonrpc"": ""2.0"",
            ""method"": ""{A2AMethods.TaskCancel}"",
            ""params"": {{""id"": ""{taskId}""}},
            ""id"": {requestId}
        }}";

        var content = new StringContent(jsonRpcRequest, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/speccompliance", content);
        var responseText = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(responseText);
    }

    public static bool IsJsonRpcSuccessResponse(JsonDocument response)
    {
        return response.RootElement.TryGetProperty("result", out _) &&
               !response.RootElement.TryGetProperty("error", out _);
    }

    public static bool IsJsonRpcErrorResponse(JsonDocument response)
    {
        return response.RootElement.TryGetProperty("error", out _);
    }

    public static string? ExtractTaskIdFromResponse(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("result", out var result))
        {
            if (result.TryGetProperty("id", out var idProperty))
            {
                return idProperty.GetString();
            }
        }
        return null;
    }

    public static string GenerateTestMessageId(string prefix = "test")
    {
        return $"{prefix}-{Guid.NewGuid()}";
    }

    public static int? GetErrorCode(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("code", out var code))
        {
            return code.GetInt32();
        }
        return null;
    }

    public static string? GetErrorMessage(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("message", out var message))
        {
            return message.GetString();
        }
        return null;
    }
}
