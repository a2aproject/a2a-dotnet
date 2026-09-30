using Microsoft.AspNetCore.Http;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>Registers HTTP+JSON bindings for the standard A2A operations.</summary>
public static class A2AStandardHttpBindingBuilderExtensions
{
    internal static IReadOnlyDictionary<
        A2AHttpOperationBindingKey,
        A2AOperationId> CanonicalRouteOperationIds { get; } =
        new Dictionary<A2AHttpOperationBindingKey, A2AOperationId>
        {
            [new(HttpMethods.Get, "/tasks/{id}")] =
                new("https://a2a-protocol.org/operations/get-task"),
            [new(HttpMethods.Get, "/tasks")] =
                new("https://a2a-protocol.org/operations/list-tasks"),
            [new(HttpMethods.Post, "/tasks/{id}:cancel")] =
                new("https://a2a-protocol.org/operations/cancel-task"),
            [new(HttpMethods.Post, "/tasks/{id}:subscribe")] =
                new("https://a2a-protocol.org/operations/subscribe-to-task"),
            [new(HttpMethods.Post, "/message:send")] =
                new("https://a2a-protocol.org/operations/send-message"),
            [new(HttpMethods.Post, "/message:stream")] =
                new("https://a2a-protocol.org/operations/send-message-stream"),
            [new(
                HttpMethods.Post,
                "/tasks/{id}/pushNotificationConfigs")] =
                new(
                    "https://a2a-protocol.org/operations/create-task-push-notification-config"),
            [new(
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs")] =
                new(
                    "https://a2a-protocol.org/operations/list-task-push-notification-configs"),
            [new(
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs/{configId}")] =
                new(
                    "https://a2a-protocol.org/operations/get-task-push-notification-config"),
            [new(
                HttpMethods.Delete,
                "/tasks/{id}/pushNotificationConfigs/{configId}")] =
                new(
                    "https://a2a-protocol.org/operations/delete-task-push-notification-config"),
            [new(HttpMethods.Get, "/extendedAgentCard")] =
                new(
                    "https://a2a-protocol.org/operations/get-extended-agent-card"),
        };

    /// <summary>Adds HTTP+JSON bindings for every standard A2A operation.</summary>
    /// <param name="builder">The HTTP binding builder.</param>
    /// <param name="standard">The standard operation handles.</param>
    /// <returns>The builder.</returns>
    public static A2AHttpOperationBindingBuilder AddStandardA2AHttpBindings(
        this A2AHttpOperationBindingBuilder builder,
        A2AStandardOperations standard)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(standard);

        return builder
            .Map(
                HttpMethods.Get,
                "/tasks/{id}",
                standard.GetTask,
                BindGetTaskAsync,
                GetTypeInfo<AgentTask>())
            .Map(
                HttpMethods.Get,
                "/tasks",
                standard.ListTasks,
                BindListTasksAsync,
                GetTypeInfo<ListTasksResponse>())
            .Map(
                HttpMethods.Post,
                "/tasks/{id}:cancel",
                standard.CancelTask,
                BindCancelTaskAsync,
                GetTypeInfo<AgentTask>())
            .MapStreaming(
                HttpMethods.Post,
                "/tasks/{id}:subscribe",
                standard.SubscribeToTask,
                BindSubscribeToTaskAsync,
                GetTypeInfo<StreamResponse>())
            .Map(
                HttpMethods.Post,
                "/message:send",
                standard.SendMessage,
                BindSendMessageAsync,
                GetTypeInfo<SendMessageResponse>())
            .MapStreaming(
                HttpMethods.Post,
                "/message:stream",
                standard.SendStreamingMessage,
                BindSendMessageAsync,
                GetTypeInfo<StreamResponse>())
            .Map(
                HttpMethods.Post,
                "/tasks/{id}/pushNotificationConfigs",
                standard.CreateTaskPushNotificationConfig,
                BindCreateTaskPushNotificationConfigAsync,
                GetTypeInfo<TaskPushNotificationConfig>())
            .Map(
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs",
                standard.ListTaskPushNotificationConfigs,
                BindListTaskPushNotificationConfigsAsync,
                GetTypeInfo<ListTaskPushNotificationConfigsResponse>())
            .Map(
                HttpMethods.Get,
                "/tasks/{id}/pushNotificationConfigs/{configId}",
                standard.GetTaskPushNotificationConfig,
                BindGetTaskPushNotificationConfigAsync,
                GetTypeInfo<TaskPushNotificationConfig>())
            .Map(
                HttpMethods.Delete,
                "/tasks/{id}/pushNotificationConfigs/{configId}",
                standard.DeleteTaskPushNotificationConfig,
                BindDeleteTaskPushNotificationConfigAsync,
                GetTypeInfo<A2AEmptyResult>())
            .Map(
                HttpMethods.Get,
                "/extendedAgentCard",
                standard.GetExtendedAgentCard,
                static (_, _) => ValueTask.FromResult(
                    new GetExtendedAgentCardRequest()),
                GetTypeInfo<AgentCard>());
    }

    internal static void ValidateReservedRouteBinding(
        string httpMethod,
        string route,
        A2AOperationRegistration registration)
    {
        if (!CanonicalRouteOperationIds.TryGetValue(
                new A2AHttpOperationBindingKey(httpMethod, route),
                out var expectedOperationId))
        {
            return;
        }

        if (registration.Source != A2AOperationSource.Standard
            || !string.Equals(
                registration.Id.Value,
                expectedOperationId.Value,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The reserved HTTP route '{httpMethod} {route}' must map to the standard A2A operation '{expectedOperationId.Value}'.");
        }
    }

    internal static string NormalizeRoutePattern(string route)
    {
        var normalized = new StringBuilder(route.Length + 1);
        if (route[0] != '/')
        {
            normalized.Append('/');
        }

        for (var index = 0; index < route.Length; index++)
        {
            var character = route[index];
            if (character != '{')
            {
                normalized.Append(char.ToLowerInvariant(character));
                continue;
            }

            normalized.Append(character);
            index++;
            while (index < route.Length && route[index] == '*')
            {
                normalized.Append(route[index]);
                index++;
            }

            while (index < route.Length
                && route[index] is not ':' and not '?' and not '=' and not '}')
            {
                index++;
            }

            for (; index < route.Length; index++)
            {
                character = route[index];
                normalized.Append(char.ToLowerInvariant(character));
                if (character == '}')
                {
                    break;
                }
            }
        }

        return normalized.ToString();
    }

    private static ValueTask<GetTaskRequest> BindGetTaskAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new GetTaskRequest
            {
                Id = GetRequiredRouteValue(context, "id"),
                HistoryLength = GetOptionalIntQuery(context, "historyLength"),
            });

    private static ValueTask<ListTasksRequest> BindListTasksAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var statusValue = GetOptionalQuery(context, "status");
        TaskState? status = null;
        if (statusValue is not null)
        {
            if (!Enum.TryParse<TaskState>(
                    statusValue,
                    ignoreCase: true,
                    out var parsedStatus))
            {
                throw new A2AHttpBindingException(
                    Results.Problem(
                        detail:
                            $"Invalid status filter: '{statusValue}'. Valid values: {string.Join(", ", Enum.GetNames<TaskState>())}",
                        statusCode: StatusCodes.Status400BadRequest));
            }

            status = parsedStatus;
        }

        return ValueTask.FromResult(
            new ListTasksRequest
            {
                ContextId = GetOptionalQuery(context, "contextId"),
                Status = status,
                PageSize = GetOptionalIntQuery(context, "pageSize"),
                PageToken = GetOptionalQuery(context, "pageToken"),
                HistoryLength = GetOptionalIntQuery(context, "historyLength"),
            });
    }

    private static ValueTask<CancelTaskRequest> BindCancelTaskAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new CancelTaskRequest
            {
                Id = GetRequiredRouteValue(context, "id"),
            });

    private static ValueTask<SubscribeToTaskRequest> BindSubscribeToTaskAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new SubscribeToTaskRequest
            {
                Id = GetRequiredRouteValue(context, "id"),
            });

    private static ValueTask<SendMessageRequest> BindSendMessageAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        DeserializeBodyAsync(
            context,
            GetTypeInfo<SendMessageRequest>(),
            cancellationToken);

    private static async ValueTask<TaskPushNotificationConfig>
        BindCreateTaskPushNotificationConfigAsync(
            HttpContext context,
            CancellationToken cancellationToken)
    {
        var config = await DeserializeBodyAsync(
            context,
            GetTypeInfo<TaskPushNotificationConfig>(),
            cancellationToken).ConfigureAwait(false);
        config.TaskId = GetRequiredRouteValue(context, "id");
        config.Tenant = null;
        return config;
    }

    private static ValueTask<ListTaskPushNotificationConfigsRequest>
        BindListTaskPushNotificationConfigsAsync(
            HttpContext context,
            CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new ListTaskPushNotificationConfigsRequest
            {
                TaskId = GetRequiredRouteValue(context, "id"),
                PageSize = GetOptionalIntQuery(context, "pageSize"),
                PageToken = GetOptionalQuery(context, "pageToken"),
            });

    private static ValueTask<GetTaskPushNotificationConfigRequest>
        BindGetTaskPushNotificationConfigAsync(
            HttpContext context,
            CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new GetTaskPushNotificationConfigRequest
            {
                TaskId = GetRequiredRouteValue(context, "id"),
                Id = GetRequiredRouteValue(context, "configId"),
            });

    private static ValueTask<DeleteTaskPushNotificationConfigRequest>
        BindDeleteTaskPushNotificationConfigAsync(
            HttpContext context,
            CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new DeleteTaskPushNotificationConfigRequest
            {
                TaskId = GetRequiredRouteValue(context, "id"),
                Id = GetRequiredRouteValue(context, "configId"),
            });

    private static async ValueTask<T> DeserializeBodyAsync<T>(
        HttpContext context,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType())
        {
            throw new A2AHttpBindingException(
                Results.StatusCode(
                    StatusCodes.Status415UnsupportedMediaType));
        }

        try
        {
            var value = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                typeInfo,
                cancellationToken).ConfigureAwait(false);
            return value
                ?? throw new A2AException(
                    $"The HTTP request body could not be deserialized as {typeof(T).Name}.",
                    A2AErrorCode.InvalidParams);
        }
        catch (JsonException exception)
        {
            throw new A2AException(
                $"The HTTP request body could not be deserialized as {typeof(T).Name}.",
                exception,
                A2AErrorCode.InvalidParams);
        }
    }

    private static string GetRequiredRouteValue(
        HttpContext context,
        string name)
    {
        var value = Convert.ToString(
            context.Request.RouteValues[name],
            CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(value))
        {
            throw new A2AException(
                $"The route value '{name}' is required.",
                A2AErrorCode.InvalidParams);
        }

        return value;
    }

    private static int? GetOptionalIntQuery(
        HttpContext context,
        string name)
    {
        var value = GetOptionalQuery(context, name);
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new A2AException(
                $"Invalid {name}: '{value}'. Expected an integer.",
                A2AErrorCode.InvalidParams);
        }

        return parsed;
    }

    private static string? GetOptionalQuery(
        HttpContext context,
        string name)
    {
        var value = context.Request.Query[name].FirstOrDefault();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static JsonTypeInfo<T> GetTypeInfo<T>() =>
        (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));
}
