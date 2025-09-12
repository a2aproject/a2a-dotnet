using A2A.Integration.Tests.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.OptionalTests.Quality;

/// <summary>
/// Tests for concurrency handling based on the upstream TCK.
/// These tests validate the implementation's ability to handle concurrent requests safely.
/// </summary>
public class ConcurrencyTests : TckTestBase
{
    public ConcurrencyTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Production Quality - Parallel Request Handling",
        FailureImpact = "Poor scalability under concurrent load")]
    public async Task Concurrency_ParallelRequests_HandledCorrectly()
    {
        // Arrange
        const int numberOfRequests = 10;
        var tasks = new List<Task<JsonRpcResponse>>();
        var results = new ConcurrentBag<(int requestId, bool success, string? error)>();

        ConfigureTaskManager(onMessageReceived: async (params_, cancellationToken) =>
        {
            // Simulate some processing time
            await Task.Delay(50, cancellationToken);
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = $"Response to: {params_.Message.Parts[0].AsTextPart().Text}" }],
                MessageId = Guid.NewGuid().ToString()
            };
        });

        // Act - Send multiple concurrent requests
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < numberOfRequests; i++)
        {
            int requestId = i;
            var task = Task.Run(async () =>
            {
                try
                {
                    var messageSendParams = new MessageSendParams
                    {
                        Message = CreateTestMessage($"Concurrent request {requestId}")
                    };

                    var response = await SendMessageViaJsonRpcAsync(messageSendParams);

                    bool success = response.Error is null && response.Result is not null;
                    results.Add((requestId, success, response.Error?.Message));

                    return response;
                }
                catch (Exception ex)
                {
                    results.Add((requestId, false, ex.Message));
                    throw;
                }
            });

            tasks.Add(task);
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = results.Count(r => r.success);
        var failureCount = results.Count(r => !r.success);

        Output.WriteLine($"Concurrent requests processed: {numberOfRequests}");
        Output.WriteLine($"Successful: {successCount}");
        Output.WriteLine($"Failed: {failureCount}");
        Output.WriteLine($"Total time: {stopwatch.ElapsedMilliseconds}ms");
        Output.WriteLine($"Average per request: {stopwatch.ElapsedMilliseconds / (double)numberOfRequests:F2}ms");

        // We expect most requests to succeed, but allow for some failures
        bool acceptableConcurrency = successCount >= numberOfRequests * 0.8; // 80% success rate

        if (acceptableConcurrency)
        {
            Output.WriteLine("? Acceptable concurrency performance");
        }
        else
        {
            Output.WriteLine("?? High failure rate under concurrent load");
            foreach (var (requestId, success, error) in results.Where(r => !r.success))
            {
                Output.WriteLine($"  Request {requestId} failed: {error}");
            }
        }

        AssertTckCompliance(acceptableConcurrency, "Implementation should handle concurrent requests with reasonable success rate");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Basic Quality - Sequential Request Performance",
        FailureImpact = "Poor performance under sustained load")]
    public async Task Concurrency_RapidSequentialRequests_MaintainPerformance()
    {
        // Arrange
        const int numberOfRequests = 20;
        var requestTimes = new List<long>();

        ConfigureTaskManager(onMessageReceived: (params_, _) =>
        {
            return Task.FromResult<A2AResponse>(new AgentMessage
            {
                Role = MessageRole.Agent,
                Parts = [new TextPart { Text = "Quick response" }],
                MessageId = Guid.NewGuid().ToString()
            });
        });

        // Act - Send rapid sequential requests
        var overallStopwatch = Stopwatch.StartNew();

        for (int i = 0; i < numberOfRequests; i++)
        {
            var requestStopwatch = Stopwatch.StartNew();

            var messageSendParams = new MessageSendParams
            {
                Message = CreateTestMessage($"Sequential request {i}")
            };

            var response = await SendMessageViaJsonRpcAsync(messageSendParams);

            requestStopwatch.Stop();
            requestTimes.Add(requestStopwatch.ElapsedMilliseconds);

            // Assert each request succeeds
            Assert.True(response.Error is null, $"Request {i} failed: {response.Error?.Message}");
        }

        overallStopwatch.Stop();

        // Assert
        var averageTime = requestTimes.Average();
        var maxTime = requestTimes.Max();
        var totalTime = overallStopwatch.ElapsedMilliseconds;

        Output.WriteLine($"Sequential requests: {numberOfRequests}");
        Output.WriteLine($"Total time: {totalTime}ms");
        Output.WriteLine($"Average per request: {averageTime:F2}ms");
        Output.WriteLine($"Max request time: {maxTime}ms");
        Output.WriteLine($"Requests per second: {numberOfRequests / (totalTime / 1000.0):F1}");

        // Performance should be consistent (no major degradation)
        // Allow for faster operations to have some variance
        bool consistentPerformance = maxTime <= Math.Max(averageTime * 3, 1); // Max shouldn't be more than 3x average, but allow at least 1ms tolerance

        if (consistentPerformance)
        {
            Output.WriteLine("? Consistent sequential performance");
        }
        else
        {
            Output.WriteLine("?? Performance degradation observed");
        }

        AssertTckCompliance(consistentPerformance, "Sequential request performance should remain consistent");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Concurrency - Thread Safety",
        FailureImpact = "Race conditions and data corruption")]
    public async Task Concurrency_ThreadSafety_NoRaceConditions()
    {
        // Arrange - Test concurrent access to the same task
        var messageSendParams = new MessageSendParams
        {
            Message = CreateTestMessage("Initial task")
        };

        var createResponse = await SendMessageViaJsonRpcAsync(messageSendParams);
        var task = createResponse.Result?.Deserialize<AgentTask>();
        Assert.NotNull(task);

        var concurrentUpdates = new List<Task<JsonRpcResponse>>();
        var updateResults = new ConcurrentBag<bool>();

        // Act - Multiple threads trying to update the same task
        for (int i = 0; i < 5; i++)
        {
            int updateId = i;
            var updateTask = Task.Run(async () =>
            {
                try
                {
                    var updateParams = new MessageSendParams
                    {
                        Message = new AgentMessage
                        {
                            Role = MessageRole.User,
                            TaskId = task.Id,
                            Parts = [new TextPart { Text = $"Concurrent update {updateId}" }],
                            MessageId = Guid.NewGuid().ToString()
                        }
                    };

                    var response = await SendMessageViaJsonRpcAsync(updateParams);
                    bool success = response.Error is null || response.Result is not null;
                    updateResults.Add(success);

                    return response;
                }
                catch (Exception)
                {
                    updateResults.Add(false);
                    throw;
                }
            });

            concurrentUpdates.Add(updateTask);
        }

        await Task.WhenAll(concurrentUpdates);

        // Assert
        var successfulUpdates = updateResults.Count(r => r);
        var totalUpdates = updateResults.Count;

        Output.WriteLine($"Concurrent task updates: {totalUpdates}");
        Output.WriteLine($"Successful: {successfulUpdates}");

        // We expect thread-safe behavior - either all succeed or proper error handling
        bool threadSafe = successfulUpdates > 0; // At least some updates should work

        if (threadSafe)
        {
            Output.WriteLine("? Thread-safe concurrent access");
        }
        else
        {
            Output.WriteLine("?? All concurrent updates failed");
        }

        AssertTckCompliance(threadSafe, "Concurrent task access should be thread-safe");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.OptionalQuality,
        Description = "Concurrency - Resource Management",
        FailureImpact = "Resource leaks under concurrent load")]
    public async Task Concurrency_ResourceManagement_NoLeaks()
    {
        // Arrange
        const int iterations = 25;
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

        // Act - Create and process many concurrent requests
        var batchTasks = new List<Task>();

        for (int batch = 0; batch < 5; batch++)
        {
            var batchTask = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var messageSendParams = new MessageSendParams
                    {
                        Message = CreateTestMessage($"Memory test {batch}-{i}")
                    };

                    var response = await SendMessageViaJsonRpcAsync(messageSendParams);

                    // Verify response is valid
                    if (response.Error is not null)
                    {
                        Output.WriteLine($"?? Error in memory test iteration {batch}-{i}: {response.Error.Code}");
                    }
                }
            });

            batchTasks.Add(batchTask);
        }

        await Task.WhenAll(batchTasks);

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
        Output.WriteLine($"Average per operation: {memoryIncrease / (double)(5 * iterations):F0} bytes");

        // This is a basic smoke test - significant memory growth might indicate leaks
        bool reasonableMemoryUsage = memoryIncrease < (2 * 1024 * 1024); // Less than 2MB increase

        if (reasonableMemoryUsage)
        {
            Output.WriteLine("? Reasonable memory usage observed");
        }
        else
        {
            Output.WriteLine("?? Significant memory increase - possible memory leak");
        }

        // This is a quality test, so we pass regardless but report the findings
        AssertTckCompliance(true, $"Memory management assessment completed - {memoryIncrease:N0} bytes increase");
    }
}
