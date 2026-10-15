namespace A2A;

using System.Diagnostics;

internal static class A2AHttpRequestPolicy
{
    internal static void NormalizeIncomingRequest(object request, string binding, string operation)
    {
        if (!ClearTenantFields(request))
        {
            return;
        }

        Activity.Current?.AddEvent(new ActivityEvent(
            "a2a.tenant.ignored",
            tags: new ActivityTagsCollection
            {
                { "a2a.protocol.binding", binding },
                { "a2a.operation", operation },
            }));
    }

    private static bool ClearTenantFields(object request)
    {
        switch (request)
        {
            case SendMessageRequest sendMessage:
                var ignored = sendMessage.Tenant is not null;
                sendMessage.Tenant = null;
                if (sendMessage.Configuration?.TaskPushNotificationConfig is { } embeddedConfig)
                {
                    ignored |= embeddedConfig.Tenant is not null;
                    embeddedConfig.Tenant = null;
                }
                return ignored;
            case TaskPushNotificationConfig config:
                var configIgnored = config.Tenant is not null;
                config.Tenant = null;
                return configIgnored;
            case GetTaskRequest getTask:
                var getTaskIgnored = getTask.Tenant is not null;
                getTask.Tenant = null;
                return getTaskIgnored;
            case ListTasksRequest listTasks:
                var listTasksIgnored = listTasks.Tenant is not null;
                listTasks.Tenant = null;
                return listTasksIgnored;
            case CancelTaskRequest cancelTask:
                var cancelTaskIgnored = cancelTask.Tenant is not null;
                cancelTask.Tenant = null;
                return cancelTaskIgnored;
            case SubscribeToTaskRequest subscribe:
                var subscribeIgnored = subscribe.Tenant is not null;
                subscribe.Tenant = null;
                return subscribeIgnored;
            case GetTaskPushNotificationConfigRequest getConfig:
                var getConfigIgnored = getConfig.Tenant is not null;
                getConfig.Tenant = null;
                return getConfigIgnored;
            case ListTaskPushNotificationConfigsRequest listConfigs:
                var listConfigsIgnored = listConfigs.Tenant is not null;
                listConfigs.Tenant = null;
                return listConfigsIgnored;
            case DeleteTaskPushNotificationConfigRequest deleteConfig:
                var deleteConfigIgnored = deleteConfig.Tenant is not null;
                deleteConfig.Tenant = null;
                return deleteConfigIgnored;
            case GetExtendedAgentCardRequest getCard:
                var getCardIgnored = getCard.Tenant is not null;
                getCard.Tenant = null;
                return getCardIgnored;
            default:
                return false;
        }
    }
}
