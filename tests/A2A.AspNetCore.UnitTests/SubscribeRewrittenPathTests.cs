using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace A2A.AspNetCore.Tests;

public class SubscribeRewrittenPathTests
{
    [Theory]
    [InlineData("task-1%3Asubscribe")]
    [InlineData("task-1:subscribe")]
    public async Task RewrittenSubscriptionPath_RejectsAmbiguousOriginalRawTarget(string originalSegment)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var handler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        app.Use(async (context, next) =>
        {
            context.Request.Path = "/tasks/task-2:subscribe";
            await next(context);
        });
        app.UseRouting();
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{address}/tasks/{originalSegment}");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        handler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("task%2Fpart%3Asubscribe", "task%2Fpart:subscribe", false)]
    [InlineData("task%25part%3Asubscribe", "task%part:subscribe", false)]
    [InlineData("task%E4%B8%AD%3Asubscribe", "task中:subscribe", false)]
    [InlineData("task%3Fpart%3Asubscribe", "task?part:subscribe", false)]
    [InlineData("task%2Fpart%3Asubscribe", "task%2Fpart:subscribe", true)]
    [InlineData("task%25part%3Asubscribe", "task%part:subscribe", true)]
    [InlineData("task%E4%B8%AD%3Asubscribe", "task中:subscribe", true)]
    [InlineData("task%3Fpart%3Asubscribe", "task?part:subscribe", true)]
    public async Task PrefixChanges_KeepEncodedIdAndQuery(string segment, string expectedId, bool pathBase)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var handler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        handler.Setup(h => h.GetTaskAsync(
                It.Is<GetTaskRequest>(r => r.Id == expectedId && r.HistoryLength == 0),
                It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTask { Id = expectedId, Status = new TaskStatus { State = TaskState.Working } });
        if (pathBase)
        {
            app.UsePathBase("/outer");
        }
        else
        {
            app.Use(async (context, next) =>
            {
                context.Request.Path = context.Request.Path.Value!["/outer".Length..];
                await next(context);
            });
        }
        app.UseRouting();
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{address}/outer/tasks/{segment}?historyLength=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        handler.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefixChanges_KeepLiteralSubscribe(bool pathBase)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var handler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        handler.Setup(h => h.SubscribeToTaskAsync(
                It.Is<SubscribeToTaskRequest>(r => r.Id == "task-1"),
                It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .Returns(SubscriptionEvents());
        if (pathBase)
        {
            app.UsePathBase("/outer");
        }
        else
        {
            app.Use(async (context, next) =>
            {
                context.Request.Path = context.Request.Path.Value!["/outer".Length..];
                await next(context);
            });
        }
        app.UseRouting();
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{address}/outer/tasks/task-1:subscribe?unused=1");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        handler.VerifyAll();
    }

    private static async IAsyncEnumerable<StreamResponse> SubscriptionEvents()
    {
        await Task.Yield();
        yield return new StreamResponse { Task = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Working } } };
    }
}
