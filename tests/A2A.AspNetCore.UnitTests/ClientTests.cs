using System.Net;
using A2A.Core;
using Json.Schema;
using System.Text.Json;

namespace A2A.AspNetCore.Tests;

public class ClientTests : IClassFixture<JsonSchemaFixture> {

     private readonly JsonSchema a2aSchema;

    public ClientTests(JsonSchemaFixture fixture) {
        a2aSchema = fixture.Schema;
    }

    [Fact]
    public async Task TestGetTask() {
        // Arrange
        var mockHandler = new MockMessageHandler();
        var client = new A2AClient(new HttpClient(mockHandler){
            BaseAddress = new Uri("http://example.org")
        });
        var taskId = "test-task";

        // Act
        var result = await client.GetTaskAsync(taskId);
        var message = mockHandler.Request?.Content != null
            ? await mockHandler.Request.Content.ReadAsStringAsync()
            : string.Empty;
        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestSendMessage() {
        // Arrange
        var mockHandler = new MockMessageHandler();
        var client = new A2AClient(new HttpClient(mockHandler){
            BaseAddress = new Uri("http://example.org")
        });
        var taskSendParams = new MessageSendParams {
            Message = new Message()
            {
                Parts =
                [
                    new TextPart()
                    {
                        Text = "Hello, World!",
                    }
                ],
            },
        };

        // Act
        var result = await client.SendMessageAsync(taskSendParams);
        var message = await mockHandler!.Request!.Content!.ReadAsStringAsync();

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestCancelTask() {
        // Arrange
        var mockHandler = new MockMessageHandler();
        var client = new A2AClient(new HttpClient(mockHandler){
            BaseAddress = new Uri("http://example.org")
        });
        var taskId = "test-task";

        // Act
        var result = await client.CancelTaskAsync(new TaskIdParams { Id = taskId });
        var message = await mockHandler!.Request!.Content!.ReadAsStringAsync();

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }

    [Fact]
    public async Task TestSetPushNotification() {
        // Arrange
        var mockHandler = new MockMessageHandler();
        var client = new A2AClient(new HttpClient(mockHandler){
            BaseAddress = new Uri("http://example.org")
        });
        var pushNotificationConfig = new TaskPushNotificationConfig
        {
         Id = "test-task",
         PushNotificationConfig = new PushNotificationConfig()
         {
             Url = "http://example.org/notify",
             Token = "test-token",
             Authentication = new AuthenticationInfo()
             {
                 Schemes = new List<string> { "Bearer" },
             }
         }
        };

        // Act
        var result = await client.SetPushNotificationAsync(pushNotificationConfig);
        var message = await mockHandler!.Request!.Content!.ReadAsStringAsync();

        // Assert
        Assert.NotNull(message);

        // JSON Schema validation using JSONSchema.Net
        var json = JsonDocument.Parse(message);
        var validationResult = a2aSchema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(validationResult.IsValid, $"JSON does not match schema: {validationResult.Details}");
    }
}

public class JsonSchemaFixture {
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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
           RequestMessage = request,
           Content = new JsonRpcContent(JsonRpcResponse.CreateJsonRpcResponse<A2AResponse>("asdas",new AgentTask()
               {
                   Id = "dummy-task-id",
                   ContextId = "dummy-context-id",
                   Status = new AgentTaskStatus()
                   {
                       State = TaskState.Completed,

                   }
               }))
        };
        return Task.FromResult(response);
    }
}