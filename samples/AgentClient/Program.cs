using AgentClient.Samples;

namespace AgentClient;

internal static class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            await GetAgentDetailsSample.RunAsync();

            await MessageBasedCommunicationSample.RunAsync();

            await TaskBasedCommunicationSample.RunAsync();
        }
        finally
        {
            await AgentServerUtils.StopLocalAgentServersAsync();
        }
    }
}
