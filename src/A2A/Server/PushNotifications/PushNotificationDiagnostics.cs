using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace A2A;

internal sealed partial class PushNotificationDiagnostics(ILogger logger)
{
    private readonly ILogger _logger = logger;

    internal void Record(string action, string? taskId = null, string? configId = null,
        string? failure = null, int attempt = 0, int statusCode = 0, double delayMilliseconds = 0)
    {
        var tags = new TagList { { "a2a.push.action", action }, { "a2a.push.failure", failure } };
        A2ADiagnostics.PushNotificationCount.Add(1, tags);
        var taskHash = taskId is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(taskId)));
        var configHash = configId is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configId)));
        using var activity = A2ADiagnostics.Source.StartActivity("A2A.Push." + action);
        activity?.SetTag("a2a.task.id_hash", taskHash);
        activity?.SetTag("a2a.push.config.id_hash", configHash);
        activity?.SetTag("a2a.push.failure", failure);
        activity?.SetTag("a2a.push.attempt", attempt);
        activity?.SetTag("http.response.status_code", statusCode);
        activity?.SetTag("a2a.push.retry_delay_ms", delayMilliseconds);
        if (failure is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        // Do not pass exceptions to the logger: their messages can contain URLs or secrets.
        LogEvent(failure is null ? LogLevel.Debug : LogLevel.Warning,
            action, taskHash, configHash, attempt, statusCode, failure, delayMilliseconds);
    }

    [LoggerMessage(EventId = 504,
        Message = "Push {Action}: task hash {TaskId}, config hash {ConfigId}, attempt {Attempt}, status {Status}, failure {FailureCategory}, retry delay {DelayMilliseconds}ms")]
    private partial void LogEvent(LogLevel level, string action, string? taskId, string? configId,
        int attempt, int status, string? failureCategory, double delayMilliseconds);
}
