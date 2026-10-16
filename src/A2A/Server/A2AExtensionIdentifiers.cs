namespace A2A;

/// <summary>Shared parsing of comma-separated extension service parameter values.</summary>
internal static class A2AExtensionIdentifiers
{
    internal static List<string> Parse(IEnumerable<string?> values)
    {
        var requested = new List<string>();
        foreach (var value in values)
        {
            if (value is null) continue;
            var start = 0;
            while (start <= value.Length)
            {
                var separator = value.IndexOf(',', start);
                var end = separator < 0 ? value.Length : separator;
                // Ignore empty list elements and trim HTTP optional whitespace only.
                // URI casing, escaping, and extension versions remain exact identifiers.
                var identifier = value[start..end].Trim(' ', '\t');
                if (identifier.Length > 0)
                {
                    if (identifier.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
                    {
                        throw new A2AException("The A2A-Extensions service parameter contains an invalid identifier.", A2AErrorCode.InvalidRequest);
                    }

                    requested.Add(identifier);
                }

                if (separator < 0) break;
                start = separator + 1;
            }
        }

        return requested;
    }
}
