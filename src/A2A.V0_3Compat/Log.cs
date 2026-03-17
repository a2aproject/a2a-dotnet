namespace A2A.V0_3Compat
{
#pragma warning disable CS8019
    using Microsoft.Extensions.Logging;
    using System;
#pragma warning restore CS8019

    static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Detected v{Version} agent card, using URL: {Url}")]
        internal static partial void DetectedAgentCardVersion(this ILogger logger, string version, string url);
    }
}
