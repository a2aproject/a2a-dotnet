namespace A2A;

/// <summary>
/// Defines the set of Part kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings.
/// </remarks>
internal static class PartKind
{
    /// <summary>
    /// A text part containing plain textual content.
    /// </summary>
    /// <seealso cref="TextPart"/>
    public const string Text = "text";

    /// <summary>
    /// A file part containing file content.
    /// </summary>
    /// <seealso cref="FilePart"/>
    public const string File = "file";

    /// <summary>
    /// A data part containing structured JSON data.
    /// </summary>
    /// <seealso cref="DataPart"/>
    public const string Data = "data";
}