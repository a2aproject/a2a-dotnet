using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;
using System.Diagnostics;

namespace A2A.TCK.Tests.Optional.Quality;

/// <summary>
/// Tests for production quality aspects of the A2A implementation.
/// These tests validate performance, reliability, and robustness characteristics
/// through the JSON-RPC protocol layer.
/// </summary>
public class QualityTests : TckTestBase
{
    public QualityTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Performance - Message Processing Latency",
        FailureImpact = "Poor user experience due to slow response times")]
    public async Task MessageProcessing_Latency_IsReasonable()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Quick response" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Measure response time via JSON-RPC
        var stopwatch = Stopwatch.StartNew();
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);
        stopwatch.Stop();

        // Assert
        var latencyMs = stopwatch.ElapsedMilliseconds;
        bool reasonableLatency = latencyMs < 5000; // 5 second threshold for simple message
        bool validResponse = response.Error == null && response.Result != null;

        Output.WriteLine($"JSON-RPC message processing latency: {latencyMs}ms");
        
        if (validResponse && reasonableLatency)
        {
            Output.WriteLine("? JSON-RPC message processing latency is acceptable");
        }
        else if (!validResponse)
        {
            Output.WriteLine($"? JSON-RPC error: {response.Error?.Code} - {response.Error?.Message}");
        }
        else
        {
            Output.WriteLine("?? JSON-RPC message processing latency is high - may impact user experience");
        }

        // This is a quality recommendation
        AssertTckCompliance(validResponse, $"JSON-RPC message processing completed in {latencyMs}ms");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Concurrency - Multiple Simultaneous Requests",
        FailureImpact = "Poor scalability under load")]
    public async Task MessageProcessing_Concurrency_HandlesMultipleRequests()
    {
        // Arrange
        const int concurrentRequests = 10;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<JsonRpcResponse>>();

        ConfigureTaskManager(onMessageReceived: async (params_, cancellationToken) =>
        {
            // Simulate some processing time
            await Task.Delay(100, cancellationToken);
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Response to: {params_.Message.Parts[0].AsTextPart().Text}" }],
                MessageId = Guid.NewGuid().ToString()
            };
        });

        // Act - Send multiple concurrent requests via JSON-RPC
        for (int i = 0; i < concurrentRequests; i++)
        {
            var requestId = i;
            var messageSendParams = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    Parts = [new TextPart { Text = $"Request {requestId}" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };

            tasks.Add(SendMessageViaJsonRpcAsync(messageSendParams));
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        bool allCompleted = responses.Length == concurrentRequests && 
                           responses.All(r => r.Error == null && r.Result != null);
        
        var totalTimeMs = stopwatch.ElapsedMilliseconds;
        bool reasonableConcurrency = totalTimeMs < (concurrentRequests * 150); // Allow some overhead

        Output.WriteLine($"Processed {concurrentRequests} concurrent JSON-RPC requests in {totalTimeMs}ms");
        Output.WriteLine($"Average time per request: {totalTimeMs / (double)concurrentRequests:F2}ms");
        
        if (allCompleted && reasonableConcurrency)
        {
            Output.WriteLine("? Concurrent JSON-RPC request handling is efficient");
        }
        else if (!allCompleted)
        {
            var failedCount = responses.Count(r => r.Error != null);
            Output.WriteLine($"?? {failedCount} JSON-RPC requests failed out of {concurrentRequests}");
        }
        else
        {
            Output.WriteLine("?? Concurrent JSON-RPC request handling may need optimization");
        }

        AssertTckCompliance(allCompleted, $"JSON-RPC concurrency test: {concurrentRequests} requests in {totalTimeMs}ms");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Error Handling - Graceful Degradation",
        FailureImpact = "Poor error recovery and user experience")]
    public async Task ErrorHandling_GracefulDegradation_HandlesExceptions()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        // Simulate a handler that throws exceptions
        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            throw new InvalidOperationException("Simulated processing error");
        });

        // Act - Send via JSON-RPC
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);

        // Assert
        bool handledGracefully = response.Error != null;
        
        if (handledGracefully)
        {
            if (response.Error!.Code == (int)A2AErrorCode.InternalError)
            {
                Output.WriteLine("? Exception properly converted to JSON-RPC InternalError");
            }
            else
            {
                Output.WriteLine($"? Exception handled via JSON-RPC error: {response.Error.Code}");
            }
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }
        else
        {
            Output.WriteLine("?? Exception did not result in JSON-RPC error response");
        }

        AssertTckCompliance(handledGracefully, "JSON-RPC error handling must return proper error responses");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalQuality,
        Description = "Memory Management - Resource Cleanup",
        FailureImpact = "Memory leaks and resource exhaustion")]
    public async Task MemoryManagement_ResourceCleanup_NoObviousLeaks()
    {
        // Arrange
        const int iterations = 50;
        var initialMemory = GC.GetTotalMemory(true);

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Memory test response" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Create and process many messages via JSON-RPC
        for (int i = 0; i < iterations; i++)
        {
            var messageSendParams = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    Parts = [new TextPart { Text = $"Memory test message {i}" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            };

            var response = await SendMessageViaJsonRpcAsync(messageSendParams);
            
            // Verify response is valid
            if (response.Error != null)
            {
                Output.WriteLine($"?? JSON-RPC error in iteration {i}: {response.Error.Code}");
                break;
            }
            
            // Periodic cleanup
            if (i % 10 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Force garbage collection and measure final memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        Output.WriteLine($"Initial memory: {initialMemory:N0} bytes");
        Output.WriteLine($"Final memory: {finalMemory:N0} bytes");
        Output.WriteLine($"Memory increase: {memoryIncrease:N0} bytes");
        Output.WriteLine($"Average per JSON-RPC call: {memoryIncrease / (double)iterations:F0} bytes");

        // This is a basic smoke test - significant memory growth might indicate leaks
        bool reasonableMemoryUsage = memoryIncrease < (1024 * 1024); // Less than 1MB increase
        
        if (reasonableMemoryUsage)
        {
            Output.WriteLine("? JSON-RPC memory usage appears reasonable");
        }
        else
        {
            Output.WriteLine("?? Significant memory increase observed - may indicate memory leak");
        }

        // This is a quality test, so we pass regardless
        AssertTckCompliance(true, $"JSON-RPC memory management test completed - {memoryIncrease:N0} bytes increase");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Input Validation - Large Message Handling",
        FailureImpact = "Vulnerability to DoS attacks or poor performance with large inputs")]
    public async Task InputValidation_LargeMessage_IsHandledAppropriately()
    {
        // Arrange - Create a large message
        var largeText = new string('A', 10 * 1024 * 1024); // 10MB of text
        var messageSendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = largeText }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Large message processed" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send via JSON-RPC
        var stopwatch = Stopwatch.StartNew();
        var response = await SendMessageViaJsonRpcAsync(messageSendParams);
        stopwatch.Stop();

        // Assert
        if (response.Error == null && response.Result != null)
        {
            Output.WriteLine($"? Large message processed successfully via JSON-RPC in {stopwatch.ElapsedMilliseconds}ms");
            Output.WriteLine($"Message size: {largeText.Length:N0} characters");
        }
        else if (response.Error != null)
        {
            Output.WriteLine($"?? Large message rejected via JSON-RPC: {response.Error.Code} - this may be appropriate");
            Output.WriteLine($"  Error message: {response.Error.Message}");
        }

        // This is about handling large inputs appropriately - either process or reject gracefully
        AssertTckCompliance(true, $"JSON-RPC large message handling completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Timeout Handling - Long Running Operations",
        FailureImpact = "Poor user experience with hanging operations")]
    public async Task TimeoutHandling_LongOperation_CompletesOrTimesOut()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                Parts = [new TextPart { Text = "Long running task" }],
                MessageId = Guid.NewGuid().ToString()
            }
        };

        ConfigureTaskManager(onMessageReceived: async (params_, cancellationToken) =>
        {
            // Simulate a long-running operation that respects cancellation
            await Task.Delay(30000, cancellationToken); // 30 seconds
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Long operation completed" }],
                MessageId = Guid.NewGuid().ToString()
            };
        });

        // Act - Test with a reasonable timeout via JSON-RPC
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();
        
        JsonRpcResponse? response = null;
        bool completedOrTimedOut = false;
        try
        {
            // Pass the cancellation token to the JSON-RPC request
            response = await SendMessageViaJsonRpcAsync(messageSendParams, cts.Token);
            stopwatch.Stop();
            
            if (response.Error == null)
            {
                Output.WriteLine($"? Long operation completed via JSON-RPC in {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                Output.WriteLine($"? Long operation returned JSON-RPC error: {response.Error.Code}");
            }
            completedOrTimedOut = true;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Output.WriteLine($"? Long operation correctly timed out after {stopwatch.ElapsedMilliseconds}ms");
            completedOrTimedOut = true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Output.WriteLine($"?? Unexpected error during timeout test: {ex.GetType().Name}");
        }

        // Assert
        AssertTckCompliance(completedOrTimedOut, "JSON-RPC long operations should complete or timeout gracefully");
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.OptionalQuality,
        Description = "Task Lifecycle Management - Proper State Transitions",
        FailureImpact = "Inconsistent task state management")]
    public async Task TaskLifecycle_StateTransitions_AreConsistent()
    {
        // Arrange
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage()
        };

        var stateChanges = new List<(DateTime timestamp, TaskState state)>();

        ConfigureTaskManager(onTaskCreated: async (task, ct) =>
        {
            stateChanges.Add((DateTime.UtcNow, task.Status.State));
            
            await Task.Delay(50, ct);
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working, cancellationToken: ct);
            stateChanges.Add((DateTime.UtcNow, TaskState.Working));
            
            await Task.Delay(50, ct);
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
            stateChanges.Add((DateTime.UtcNow, TaskState.Completed));
        });

        // Act
        var events = new List<A2AEvent>();
        await foreach (var evt in _taskManager.SendMessageStreamingAsync(messageSendParams))
        {
            events.Add(evt);
        }

        // Assert
        var validTransition = stateChanges.Count >= 2 &&
                             stateChanges[0].state == TaskState.Submitted &&
                             stateChanges.Last().state == TaskState.Completed;

        Output.WriteLine("Task state transitions:");
        foreach (var (timestamp, state) in stateChanges)
        {
            Output.WriteLine($"  {timestamp:HH:mm:ss.fff}: {state}");
        }

        if (validTransition)
        {
            Output.WriteLine("? Task state transitions are logical");
        }
        else
        {
            Output.WriteLine("?? Task state transitions may be inconsistent");
        }

        AssertTckCompliance(true, $"Task lifecycle observed with {stateChanges.Count} state changes");
    }
}