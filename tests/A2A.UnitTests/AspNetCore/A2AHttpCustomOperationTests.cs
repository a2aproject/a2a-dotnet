using A2A.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.AspNetCore;

public partial class A2AHttpCustomOperationTests
{
    [Fact]
    public async Task MapHttpA2A_StandardOperationUsesRequestSelectedHandler()
    {
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler(
                        request => Task.FromResult(
                            new AgentTask
                            {
                                Id = request.Id,
                                ContextId = "context-1",
                                Status = new TaskStatus
                                {
                                    State = TaskState.Working,
                                },
                            }))),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapHttpA2A(
            scopeFactory,
            new A2AOperationHandlerCatalogBuilder().Build(),
            new A2AHttpOperationBindingBuilder().Build());
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.RouteValues["id"] = "task-1";
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        responseBody.Position = 0;
        var task = await JsonSerializer.DeserializeAsync<AgentTask>(
            responseBody,
            A2AJsonUtilities.DefaultOptions);
        Assert.Equal("task-1", task!.Id);
    }

    [Fact]
    public async Task MapHttpA2A_CustomOperationBindsRouteAndBodyAndDisposesRequestScope()
    {
        var operation = new A2AOperation<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.resumeAuth"));
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map(
                operation,
                static (_, request, _) => ValueTask.FromResult(
                    new ResumeResult($"{request.TaskId}:{request.Token}")))
            .Build();
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                operation,
                static async (httpContext, cancellationToken) =>
                {
                    var body = await JsonSerializer.DeserializeAsync(
                        httpContext.Request.Body,
                        CustomHttpJsonContext.Default.ResumeBody,
                        cancellationToken);
                    return new ResumeRequest(
                        (string)httpContext.Request.RouteValues["id"]!,
                        body!.Token);
                },
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}:resumeAuth");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.RouteValues["id"] = "task-1";
        httpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes("""{"token":"opaque-token"}"""));
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(1, disposeCount);
        responseBody.Position = 0;
        var result = await JsonSerializer.DeserializeAsync(
            responseBody,
            CustomHttpJsonContext.Default.ResumeResult);
        Assert.Equal("task-1:opaque-token", result!.Value);
    }

    [Fact]
    public async Task MapHttpA2A_CustomOperationMapsA2AExceptionAndDisposesRequestScope()
    {
        var operation = new A2AOperation<ResumeRequest, ResumeResult>(
            new A2AOperationId("test.resumeAuth"));
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .Map<ResumeRequest, ResumeResult>(
                operation,
                static (_, _, _) => throw new A2AException(
                    "Invalid resume request.",
                    A2AErrorCode.InvalidParams))
            .Build();
        var bindings = new A2AHttpOperationBindingBuilder()
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:resumeAuth",
                operation,
                static (httpContext, _) => ValueTask.FromResult(
                    new ResumeRequest(
                        (string)httpContext.Request.RouteValues["id"]!,
                        "opaque-token")),
                CustomHttpJsonContext.Default.ResumeResult)
            .Build();
        var disposeCount = 0;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(
                    new A2AJsonRpcCustomOperationTests.TestRequestHandler()),
                () =>
                {
                    disposeCount++;
                    return ValueTask.CompletedTask;
                }));
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapHttpA2A(scopeFactory, handlers, bindings);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static endpoint =>
                endpoint.RoutePattern.RawText == "/tasks/{id}:resumeAuth");
        var httpContext = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.RouteValues["id"] = "task-1";
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await endpoint.RequestDelegate!(httpContext);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(1, disposeCount);
    }

    internal sealed record ResumeBody(
        [property: JsonPropertyName("token")] string Token);

    internal sealed record ResumeRequest(string TaskId, string Token);

    internal sealed record ResumeResult(
        [property: JsonPropertyName("value")] string Value);

    [JsonSerializable(typeof(ResumeBody))]
    [JsonSerializable(typeof(ResumeResult))]
    internal sealed partial class CustomHttpJsonContext : JsonSerializerContext;
}
