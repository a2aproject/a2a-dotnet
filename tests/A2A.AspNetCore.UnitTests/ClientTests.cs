using Json.Schema;
using System.Net;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public sealed class ClientTests : IClassFixture<JsonSchemaFixture>, IDisposable
{
    private readonly JsonSchema a2aSchema;
    private readonly MockMessageHandler mockHandler;
    private readonly A2AClient client;
    private readonly HttpClient httpClient;

    public ClientTests(JsonSchemaFixture fixture)
    {
        a2aSchema = fixture.Schema;

        mockHandler = new MockMessageHandler();

        httpClient = new HttpClient(mockHandler);

        client = new A2AClient(new Uri("http://example.org"), httpClient);
    }

    [Fact]
    public async Task TestGetTask()
    {
        // Arrange
        var taskId = "test-task";

        // Act
        await client.GetTaskAsync(new GetTaskRequest { Id = taskId });
        var message = mockHandler.RequestBody ?? string.Empty;
        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestSendMessage()
    {
        // Arrange
        var sendRequest = new SendMessageRequest
        {
            Message = new Message()
            {
                Role = Role.User,
                Parts =
                [
                    Part.FromText("Hello, World!"),
                ],
            },
        };

        // Act
        await client.SendMessageAsync(sendRequest);
        var message = mockHandler.RequestBody ?? string.Empty;

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestCancelTask()
    {
        // Arrange
        var taskId = "test-task";

        // Act
        await client.CancelTaskAsync(new CancelTaskRequest { Id = taskId });
        var message = mockHandler.RequestBody ?? string.Empty;

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestCreatePushNotificationConfig()
    {
        // Arrange

        // Set up the response provider for push notification
        mockHandler.ResponseProvider = request =>
        {
            var pushNotificationResponse = new TaskPushNotificationConfig
            {
                Id = "response-config-id",
                TaskId = "test-task",
                PushNotificationConfig = new PushNotificationConfig
                {
                    Url = "http://example.org/notify",
                    Token = "test-token",
                    Authentication = new AuthenticationInfo
                    {
                        Scheme = "Bearer"
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new JsonRpcContent(JsonRpcResponse.CreateJsonRpcResponse<object>("test-id", pushNotificationResponse))
            };
        };

        var createRequest = new CreateTaskPushNotificationConfigRequest
        {
            TaskId = "test-task",
            Config = new PushNotificationConfig()
            {
                Url = "http://example.org/notify",
                Token = "test-token",
                Authentication = new AuthenticationInfo()
                {
                    Scheme = "Bearer",
                }
            }
        };

        // Act
        await client.CreateTaskPushNotificationConfigAsync(createRequest);
        var message = mockHandler.RequestBody ?? string.Empty;

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }
}

public class JsonSchemaFixture
{
    public JsonSchema Schema { get; }

    public JsonSchemaFixture()
    {
        var schemaText = File.ReadAllText("a2a.json");
        Schema = JsonSchema.FromText(schemaText);
    }
}
public class MockMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }
    public Func<HttpRequestMessage, HttpResponseMessage>? ResponseProvider { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;

        // Capture the request body before the content may be disposed
        if (request.Content is not null)
        {
            RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        // Use custom response provider if available, otherwise create default
        var response = ResponseProvider?.Invoke(request) ?? CreateDefaultResponse(request);

        return response;
    }

    private static HttpResponseMessage CreateDefaultResponse(HttpRequestMessage request)
    {
        // Create a default successful response with AgentTask
        var defaultResult = new AgentTask
        {
            Id = "dummy-task-id",
            ContextId = "dummy-context-id",
            Status = new TaskStatus
            {
                State = TaskState.Completed,
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new JsonRpcContent(JsonRpcResponse.CreateJsonRpcResponse<object>("test-id", defaultResult))
        };
    }
}