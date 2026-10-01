using A2A.AspNetCore;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace A2A.UnitTests.Operations;

public partial class A2AOperationDiagnosticsTests
{
    [Fact]
    public async Task PublicStreamResult_DoesNotMutateAnAmbientOperation()
    {
        using var capture = new ActivityCapture();
        using var source = new ActivitySource("A2A");
        using var ambient = source.StartActivity("a2a.operation");
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var task = new AgentTask
        {
            Id = "task",
            ContextId = "context",
            Status = new TaskStatus { State = TaskState.Working },
        };

        await new JsonRpcStreamedResult(
            StandardEventsAsync(task),
            new JsonRpcId(42)).ExecuteAsync(context);

        Assert.NotEmpty(body.ToArray());
        Assert.NotNull(ambient);
        Assert.Null(ambient.GetTagItem("a2a.operation.outcome"));
    }
}
