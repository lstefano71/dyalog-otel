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
    /// Expected format: nested vector where each element is a 2-element nested vector (key, value).
    /// APL: ('key1' val1)('key2' val2)...
    /// </summary>
    public static OTelAttribute[]? ReadAttributes(Localp attrs)
    {
        if (!attrs.HasValue) return null;

        int count = attrs.Bound();
        if (count == 0) return null;

        // Each element is a 2-element nested vector: (key value)
        var result = new OTelAttribute[count];
        for (int i = 0; i < count; i++)
        {
            string key = attrs.ReadString(i * 2);     // flat: key1 val1 key2 val2 ...
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
    /// attrs contains plain values (not key-value pairs), matched
    /// positionally with placeholder names from the template.
    /// </summary>
    public static OTelAttribute[] ReadFillers(Localp attrs, string[] placeholderNames)
    {
        int count = Math.Min(attrs.Bound(), placeholderNames.Length);
        var result = new OTelAttribute[count];
        for (int i = 0; i < count; i++)
        {
            object value = ReadValue(attrs, i);
            result[i] = new OTelAttribute(placeholderNames[i], value);
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
