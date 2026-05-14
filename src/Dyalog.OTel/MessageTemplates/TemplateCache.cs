namespace Dyalog.OTel.MessageTemplates;

/// <summary>
/// Caches parsed message templates keyed by the raw template string.
/// Size-capped to prevent unbounded growth from accidental dynamic templates.
/// Access is single-threaded (interpreter thread only) so a plain Dictionary suffices.
/// </summary>
public sealed class TemplateCache
{
    /// <summary>Shared singleton (interpreter thread only — no contention).</summary>
    public static readonly TemplateCache Instance = new();

    private readonly Dictionary<string, MessageTemplate> _cache = new();
    private readonly int _maxSize;

    public TemplateCache(int maxSize = 1024)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Get or parse a message template. Returns cached instance on subsequent calls.
    /// When the cache is full, the oldest entry is evicted (simple clear strategy).
    /// </summary>
    public MessageTemplate GetOrParse(string template)
    {
        if (_cache.TryGetValue(template, out var cached))
            return cached;

        if (_cache.Count >= _maxSize)
            _cache.Clear(); // Simple eviction: full reset. Acceptable for 1024 cap.

        var parsed = MessageTemplate.Parse(template);
        _cache[template] = parsed;
        return parsed;
    }
}
