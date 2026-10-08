using A2A;
using A2A.AspNetCore;
using A2A.Grpc;
using A2A.V0_3Compat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using V03 = A2A.V0_3;

namespace PushNotificationReceiver.Tests;

public sealed class PushNotificationIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private readonly string _credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentQueue<StreamResponse> _received = new();
    private readonly LocalReceiverUrlValidator _policy = new();
    private readonly string _contextId = Guid.NewGuid().ToString("N");
    private readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentBag<Socket> _connections = new();

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task RealCrud_LifecycleDeliveryRetentionAndExplicitDeletion(string binding)
    {
        await using var host = CreateHost(binding);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            await using var receiver = new WebhookReceiver(task.Id, _credential, _received.Enqueue).CreateApplication();
            await receiver.StartAsync();
            var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
            _policy.SetReceiver(callback);
            Assert.IsType<InMemoryPushNotificationStore>(host.Services.GetRequiredService<IPushNotificationStore>());
            Assert.IsType<HttpPushNotificationSender>(host.Services.GetRequiredService<IPushNotificationSender>());

            var created = await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "one", callback));
            Assert.Equal(_credential, created.Authentication!.Credentials);
            Assert.Equal("legacy-token", created.Token);
            var get = await client.GetTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = "one" });
            Assert.Null(get.Token);
            Assert.Null(get.Authentication!.Credentials);
            Assert.Equal("Bearer", get.Authentication.Scheme);
            await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "two", callback));
            await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "deleted", callback));
            await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = "deleted" });
            await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = "deleted" });
            var omittedSize = await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id });
            var zeroSize = await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id, PageSize = 0 });
            var omittedConfigs = omittedSize.Configs;
            var zeroConfigs = zeroSize.Configs;
            Assert.NotNull(omittedConfigs);
            Assert.NotNull(zeroConfigs);
            var omittedIds = omittedConfigs.Select(config => config.Id).ToArray();
            Assert.Collection(omittedIds,
                id => Assert.Equal("one", id),
                id => Assert.Equal("two", id));
            Assert.Equal(omittedIds, zeroConfigs.Select(config => config.Id));
            var negativeSize = await Assert.ThrowsAsync<A2AException>(() =>
                client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id, PageSize = -1 }));
            Assert.Equal(A2AErrorCode.InvalidParams, negativeSize.ErrorCode);
            var duplicate = await Assert.ThrowsAsync<A2AException>(() =>
                client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "one", callback)));
            Assert.Equal(A2AErrorCode.InvalidParams, duplicate.ErrorCode);
            var invalidId = await Assert.ThrowsAsync<A2AException>(() =>
                client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "bad\nid", callback)));
            Assert.Equal(A2AErrorCode.InvalidParams, invalidId.ErrorCode);
            var firstPage = await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id, PageSize = 1 });
            Assert.Single(firstPage.Configs!);
            Assert.False(string.IsNullOrEmpty(firstPage.NextPageToken));
            var secondPage = await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id, PageSize = 1, PageToken = firstPage.NextPageToken });
            Assert.Single(secondPage.Configs!);
            Assert.Equal(string.Empty, secondPage.NextPageToken);
            Assert.Null(firstPage.Configs![0].Token);

            var completed = await client.SendMessageAsync(Request("finish", task.Id));
            Assert.Equal(TaskState.Completed, completed.Task!.Status.State);
            await DrainAsync(host);
            Assert.Equal(6, _received.Count); // history message, artifact, completed status, each to two configs
            Assert.Equal(2, _received.Count(item => (item.StatusUpdate?.Status.State).GetValueOrDefault() == TaskState.Completed));
            Assert.Equal(2, _received.Count(item => item.ArtifactUpdate is not null));
            Assert.Equal(TaskState.Completed, (await client.GetTaskAsync(new() { Id = task.Id })).Status.State);
            Assert.Equal(2, (await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id })).Configs!.Count);
            await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = "one" });
            await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = "two" });
            Assert.Empty((await client.ListTaskPushNotificationConfigsAsync(new() { TaskId = task.Id })).Configs!);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task UnsafeConfigIds_AreRejectedBeforeStorageOrInlineExecution(string binding)
    {
        await using var host = CreateHost(binding);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            var callback = new Uri(new Uri(host.Urls.Single()), "/unused-callback");
            _policy.SetReceiver(callback);
            var store = host.Services.GetRequiredService<IPushNotificationStore>();
            var agent = Assert.IsType<IntegrationAgent>(host.Services.GetRequiredService<IAgentHandler>());
            int executions = 0;
            agent.BeforeExecute = _ => executions++;
            var invalidIds = new[]
            {
                "/", "segment/child", "\\", "segment\\child", ".", "..",
                "%", "%2f", "%2F", "segment%2Fchild", "%5c", "%2e", "%2e%2e", "%252f",
                "%00", "id%3Fquery", "id%23fragment",
            };
            foreach (var id in invalidIds)
            {
                var exception = await Assert.ThrowsAsync<A2AException>(() =>
                    client.CreateTaskPushNotificationConfigAsync(Config(task.Id, id, callback)));
                Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
                Assert.Null(await store.GetAsync(task.Id, id));

                var request = Request("start");
                request.Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = Config(task.Id, id, callback) };
                exception = await Assert.ThrowsAsync<A2AException>(() => client.SendMessageAsync(request));
                Assert.Equal(A2AErrorCode.InvalidParams, exception.ErrorCode);
                Assert.Equal(0, executions);
            }
            Assert.Empty(await store.GetAllAsync(task.Id));
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task PathSafeConfigIds_RoundTripAndInvalidateTheActualRegistration(string binding)
    {
        await using var host = CreateHost(binding);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            var callback = new Uri(new Uri(host.Urls.Single()), "/unused-callback");
            _policy.SetReceiver(callback);
            var store = host.Services.GetRequiredService<IPushNotificationStore>();
            var ids = new[]
            {
                "plain", "not-a-GUID_123-~", ".hidden", "id..part", "...", "trailing.",
                "space id", " leading", "trailing ", "café-日本-😀",
                "id:colon", "id?query", "id#fragment", "id[brackets]", "id@host",
                "id!bang", "id$dollar", "id&amp", "id'quote", "id(paren)", "id*star",
                "id+plus", "id,comma", "id;semicolon", "id=equals",
            };
            foreach (var id in ids)
            {
                Assert.Equal(id, (await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, id, callback))).Id);
                Assert.Equal(id, (await client.GetTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = id })).Id);
                var registration = await store.GetAsync(task.Id, id);
                Assert.NotNull(registration);
                await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = id });
                Assert.Null(await store.GetAsync(task.Id, id));
                Assert.True(registration.RemovalToken.IsCancellationRequested);
                await client.DeleteTaskPushNotificationConfigAsync(new() { TaskId = task.Id, Id = id });
            }
            Assert.Empty(await store.GetAllAsync(task.Id));
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task MixedHandlers_CustomLegacyDiscoveryKeepsItsWorkingCapability()
    {
        await using var host = CreateHost("rpc");
        await using var custom = new CustomLegacyHandler();
        var card = new AgentCard { Name = "Custom", Capabilities = new AgentCapabilities { PushNotifications = true } };
        host.MapA2AWithV03Compat(custom, "/custom");
        host.MapAgentCardGetWithV03Compat(() => Task.FromResult(card), "/custom-card");
        host.MapAgentCardGetWithV03Compat(() => Task.FromResult(card), "/custom-strict", blendedCard: false);
        host.MapAgentCardGetWithV03Compat(custom, () => Task.FromResult(card), "/custom-associated");
        host.MapAgentCardGetWithV03Compat(custom, () => Task.FromResult(card), "/custom-associated-strict", blendedCard: false);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var legacy = new V03.A2AClient(new Uri(new Uri(host.Urls.Single()), "/custom"), http);
        var set = await legacy.SetPushNotificationAsync(new V03.TaskPushNotificationConfig
        {
            TaskId = "custom-task",
            PushNotificationConfig = new V03.PushNotificationConfig { Id = "custom-config", Url = "https://owned.invalid/callback" },
        });
        Assert.Equal("custom-config", set.PushNotificationConfig.Id);
        foreach (var prefix in new[] { "/custom-card", "/custom-strict", "/custom-associated", "/custom-associated-strict",
            "/discovery", "/discovery-strict" })
        {
            foreach (var suffix in new[] { "", "/.well-known/agent-card.json" })
            {
                foreach (var version in new[] { "", "0.3", "1.0" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(host.Urls.Single()), prefix + suffix));
                    if (version.Length != 0)
                    {
                        request.Headers.Add("A2A-Version", version);
                    }
                    using var response = await http.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    bool expectedPush = prefix.StartsWith("/custom", StringComparison.Ordinal) ||
                        version == "1.0";
                    Assert.Equal(expectedPush, body.RootElement.GetProperty("capabilities").GetProperty("pushNotifications").GetBoolean());
                }
            }
        }
        Assert.True(card.Capabilities.PushNotifications.GetValueOrDefault());
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task CapabilityDisabled_IsAProtocolError(string binding)
    {
        await using var host = CreateHost(binding, enabled: false);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            var exception = await Assert.ThrowsAsync<A2AException>(() =>
                client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "disabled", new Uri("http://127.0.0.1:1/unused"))));
            Assert.Equal(A2AErrorCode.PushNotificationNotSupported, exception.ErrorCode);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("rpc", false)]
    [InlineData("rest", false)]
    [InlineData("grpc", false)]
    [InlineData("rpc", true)]
    [InlineData("rest", true)]
    [InlineData("grpc", true)]
    public async Task InlineConfig_DeliversThroughDefaultSenderForUnaryAndStreaming(string binding, bool streaming)
    {
        await using var host = CreateHost(binding);
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            await using var receiver = new WebhookReceiver(task.Id, _credential, _received.Enqueue).CreateApplication();
            await receiver.StartAsync();
            var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
            _policy.SetReceiver(callback);
            var request = Request("finish", task.Id);
            request.Configuration = new SendMessageConfiguration
            {
                TaskPushNotificationConfig = Config("body-id-is-not-authoritative", "inline", callback),
            };
            if (streaming)
            {
                await foreach (var response in client.SendStreamingMessageAsync(request))
                {
                    Assert.NotEqual(StreamResponseCase.None, response.PayloadCase);
                }
            }
            else
            {
                Assert.Equal(TaskState.Completed, (await client.SendMessageAsync(request)).Task!.Status.State);
            }
            await DrainAsync(host);
            Assert.Equal(3, _received.Count);
            Assert.Equal(TaskState.Completed, Assert.Single(_received, item => item.StatusUpdate is not null).StatusUpdate!.Status.State);
            Assert.Single(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(task.Id));
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task RestRouteTaskId_OverridesBodyAndDoesNotStoreUnderSpoofedTask()
    {
        await using var host = CreateHost("rest");
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, "rest", http);
        var task = (await client.SendMessageAsync(Request("start"))).Task!;
        await using var receiver = new WebhookReceiver(task.Id, _credential, _received.Enqueue).CreateApplication();
        await receiver.StartAsync();
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
        _policy.SetReceiver(callback);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(host.Urls.Single()), $"/rest/tasks/{task.Id}/pushNotificationConfigs"))
        {
            Content = new StringContent(JsonSerializer.Serialize(Config("spoofed-task", "route", callback),
                A2AJsonUtilities.DefaultOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var configs = host.Services.GetRequiredService<IPushNotificationStore>();
        Assert.NotNull(await configs.GetAsync(task.Id, "route"));
        Assert.Null(await configs.GetAsync("spoofed-task", "route"));
    }

    [Fact]
    public async Task StockV1Server_DoesNotAdvertiseOrAcceptIncompleteV03Push()
    {
        await using var host = CreateHost("rpc");
        await host.StartAsync();
        using var http = CreateHttpClient();
        var current = Client(host, "rpc", http);
        var legacy = new V03.A2AClient(new Uri(new Uri(host.Urls.Single()), "/compat"), http);
        var task = (await current.SendMessageAsync(Request("start"))).Task!;
        var rejected = await Assert.ThrowsAsync<V03.A2AException>(() => legacy.SetPushNotificationAsync(new V03.TaskPushNotificationConfig
        {
            TaskId = task.Id,
            PushNotificationConfig = new V03.PushNotificationConfig
            {
                Id = "legacy",
                Url = "https://unused.example/notify",
                Token = "legacy-token",
                Authentication = new V03.PushNotificationAuthenticationInfo { Schemes = ["Bearer"], Credentials = _credential },
            },
        }));
        Assert.Equal(V03.A2AErrorCode.PushNotificationNotSupported, rejected.ErrorCode);
        foreach (var method in new[] { "tasks/pushNotificationConfig/list", "tasks/pushNotificationConfig/delete" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(host.Urls.Single()), "/compat"))
            {
                Content = new StringContent($$$"""{"jsonrpc":"2.0","id":37,"method":"{{{method}}}","params":{"id":"{{{task.Id}}}"}}""",
                    Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("A2A-Version", "0.3");
            using var response = await http.SendAsync(request);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(37, body.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(-32003, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }
        foreach (var prefix in new[] { "/discovery", "/discovery-strict" })
        {
            foreach (var suffix in new[] { "", "/.well-known/agent-card.json" })
            {
                foreach (var version in new[] { "", "0.3", "1.0" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(host.Urls.Single()), prefix + suffix));
                    if (version.Length != 0)
                    {
                        request.Headers.Add("A2A-Version", version);
                    }
                    using var response = await http.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.Equal(version == "1.0", body.RootElement.GetProperty("capabilities").GetProperty("pushNotifications").GetBoolean());
                }
            }
        }
        Assert.Empty(_received);
        Assert.Empty(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(task.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrRedirectedWebhook_DoesNotChangeTaskOrDeleteRegistration(bool redirect)
    {
        await using var host = CreateHost("rpc");
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, "rpc", http);
        var task = (await client.SendMessageAsync(Request("start"))).Task!;
        int attempts = 0;
        string? destination = null;
        await using var receiver = new WebhookReceiver(task.Id, _credential, _received.Enqueue).CreateApplication();
        receiver.MapPost("/reject", () =>
        {
            Interlocked.Increment(ref attempts);
            return redirect ? Results.Redirect(destination!, preserveMethod: true) : Results.StatusCode(503);
        });
        await receiver.StartAsync();
        destination = new Uri(new Uri(receiver.Urls.Single()), "/notifications").AbsoluteUri;
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/reject");
        _policy.SetReceiver(callback);
        await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "failure", callback));
        Assert.Equal(TaskState.Completed, (await client.SendMessageAsync(Request("finish", task.Id))).Task!.Status.State);
        await DrainAsync(host);
        Assert.Equal(redirect ? 3 : 6, attempts); // three persisted events, one/two attempts each
        Assert.Empty(_received); // redirects were never followed
        Assert.Equal(TaskState.Completed, (await client.GetTaskAsync(new() { Id = task.Id })).Status.State);
        Assert.Single(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(task.Id));
    }

    [Fact]
    public async Task Cancel_EmitsTerminalNotificationAndRetainsRegistration()
    {
        await using var host = CreateHost("rpc");
        await host.StartAsync();
        using var http = CreateHttpClient();
        var client = Client(host, "rpc", http);
        var task = (await client.SendMessageAsync(Request("start"))).Task!;
        await using var receiver = new WebhookReceiver(task.Id, _credential, _received.Enqueue).CreateApplication();
        await receiver.StartAsync();
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
        _policy.SetReceiver(callback);
        await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "cancel", callback));
        Assert.Equal(TaskState.Canceled, (await client.CancelTaskAsync(new() { Id = task.Id })).Status.State);
        await DrainAsync(host);
        Assert.Equal(TaskState.Canceled, Assert.Single(_received).StatusUpdate!.Status.State);
        Assert.Single(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(task.Id));
    }

    [Theory]
    [InlineData("rpc", false)]
    [InlineData("rest", false)]
    [InlineData("grpc", false)]
    [InlineData("rpc", true)]
    [InlineData("rest", true)]
    [InlineData("grpc", true)]
    public async Task NewInlineMessage_PreservesMessageWithoutCreatingHiddenTaskOrRegistration(string binding, bool streaming)
    {
        await using var host = CreateHost(binding);
        var handler = Assert.IsType<IntegrationAgent>(host.Services.GetRequiredService<IAgentHandler>());
        string? expectedTaskId = null;
        handler.BeforeExecute = context => expectedTaskId = context.TaskId;
        await host.StartAsync();
        await using var receiver = new WebhookReceiver("unused", _credential, _received.Enqueue).CreateApplication();
        MapAuthenticatedReceiver(receiver, "/automatic", () => expectedTaskId);
        await receiver.StartAsync();
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/automatic");
        _policy.SetReceiver(callback);
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var request = Request("message-only");
            request.Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = Config("ignored", "automatic", callback) };
            Message? message;
            if (streaming)
            {
                var events = new List<StreamResponse>();
                await foreach (var response in client.SendStreamingMessageAsync(request))
                {
                    events.Add(response);
                }
                var result = Assert.Single(events);
                Assert.Null(result.Task);
                message = result.Message;
            }
            else
            {
                var response = await client.SendMessageAsync(request);
                Assert.Null(response.Task);
                message = response.Message;
            }
            Assert.NotNull(message);
            await DrainAsync(host);
            Assert.Empty(_received);
            Assert.Null(await host.Services.GetRequiredService<ITaskStore>().GetTaskAsync(expectedTaskId!));
            Assert.Empty(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(expectedTaskId!));
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task DisconnectedStream_StillPersistsAndPushesBackgroundCompletion(string binding)
    {
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trace = new ConcurrentQueue<string>();
        bool watchDisconnect = false;
        await using var host = CreateHost(binding);
        host.Use(async (context, next) =>
        {
            trace.Enqueue($"begin {context.Request.Path}, watching={watchDisconnect}, cancellable={context.RequestAborted.CanBeCanceled}");
            using var registration = context.RequestAborted.Register(() =>
            {
                trace.Enqueue($"abort {context.Request.Path}, watching={watchDisconnect}");
                if (watchDisconnect)
                {
                    disconnected.TrySetResult();
                }
            });
            try
            {
                await next(context);
            }
            finally
            {
                bool aborted = context.RequestAborted.IsCancellationRequested;
                trace.Enqueue($"end {context.Request.Path}, aborted={aborted}");
                // The endpoint's cancellation continuation can finish before this
                // registration runs; disposal must not lose an already observed abort.
                if (watchDisconnect &&
                    aborted)
                {
                    disconnected.TrySetResult();
                }
            }
        });
        await host.StartAsync();
        using var http = CreateHttpClient(resetConnections: true);
        var client = Client(host, binding, http);
        var handler = Assert.IsType<IntegrationAgent>(host.Services.GetRequiredService<IAgentHandler>());
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var task = (await client.SendMessageAsync(Request("start"))).Task!;
            await using var receiver = new WebhookReceiver(task.Id, _credential, notification =>
            {
                _received.Enqueue(notification);
                if ((notification.StatusUpdate?.Status.State).GetValueOrDefault() == TaskState.Completed)
                {
                    delivered.TrySetResult();
                }
            }).CreateApplication();
            await receiver.StartAsync();
            var callback = new Uri(new Uri(receiver.Urls.Single()), "/notifications");
            _policy.SetReceiver(callback);
            await client.CreateTaskPushNotificationConfigAsync(Config(task.Id, "disconnect", callback));
            handler.FinishGate = finish;
            watchDisconnect = true;
            await foreach (var response in client.SendStreamingMessageAsync(Request("finish", task.Id)))
            {
                if (response.Task is not null)
                {
                    Assert.Equal(task.Id, response.Task.Id);
                    continue;
                }
                Assert.NotNull(response.ArtifactUpdate);
                // Reset while the stream is still live. Iterator disposal may otherwise
                // gracefully drain/half-close HTTP/1.1 before the explicit reset gets here.
                int resetCount = 0;
                foreach (var connection in _connections)
                {
                    if (!connection.SafeHandle.IsClosed)
                    {
                        connection.Dispose();
                        resetCount++;
                    }
                }
                Assert.True(resetCount > 0, "The subscriber must own a live connection to reset.");
                trace.Enqueue($"reset {resetCount}, abort observed={disconnected.Task.IsCompleted}");
                break;
            }
            // Close the connection owned by this subscriber, not just its event iterator.
            (client as IDisposable)?.Dispose();
            http.Dispose();
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            finish.TrySetResult();
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DrainAsync(host);
            Assert.Equal(TaskState.Completed, (await host.Services.GetRequiredService<ITaskStore>().GetTaskAsync(task.Id))!.Status.State);
            Assert.Single(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(task.Id));
        }
        finally
        {
            finish.TrySetResult();
            (client as IDisposable)?.Dispose();
            output.WriteLine(string.Join(Environment.NewLine, trace));
        }
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("rest")]
    [InlineData("grpc")]
    public async Task ReturnImmediately_ContinuesSdkDeliveryInBackground(string binding)
    {
        await using var host = CreateHost(binding);
        var handler = Assert.IsType<IntegrationAgent>(host.Services.GetRequiredService<IAgentHandler>());
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? expectedTaskId = null;
        handler.BeforeExecute = context => expectedTaskId = context.TaskId;
        handler.FinishGate = finish;
        await host.StartAsync();
        await using var receiver = new WebhookReceiver("unused", _credential, _received.Enqueue).CreateApplication();
        MapAuthenticatedReceiver(receiver, "/background", () => expectedTaskId, delivered);
        await receiver.StartAsync();
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/background");
        _policy.SetReceiver(callback);
        using var http = CreateHttpClient();
        var client = Client(host, binding, http);
        try
        {
            var request = Request("finish-new");
            request.Configuration = new SendMessageConfiguration
            {
                ReturnImmediately = true,
                TaskPushNotificationConfig = Config("ignored", "background", callback),
            };
            var result = await client.SendMessageAsync(request);
            Assert.NotNull(result.Task);
            Assert.NotEqual(TaskState.Completed, result.Task.Status.State);
            finish.TrySetResult();
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DrainAsync(host);
            Assert.Equal(4, _received.Count);
            Assert.Equal(TaskState.Completed, (await client.GetTaskAsync(new() { Id = result.Task.Id })).Status.State);
            Assert.Single(await host.Services.GetRequiredService<IPushNotificationStore>().GetAllAsync(result.Task.Id));
        }
        finally
        {
            finish.TrySetResult();
            (client as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task DefaultTransport_IsolatesSameHostCredentialsCookiesAndFactoryLogs()
    {
        var observed = new ConcurrentQueue<(string Authorization, string Cookie, string MediaType)>();
        await using var receiver = new WebhookReceiver("task", _credential, _received.Enqueue).CreateApplication();
        receiver.MapPost("/private-path-marker", (HttpContext context) =>
        {
            observed.Enqueue((context.Request.Headers.Authorization.ToString(),
                context.Request.Headers.Cookie.ToString(), context.Request.ContentType ?? string.Empty));
            context.Response.Headers.Append("Set-Cookie", "private-cookie-marker=value; Path=/");
            return Results.NoContent();
        });
        await receiver.StartAsync();
        var callback = new Uri(new Uri(receiver.Urls.Single()), "/private-path-marker?private-query-marker=value");
        _policy.SetReceiver(callback);
        using var capture = new CaptureProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(capture).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<IPushNotificationUrlValidator>(_policy);
        services.AddA2AAgent<IntegrationAgent>(new AgentCard
        {
            Capabilities = new AgentCapabilities { PushNotifications = true },
        });
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IPushNotificationStore>();
        var sender = Assert.IsType<HttpPushNotificationSender>(provider.GetRequiredService<IPushNotificationSender>());
        var secondCredential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var first = Config("task", "first", callback);
        var second = Config("task", "second", callback);
        second.Authentication!.Credentials = secondCredential;
        var notification = new StreamResponse
        {
            StatusUpdate = new TaskStatusUpdateEvent
            {
                TaskId = "task",
                ContextId = _contextId,
                Status = new A2A.TaskStatus
                {
                    State = TaskState.Working,
                    Message = new Message { MessageId = "private-body-marker", Parts = [Part.FromText("private-body-marker")] },
                },
            },
        };
        await sender.EnqueueAsync(await store.SaveAsync(first), notification);
        await sender.EnqueueAsync(await store.SaveAsync(second), notification);
        await sender.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        var requests = observed.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal("Bearer " + _credential, requests[0].Authorization);
        Assert.Equal("Bearer " + secondCredential, requests[1].Authorization);
        Assert.All(requests, request => Assert.True(string.IsNullOrEmpty(request.Cookie)));
        Assert.All(requests, request => Assert.Equal("application/json", request.MediaType));

        var logs = string.Join('\n', capture.Messages);
        Assert.Contains("succeeded", logs, StringComparison.Ordinal);
        foreach (var marker in new[] { _credential, secondCredential, "legacy-token", "private-path-marker",
            "private-query-marker", "private-body-marker", "private-cookie-marker" })
        {
            Assert.DoesNotContain(marker, logs, StringComparison.Ordinal);
        }
    }

    private void MapAuthenticatedReceiver(WebApplication receiver, string path, Func<string?> expectedTask, TaskCompletionSource? completed = null)
    {
        receiver.MapPost(path, async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            bool authenticated = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes("Bearer " + _credential), Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString()));
            if (!authenticated)
            {
                return Results.Unauthorized();
            }
            var notification = await JsonSerializer.DeserializeAsync<StreamResponse>(request.Body, A2AJsonUtilities.DefaultOptions, cancellationToken);
            var taskId = notification?.Task?.Id ?? notification?.StatusUpdate?.TaskId ??
                notification?.ArtifactUpdate?.TaskId ?? notification?.Message?.TaskId;
            if (expectedTask() is not { } expected ||
                taskId != expected ||
                notification is null)
            {
                return Results.BadRequest();
            }
            _received.Enqueue(notification);
            if ((notification.StatusUpdate?.Status.State).GetValueOrDefault() == TaskState.Completed)
            {
                completed?.TrySetResult();
            }
            return Results.NoContent();
        });
    }

    private WebApplication CreateHost(string binding, bool enabled = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listener => listener.Protocols = binding == "grpc" ? HttpProtocols.Http2 : HttpProtocols.Http1));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IPushNotificationUrlValidator>(_policy);
        builder.Services.AddSingleton(new PushNotificationDeliveryOptions { MaximumAttempts = 2, InitialRetryDelay = TimeSpan.Zero });
        builder.Services.AddA2AAgent<IntegrationAgent>(new AgentCard
        {
            Name = "Loopback integration",
            Capabilities = new AgentCapabilities { PushNotifications = enabled, Streaming = true },
        });
        builder.Services.AddA2AGrpc();
        var app = builder.Build();
        var server = app.Services.GetRequiredService<IA2ARequestHandler>();
        app.MapA2A("/rpc");
        app.MapHttpA2A(server, "/rest");
        app.MapA2AWithV03Compat(server, "/compat");
        app.MapAgentCardGetWithV03Compat(server, () => Task.FromResult(app.Services.GetRequiredService<AgentCard>()), "/discovery");
        app.MapAgentCardGetWithV03Compat(server, () => Task.FromResult(app.Services.GetRequiredService<AgentCard>()), "/discovery-strict", blendedCard: false);
        app.MapGrpcA2A();
        return app;
    }

    private HttpClient CreateHttpClient(bool resetConnections = false)
    {
        if (resetConnections)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    if (!IPAddress.TryParse(context.DnsEndPoint.Host, out var address) ||
                        !IPAddress.IsLoopback(address))
                    {
                        throw new InvalidOperationException("Disconnection tests connect only to owned loopback listeners.");
                    }
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                    {
                        LingerState = new LingerOption(true, 0),
                    };
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                        _connections.Add(socket);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
            return new HttpClient(handler) { Timeout = _httpTimeout };
        }
        return new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = _httpTimeout,
        };
    }

    private IA2AClient Client(WebApplication host, string binding, HttpClient http)
    {
        // The configured policy belongs to this host, never to a global bypass.
        Assert.Same(_policy, host.Services.GetRequiredService<IPushNotificationUrlValidator>());
        var address = new Uri(host.Urls.Single());
        return binding switch
        {
            "rpc" => new A2AClient(new Uri(address, "/rpc"), http),
            "rest" => new A2AHttpJsonClient(new Uri(address, "/rest"), http),
            "grpc" => new A2AGrpcClient(address, http),
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };
    }

    private SendMessageRequest Request(string text, string? taskId = null) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), TaskId = taskId, ContextId = _contextId, Role = Role.User, Parts = [Part.FromText(text)] },
    };

    private TaskPushNotificationConfig Config(string taskId, string id, Uri callback) => new()
    {
        TaskId = taskId,
        Id = id,
        Url = callback.AbsoluteUri,
        Token = "legacy-token",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = _credential },
    };

    private async Task DrainAsync(WebApplication host)
    {
        Assert.Same(_policy, host.Services.GetRequiredService<IPushNotificationUrlValidator>());
        await ((HttpPushNotificationSender)host.Services.GetRequiredService<IPushNotificationSender>())
            .StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Messages);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<string> messages) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue($"{category}: {formatter(state, exception)}");
    }

    private sealed class CustomLegacyHandler : A2AServer
    {
        internal CustomLegacyHandler() : base(new IntegrationAgent(), new InMemoryTaskStore(),
            new ChannelEventNotifier(), NullLogger<A2AServer>.Instance)
        {
        }

        public override Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config, CancellationToken cancellationToken = default) => Task.FromResult(config);
    }

    public sealed class IntegrationAgent : IAgentHandler
    {
        public Action<RequestContext>? BeforeExecute { get; set; }
        public TaskCompletionSource? FinishGate { get; set; }

        public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        {
            BeforeExecute?.Invoke(context);
            if (context.UserText == "message-only")
            {
                await eventQueue.EnqueueMessageAsync(new Message
                {
                    MessageId = "direct-reply",
                    Role = Role.Agent,
                    ContextId = context.ContextId,
                    Parts = [Part.FromText("Direct reply")],
                }, cancellationToken);
                return;
            }
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            if (context.Task is null)
            {
                await updater.SubmitAsync(cancellationToken: cancellationToken);
                if (context.UserText != "finish-new")
                {
                    await updater.RequireInputAsync(new Message
                    {
                        MessageId = Guid.NewGuid().ToString("N"),
                        Role = Role.Agent,
                        ContextId = context.ContextId,
                        Parts = [Part.FromText("Ready for continuation.")],
                    }, cancellationToken: cancellationToken);
                    return;
                }
                await updater.StartWorkAsync(cancellationToken: cancellationToken);
            }
            await updater.AddArtifactAsync([Part.FromText("SDK-delivered result")], artifactId: "result", cancellationToken: cancellationToken);
            if (FinishGate is not null)
            {
                await FinishGate.Task.WaitAsync(cancellationToken);
            }
            await updater.CompleteAsync(cancellationToken: cancellationToken);
        }
        public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
            new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken).AsTask();
    }
}
