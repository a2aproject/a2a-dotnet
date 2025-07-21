
namespace A2A.AspNetCore
{
#pragma warning disable CS8019
    using Microsoft.Extensions.Logging;
    using System;
#pragma warning restore CS8019

    static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "A2A error retrieving agent card")]
        internal static partial void AAErrorRetrievingAgentCard(this ILogger logger, Exception exception);

        [LoggerMessage(1, LogLevel.Error, "Error retrieving agent card")]
        internal static partial void ErrorRetrievingAgentCard(this ILogger logger, Exception exception);

        [LoggerMessage(2, LogLevel.Error, "A2A error cancelling task")]
        internal static partial void AAErrorCancellingTask(this ILogger logger, Exception exception);

        [LoggerMessage(3, LogLevel.Error, "Error cancelling task")]
        internal static partial void ErrorCancellingTask(this ILogger logger, Exception exception);

        [LoggerMessage(4, LogLevel.Error, "A2A error sending message to task")]
        internal static partial void AAErrorSendingMessageToTask(this ILogger logger, Exception exception);

        [LoggerMessage(5, LogLevel.Error, "Error sending message to task")]
        internal static partial void ErrorSendingMessageToTask(this ILogger logger, Exception exception);

        [LoggerMessage(6, LogLevel.Error, "A2A error subscribing to task")]
        internal static partial void AAErrorSubscribingToTask(this ILogger logger, Exception exception);

        [LoggerMessage(7, LogLevel.Error, "Error subscribing to task")]
        internal static partial void ErrorSubscribingToTask(this ILogger logger, Exception exception);

        [LoggerMessage(8, LogLevel.Error, "A2A error configuring push notification")]
        internal static partial void AAErrorConfiguringPushNotification(this ILogger logger, Exception exception);

        [LoggerMessage(9, LogLevel.Error, "Error configuring push notification")]
        internal static partial void ErrorConfiguringPushNotification(this ILogger logger, Exception exception);
    }
}
