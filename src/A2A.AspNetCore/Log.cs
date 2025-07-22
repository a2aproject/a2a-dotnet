
namespace A2A.AspNetCore
{
#pragma warning disable CS8019
    using Microsoft.Extensions.Logging;
    using System;
#pragma warning restore CS8019

    static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "Unexpected error in {ActivityName}")]
        internal static partial void UnexpectedErrorInActivityName(this ILogger logger, Exception exception, string ActivityName);

        [LoggerMessage(1, LogLevel.Error, "A2A error in {ActivityName}")]
        internal static partial void A2AErrorInActivityName(this ILogger logger, Exception exception, string ActivityName);
    }
}
