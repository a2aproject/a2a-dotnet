using A2A;
using System.Text.Json;

#pragma warning disable EA0011 // Consider removing unnecessary conditional access operator
#pragma warning disable EA0013 // Consider removing unnecessary coalescing

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides extension methods for integrating with <see cref="AIContent"/> and other types from Microsoft.Extensions.AI.
/// </summary>
internal static class AIContentExtensions
{
    /// <summary>Creates a <see cref="ChatMessage"/> from the A2A <see cref="AgentMessage"/>.</summary>
    /// <param name="agentMessage">The agent message to convert to an <see cref="ChatMessage"/>.</param>
    /// <returns>The <see cref="ChatMessage"/> created to represent the <see cref="AgentMessage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agentMessage"/> is <see langword="null"/>.</exception>
    public static ChatMessage ToChatMessage(this AgentMessage agentMessage)
    {
        if (agentMessage is null)
        {
            throw new ArgumentNullException(nameof(agentMessage));
        }

        return new()
        {
            Contents = agentMessage.Parts.ConvertAll(p => p.ToAIContent()),
            MessageId = agentMessage.MessageId,
            RawRepresentation = agentMessage,
            Role = agentMessage.Role switch
            {
                MessageRole.Agent => ChatRole.Assistant,
                _ => ChatRole.User,
            },
        };
    }

    /// <summary>Creates an A2A <see cref="AgentMessage"/> from the Microsoft.Extensions.AI <see cref="ChatMessage"/>.</summary>
    /// <param name="chatMessage">The chat message to convert to an <see cref="AgentMessage"/>.</param>
    /// <returns>The <see cref="AgentMessage"/> created to represent the <see cref="ChatMessage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatMessage"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If the <paramref name="chatMessage"/>'s <see cref="ChatMessage.RawRepresentation"/> is already a <see cref="AgentMessage"/>,
    /// that existing instance is returned.
    /// </remarks>
    public static AgentMessage ToAgentMessage(this ChatMessage chatMessage)
    {
        if (chatMessage is null)
        {
            throw new ArgumentNullException(nameof(chatMessage));
        }

        if (chatMessage.RawRepresentation is AgentMessage existingAgentMessage)
        {
            return existingAgentMessage;
        }

        return new AgentMessage
        {
            MessageId = chatMessage.MessageId ?? Guid.NewGuid().ToString("N"),
            Parts = chatMessage.Contents.Select(ToPart).Where(p => p is not null).ToList()!,
            Role = chatMessage.Role == ChatRole.Assistant ? MessageRole.Agent : MessageRole.User,
        };
    }

    /// <summary>Creates an <see cref="AIContent"/> from the A2A <see cref="Part"/>.</summary>
    /// <param name="part">The part to convert to an <see cref="AIContent"/>.</param>
    /// <returns>The <see cref="AIContent"/> created to represent the <see cref="Part"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="part"/> is <see langword="null"/>.</exception>
    public static AIContent ToAIContent(this Part part)
    {
        if (part is null)
        {
            throw new ArgumentNullException(nameof(part));
        }

        AIContent? content = null;
        switch (part)
        {
            case TextPart textPart:
                content = new TextContent(textPart.Text);
                break;

            case FilePart { File: { } file }:
                if (file.Uri is not null)
                {
                    content = new UriContent(file.Uri, file.MimeType ?? "application/octet-stream");
                }
                else if (file.Bytes is not null)
                {
                    content = new DataContent(Convert.FromBase64String(file.Bytes), file.MimeType ?? "application/octet-stream")
                    {
                        Name = file.Name,
                    };
                }
                break;

            case DataPart dataPart:
                content = new DataContent(
                    JsonSerializer.SerializeToUtf8Bytes(dataPart.Data, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Dictionary<string, JsonElement>))),
                    "application/json");
                break;
        }

        content ??= new AIContent();

        content.RawRepresentation = part;
        if (part.Metadata is { Count: > 0 } metadata)
        {
            content.AdditionalProperties = [];
            foreach (var kvp in metadata)
            {
                content.AdditionalProperties[kvp.Key] = kvp.Value;
            }
        }

        return content;
    }

    /// <summary>Creates an A2A <see cref="Part"/> from the Microsoft.Extensions.AI <see cref="AIContent"/>.</summary>
    /// <param name="content">The content to convert to a <see cref="Part"/>.</param>
    /// <returns>The <see cref="Part"/> created to represent the <see cref="AIContent"/>, or null if the content could not be mapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If the <paramref name="content"/>'s <see cref="AIContent.RawRepresentation"/> is already a <see cref="Part"/>,
    /// that existing instance is returned.
    /// </remarks>
    public static Part? ToPart(this AIContent content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (content.RawRepresentation is Part existingPart)
        {
            return existingPart;
        }

        Part? part = null;
        switch (content)
        {
            case TextContent textContent:
                part = new TextPart { Text = textContent.Text };
                break;

            case UriContent uriContent:
                part = new FilePart
                {
                    File = new FileContent(uriContent.Uri) { MimeType = uriContent.MediaType },
                };
                break;

            case DataContent dataContent:
                part = new FilePart
                {
                    File = new FileContent(dataContent.Base64Data.ToString()) { MimeType = dataContent.MediaType },
                };
                break;
        }

        if (part is not null && content.AdditionalProperties is { Count: > 0 } props)
        {
            foreach (var kvp in props)
            {
                try
                {
                    (part.Metadata ??= [])[kvp.Key] = JsonSerializer.SerializeToElement(kvp.Value, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
                }
                catch (JsonException)
                {
                    // Ignore properties that can't be converted to JsonElement
                }
            }
        }

        return part;
    }
}