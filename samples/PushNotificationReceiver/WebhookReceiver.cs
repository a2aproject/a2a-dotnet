using A2A;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PushNotificationReceiver;

/// <summary>A local, task-specific receiver for v1 push notification payloads.</summary>
public sealed class WebhookReceiver
{
    private readonly string _taskId;
    private readonly byte[] _credential;
    private readonly byte[]? _legacyToken;
    private readonly Action<StreamResponse> _onNotification;

    /// <summary>Creates a receiver with an expected task and local test credentials.</summary>
    public WebhookReceiver(
        string taskId,
        string credential,
        Action<StreamResponse> onNotification,
        string? legacyToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        ArgumentNullException.ThrowIfNull(onNotification);
        if (legacyToken is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legacyToken);
        }

        _taskId = taskId;
        _credential = Encoding.UTF8.GetBytes(credential);
        _legacyToken = legacyToken is null ? null : Encoding.UTF8.GetBytes(legacyToken);
        _onNotification = onNotification;
    }

    /// <summary>Creates an application listening only on an assigned IPv4 loopback port.</summary>
    public WebApplication CreateApplication()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // This local sample must not load an externally configured listening endpoint.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 64 * 1024;
        });

        var app = builder.Build();
        app.MapPost("/notifications", ReceiveAsync);
        return app;
    }

    private async Task<IResult> ReceiveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(request))
        {
            return Results.Unauthorized();
        }

        bool hasSupportedContentType =
            MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) &&
            (string.Equals(contentType.MediaType, "application/a2a+json", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(contentType.CharSet) ||
             string.Equals(contentType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase));
        if (!hasSupportedContentType)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        StreamResponse? notification;
        try
        {
            notification = await JsonSerializer.DeserializeAsync<StreamResponse>(
                request.Body, A2AJsonUtilities.DefaultOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (notification is null)
        {
            return Results.BadRequest();
        }

        int payloadCount =
            (notification.Task is null ? 0 : 1) +
            (notification.Message is null ? 0 : 1) +
            (notification.StatusUpdate is null ? 0 : 1) +
            (notification.ArtifactUpdate is null ? 0 : 1);
        bool hasInvalidPayload =
            payloadCount != 1 ||
            notification.Task is { Status: null } ||
            notification.StatusUpdate is { Status: null } ||
            notification.ArtifactUpdate is { Artifact: null };
        if (hasInvalidPayload)
        {
            return Results.BadRequest();
        }

        var taskId = notification.PayloadCase switch
        {
            StreamResponseCase.Task => notification.Task!.Id,
            StreamResponseCase.Message => notification.Message!.TaskId,
            StreamResponseCase.StatusUpdate => notification.StatusUpdate!.TaskId,
            StreamResponseCase.ArtifactUpdate => notification.ArtifactUpdate!.TaskId,
            _ => null,
        };
        if (string.IsNullOrEmpty(taskId))
        {
            return Results.BadRequest();
        }

        if (!string.Equals(taskId, _taskId, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        _onNotification(notification);
        return Results.NoContent();
    }

    private bool IsAuthorized(HttpRequest request)
    {
        bool hasBearerCredential =
            request.Headers.Authorization.Count == 1 &&
            AuthenticationHeaderValue.TryParse(request.Headers.Authorization[0], out var authorization) &&
            string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
            MatchesBearerCredential(authorization.Parameter);
        if (!hasBearerCredential)
        {
            return false;
        }

        if (_legacyToken is null)
        {
            return true;
        }

        var values = request.Headers["X-A2A-Notification-Token"];
        return values.Count == 1 &&
            values[0] is { } token &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _legacyToken);
    }

    private bool MatchesBearerCredential(string? value)
    {
        return value is not null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), _credential);
    }
}
