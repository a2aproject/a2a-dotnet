
namespace A2A
{
#pragma warning disable CS8019
    using Microsoft.Extensions.Logging;
    using System;
#pragma warning restore CS8019

    static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Fetching agent card from '{Url}'")]
        internal static partial void FetchingAgentCardFromUrl(this ILogger logger, Uri Url);

        [LoggerMessage(1, LogLevel.Error, "Failed to parse agent card JSON")]
        internal static partial void FailedToParseAgentCardJson(this ILogger logger, Exception exception);

        [LoggerMessage(2, LogLevel.Error, "HTTP request failed with status code {StatusCode}")]
        internal static partial void HttpRequestFailedWithStatusCode(this ILogger logger, Exception exception, System.Net.HttpStatusCode StatusCode);
    }
}
