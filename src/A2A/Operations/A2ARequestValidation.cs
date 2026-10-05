namespace A2A;

/// <summary>
/// Shared request-level validation for the standard A2A operations. Invoked both by the
/// operation catalog (for the JSON-RPC and HTTP+JSON transports) and directly by
/// <see cref="A2AServer"/>, so every <see cref="IA2ARequestHandler"/> caller — including
/// transports such as gRPC that bypass the catalog entirely — enforces the same rules.
/// </summary>
internal static class A2ARequestValidation
{
    /// <summary>Validates a <see cref="SendMessageRequest"/>.</summary>
    /// <param name="request">The request to validate.</param>
    public static void ValidateSendMessage(SendMessageRequest request)
    {
        if (request.Message is null)
        {
            throw new A2AException(
                "Message is required",
                A2AErrorCode.InvalidParams);
        }

        if (request.Message.Parts is null || request.Message.Parts.Count == 0)
        {
            throw new A2AException(
                "Message parts cannot be empty",
                A2AErrorCode.InvalidParams);
        }
    }

    /// <summary>Validates a history-length value.</summary>
    /// <param name="historyLength">The history length to validate.</param>
    public static void ValidateHistoryLength(int? historyLength)
    {
        if (historyLength is { } value && value < 0)
        {
            throw new A2AException(
                $"Invalid historyLength: {value}. Must be non-negative.",
                A2AErrorCode.InvalidParams);
        }
    }

    /// <summary>Validates a page-size value.</summary>
    /// <param name="pageSize">The page size to validate.</param>
    public static void ValidatePageSize(int? pageSize)
    {
        if (pageSize is { } value && (value <= 0 || value > 100))
        {
            throw new A2AException(
                $"Invalid pageSize: {value}. Must be between 1 and 100.",
                A2AErrorCode.InvalidParams);
        }
    }
}
