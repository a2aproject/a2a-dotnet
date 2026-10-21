using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace A2A;

internal sealed class PushNotificationReceiver : IAsyncDisposable
{
    private readonly WebApplication _application;

    private PushNotificationReceiver(WebApplication application, Uri notificationUri)
    {
        _application = application;
        NotificationUri = notificationUri;
    }

    internal Uri NotificationUri { get; }

    internal static async Task<PushNotificationReceiver> StartAsync(
        Uri receiverUri,
        Action<StreamResponse> notificationHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiverUri);
        ArgumentNullException.ThrowIfNull(notificationHandler);

        if (!receiverUri.IsAbsoluteUri ||
            receiverUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The push notification receiver URL must be an absolute HTTP or HTTPS URL.", nameof(receiverUri));
        }

        if (!receiverUri.IsLoopback)
        {
            throw new ArgumentException("The unauthenticated push notification receiver must use a loopback URL.", nameof(receiverUri));
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(receiverUri.GetLeftPart(UriPartial.Authority));

        var application = builder.Build();
        string callbackPath = $"{receiverUri.AbsolutePath.TrimEnd('/')}/notify";
        application.MapPost(callbackPath, async context =>
        {
            try
            {
                var notification = await JsonSerializer.DeserializeAsync<StreamResponse>(
                    context.Request.Body,
                    A2AJsonUtilities.DefaultOptions,
                    context.RequestAborted).ConfigureAwait(false);

                if (notification is null)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                notificationHandler(notification);
                context.Response.StatusCode = StatusCodes.Status200OK;
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        string boundAddress = addresses?.Single()
            ?? throw new InvalidOperationException("The push notification receiver did not report a listening address.");
        var notificationUri = new UriBuilder(boundAddress)
        {
            Path = callbackPath
        }.Uri;

        return new PushNotificationReceiver(application, notificationUri);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
