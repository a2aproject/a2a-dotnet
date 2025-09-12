using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace A2A.Integration.Tests.Tck.Utils;

/// <summary>
/// Transport-agnostic helpers for testing A2A implementations via HTTP endpoints.
/// This matches the upstream TCK approach but uses ASP.NET Core TestServer instead of external endpoints.
/// </summary>
public static class TransportHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = A2AJsonUtilities.DefaultOptions;

    /// <summary>
    /// Creates a test web application factory for A2A endpoint testing.
    /// This simulates the SUT (System Under Test) that the upstream TCK tests against.
    /// </summary>
    public static WebApplicationFactory<Program> CreateTestApplication(
        Action<IServiceCollection>? configureServices = null,
        Action<TaskManager>? configureTaskManager = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Add A2A services
                    services.AddSingleton<TaskManager>(provider =>
                    {
                        var taskManager = new TaskManager();
                        configureTaskManager?.Invoke(taskManager);
                        return taskManager;
                    });
                    
                    configureServices?.Invoke(services);
                });
                
                builder.Configure(app =>
                {
                    // Configure A2A endpoints exactly like the samples
                    app.MapA2AEndpoints();
                });
            });
    }

    /// <summary>
    /// Sends a message via transport-agnostic method (currently HTTP JSON-RPC).
    /// Matches the upstream transport_send_message function.
    /// </summary>
    public static async Task<JsonDocument> TransportSendMessage(
        HttpClient client,
        object messageSendParams,
        int requestId = 1)
    {
        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            method = A2AMethods.MessageSend,
            @params = messageSendParams,
            id = requestId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(jsonRpcRequest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/", content);
        var responseText = await response.Content.ReadAsStringAsync();
        
        return JsonDocument.Parse(responseText);
    }

    /// <summary>
    /// Gets a task via transport-agnostic method.
    /// Matches the upstream transport_get_task function.
    /// </summary>
    public static async Task<JsonDocument> TransportGetTask(
        HttpClient client,
        string taskId,
        int? historyLength = null,
        int requestId = 1)
    {
        var taskQueryParams = new Dictionary<string, object>
        {
            ["id"] = taskId
        };
        
        if (historyLength.HasValue)
        {
            taskQueryParams["historyLength"] = historyLength.Value;
        }

        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            method = A2AMethods.TaskGet,
            @params = taskQueryParams,
            id = requestId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(jsonRpcRequest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/", content);
        var responseText = await response.Content.ReadAsStringAsync();
        
        return JsonDocument.Parse(responseText);
    }

    /// <summary>
    /// Cancels a task via transport-agnostic method.
    /// Matches the upstream transport_cancel_task function.
    /// </summary>
    public static async Task<JsonDocument> TransportCancelTask(
        HttpClient client,
        string taskId,
        int requestId = 1)
    {
        var taskIdParams = new { id = taskId };

        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            method = A2AMethods.TaskCancel,
            @params = taskIdParams,
            id = requestId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(jsonRpcRequest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/", content);
        var responseText = await response.Content.ReadAsStringAsync();
        
        return JsonDocument.Parse(responseText);
    }

    /// <summary>
    /// Checks if a JSON-RPC response indicates success.
    /// Matches the upstream is_json_rpc_success_response function.
    /// </summary>
    public static bool IsJsonRpcSuccessResponse(JsonDocument response)
    {
        return response.RootElement.TryGetProperty("result", out _) &&
               !response.RootElement.TryGetProperty("error", out _);
    }

    /// <summary>
    /// Checks if a JSON-RPC response indicates an error.
    /// Matches the upstream is_json_rpc_error_response function.
    /// </summary>
    public static bool IsJsonRpcErrorResponse(JsonDocument response)
    {
        return response.RootElement.TryGetProperty("error", out _);
    }

    /// <summary>
    /// Extracts a task ID from a JSON-RPC response.
    /// Matches the upstream extract_task_id_from_response function.
    /// </summary>
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

    /// <summary>
    /// Generates a test message ID for transport-agnostic testing.
    /// Matches the upstream generate_test_message_id function.
    /// </summary>
    public static string GenerateTestMessageId(string prefix = "test")
    {
        return $"{prefix}-{Guid.NewGuid()}";
    }

    /// <summary>
    /// Gets the error code from a JSON-RPC error response.
    /// </summary>
    public static int? GetErrorCode(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("code", out var code))
        {
            return code.GetInt32();
        }
        return null;
    }

    /// <summary>
    /// Gets the error message from a JSON-RPC error response.
    /// </summary>
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

/// <summary>
/// Minimal Program class for WebApplicationFactory.
/// This represents the SUT (System Under Test) web application.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add A2A services
        builder.Services.AddSingleton<TaskManager>();
        
        var app = builder.Build();
        
        // Configure A2A endpoints
        app.MapA2AEndpoints();
        
        app.Run();
    }
}
