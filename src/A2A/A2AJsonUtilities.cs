using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Provides a collection of utility methods for working with JSON data in the context of A2A.
/// </summary>
public static partial class A2AJsonUtilities
{
    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> singleton used as the default in JSON serialization operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For Native AOT or applications disabling <see cref="JsonSerializer.IsReflectionEnabledByDefault"/>, this instance
    /// includes source generated contracts for all common exchange types contained in the A2A library.
    /// </para>
    /// <para>
    /// It additionally turns on the following settings:
    /// <list type="number">
    /// <item>Enables <see cref="JsonSerializerDefaults.Web"/> defaults.</item>
    /// <item>Enables <see cref="JsonIgnoreCondition.WhenWritingNull"/> as the default ignore condition for properties.</item>
    /// <item>Enables <see cref="JsonNumberHandling.AllowReadingFromString"/> as the default number handling for number types.</item>
    /// <item>Enables <c>AllowOutOfOrderMetadataProperties</c> to allow for type discriminators anywhere in a JSON payload.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions DefaultOptions => defaultOptions.Value;

    private static Lazy<JsonSerializerOptions> defaultOptions = new(() =>
    {
        // Clone source-generated options so we can customize
        var opts = new JsonSerializerOptions(JsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // optional: keep '+' unescaped
        };

        // Register custom converters at options-level (not attributes)
        opts.Converters.Add(new A2AJsonConverter<MessageSendParams>());
        return opts;
    });

    // Keep in sync with CreateDefaultOptions above.
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowOutOfOrderMetadataProperties = true)]

    // JSON-RPC
    [JsonSerializable(typeof(JsonRpcError))]
    [JsonSerializable(typeof(JsonRpcId))]
    [JsonSerializable(typeof(JsonRpcRequest))]
    [JsonSerializable(typeof(JsonRpcResponse))]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]

    // A2A
    [JsonSerializable(typeof(A2AEvent))]
    [JsonSerializable(typeof(A2AResponse))]
    [JsonSerializable(typeof(AgentCard))]

    [JsonSerializable(typeof(AgentTask))]
    [JsonSerializable(typeof(GetTaskPushNotificationConfigParams))]
    [JsonSerializable(typeof(MessageSendParams))]
    [JsonSerializable(typeof(TaskIdParams))]
    [JsonSerializable(typeof(TaskPushNotificationConfig))]
    [JsonSerializable(typeof(List<TaskPushNotificationConfig>))]
    [JsonSerializable(typeof(TaskQueryParams))]

    [ExcludeFromCodeCoverage]
    internal sealed partial class JsonContext : JsonSerializerContext;
}
