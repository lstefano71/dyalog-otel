using Dyalog.DWA;
using Dyalog.DWA.Native;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Exports;
/// <summary>
/// Helper methods for reading APL arguments into managed types.
/// Used by all export classes on the hot path.
/// </summary>
internal static class ExportHelpers
{
    /// <summary>
    /// Read a nested APL array as key-value attribute pairs.
    /// Expected format: flat nested vector with alternating keys and values.
    /// APL: 'key1' val1 'key2' val2
    /// </summary>
    public static OTelAttribute[]? ReadAttributes(Localp attrs)
    {
        if (!attrs.HasValue) return null;

        int bound = attrs.Bound();
        if (bound < 2) return null;

        int pairCount = bound / 2;
        var result = new OTelAttribute[pairCount];
        for (int i = 0; i < pairCount; i++)
        {
            string key = attrs.ReadString(i * 2);
            object value = ReadValue(attrs, i * 2 + 1);
            result[i] = new OTelAttribute(key, value);
        }
        return result;
    }

    /// <summary>
    /// Read attributes as KeyValuePair for template creation.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, object>> ReadAttributesAsKvp(Localp attrs)
    {
        var otelAttrs = ReadAttributes(attrs);
        if (otelAttrs == null) return Enumerable.Empty<KeyValuePair<string, object>>();
        return otelAttrs.Select(a => new KeyValuePair<string, object>(a.Key, a.Value));
    }

    /// <summary>
    /// Read positional fillers for message template expansion.
    /// attrs can be a simple numeric vector or a nested vector of mixed values.
    /// </summary>
    public static OTelAttribute[] ReadFillers(Localp attrs, string[] placeholderNames)
    {
        int count = Math.Min(attrs.Bound(), placeholderNames.Length);
        var result = new OTelAttribute[count];

        if (attrs.IsSimple())
        {
            // Simple numeric vector: read all as doubles
            var doubles = attrs.ReadAsDoubles();
            for (int i = 0; i < count; i++)
                result[i] = new OTelAttribute(placeholderNames[i], doubles[i]);
        }
        else
        {
            // Nested: each element can be a different type
            for (int i = 0; i < count; i++)
            {
                object value = ReadValue(attrs, i);
                result[i] = new OTelAttribute(placeholderNames[i], value);
            }
        }
        return result;
    }

    /// <summary>
    /// Read a single value from a nested element. Dispatches by APL type.
    /// </summary>
    private static object ReadValue(Localp lp, int index)
    {
        // Check element type at the nested index
        var eltype = lp.ElType(index);
        return eltype switch
        {
            ELTYPES.APLWCHAR8 or ELTYPES.APLWCHAR16 or ELTYPES.APLWCHAR32 or ELTYPES.APLNCHAR
                => lp.ReadString(index),
            ELTYPES.APLDOUB
                => lp.ReadDouble(index),
            ELTYPES.APLLONG or ELTYPES.APLINTG or ELTYPES.APLSINT
                => (long)lp.ReadInt32(index),
            _ => lp.ReadString(index)  // Fallback: try as string
        };
    }
}
