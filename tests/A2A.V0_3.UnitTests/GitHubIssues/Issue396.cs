using System.Text.Json;

namespace A2A.V0_3.UnitTests.GitHubIssues
{
    public sealed class Issue396
    {
        [Fact]
        public void Issue_396_Passes()
        {
            var json = """
            {
              "jsonrpc": "2.0",
              "id": "1",
              "result": {
                "id": "63b2861e-1234-4a1a-9a3a-000000000001",
                "contextId": "2c727c41-1234-4a1a-9a3a-000000000002",
                "kind": "task",
                "status": {
                  "state": "completed",
                  "message": {
                    "role": "agent",
                    "parts": [ { "kind": "text", "text": "Skill completed." } ]
                  },
                  "timestamp": "2026-05-17T15:59:43.401Z"
                }
              }
            }
            """;

            var deserializedResponseObj = JsonSerializer.Deserialize<JsonRpcResponse>(json, A2AJsonUtilities.DefaultOptions);
            Assert.NotNull(deserializedResponseObj);

            var task = deserializedResponseObj.Result.Deserialize<AgentTask>(A2AJsonUtilities.DefaultOptions);
            Assert.NotNull(task);

            Assert.Equal("63b2861e-1234-4a1a-9a3a-000000000001", task.Id);
            Assert.NotNull(task.Status.Message);

            // kind and messageId were both omitted from status.message; both fall back to
            // their defaults instead of failing deserialization.
            Assert.Equal("message", task.Status.Message!.Kind);
            Assert.False(string.IsNullOrEmpty(task.Status.Message.MessageId));
            Assert.Equal(MessageRole.Agent, task.Status.Message.Role);
            Assert.Single(task.Status.Message.Parts);
        }
    }
}
