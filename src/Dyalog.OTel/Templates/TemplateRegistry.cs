using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Dyalog.OTel.Templates;

/// <summary>
/// Thread-safe registry of named template snapshots.
/// String-keyed lookup on the hot path (~50-80ns for short names).
/// Cascade-delete via parent-child links.
/// </summary>
public sealed class TemplateRegistry
{
    private readonly ConcurrentDictionary<string, TemplateSnapshot> _templates = new();

    /// <summary>Register a new root template.</summary>
    public void Create(string name, IEnumerable<KeyValuePair<string, object>> attributes)
    {
        var snapshot = new TemplateSnapshot
        {
            Name = name,
            ParentName = null,
            Attributes = attributes.ToFrozenDictionary()
        };
        _templates[name] = snapshot;
    }

    /// <summary>
    /// Derive a child template from an existing parent (snapshot-on-derive).
    /// The child gets a copy of the parent's attributes merged with extras.
    /// </summary>
    public bool Derive(string childName, string parentName, IEnumerable<KeyValuePair<string, object>> extraAttributes)
    {
        if (!_templates.TryGetValue(parentName, out var parent))
            return false;

        _templates[childName] = parent.Derive(childName, extraAttributes);
        return true;
    }

    /// <summary>
    /// Delete a template and cascade to all children.
    /// In-flight signals that reference deleted snapshots keep them alive via GC.
    /// </summary>
    public void Delete(string name)
    {
        _templates.TryRemove(name, out _);

        // Cascade: find all templates that have this as parent
        foreach (var kvp in _templates)
        {
            if (kvp.Value.ParentName == name)
                Delete(kvp.Key);
        }
    }

    /// <summary>Look up a template by name. Returns null if not found.</summary>
    public TemplateSnapshot? TryGet(string name)
    {
        _templates.TryGetValue(name, out var snapshot);
        return snapshot;
    }
}
