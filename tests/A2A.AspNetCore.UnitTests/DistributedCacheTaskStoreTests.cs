using A2A.AspNetCore.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace A2A.UnitTests.Server;

public class DistributedCacheTaskStoreTests
{
    [Fact]
    public async Task SetTaskAsync_And_GetTaskAsync_ShouldStoreAndRetrieveTask()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();
        var task = new AgentTask { Id = "task1", Status = new TaskStatus { State = TaskState.Submitted } };

        // Act
        await sut.SetTaskAsync(task);
        var result = await sut.GetTaskAsync("task1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task1", result!.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnNull_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        // Act
        var result = await sut.GetTaskAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldThrowArgumentException_WhenTaskIdIsNullOrEmpty()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        // Act
        var task = sut.GetTaskAsync(string.Empty);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => task);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldUpdateTaskStatus()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();
        var task = new AgentTask { Id = "task2", Status = new TaskStatus { State = TaskState.Submitted } };
        await sut.SetTaskAsync(task);
        var message = new Message { MessageId = "msg1", Role = Role.Agent, Parts = [Part.FromText("status update")] };

        // Act
        var updatedTask = await sut.UpdateStatusAsync("task2", new TaskStatus { State = TaskState.Working, Message = message });
        var retrievedTask = await sut.GetTaskAsync("task2");

        // Assert
        Assert.Equal(TaskState.Working, updatedTask.Status.State);
        Assert.Equal(TaskState.Working, retrievedTask!.Status.State);
        Assert.Equal("msg1", updatedTask.Status.Message!.MessageId);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        // Act & Assert
        var x = await Assert.ThrowsAsync<A2AException>(() => sut.UpdateStatusAsync("notfound", new TaskStatus { State = TaskState.Completed }));
        Assert.Equal(A2AErrorCode.TaskNotFound, x.ErrorCode);
    }

    [Fact]
    public async Task AppendHistoryAsync_ShouldAppendMessage()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();
        var task = new AgentTask { Id = "task3", Status = new TaskStatus { State = TaskState.Working } };
        await sut.SetTaskAsync(task);
        var message = new Message { MessageId = "msg1", Role = Role.User, Parts = [Part.FromText("hello")] };

        // Act
        var updatedTask = await sut.AppendHistoryAsync("task3", message);

        // Assert
        Assert.NotNull(updatedTask.History);
        Assert.Single(updatedTask.History);
        Assert.Equal("msg1", updatedTask.History[0].MessageId);
    }

    [Fact]
    public async Task AppendHistoryAsync_ShouldThrow_WhenTaskDoesNotExist()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();
        var message = new Message { MessageId = "msg1", Role = Role.User, Parts = [Part.FromText("hello")] };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2AException>(() => sut.AppendHistoryAsync("notfound", message));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task GetTaskAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.GetTaskAsync("test-id", cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.UpdateStatusAsync("test-id", new TaskStatus { State = TaskState.Working }, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SetTaskAsync_ShouldReturnCanceledTask_WhenCancellationTokenIsCanceled()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();
        var agentTask = new AgentTask { Id = "test-id", Status = new TaskStatus { State = TaskState.Submitted } };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = sut.SetTaskAsync(agentTask, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var sut = BuildDistributedCacheTaskStore();

        // Act - distributed cache does not support listing
        var result = await sut.ListTasksAsync(new ListTasksRequest());

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Tasks);
        Assert.Empty(result.Tasks);
    }

    static DistributedCacheTaskStore BuildDistributedCacheTaskStore()
    {
        var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new DistributedCacheTaskStore(memoryCache);
    }
}