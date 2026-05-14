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
        // Collect all names to delete (iterative breadth-first)
        var toDelete = new Queue<string>();
        toDelete.Enqueue(name);
        var deleted = new HashSet<string>();

        while (toDelete.Count > 0)
        {
            var current = toDelete.Dequeue();
            if (!deleted.Add(current))
                continue;

            _templates.TryRemove(current, out _);

            // Find children of the just-deleted template
            foreach (var kvp in _templates)
            {
                if (kvp.Value.ParentName == current)
                    toDelete.Enqueue(kvp.Key);
            }
        }
    }

    /// <summary>Look up a template by name. Returns null if not found.</summary>
    public TemplateSnapshot? TryGet(string name)
    {
        _templates.TryGetValue(name, out var snapshot);
        return snapshot;
    }
}
