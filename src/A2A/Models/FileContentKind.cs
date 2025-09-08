namespace A2A;

/// <summary>
/// Defines the set of FileContent kinds used as the 'kind' discriminator in serialized payloads.
/// </summary>
/// <remarks>
/// Values are serialized as lowercase kebab-case strings.
/// </remarks>
internal static class FileContentKind
{
    /// <summary>
    /// A file content containing bytes.
    /// </summary>
    /// <seealso cref="FileWithBytes"/>
    public const string Bytes = "bytes";

    /// <summary>
    /// A file content containing a URI.
    /// </summary>
    /// <seealso cref="FileWithUri"/>
    public const string Uri = "uri";
}