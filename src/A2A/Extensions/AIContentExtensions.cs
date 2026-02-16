using A2A;
using System;
using System.Text.Json;

#pragma warning disable EA0011 // Consider removing unnecessary conditional access operator
#pragma warning disable EA0013 // Consider removing unnecessary coalescing

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides extension methods for integrating with <see cref="AIContent"/> and other types from Microsoft.Extensions.AI.
/// </summary>
public static class AIContentExtensions
{
    /// <summary>Creates a <see cref="ChatMessage"/> from the A2A <see cref="Message"/>.</summary>
    /// <param name="message">The message to convert to an <see cref="ChatMessage"/>.</param>
    /// <returns>The <see cref="ChatMessage"/> created to represent the <see cref="Message"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public static ChatMessage ToChatMessage(this Message message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new()
        {
            AdditionalProperties = message.Metadata.ToAdditionalProperties(),
            Contents = message.Parts.ConvertAll(p => p.ToAIContent()),
            MessageId = message.MessageId,
            RawRepresentation = message,
            Role = message.Role switch
            {
                Role.Agent => ChatRole.Assistant,
                _ => ChatRole.User,
            },
        };
    }

    /// <summary>Creates an A2A <see cref="Message"/> from the Microsoft.Extensions.AI <see cref="ChatMessage"/>.</summary>
    /// <param name="chatMessage">The chat message to convert to an <see cref="Message"/>.</param>
    /// <returns>The <see cref="Message"/> created to represent the <see cref="ChatMessage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatMessage"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If the <paramref name="chatMessage"/>'s <see cref="ChatMessage.RawRepresentation"/> is already a <see cref="Message"/>,
    /// that existing instance is returned.
    /// </remarks>
    public static Message ToA2AMessage(this ChatMessage chatMessage)
    {
        if (chatMessage is null)
        {
            throw new ArgumentNullException(nameof(chatMessage));
        }

        if (chatMessage.RawRepresentation is Message existingMessage)
        {
            return existingMessage;
        }

        return new Message
        {
            MessageId = chatMessage.MessageId ?? Guid.NewGuid().ToString("N"),
            Parts = chatMessage.Contents.Select(ToPart).Where(p => p is not null).ToList()!,
            Role = chatMessage.Role == ChatRole.Assistant ? Role.Agent : Role.User,
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

        if (part.Text is not null)
        {
            content = new TextContent(part.Text);
        }
        else if (part.Url is not null)
        {
            content = new UriContent(part.Url, part.MediaType ?? "application/octet-stream");
        }
        else if (part.Raw is not null)
        {
            content = new DataContent(part.Raw, part.MediaType ?? "application/octet-stream")
            {
                Name = part.Filename,
            };
        }
        else if (part.Data is { } data)
        {
            content = new DataContent(
                JsonSerializer.SerializeToUtf8Bytes(data, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement))),
                "application/json");
        }

        content ??= new AIContent();

        content.AdditionalProperties = part.Metadata.ToAdditionalProperties();
        content.RawRepresentation = part;

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
                part = Part.FromText(textContent.Text);
                break;

            case UriContent uriContent:
                part = Part.FromUrl(uriContent.Uri.ToString(), uriContent.MediaType);
                break;

            case DataContent dataContent:
                part = Part.FromRaw(dataContent.Data.ToArray(), dataContent.MediaType);
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