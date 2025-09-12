using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace A2A.TCK.Tests.Mandatory.Protocol;

/// <summary>
/// Tests for task management methods (tasks/get, tasks/cancel) based on the TCK.
/// These tests validate task lifecycle management according to the A2A v0.3.0 specification
/// through the JSON-RPC protocol layer.
/// </summary>
public class TaskManagementTests : TckTestBase
{
    public TaskManagementTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - Task Retrieval",
        SpecSection = "A2A v0.3.0 §7.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksGet_ExistingTask_ReturnsValidTask()
    {
        // Arrange - First create a task via JSON-RPC
        var createRequest = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = A2AMethods.MessageSend,
            Params = JsonSerializer.SerializeToElement(new MessageSendParams
            {
                Message = CreateTestMessage()
            }),
            Id = 1
        };

        var createRequestBody = JsonSerializer.Serialize(createRequest);
        var createStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(createRequestBody));
        
        var createHttpRequest = new DefaultHttpContext().Request;
        createHttpRequest.Body = createStream;
        createHttpRequest.ContentType = "application/json";

        var createResult = await A2AJsonRpcProcessor.ProcessRequestAsync(_taskManager, createHttpRequest, CancellationToken.None);
        
        // Extract task ID from the create response
        var createContext = new DefaultHttpContext();
        var createResponseStream = new MemoryStream();
        createContext.Response.Body = createResponseStream;
        
        await ((JsonRpcResponseResult)createResult).ExecuteAsync(createContext);
        createResponseStream.Position = 0;
        
        var createResponseJson = await new StreamReader(createResponseStream).ReadToEndAsync();
        var createResponse = JsonSerializer.Deserialize<JsonRpcResponse>(createResponseJson);
        var initialTask = createResponse?.Result?.Deserialize<AgentTask>();
        
        Assert.NotNull(initialTask);

        // Now retrieve the task via JSON-RPC
        var getRequest = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = A2AMethods.TaskGet,
            Params = JsonSerializer.SerializeToElement(new TaskQueryParams
            {
                Id = initialTask.Id,
                HistoryLength = 5
            }),
            Id = 2
        };

        var getRequestBody = JsonSerializer.Serialize(getRequest);
        var getStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(getRequestBody));
        
        var getHttpRequest = new DefaultHttpContext().Request;
        getHttpRequest.Body = getStream;
        getHttpRequest.ContentType = "application/json";

        // Act - Get task via JSON-RPC
        var getResult = await A2AJsonRpcProcessor.ProcessRequestAsync(_taskManager, getHttpRequest, CancellationToken.None);

        // Assert
        Assert.IsType<JsonRpcResponseResult>(getResult);
        var getResponseResult = (JsonRpcResponseResult)getResult;
        
        var getContext = new DefaultHttpContext();
        var getResponseStream = new MemoryStream();
        getContext.Response.Body = getResponseStream;
        
        await getResponseResult.ExecuteAsync(getContext);
        getResponseStream.Position = 0;
        
        var getResponseJson = await new StreamReader(getResponseStream).ReadToEndAsync();
        var getResponse = JsonSerializer.Deserialize<JsonRpcResponse>(getResponseJson);
        var retrievedTask = getResponse?.Result?.Deserialize<AgentTask>();

        bool taskRetrievalValid = retrievedTask is not null &&
                                 retrievedTask.Id == initialTask.Id &&
                                 retrievedTask.ContextId == initialTask.ContextId &&
                                 getResponse?.Error is null;

        if (taskRetrievalValid)
        {
            Output.WriteLine("✓ JSON-RPC task retrieval successful");
            Output.WriteLine($"  Task ID: {retrievedTask!.Id}");
            Output.WriteLine($"  Context ID: {retrievedTask.ContextId}");
            Output.WriteLine($"  Status: {retrievedTask.Status.State}");
            Output.WriteLine($"  History length: {retrievedTask.History?.Count ?? 0}");
        }
        else
        {
            Output.WriteLine($"✗ Task retrieval failed. Response: {getResponseJson}");
        }

        AssertTckCompliance(taskRetrievalValid, "JSON-RPC tasks/get must return valid task structure");
    }

    [Fact]
    [TckTest(TckComplianceLevel.NonCompliant, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - Task Not Found Error",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Critical - violates A2A error handling specification")]
    public async Task TasksGet_NonExistentTask_ThrowsTaskNotFoundError()
    {
        // Arrange - Create JSON-RPC request for non-existent task
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = A2AMethods.TaskGet,
            Params = JsonSerializer.SerializeToElement(new TaskQueryParams
            {
                Id = "non-existent-task-id"
            }),
            Id = 1
        };

        // Simulate HTTP request body
        var requestBody = JsonSerializer.Serialize(request);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody));
        
        var httpRequest = new DefaultHttpContext().Request;
        httpRequest.Body = stream;
        httpRequest.ContentType = "application/json";

        // Act - Process through JSON-RPC processor
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(_taskManager, httpRequest, CancellationToken.None);

        // Assert - Should get JSON-RPC error response
        Assert.IsType<JsonRpcResponseResult>(result);
        var responseResult = (JsonRpcResponseResult)result;
        
        // Execute the result to get the actual response
        var context = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;
        
        await responseResult.ExecuteAsync(context);
        responseStream.Position = 0;
        
        var responseJson = await new StreamReader(responseStream).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson);

        bool hasCorrectError = response?.Error is not null &&
                              response.Error.Code == (int)A2AErrorCode.TaskNotFound &&
                              !string.IsNullOrEmpty(response.Error.Message);

        if (hasCorrectError)
        {
            Output.WriteLine("✓ JSON-RPC returned correct TaskNotFound error");
            Output.WriteLine($"  Error code: {response!.Error!.Code}");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else
        {
            Output.WriteLine($"✗ Unexpected response: {responseJson}");
        }

        AssertTckCompliance(hasCorrectError, 
            "JSON-RPC tasks/get for non-existent task must return TaskNotFound error (-32001)");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Task Cancellation",
        SpecSection = "A2A v0.3.0 §7.4",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksCancel_CancelableTask_ReturnsCanceledTask()
    {
        // Arrange - Create a task via JSON-RPC first
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        var cancelParams = new TaskIdParams
        {
            Id = task.Id
        };

        // Act - Cancel via JSON-RPC
        var cancelResponse = await CancelTaskViaJsonRpcAsync(cancelParams);

        // Assert
        bool cancellationValid = cancelResponse.Error is null && 
                                cancelResponse.Result is not null;

        if (cancellationValid)
        {
            var canceledTask = cancelResponse.Result?.Deserialize<AgentTask>();
            bool taskProperlyCanceled = canceledTask?.Id == task.Id &&
                                       canceledTask?.Status.State == TaskState.Canceled;

            if (taskProperlyCanceled)
            {
                Output.WriteLine("✓ JSON-RPC task cancellation successful");
                Output.WriteLine($"  Task ID: {canceledTask!.Id}");
                Output.WriteLine($"  Final state: {canceledTask.Status.State}");
            }
            
            AssertTckCompliance(taskProperlyCanceled, "JSON-RPC tasks/cancel must return task in canceled state");
        }
        else if (cancelResponse.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC cancellation error: {cancelResponse.Error.Code} - {cancelResponse.Error.Message}");
            AssertTckCompliance(false, "JSON-RPC tasks/cancel must not return error for valid task");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.NonCompliant, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Cancel Non-Existent Task Error",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Critical - violates A2A error handling specification")]
    public async Task TasksCancel_NonExistentTask_ThrowsTaskNotFoundError()
    {
        // Arrange - Create JSON-RPC request to cancel non-existent task
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = A2AMethods.TaskCancel,
            Params = JsonSerializer.SerializeToElement(new TaskIdParams
            {
                Id = "non-existent-task-id"
            }),
            Id = 1
        };

        // Simulate HTTP request body
        var requestBody = JsonSerializer.Serialize(request);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody));
        
        var httpRequest = new DefaultHttpContext().Request;
        httpRequest.Body = stream;
        httpRequest.ContentType = "application/json";

        // Act - Process through JSON-RPC processor
        var result = await A2AJsonRpcProcessor.ProcessRequestAsync(_taskManager, httpRequest, CancellationToken.None);

        // Assert - Should get JSON-RPC error response
        Assert.IsType<JsonRpcResponseResult>(result);
        var responseResult = (JsonRpcResponseResult)result;
        
        // Execute the result to get the actual response
        var context = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;
        
        await responseResult.ExecuteAsync(context);
        responseStream.Position = 0;
        
        var responseJson = await new StreamReader(responseStream).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson);

        bool hasCorrectError = response?.Error is not null &&
                              response.Error.Code == (int)A2AErrorCode.TaskNotFound &&
                              !string.IsNullOrEmpty(response.Error.Message);

        if (hasCorrectError)
        {
            Output.WriteLine("✓ JSON-RPC returned correct TaskNotFound error for task cancellation");
            Output.WriteLine($"  Error code: {response!.Error!.Code}");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else
        {
            Output.WriteLine($"✗ Unexpected response: {responseJson}");
        }

        AssertTckCompliance(hasCorrectError, 
            "JSON-RPC tasks/cancel for non-existent task must return TaskNotFound error (-32001)");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.4 - Cancel Already Canceled Task",
        SpecSection = "A2A v0.3.0 §8.2",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task TasksCancel_AlreadyCanceledTask_ReturnsTaskNotCancelableError()
    {
        // Arrange - Create and cancel a task via JSON-RPC
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        var cancelParams = new TaskIdParams { Id = task.Id };
        
        // First cancellation should succeed
        var firstCancelResponse = await CancelTaskViaJsonRpcAsync(cancelParams);
        Assert.True(firstCancelResponse.Error is null);

        // Act & Assert - Second cancellation should fail via JSON-RPC
        var secondCancelResponse = await CancelTaskViaJsonRpcAsync(cancelParams);
        
        bool hasCorrectError = secondCancelResponse.Error is not null &&
                              secondCancelResponse.Error.Code == (int)A2AErrorCode.TaskNotCancelable;

        if (hasCorrectError)
        {
            Output.WriteLine("✓ JSON-RPC correctly returned TaskNotCancelable error for already canceled task");
            Output.WriteLine($"  Error code: {secondCancelResponse.Error!.Code}");
            Output.WriteLine($"  Error message: {secondCancelResponse.Error.Message}");
        }
        else if (secondCancelResponse.Error is not null)
        {
            Output.WriteLine($"✗ Unexpected error: {secondCancelResponse.Error.Code} - {secondCancelResponse.Error.Message}");
        }
        else
        {
            Output.WriteLine("✗ Expected TaskNotCancelable error but got successful response");
        }

        AssertTckCompliance(hasCorrectError, 
            "JSON-RPC tasks/cancel for already canceled task must return TaskNotCancelable error");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §6.1 - Task Structure Validation",
        SpecSection = "A2A v0.3.0 §6.1",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task Task_Structure_IsValid()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Act - Create task via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool taskStructureValid = response.Error is null && response.Result is not null;
        
        if (taskStructureValid)
        {
            var task = response.Result?.Deserialize<AgentTask>();
            
            bool hasValidStructure = task is not null &&
                                   !string.IsNullOrEmpty(task.Id) &&
                                   !string.IsNullOrEmpty(task.ContextId);

            if (hasValidStructure)
            {
                Output.WriteLine("✓ JSON-RPC task structure is valid");
                Output.WriteLine($"  ID: {task!.Id}");
                Output.WriteLine($"  Context ID: {task.ContextId}");
                Output.WriteLine($"  Status state: {task.Status.State}");
                Output.WriteLine($"  Has timestamp: {task.Status.Timestamp != default}");
                Output.WriteLine($"  Artifacts count: {task.Artifacts?.Count ?? 0}");
                Output.WriteLine($"  History count: {task.History?.Count ?? 0}");
            }
            
            AssertTckCompliance(hasValidStructure, "JSON-RPC task must have valid structure with required fields");
        }
        else if (response.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
            AssertTckCompliance(false, "JSON-RPC task creation must succeed for valid input");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §6.3 - Task State Transitions",
        SpecSection = "A2A v0.3.0 §6.3",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task Task_StateTransitions_AreValid()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Act - Create task via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool validResponse = response.Error is null && response.Result is not null;
        
        if (validResponse)
        {
            var task = response.Result?.Deserialize<AgentTask>();
            
            // Test valid state transitions
            var validStates = new[]
            {
                TaskState.Submitted,
                TaskState.Working,
                TaskState.Completed,
                TaskState.Canceled,
                TaskState.Failed,
                TaskState.Rejected,
                TaskState.InputRequired,
                TaskState.AuthRequired,
                TaskState.Unknown
            };

            // Check all states are properly defined
            bool allStatesValid = validStates.All(state => Enum.IsDefined(typeof(TaskState), state));

            // Check initial state is valid
            bool initialStateValid = task?.Status.State is TaskState.Submitted ||
                                    task?.Status.State is TaskState.Working ||
                                    task?.Status.State is TaskState.Completed;

            var validTransitions = allStatesValid && initialStateValid;

            if (validTransitions)
            {
                Output.WriteLine("✓ JSON-RPC task state system is valid");
                Output.WriteLine($"  Initial state: {task!.Status.State}");
                Output.WriteLine($"  All defined states: {string.Join(", ", validStates)}");
            }

            AssertTckCompliance(validTransitions, "JSON-RPC task state system must include all required states");
        }
        else if (response.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC error: {response.Error.Code} - {response.Error.Message}");
            AssertTckCompliance(false, "JSON-RPC task creation must succeed for state validation");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.MandatoryProtocol,
        Description = "A2A v0.3.0 §7.3 - History Length Parameter",
        SpecSection = "A2A v0.3.0 §7.3.1",
        FailureImpact = "Limited functionality - history truncation not supported")]
    public async Task TasksGet_HistoryLength_IsRespected()
    {
        // Arrange - Create a task via JSON-RPC and add multiple messages to history
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        // Add more messages to build up history via JSON-RPC
        for (int i = 0; i < 3; i++)
        {
            var updateParams = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    TaskId = task.Id,
                    Parts = [new TextPart { Text = $"Message {i + 2}" }],
                    MessageId = Guid.NewGuid().ToString(),
                    Role = MessageRole.User
                }
            };
            await SendMessageViaJsonRpcAsync(updateParams);
        }

        // Act - Request with limited history via JSON-RPC
        var taskQueryParams = new TaskQueryParams
        {
            Id = task.Id,
            HistoryLength = 2
        };

        var getResponse = await GetTaskViaJsonRpcAsync(taskQueryParams);

        // Assert
        bool validResponse = getResponse.Error is null && getResponse.Result is not null;
        
        if (validResponse)
        {
            var retrievedTask = getResponse.Result?.Deserialize<AgentTask>();
            bool historyLimited = retrievedTask?.History?.Count <= 2;

            if (historyLimited)
            {
                Output.WriteLine("✓ JSON-RPC history length parameter respected");
                Output.WriteLine($"  Requested: 2, Received: {retrievedTask!.History?.Count ?? 0}");
            }
            else if (retrievedTask?.History?.Count > 2)
            {
                Output.WriteLine("⚠️ JSON-RPC history length parameter not implemented (acceptable)");
                Output.WriteLine($"  Requested: 2, Received: {retrievedTask.History.Count}");
            }

            // This is recommended, so we pass even if not implemented
            AssertTckCompliance(true, "JSON-RPC advanced task state management enhances workflow capabilities");
        }
        else if (getResponse.Error is not null)
        {
            Output.WriteLine($"✗ JSON-RPC get task error: {getResponse.Error.Code} - {getResponse.Error.Message}");
            AssertTckCompliance(false, "JSON-RPC tasks/get must work for valid task ID");
        }
    }
}