namespace Dyalog.OTel.MessageTemplates;

/// <summary>
/// A parsed message template with extracted placeholder names and literal segments.
/// Immutable after construction.
/// </summary>
public sealed class MessageTemplate
{
    /// <summary>The original template string.</summary>
    public string Original { get; }

    /// <summary>Literal text segments between placeholders.</summary>
    public string[] Segments { get; }

    /// <summary>Placeholder names in order of appearance.</summary>
    public string[] PlaceholderNames { get; }

    /// <summary>True if this template has at least one placeholder.</summary>
    public bool HasPlaceholders => PlaceholderNames.Length > 0;

    private MessageTemplate(string original, string[] segments, string[] placeholderNames)
    {
        Original = original;
        Segments = segments;
        PlaceholderNames = placeholderNames;
    }

    /// <summary>
    /// Parse a message template string. Extracts {Name} placeholders.
    /// Escaped braces {{ and }} are treated as literal braces.
    /// </summary>
    public static MessageTemplate Parse(string template)
    {
        var segments = new List<string>();
        var names = new List<string>();
        int pos = 0;
        var currentSegment = new System.Text.StringBuilder();

        while (pos < template.Length)
        {
            char c = template[pos];

            if (c == '{')
            {
                if (pos + 1 < template.Length && template[pos + 1] == '{')
                {
                    currentSegment.Append('{');
                    pos += 2;
                    continue;
                }

                segments.Add(currentSegment.ToString());
                currentSegment.Clear();

                int end = template.IndexOf('}', pos + 1);
                if (end < 0)
                {
                    // Malformed — treat rest as literal
                    currentSegment.Append(template, pos, template.Length - pos);
                    pos = template.Length;
                    break;
                }

                // Extract name (strip format specifier after ':' if present)
                string placeholder = template.Substring(pos + 1, end - pos - 1);
                int colonIdx = placeholder.IndexOf(':');
                string name = colonIdx >= 0 ? placeholder[..colonIdx] : placeholder;
                names.Add(name.Trim());

                pos = end + 1;
            }
            else if (c == '}' && pos + 1 < template.Length && template[pos + 1] == '}')
            {
                currentSegment.Append('}');
                pos += 2;
            }
            else
            {
                currentSegment.Append(c);
                pos++;
            }
        }

        segments.Add(currentSegment.ToString());
        return new MessageTemplate(template, segments.ToArray(), names.ToArray());
    }

    /// <summary>
    /// Render the template with the given values, substituting placeholders positionally.
    /// </summary>
    public string Render(ReadOnlySpan<object> values)
    {
        if (!HasPlaceholders) return Original;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Segments.Length; i++)
        {
            sb.Append(Segments[i]);
            if (i < PlaceholderNames.Length && i < values.Length)
                sb.Append(values[i]?.ToString() ?? "");
        }
        return sb.ToString();
    }
}
