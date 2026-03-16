using System.Net;
using System.Text.Json;

namespace A2A.V0_3.UnitTests;

/// <summary>
/// End-to-end integration tests for V0.3 backward compatibility.
/// Uses a loopback HTTP handler to route A2AClient requests through
/// TaskManager and validate the full serialization round-trip.
/// </summary>
public class V03IntegrationTests
{
    private static (A2AClient client, TaskManager taskManager) CreateTestHarness(Action<string>? onRequest = null)
    {
        var taskManager = new TaskManager(taskStore: new InMemoryTaskStore());
        var handler = new LoopbackHandler(taskManager, onRequest);
        var httpClient = new HttpClient(handler);
        var client = new A2AClient(new Uri("http://localhost/test"), httpClient);
        return (client, taskManager);
    }

    [Fact]
    public async Task SendMessage_ReturnsTask_RoundTrip()
    {
        var (client, taskManager) = CreateTestHarness();

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            var task = await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
            await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, cancellationToken: ct);
            return (await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id }, ct))!;
        };

        var response = await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Hello V0.3!" }]
            }
        });

        Assert.NotNull(response);
        var task = Assert.IsType<AgentTask>(response);
        Assert.NotEmpty(task.Id);
        Assert.Equal(TaskState.Completed, task.Status.State);
    }

    [Fact]
    public async Task SendMessage_WithTextAndDataParts_RoundTrip()
    {
        var (client, taskManager) = CreateTestHarness();
        List<Part>? capturedParts = null;

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            capturedParts = sendParams.Message.Parts;
            return await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
        };

        var response = await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts =
                [
                    new TextPart { Text = "Hello!" },
                    new DataPart { Data = new Dictionary<string, JsonElement>
                    {
                        ["key"] = JsonDocument.Parse("\"value\"").RootElement
                    }},
                ]
            }
        });

        Assert.NotNull(response);
        Assert.NotNull(capturedParts);
        Assert.Equal(2, capturedParts.Count);
        Assert.IsType<TextPart>(capturedParts[0]);
        Assert.Equal("Hello!", ((TextPart)capturedParts[0]).Text);
        Assert.IsType<DataPart>(capturedParts[1]);
    }

    [Fact]
    public async Task GetTask_RoundTrip()
    {
        var (client, taskManager) = CreateTestHarness();

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            return await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
        };

        // Create a task first
        var sendResponse = await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Create task" }]
            }
        });

        var createdTask = Assert.IsType<AgentTask>(sendResponse);

        // Now get it by ID
        var retrieved = await client.GetTaskAsync(createdTask.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(createdTask.Id, retrieved.Id);
        Assert.Equal(createdTask.ContextId, retrieved.ContextId);
    }

    [Fact]
    public async Task CancelTask_RoundTrip()
    {
        var (client, taskManager) = CreateTestHarness();

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            return await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
        };

        taskManager.OnTaskCancelled = (task, ct) => Task.CompletedTask;

        // Create a task
        var sendResponse = await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Cancel me" }]
            }
        });

        var createdTask = Assert.IsType<AgentTask>(sendResponse);

        // Cancel it
        var canceled = await client.CancelTaskAsync(new TaskIdParams { Id = createdTask.Id });

        Assert.NotNull(canceled);
        Assert.Equal(createdTask.Id, canceled.Id);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);
    }

    [Fact]
    public async Task GetTask_NonExistent_ThrowsA2AException()
    {
        var (client, _) = CreateTestHarness();

        var ex = await Assert.ThrowsAsync<A2AException>(
            () => client.GetTaskAsync("non-existent-id"));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task TaskState_SerializesAsKebabCase()
    {
        var (client, taskManager) = CreateTestHarness();

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            var task = await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
            await taskManager.UpdateStatusAsync(task.Id, TaskState.InputRequired, cancellationToken: ct);
            return (await taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id }, ct))!;
        };

        var response = await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "test" }]
            }
        });

        var task = Assert.IsType<AgentTask>(response);
        Assert.Equal(TaskState.InputRequired, task.Status.State);

        // Verify kebab-case serialization
        var json = JsonSerializer.Serialize(task.Status.State, A2AJsonUtilities.DefaultOptions);
        Assert.Equal("\"input-required\"", json);
    }

    [Fact]
    public async Task MethodNames_UseSlashDelimitedFormat()
    {
        string? capturedMethod = null;

        var (client, taskManager) = CreateTestHarness(onRequest: body =>
        {
            var doc = JsonDocument.Parse(body);
            capturedMethod = doc.RootElement.GetProperty("method").GetString();
        });

        taskManager.OnMessageReceived = async (sendParams, ct) =>
        {
            return await taskManager.CreateTaskAsync(sendParams.Message.ContextId, sendParams.Message.TaskId, ct);
        };

        await client.SendMessageAsync(new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "test" }]
            }
        });

        Assert.Equal("message/send", capturedMethod);
    }

    /// <summary>
    /// Loopback HTTP handler that routes JSON-RPC requests through a V0.3 TaskManager.
    /// </summary>
    private sealed class LoopbackHandler : HttpMessageHandler
    {
        private readonly TaskManager _taskManager;
        private readonly Action<string>? _onRequest;

        public LoopbackHandler(TaskManager taskManager, Action<string>? onRequest = null)
        {
            _taskManager = taskManager;
            _onRequest = onRequest;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _onRequest?.Invoke(body);

            var rpcRequest = JsonSerializer.Deserialize<JsonRpcRequest>(body, A2AJsonUtilities.DefaultOptions)
                ?? throw new InvalidOperationException("Failed to deserialize JSON-RPC request");

            JsonRpcResponse rpcResponse;
            try
            {
                rpcResponse = await RouteRequestAsync(rpcRequest, cancellationToken);
            }
            catch (A2AException ex)
            {
                rpcResponse = JsonRpcResponse.CreateJsonRpcErrorResponse(rpcRequest.Id, ex);
            }

            var responseJson = JsonSerializer.Serialize(rpcResponse, A2AJsonUtilities.DefaultOptions);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private async Task<JsonRpcResponse> RouteRequestAsync(
            JsonRpcRequest rpcRequest, CancellationToken ct)
        {
            var parameters = rpcRequest.Params;

            switch (rpcRequest.Method)
            {
                case A2AMethods.MessageSend:
                    var sendParams = parameters?.Deserialize<MessageSendParams>(A2AJsonUtilities.DefaultOptions)
                        ?? throw new A2AException("Invalid params", A2AErrorCode.InvalidParams);
                    var sendResult = await _taskManager.SendMessageAsync(sendParams, ct);
                    return JsonRpcResponse.CreateJsonRpcResponse(rpcRequest.Id, sendResult);

                case A2AMethods.TaskGet:
                    var getParams = parameters?.Deserialize<TaskQueryParams>(A2AJsonUtilities.DefaultOptions)
                        ?? throw new A2AException("Invalid params", A2AErrorCode.InvalidParams);
                    var task = await _taskManager.GetTaskAsync(getParams, ct)
                        ?? throw new A2AException("Task not found", A2AErrorCode.TaskNotFound);
                    return JsonRpcResponse.CreateJsonRpcResponse(rpcRequest.Id, task);

                case A2AMethods.TaskCancel:
                    var cancelParams = parameters?.Deserialize<TaskIdParams>(A2AJsonUtilities.DefaultOptions)
                        ?? throw new A2AException("Invalid params", A2AErrorCode.InvalidParams);
                    var canceled = await _taskManager.CancelTaskAsync(cancelParams, ct)
                        ?? throw new A2AException("Task not found", A2AErrorCode.TaskNotFound);
                    return JsonRpcResponse.CreateJsonRpcResponse(rpcRequest.Id, canceled);

                case A2AMethods.TaskPushNotificationConfigSet:
                    var setPushParams = parameters?.Deserialize<TaskPushNotificationConfig>(A2AJsonUtilities.DefaultOptions)
                        ?? throw new A2AException("Invalid params", A2AErrorCode.InvalidParams);
                    var setResult = await _taskManager.SetPushNotificationAsync(setPushParams, ct)
                        ?? throw new A2AException("Failed to set push notification config", A2AErrorCode.InternalError);
                    return JsonRpcResponse.CreateJsonRpcResponse(rpcRequest.Id, setResult);

                case A2AMethods.TaskPushNotificationConfigGet:
                    var getPushParams = parameters?.Deserialize<GetTaskPushNotificationConfigParams>(A2AJsonUtilities.DefaultOptions)
                        ?? throw new A2AException("Invalid params", A2AErrorCode.InvalidParams);
                    var getResult = await _taskManager.GetPushNotificationAsync(getPushParams, ct)
                        ?? throw new A2AException("Push notification config not found", A2AErrorCode.TaskNotFound);
                    return JsonRpcResponse.CreateJsonRpcResponse(rpcRequest.Id, getResult);

                default:
                    throw new A2AException($"Method not found: {rpcRequest.Method}", A2AErrorCode.MethodNotFound);
            }
        }
    }
}
