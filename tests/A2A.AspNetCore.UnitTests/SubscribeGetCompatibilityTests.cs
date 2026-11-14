using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace A2A.AspNetCore.Tests;

public class SubscribeGetCompatibilityTests
{
    [Fact]
    public async Task GetSubscribe_WithoutRawTarget_ReturnsBadRequestWithoutDispatch()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var handler = new Mock<IA2ARequestHandler>(MockBehavior.Strict);
        app.Use(async (context, next) =>
        {
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>()!.RawTarget = "";
            await next(context);
        });
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{address}/tasks/task-1%3Asubscribe");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        handler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("task-1%3Asubscribe", "task-1:subscribe")]
    [InlineData("task-1:other%3Asubscribe", "task-1:other:subscribe")]
    [InlineData("task-1%3ASubscribe", "task-1:Subscribe")]
    public async Task GetTask_EncodedColonInId_DoesNotSubscribe(string pathId, string expectedId)
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
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{address}/tasks/{pathId}?historyLength=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/a2a+json", response.Content.Headers.ContentType?.MediaType);
        handler.VerifyAll();
    }

    [Theory]
    [InlineData("GET", "task-1:subscribe")]
    [InlineData("GET", "task-1:s%75bscribe")]
    [InlineData("POST", "task-1:subscribe")]
    public async Task Subscribe_ReceivesEventsWithOriginalTaskId(string method, string pathId)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolver = A2AJsonUtilities.DefaultOptions.TypeInfoResolver);
        await using var app = builder.Build();
        var handler = new Mock<IA2ARequestHandler>();
        handler.Setup(h => h.SubscribeToTaskAsync(
                It.Is<SubscribeToTaskRequest>(r => r.Id == "task-1"),
                It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()))
            .Returns(OneEvent());
        app.MapHttpA2A(handler.Object);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{address}/tasks/{pathId}");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("task-1", body, StringComparison.Ordinal);
        handler.Verify(h => h.SubscribeToTaskAsync(
            It.Is<SubscribeToTaskRequest>(r => r.Id == "task-1"),
            It.IsAny<A2ARequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<StreamResponse> OneEvent()
    {
        await Task.Yield();
        yield return new StreamResponse
        {
            Task = new AgentTask { Id = "task-1", Status = new TaskStatus { State = TaskState.Working } }
        };
    }
}
