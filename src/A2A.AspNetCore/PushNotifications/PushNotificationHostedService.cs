using Microsoft.Extensions.Hosting;

namespace A2A.AspNetCore;

internal sealed class PushNotificationHostedService(IPushNotificationSender sender, IA2ARequestHandler handler) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        sender is IHostedService hosted ? hosted.StartAsync(cancellationToken) : Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (handler is A2AServer server)
            {
                await server.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (sender is IHostedService hosted)
            {
                await hosted.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
