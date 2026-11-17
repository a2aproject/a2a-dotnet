namespace A2A.Grpc.UnitTests;

using global::Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class ClientMessageRoleTests
{
    private sealed class RecordingAgent : IAgentHandler
    {
        public int Calls { get; private set; }

        public Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
        {
            Calls++;
            queue.Complete();
            return Task.CompletedTask;
        }

        public Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Theory]
    [InlineData(false, Role.Agent)]
    [InlineData(true, Role.Agent)]
    [InlineData(false, Role.Unspecified)]
    [InlineData(true, Role.Unspecified)]
    public async Task ClientMessage_InvalidRole_IsRejectedBeforeAgent(bool streaming, Role role)
    {
        var agent = new RecordingAgent();
        await using var server = new A2AServer(agent, new InMemoryTaskStore(), new ChannelEventNotifier(), NullLogger<A2AServer>.Instance);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IA2ARequestHandler>(server);
        await using var app = builder.Build();
        app.MapGrpcA2A();
        await app.StartAsync();
        var testServer = app.GetTestServer();
        using var channel = GrpcChannel.ForAddress(testServer.BaseAddress, new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });
        using var client = new A2AGrpcClient(channel);
        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = "role-probe", Role = role, Parts = [Part.FromText("hello")] },
        };

        var error = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            if (streaming)
            {
                await foreach (var _ in client.SendStreamingMessageAsync(request)) { }
            }
            else
            {
                await client.SendMessageAsync(request);
            }
        });

        Assert.Equal(A2AErrorCode.InvalidParams, error.ErrorCode);
        Assert.Equal(0, agent.Calls);
    }
}
