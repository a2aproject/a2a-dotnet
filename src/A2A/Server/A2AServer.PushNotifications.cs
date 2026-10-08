using Microsoft.Extensions.Logging;
using A2A.Extensions;

namespace A2A;

public partial class A2AServer
{
    private readonly PushNotificationManager? _pushNotifications;

    /// <summary>Creates a server with replaceable push services, enabled only by the supplied card.</summary>
    /// <param name="handler">Agent handler.</param>
    /// <param name="taskStore">Task persistence.</param>
    /// <param name="notifier">Task locks and SSE notifications.</param>
    /// <param name="logger">Server logger.</param>
    /// <param name="options">Server options.</param>
    /// <param name="agentCard">Authoritative capability declaration.</param>
    /// <param name="pushNotificationStore">Push configuration persistence.</param>
    /// <param name="pushNotificationUrlValidator">Destination policy.</param>
    /// <param name="pushNotificationSender">Delivery implementation.</param>
    public A2AServer(IAgentHandler handler, ITaskStore taskStore,
        ChannelEventNotifier notifier, ILogger<A2AServer> logger,
        A2AServerOptions? options, AgentCard agentCard,
        IPushNotificationStore pushNotificationStore,
        IPushNotificationUrlValidator pushNotificationUrlValidator,
        IPushNotificationSender pushNotificationSender)
        : this(handler, taskStore, notifier, logger, options)
    {
        ArgumentNullException.ThrowIfNull(agentCard);
        ArgumentNullException.ThrowIfNull(pushNotificationStore);
        ArgumentNullException.ThrowIfNull(pushNotificationUrlValidator);
        ArgumentNullException.ThrowIfNull(pushNotificationSender);
        _pushNotifications = new PushNotificationManager(
            agentCard, taskStore, pushNotificationStore, pushNotificationUrlValidator, pushNotificationSender, notifier, logger);
    }

    private PushNotificationManager PushNotifications =>
        _pushNotifications ?? throw new A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported);

    private void RecordUnusedInlinePush(RequestContext context)
    {
        if (context.PushNotifications?.Pending is not null)
        {
            PushNotifications.RecordInline("inline_unused", context);
            context.PushNotifications.Pending = null;
        }
    }

    private SendMessageResponse PrepareMessageResponse(SendMessageResponse response, RequestContext context)
    {
        RecordUnusedInlinePush(context);
        if (response.Task is not null)
        {
            bool nonBlocking = (context.Configuration?.ReturnImmediately).GetValueOrDefault();
            var state = response.Task.Status.State;
            bool finishedOrInterrupted = state.IsTerminal() ||
                state is TaskState.InputRequired or TaskState.AuthRequired;
            if (!nonBlocking &&
                !finishedOrInterrupted)
            {
                throw new A2AException("A blocking handler ended without a terminal or interrupted task state.",
                    A2AErrorCode.InvalidAgentResponse);
            }
            response.Task = response.Task.WithHistoryTrimmedTo(context.Configuration?.HistoryLength);
        }
        return response;
    }
}
