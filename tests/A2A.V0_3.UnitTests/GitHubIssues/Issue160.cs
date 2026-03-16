using System.Text.Json;

namespace A2A.V0_3.UnitTests.GitHubIssues
{
    public sealed class Issue160
    {
        [Fact]
        public void Issue_160_Passes()
        {
            var json = """
            {
              "id": "aaa2d907-e493-483c-a569-6aa38c6951d4",
              "jsonrpc": "2.0",
              "result": {
                "artifacts": [
                  {
                    "artifactId": "artifact-1",
                    "description": null,
                    "extensions": null,
                    "metadata": null,
                    "name": "artifact-1",
                    "parts": [
                      {
                        "kind": "text",
                        "metadata": null,
                        "text": "Artifact update from the Movie Agent"
                      }
                    ]
                  }
                ],
                "contextId": "32fef1d4-a1e5-4cb2-83cb-177808deac39",
                "history": [
                  {
                    "contextId": "32fef1d4-a1e5-4cb2-83cb-177808deac39",
                    "extensions": null,
                    "kind": "message",
                    "messageId": "From Dotnet",
                    "metadata": null,
                    "parts": [
                      {
                        "kind": "text",
                        "metadata": null,
                        "text": "jimmy"
                      }
                    ],
                    "referenceTaskIds": null,
                    "role": "user",
                    "taskId": null
                  },
                  {
                    "contextId": "32fef1d4-a1e5-4cb2-83cb-177808deac39",
                    "extensions": null,
                    "kind": "message",
                    "messageId": "488f7027-6805-4d1c-bafa-1bf55d438eb3",
                    "metadata": null,
                    "parts": [
                      {
                        "kind": "text",
                        "metadata": null,
                        "text": "Generating code..."
                      }
                    ],
                    "referenceTaskIds": null,
                    "role": "agent",
                    "taskId": "6b349583-196e-444c-a0bd-a4f22f0753f0"
                  },
                  {
                    "contextId": "32fef1d4-a1e5-4cb2-83cb-177808deac39",
                    "extensions": null,
                    "kind": "message",
                    "messageId": "31e24763-63ae-4509-9ff2-11d789640ae4",
                    "metadata": null,
                    "parts": [],
                    "referenceTaskIds": null,
                    "role": "agent",
                    "taskId": "6b349583-196e-444c-a0bd-a4f22f0753f0"
                  }
                ],
                "id": "6b349583-196e-444c-a0bd-a4f22f0753f0",
                "kind": "task",
                "metadata": null,
                "status": {
                  "message": {
                    "contextId": "32fef1d4-a1e5-4cb2-83cb-177808deac39",
                    "extensions": null,
                    "kind": "message",
                    "messageId": "31e24763-63ae-4509-9ff2-11d789640ae4",
                    "metadata": null,
                    "parts": [],
                    "referenceTaskIds": null,
                    "role": "agent",
                    "taskId": "6b349583-196e-444c-a0bd-a4f22f0753f0"
                  },
                  "state": "completed",
                  "timestamp": "2025-08-25T09:58:01.545"
                }
              }
            }
            """;

            var deserializedResponseObj = JsonSerializer.Deserialize<JsonRpcResponse>(json, A2AJsonUtilities.DefaultOptions);
            Assert.NotNull(deserializedResponseObj);

            var task = deserializedResponseObj.Result.Deserialize<AgentTask>(A2AJsonUtilities.DefaultOptions);
            Assert.NotNull(task);

            Assert.Equal("6b349583-196e-444c-a0bd-a4f22f0753f0", task.Id);
            Assert.Equal("32fef1d4-a1e5-4cb2-83cb-177808deac39", task.Status.Message?.ContextId);

            Assert.Equal(1, task.Artifacts?.Count);
            Assert.Equal(3, task.History?.Count);
        }
    }
}
