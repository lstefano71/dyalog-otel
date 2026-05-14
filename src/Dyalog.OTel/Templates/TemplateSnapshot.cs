using System.Collections.Frozen;

namespace Dyalog.OTel.Templates;

/// <summary>
/// An immutable snapshot of key-value attributes. Referenced by signal records
/// via a strong reference — the GC keeps it alive until all in-flight signals
/// that reference it have been serialized, even if the template is deleted
/// from the registry.
/// </summary>
public sealed class TemplateSnapshot
{
    /// <summary>The template name (used for registry lookup and lifecycle).</summary>
    public required string Name { get; init; }

    /// <summary>Parent template name, or null for root templates.</summary>
    public string? ParentName { get; init; }

    /// <summary>Frozen attribute dictionary for fast read access on the background thread.</summary>
    public required FrozenDictionary<string, object> Attributes { get; init; }

    /// <summary>
    /// Creates a new snapshot by merging this template's attributes with additional ones.
    /// The extra attributes override any existing keys.
    /// </summary>
    public TemplateSnapshot Derive(string childName, IEnumerable<KeyValuePair<string, object>> extraAttributes)
    {
        var merged = new Dictionary<string, object>(Attributes);
        foreach (var kv in extraAttributes)
            merged[kv.Key] = kv.Value;

        return new TemplateSnapshot
        {
            Name = childName,
            ParentName = Name,
            Attributes = merged.ToFrozenDictionary()
        };
    }
}
