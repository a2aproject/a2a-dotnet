using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A;

/// <summary>An immutable registration generation. The version must change whenever an ID is recreated.</summary>
public sealed class PushNotificationConfigSnapshot
{
    private readonly JsonTypeInfo<TaskPushNotificationConfig> _typeInfo =
        (JsonTypeInfo<TaskPushNotificationConfig>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TaskPushNotificationConfig));
    private readonly byte[] _configuration;

    /// <summary>Creates an independent snapshot with an opaque, non-empty generation.</summary>
    /// <param name="configuration">Configuration to snapshot.</param>
    /// <param name="version">Opaque registration generation.</param>
    /// <param name="removalToken">Generation-scoped invalidation; stores cancel it when deletion completes.</param>
    public PushNotificationConfigSnapshot(TaskPushNotificationConfig configuration, string version, CancellationToken removalToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _configuration = JsonSerializer.SerializeToUtf8Bytes(configuration, _typeInfo);
        Version = version;
        RemovalToken = removalToken;
    }

    /// <summary>Gets a fresh copy of the secret-bearing configuration.</summary>
    public TaskPushNotificationConfig Configuration => JsonSerializer.Deserialize(_configuration, _typeInfo)!;

    /// <summary>Gets the immutable registration generation, not a protocol field.</summary>
    public string Version { get; }

    /// <summary>Gets the generation-scoped deletion signal. Immutable data remains readable after cancellation.</summary>
    public CancellationToken RemovalToken { get; }
}
