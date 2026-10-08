namespace A2A.UnitTests.Server;

public sealed class PushNotificationStoreTests
{
    private readonly InMemoryPushNotificationStore _store = new();
    private readonly string _tenant = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Snapshots_IsolateInputReadsAndTasks()
    {
        var input = Config("first", "same");
        var saved = await _store.SaveAsync(input);
        input.Token = "changed";
        input.Authentication!.Credentials = "changed";
        saved.Configuration.Authentication!.Credentials = "also-changed";
        var read = await _store.GetAsync("first", "same");
        Assert.NotNull(read);
        Assert.Equal("secret", read.Configuration.Authentication!.Credentials);
        Assert.Equal("token", read.Configuration.Token);
        Assert.Equal(_tenant, read.Configuration.Tenant);
        await _store.SaveAsync(Config("second", "same"));
        await _store.DeleteAsync("first", "same");
        Assert.Null(await _store.GetAsync("first", "same"));
        Assert.NotNull(await _store.GetAsync("second", "same"));
    }

    [Fact]
    public async Task HostOwnedStoreInstances_KeepIdenticalIdsAndCursorsSeparate()
    {
        var otherScope = new InMemoryPushNotificationStore();
        foreach (var id in new[] { "one", "two" })
        {
            await _store.SaveAsync(Config("task", id));
            var other = Config("task", id);
            other.Authentication!.Credentials = "other-scope-secret";
            await otherScope.SaveAsync(other);
        }
        Assert.Equal("secret", (await _store.GetAsync("task", "one"))!.Configuration.Authentication!.Credentials);
        Assert.Equal("other-scope-secret", (await otherScope.GetAsync("task", "one"))!.Configuration.Authentication!.Credentials);
        var page = await _store.ListAsync(new() { TaskId = "task", PageSize = 1 });
        var exception = await Assert.ThrowsAsync<A2AException>(() =>
            otherScope.ListAsync(new() { TaskId = "task", PageSize = 1, PageToken = page.NextPageToken }));
        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
        await _store.DeleteAsync("task", "one");
        Assert.Null(await _store.GetAsync("task", "one"));
        Assert.NotNull(await otherScope.GetAsync("task", "one"));
    }

    [Fact]
    public async Task ConcurrentCreate_HasOneWinner()
    {
        var attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            try
            {
                await _store.SaveAsync(Config("task", "same"));
                return true;
            }
            catch (A2AException exception) when (exception.ErrorCode == A2AErrorCode.InvalidParams)
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, success => success);
        Assert.Single(await _store.GetAllAsync("task"));
    }

    [Fact]
    public async Task Delete_IsIdempotentAndGenerationConditional()
    {
        var old = await _store.SaveAsync(Config("task", "same"));
        await _store.DeleteAsync("task", "same", "wrong-version");
        Assert.NotNull(await _store.GetAsync("task", "same"));
        await _store.DeleteAsync("task", "same");
        await _store.DeleteAsync("task", "same");
        var current = await _store.SaveAsync(Config("task", "same"));
        Assert.NotEqual(old.Version, current.Version);
        await _store.DeleteAsync("task", "same", old.Version);
        Assert.Equal(current.Version, (await _store.GetAsync("task", "same"))!.Version);
        await _store.DeleteAsync("task", "same", current.Version);
        Assert.Empty(await _store.GetAllAsync("task"));
    }

    [Fact]
    public async Task Pagination_IsOrderedBoundedTaskBoundAndDetectsMutation()
    {
        for (int index = 104; index >= 0; index--)
        {
            await _store.SaveAsync(Config("task", index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
        }
        var first = await _store.ListAsync(new() { TaskId = "task" });
        Assert.Equal(50, first.Configs!.Count);
        Assert.Equal("000", first.Configs[0].Id);
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));
        var second = await _store.ListAsync(new() { TaskId = "task", PageToken = first.NextPageToken, PageSize = 500 });
        Assert.Equal(55, second.Configs!.Count);
        Assert.Equal("050", second.Configs[0].Id);
        Assert.Equal(string.Empty, second.NextPageToken);
        var capped = await _store.ListAsync(new() { TaskId = "task", PageSize = 500 });
        Assert.Equal(100, capped.Configs!.Count);
        first.Configs[0].Authentication!.Credentials = "redacted";
        Assert.Equal("secret", (await _store.GetAsync("task", "000"))!.Configuration.Authentication!.Credentials);

        await InvalidAsync(store => store.ListAsync(new() { TaskId = "other", PageToken = first.NextPageToken }));
        await InvalidAsync(store => store.ListAsync(new() { TaskId = "task", PageToken = first.NextPageToken + "x" }));
        await _store.DeleteAsync("task", "001");
        await InvalidAsync(store => store.ListAsync(new() { TaskId = "task", PageToken = first.NextPageToken }));
        var empty = await _store.ListAsync(new() { TaskId = "empty" });
        Assert.Empty(empty.Configs!);
        Assert.Equal(string.Empty, empty.NextPageToken);
    }

    [Theory]
    [InlineData(-1)]
    public async Task InvalidPageSize_IsRejected(int size)
    {
        await InvalidAsync(store => store.ListAsync(new() { TaskId = "task", PageSize = size }));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("AA==.AA==")]
    public async Task MalformedCursor_IsRejected(string token)
    {
        await InvalidAsync(store => store.ListAsync(new() { TaskId = "task", PageToken = token }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("id\n")]
    public async Task MissingOrInvalidConfigId_IsRejected(string? id)
    {
        var config = Config("task", "original");
        config.Id = id;
        await InvalidAsync(store => store.SaveAsync(config));
        await InvalidAsync(store => store.GetAsync("task", id!));
        await InvalidAsync(store => store.DeleteAsync("task", id!));
    }

    [Fact]
    public async Task AllOperations_HonorCancellationWithoutMutation()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SaveAsync(Config("task", "id"), canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.GetAsync("task", "id", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.GetAllAsync("task", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.ListAsync(new() { TaskId = "task" }, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.DeleteAsync("task", "id", cancellationToken: canceled.Token));
        Assert.Empty(await _store.GetAllAsync("task"));
    }

    private TaskPushNotificationConfig Config(string task, string id) => new()
    {
        TaskId = task,
        Id = id,
        Url = "https://callback.example/notify",
        Token = "token",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "secret" },
        Tenant = _tenant,
    };

    private async Task InvalidAsync(Func<InMemoryPushNotificationStore, Task> operation)
    {
        var exception = await Assert.ThrowsAsync<A2AException>(() => operation(_store));
        Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
    }
}
