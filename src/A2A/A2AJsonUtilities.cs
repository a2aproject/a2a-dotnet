using Microsoft.Extensions.AI;
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
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Chain with all supported types from MEAI.
        opts.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);

        opts.MakeReadOnly();
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

    // Core types
    [JsonSerializable(typeof(AgentTask))]
    [JsonSerializable(typeof(Message))]
    [JsonSerializable(typeof(Part))]
    [JsonSerializable(typeof(Artifact))]
    [JsonSerializable(typeof(TaskStatus))]
    [JsonSerializable(typeof(TaskState))]
    [JsonSerializable(typeof(Role))]

    // Event types
    [JsonSerializable(typeof(TaskStatusUpdateEvent))]
    [JsonSerializable(typeof(TaskArtifactUpdateEvent))]

    // Response types
    [JsonSerializable(typeof(SendMessageResponse))]
    [JsonSerializable(typeof(StreamResponse))]
    [JsonSerializable(typeof(ListTasksResponse))]
    [JsonSerializable(typeof(ListTaskPushNotificationConfigResponse))]

    // Agent discovery
    [JsonSerializable(typeof(AgentCard))]
    [JsonSerializable(typeof(AgentInterface))]
    [JsonSerializable(typeof(AgentCapabilities))]
    [JsonSerializable(typeof(AgentProvider))]
    [JsonSerializable(typeof(AgentSkill))]
    [JsonSerializable(typeof(AgentExtension))]
    [JsonSerializable(typeof(AgentCardSignature))]

    // Security
    [JsonSerializable(typeof(SecurityScheme))]
    [JsonSerializable(typeof(ApiKeySecurityScheme))]
    [JsonSerializable(typeof(HttpAuthSecurityScheme))]
    [JsonSerializable(typeof(OAuth2SecurityScheme))]
    [JsonSerializable(typeof(OpenIdConnectSecurityScheme))]
    [JsonSerializable(typeof(MutualTlsSecurityScheme))]
    [JsonSerializable(typeof(OAuthFlows))]
    [JsonSerializable(typeof(AuthorizationCodeOAuthFlow))]
    [JsonSerializable(typeof(ClientCredentialsOAuthFlow))]
    [JsonSerializable(typeof(DeviceCodeOAuthFlow))]
#pragma warning disable CS0618 // Obsolete types
    [JsonSerializable(typeof(ImplicitOAuthFlow))]
    [JsonSerializable(typeof(PasswordOAuthFlow))]
#pragma warning restore CS0618
    [JsonSerializable(typeof(SecurityRequirement))]
    [JsonSerializable(typeof(StringList))]

    // Request types
    [JsonSerializable(typeof(SendMessageRequest))]
    [JsonSerializable(typeof(SendMessageConfiguration))]
    [JsonSerializable(typeof(GetTaskRequest))]
    [JsonSerializable(typeof(ListTasksRequest))]
    [JsonSerializable(typeof(CancelTaskRequest))]
    [JsonSerializable(typeof(SubscribeToTaskRequest))]
    [JsonSerializable(typeof(CreateTaskPushNotificationConfigRequest))]
    [JsonSerializable(typeof(GetTaskPushNotificationConfigRequest))]
    [JsonSerializable(typeof(ListTaskPushNotificationConfigRequest))]
    [JsonSerializable(typeof(DeleteTaskPushNotificationConfigRequest))]
    [JsonSerializable(typeof(GetExtendedAgentCardRequest))]

    // Push notification types
    [JsonSerializable(typeof(PushNotificationConfig))]
    [JsonSerializable(typeof(AuthenticationInfo))]
    [JsonSerializable(typeof(TaskPushNotificationConfig))]

    [ExcludeFromCodeCoverage]
    internal sealed partial class JsonContext : JsonSerializerContext;
}
