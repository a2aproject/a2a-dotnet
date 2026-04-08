namespace A2A.V0_3Compat;

using System.Runtime.CompilerServices;

using V03 = A2A.V0_3;

/// <summary>Adapts a v0.3 A2A client to the v1.0 <see cref="A2A.IA2AClient"/> interface.</summary>
internal sealed class V03ClientAdapter : A2A.IA2AClient, IDisposable
{
    private readonly V03.A2AClient _v03Client;

    /// <summary>Initializes a new instance of the <see cref="V03ClientAdapter"/> class.</summary>
    /// <param name="v03Client">The v0.3 client to wrap.</param>
    internal V03ClientAdapter(V03.A2AClient v03Client)
    {
        ArgumentNullException.ThrowIfNull(v03Client);
        _v03Client = v03Client;
    }

    /// <inheritdoc />
    public async Task<A2A.SendMessageResponse> SendMessageAsync(
        A2A.SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var v03Params = V03TypeConverter.ToV03(request);
        var v03Response = await _v03Client.SendMessageAsync(v03Params, cancellationToken).ConfigureAwait(false);
        return V03TypeConverter.ToV1Response(v03Response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2A.StreamResponse> SendStreamingMessageAsync(
        A2A.SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var v03Params = V03TypeConverter.ToV03(request);
        await foreach (var sseItem in _v03Client.SendMessageStreamingAsync(v03Params, cancellationToken).ConfigureAwait(false))
        {
            if (sseItem.Data is { } evt)
            {
                yield return V03TypeConverter.ToV1StreamResponse(evt);
            }
        }
    }

    /// <inheritdoc />
    public async Task<A2A.AgentTask> GetTaskAsync(
        A2A.GetTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        // request.HistoryLength is not forwarded — v0.3's GetTaskAsync only accepts (taskId, CancellationToken).
        // HistoryLength is a v1.0-only feature; the full history is always returned when talking to a v0.3 server.
        var v03Task = await _v03Client.GetTaskAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return V03TypeConverter.ToV1Task(v03Task);
    }

    /// <inheritdoc />
    public Task<A2A.ListTasksResponse> ListTasksAsync(
        A2A.ListTasksRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("v0.3 does not support listing tasks.");

    /// <inheritdoc />
    public async Task<A2A.AgentTask> CancelTaskAsync(
        A2A.CancelTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var v03Params = new V03.TaskIdParams
        {
            Id = request.Id,
            Metadata = request.Metadata,
        };
        var v03Task = await _v03Client.CancelTaskAsync(v03Params, cancellationToken).ConfigureAwait(false);
        return V03TypeConverter.ToV1Task(v03Task);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2A.StreamResponse> SubscribeToTaskAsync(
        A2A.SubscribeToTaskRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var sseItem in _v03Client.SubscribeToTaskAsync(request.Id, cancellationToken).ConfigureAwait(false))
        {
            if (sseItem.Data is { } evt)
            {
                yield return V03TypeConverter.ToV1StreamResponse(evt);
            }
        }
    }

    /// <inheritdoc />
    public async Task<A2A.TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
        A2A.CreateTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var v03Config = new V03.TaskPushNotificationConfig
        {
            TaskId = request.TaskId,
            PushNotificationConfig = V03TypeConverter.ToV03PushNotificationConfig(request.Config),
        };
        var v03Result = await _v03Client.SetPushNotificationAsync(v03Config, cancellationToken).ConfigureAwait(false);
        return V03TypeConverter.ToV1TaskPushNotificationConfig(v03Result);
    }

    /// <inheritdoc />
    public async Task<A2A.TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
        A2A.GetTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var v03Params = new V03.GetTaskPushNotificationConfigParams
        {
            Id = request.TaskId,
            PushNotificationConfigId = request.Id,
        };
        var v03Result = await _v03Client.GetPushNotificationAsync(v03Params, cancellationToken).ConfigureAwait(false);
        return V03TypeConverter.ToV1TaskPushNotificationConfig(v03Result);
    }

    /// <inheritdoc />
    public Task<A2A.ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(
        A2A.ListTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("v0.3 does not support listing push notification configs.");

    /// <inheritdoc />
    public Task DeleteTaskPushNotificationConfigAsync(
        A2A.DeleteTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("v0.3 does not support deleting push notification configs.");

    /// <inheritdoc />
    public Task<A2A.AgentCard> GetExtendedAgentCardAsync(
        A2A.GetExtendedAgentCardRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("v0.3 does not support extended agent cards.");

    /// <inheritdoc />
    public void Dispose()
    {
        // The v0.3 A2AClient does not implement IDisposable, so nothing to dispose.
    }
}
