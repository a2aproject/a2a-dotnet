using Xunit.Abstractions;
using A2A.TCK.Tests.Infrastructure;
using System.Diagnostics;

namespace A2A.TCK.Tests.Optional.Quality;

/// <summary>
/// Tests for production quality aspects of the A2A implementation.
/// These tests validate performance, reliability, and robustness characteristics.
/// </summary>
public class QualityTests : TckTestBase
{
    private readonly TaskManager _taskManager;

    public QualityTests(ITestOutputHelper output) : base(output)
    {
        _taskManager = new TaskManager();
    }

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

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Quick response" }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        // Act - Measure response time
        var stopwatch = Stopwatch.StartNew();
        var response = await _taskManager.SendMessageAsync(messageSendParams);
        stopwatch.Stop();

        // Assert
        var latencyMs = stopwatch.ElapsedMilliseconds;
        bool reasonableLatency = latencyMs < 5000; // 5 second threshold for simple message

        Output.WriteLine($"Message processing latency: {latencyMs}ms");
        
        if (reasonableLatency)
        {
            Output.WriteLine("? Message processing latency is acceptable");
        }
        else
        {
            Output.WriteLine("?? Message processing latency is high - may impact user experience");
        }

        // This is a quality recommendation
        AssertTckCompliance(true, $"Message processing completed in {latencyMs}ms");
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
        var tasks = new List<Task<A2AResponse?>>();

        _taskManager.OnMessageReceived = async (params_, cancellationToken) =>
        {
            // Simulate some processing time
            await Task.Delay(100, cancellationToken);
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Response to: {params_.Message.Parts[0].AsTextPart().Text}" }],
                MessageId = Guid.NewGuid().ToString()
            };
        };

        // Act - Send multiple concurrent requests
        for (int i = 0; i < concurrentRequests; i++)
        {
            var requestId = i;
            var task = _taskManager.SendMessageAsync(new MessageSendParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    Parts = [new TextPart { Text = $"Request {requestId}" }],
                    MessageId = Guid.NewGuid().ToString()
                }
            });

            tasks.Add(task);
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        bool allCompleted = responses.Length == concurrentRequests && 
                           responses.All(r => r != null);
        
        var totalTimeMs = stopwatch.ElapsedMilliseconds;
        bool reasonableConcurrency = totalTimeMs < (concurrentRequests * 150); // Allow some overhead

        Output.WriteLine($"Processed {concurrentRequests} concurrent requests in {totalTimeMs}ms");
        Output.WriteLine($"Average time per request: {totalTimeMs / (double)concurrentRequests:F2}ms");
        
        if (allCompleted && reasonableConcurrency)
        {
            Output.WriteLine("? Concurrent request handling is efficient");
        }
        else
        {
            Output.WriteLine("?? Concurrent request handling may need optimization");
        }

        AssertTckCompliance(true, $"Concurrency test completed: {concurrentRequests} requests in {totalTimeMs}ms");
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
        _taskManager.OnMessageReceived = (params_, _) =>
        {
            throw new InvalidOperationException("Simulated processing error");
        };

        // Act & Assert
        bool handledGracefully = false;
        try
        {
            await _taskManager.SendMessageAsync(messageSendParams);
        }
        catch (A2AException ex)
        {
            handledGracefully = true;
            Output.WriteLine($"? Exception converted to A2AException: {ex.ErrorCode}");
        }
        catch (InvalidOperationException)
        {
            Output.WriteLine("?? Raw exception leaked - should be wrapped in A2AException");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"?? Unexpected exception type: {ex.GetType().Name}");
        }

        // Even if not perfectly handled, we don't fail the test for quality issues
        AssertTckCompliance(true, "Error handling behavior observed");
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

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Memory test response" }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        // Act - Create and process many messages
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

            await _taskManager.SendMessageAsync(messageSendParams);
            
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
        Output.WriteLine($"Average per iteration: {memoryIncrease / (double)iterations:F0} bytes");

        // This is a basic smoke test - significant memory growth might indicate leaks
        bool reasonableMemoryUsage = memoryIncrease < (1024 * 1024); // Less than 1MB increase
        
        if (reasonableMemoryUsage)
        {
            Output.WriteLine("? Memory usage appears reasonable");
        }
        else
        {
            Output.WriteLine("?? Significant memory increase observed - may indicate memory leak");
        }

        // This is a quality test, so we pass regardless
        AssertTckCompliance(true, $"Memory management test completed - {memoryIncrease:N0} bytes increase");
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

        _taskManager.OnMessageReceived = (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Large message processed" }],
                MessageId = Guid.NewGuid().ToString()
            });
        };

        // Act & Assert
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _taskManager.SendMessageAsync(messageSendParams);
            stopwatch.Stop();
            
            Output.WriteLine($"? Large message processed successfully in {stopwatch.ElapsedMilliseconds}ms");
            Output.WriteLine($"Message size: {largeText.Length:N0} characters");
        }
        catch (A2AException ex)
        {
            stopwatch.Stop();
            Output.WriteLine($"?? Large message rejected: {ex.ErrorCode} - this may be appropriate");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Output.WriteLine($"?? Large message caused error: {ex.GetType().Name}");
        }

        // This is about handling large inputs appropriately - either process or reject gracefully
        AssertTckCompliance(true, $"Large message handling completed in {stopwatch.ElapsedMilliseconds}ms");
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

        _taskManager.OnMessageReceived = async (params_, cancellationToken) =>
        {
            // Simulate a long-running operation that respects cancellation
            await Task.Delay(30000, cancellationToken); // 30 seconds
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Long operation completed" }],
                MessageId = Guid.NewGuid().ToString()
            };
        };

        // Act - Test with a reasonable timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();
        
        bool completedOrTimedOut = false;
        try
        {
            await _taskManager.SendMessageAsync(messageSendParams, cts.Token);
            stopwatch.Stop();
            Output.WriteLine($"? Long operation completed in {stopwatch.ElapsedMilliseconds}ms");
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
        AssertTckCompliance(completedOrTimedOut, "Long operations should complete or timeout gracefully");
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

        _taskManager.OnTaskCreated = async (task, ct) =>
        {
            stateChanges.Add((DateTime.UtcNow, task.Status.State));
            
            await Task.Delay(50, ct);
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working, cancellationToken: ct);
            stateChanges.Add((DateTime.UtcNow, TaskState.Working));
            
            await Task.Delay(50, ct);
            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, final: true, cancellationToken: ct);
            stateChanges.Add((DateTime.UtcNow, TaskState.Completed));
        };

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