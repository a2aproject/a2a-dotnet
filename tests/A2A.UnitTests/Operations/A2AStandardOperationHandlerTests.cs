using System.Runtime.CompilerServices;
using Xunit.Sdk;

namespace A2A.UnitTests.Operations;

public class A2AStandardOperationHandlerTests
{
    [Fact]
    public async Task SendMessageHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new SendMessageRequest();
        var expected = new SendMessageResponse();
        using var cts = new CancellationTokenSource();
        requestHandler.OnSendMessageAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.SendMessage,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.SendMessageAsync));
    }

    [Fact]
    public async Task SendStreamingMessageHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new SendMessageRequest();
        var expected = new StreamResponse();
        using var cts = new CancellationTokenSource();
        requestHandler.OnSendStreamingMessageAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Yield(expected, actualToken);
        };

        var result = await ToListAsync(handlers.InvokeStreamingAsync(
            standard.SendStreamingMessage,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token),
            cts.Token);

        Assert.Same(expected, Assert.Single(result));
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.SendStreamingMessageAsync));
    }

    [Fact]
    public async Task GetTaskHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new GetTaskRequest { Id = "task-1" };
        var expected = new AgentTask { Id = "task-1" };
        using var cts = new CancellationTokenSource();
        requestHandler.OnGetTaskAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.GetTask,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.GetTaskAsync));
    }

    [Theory]
    [InlineData(-1)]
    public void GetTaskHandler_RejectsNegativeHistoryLength(int historyLength)
    {
        var (standard, operationCatalog, _, requestHandler) = CreateHandlers();

        var exception = Assert.Throws<A2AException>(() =>
            operationCatalog.Validate(
                standard.GetTask,
                new GetTaskRequest { Id = "task-1", HistoryLength = historyLength }));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        requestHandler.AssertNoCalls();
    }

    [Fact]
    public async Task ListTasksHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new ListTasksRequest { ContextId = "context-1" };
        var expected = new ListTasksResponse();
        using var cts = new CancellationTokenSource();
        requestHandler.OnListTasksAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.ListTasks,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.ListTasksAsync));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ListTasksHandler_RejectsPageSizeOutsideOneToHundred(int pageSize)
    {
        var (standard, operationCatalog, _, requestHandler) = CreateHandlers();

        var exception = Assert.Throws<A2AException>(() =>
            operationCatalog.Validate(
                standard.ListTasks,
                new ListTasksRequest { PageSize = pageSize }));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        requestHandler.AssertNoCalls();
    }

    [Theory]
    [InlineData(-1)]
    public void ListTasksHandler_RejectsNegativeHistoryLength(int historyLength)
    {
        var (standard, operationCatalog, _, requestHandler) = CreateHandlers();

        var exception = Assert.Throws<A2AException>(() =>
            operationCatalog.Validate(
                standard.ListTasks,
                new ListTasksRequest { HistoryLength = historyLength }));

        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        requestHandler.AssertNoCalls();
    }

    [Fact]
    public async Task CancelTaskHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new CancelTaskRequest { Id = "task-1" };
        var expected = new AgentTask { Id = "task-1" };
        using var cts = new CancellationTokenSource();
        requestHandler.OnCancelTaskAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.CancelTask,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.CancelTaskAsync));
    }

    [Fact]
    public async Task SubscribeToTaskHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new SubscribeToTaskRequest { Id = "task-1" };
        var expected = new StreamResponse();
        using var cts = new CancellationTokenSource();
        requestHandler.OnSubscribeToTaskAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Yield(expected, actualToken);
        };

        var result = await ToListAsync(handlers.InvokeStreamingAsync(
            standard.SubscribeToTask,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token),
            cts.Token);

        Assert.Same(expected, Assert.Single(result));
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.SubscribeToTaskAsync));
    }

    [Fact]
    public async Task CreateTaskPushNotificationConfigHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new TaskPushNotificationConfig { TaskId = "task-1" };
        var expected = new TaskPushNotificationConfig { TaskId = "task-1" };
        using var cts = new CancellationTokenSource();
        requestHandler.OnCreateTaskPushNotificationConfigAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.CreateTaskPushNotificationConfig,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.CreateTaskPushNotificationConfigAsync));
    }

    [Fact]
    public async Task GetTaskPushNotificationConfigHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new GetTaskPushNotificationConfigRequest { TaskId = "task-1", Id = "config-1" };
        var expected = new TaskPushNotificationConfig { TaskId = "task-1" };
        using var cts = new CancellationTokenSource();
        requestHandler.OnGetTaskPushNotificationConfigAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.GetTaskPushNotificationConfig,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.GetTaskPushNotificationConfigAsync));
    }

    [Fact]
    public async Task ListTaskPushNotificationConfigsHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new ListTaskPushNotificationConfigsRequest { TaskId = "task-1" };
        var expected = new ListTaskPushNotificationConfigsResponse();
        using var cts = new CancellationTokenSource();
        requestHandler.OnListTaskPushNotificationConfigsAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.ListTaskPushNotificationConfigs,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.ListTaskPushNotificationConfigsAsync));
    }

    [Fact]
    public async Task DeleteTaskPushNotificationConfigHandler_ReturnsEmptyResultAfterRequestHandlerCompletes()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new DeleteTaskPushNotificationConfigRequest { TaskId = "task-1", Id = "config-1" };
        var completed = false;
        using var cts = new CancellationTokenSource();
        requestHandler.OnDeleteTaskPushNotificationConfigAsync = async (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            await Task.Yield();
            completed = true;
        };

        var result = await handlers.InvokeAsync(
            standard.DeleteTaskPushNotificationConfig,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.True(completed);
        Assert.Same(A2AEmptyResult.Instance, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.DeleteTaskPushNotificationConfigAsync));
    }

    [Fact]
    public async Task GetExtendedAgentCardHandler_CallsRequestHandlerOnce()
    {
        var (standard, _, handlers, requestHandler) = CreateHandlers();
        var request = new GetExtendedAgentCardRequest();
        var expected = new AgentCard();
        using var cts = new CancellationTokenSource();
        requestHandler.OnGetExtendedAgentCardAsync = (actualRequest, actualToken) =>
        {
            Assert.Same(request, actualRequest);
            Assert.Equal(cts.Token, actualToken);
            return Task.FromResult(expected);
        };

        var result = await handlers.InvokeAsync(
            standard.GetExtendedAgentCard,
            new A2AOperationContext(requestHandler),
            request,
            cts.Token);

        Assert.Same(expected, result);
        requestHandler.AssertCalledOnce(nameof(IA2ARequestHandler.GetExtendedAgentCardAsync));
    }

    private static (
        A2AStandardOperations Standard,
        A2AOperationCatalog OperationCatalog,
        A2AOperationHandlerCatalog Handlers,
        StrictRequestHandler RequestHandler) CreateHandlers()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operationCatalog = operationBuilder.Build();
        var requestHandler = new StrictRequestHandler();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .AddStandardA2AHandlers(standard)
            .Build(operationCatalog);
        return (standard, operationCatalog, handlers, requestHandler);
    }

    private static async Task<List<T>> ToListAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            results.Add(item);
        }

        return results;
    }

    private static async IAsyncEnumerable<StreamResponse> Yield(
        StreamResponse response,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return response;
    }

    private sealed class StrictRequestHandler : IA2ARequestHandler
    {
        private readonly Dictionary<string, int> _callCounts = new(StringComparer.Ordinal);

        public Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>>?
            OnSendMessageAsync
        { get; set; }

        public Func<SendMessageRequest, CancellationToken, IAsyncEnumerable<StreamResponse>>?
            OnSendStreamingMessageAsync
        { get; set; }

        public Func<GetTaskRequest, CancellationToken, Task<AgentTask>>?
            OnGetTaskAsync
        { get; set; }

        public Func<ListTasksRequest, CancellationToken, Task<ListTasksResponse>>?
            OnListTasksAsync
        { get; set; }

        public Func<CancelTaskRequest, CancellationToken, Task<AgentTask>>?
            OnCancelTaskAsync
        { get; set; }

        public Func<SubscribeToTaskRequest, CancellationToken, IAsyncEnumerable<StreamResponse>>?
            OnSubscribeToTaskAsync
        { get; set; }

        public Func<TaskPushNotificationConfig, CancellationToken, Task<TaskPushNotificationConfig>>?
            OnCreateTaskPushNotificationConfigAsync
        { get; set; }

        public Func<GetTaskPushNotificationConfigRequest, CancellationToken, Task<TaskPushNotificationConfig>>?
            OnGetTaskPushNotificationConfigAsync
        { get; set; }

        public Func<ListTaskPushNotificationConfigsRequest, CancellationToken, Task<ListTaskPushNotificationConfigsResponse>>?
            OnListTaskPushNotificationConfigsAsync
        { get; set; }

        public Func<DeleteTaskPushNotificationConfigRequest, CancellationToken, Task>?
            OnDeleteTaskPushNotificationConfigAsync
        { get; set; }

        public Func<GetExtendedAgentCardRequest, CancellationToken, Task<AgentCard>>?
            OnGetExtendedAgentCardAsync
        { get; set; }

        public void AssertNoCalls()
            => Assert.All(_callCounts.Values, count => Assert.Equal(0, count));

        public void AssertCalledOnce(string methodName)
        {
            foreach (var pair in _callCounts)
            {
                if (pair.Key == methodName)
                {
                    Assert.Equal(1, pair.Value);
                }
                else
                {
                    Assert.Equal(0, pair.Value);
                }
            }
        }

        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(SendMessageAsync), OnSendMessageAsync, request, cancellationToken);

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(SendStreamingMessageAsync), OnSendStreamingMessageAsync, request, cancellationToken);

        public Task<AgentTask> GetTaskAsync(
            GetTaskRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(GetTaskAsync), OnGetTaskAsync, request, cancellationToken);

        public Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(ListTasksAsync), OnListTasksAsync, request, cancellationToken);

        public Task<AgentTask> CancelTaskAsync(
            CancelTaskRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(CancelTaskAsync), OnCancelTaskAsync, request, cancellationToken);

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
            SubscribeToTaskRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(nameof(SubscribeToTaskAsync), OnSubscribeToTaskAsync, request, cancellationToken);

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config,
            CancellationToken cancellationToken = default)
            => Invoke(
                nameof(CreateTaskPushNotificationConfigAsync),
                OnCreateTaskPushNotificationConfigAsync,
                config,
                cancellationToken);

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(
                nameof(GetTaskPushNotificationConfigAsync),
                OnGetTaskPushNotificationConfigAsync,
                request,
                cancellationToken);

        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(
                nameof(ListTaskPushNotificationConfigsAsync),
                OnListTaskPushNotificationConfigsAsync,
                request,
                cancellationToken);

        public Task DeleteTaskPushNotificationConfigAsync(
            DeleteTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(
                nameof(DeleteTaskPushNotificationConfigAsync),
                OnDeleteTaskPushNotificationConfigAsync,
                request,
                cancellationToken);

        public Task<AgentCard> GetExtendedAgentCardAsync(
            GetExtendedAgentCardRequest request,
            CancellationToken cancellationToken = default)
            => Invoke(
                nameof(GetExtendedAgentCardAsync),
                OnGetExtendedAgentCardAsync,
                request,
                cancellationToken);

        private TResult Invoke<TRequest, TResult>(
            string methodName,
            Func<TRequest, CancellationToken, TResult>? callback,
            TRequest request,
            CancellationToken cancellationToken)
        {
            Increment(methodName);
            return callback is not null
                ? callback(request, cancellationToken)
                : throw new XunitException($"Unexpected call to {methodName}.");
        }

        private void Increment(string methodName)
        {
            _callCounts.TryGetValue(methodName, out var count);
            _callCounts[methodName] = count + 1;
        }
    }
}
