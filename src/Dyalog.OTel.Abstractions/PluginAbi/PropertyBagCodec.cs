using System.Text;

namespace Dyalog.OTel.PluginAbi;

public static class PropertyBagCodec
{
    public static byte[] Encode(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        using var stream = new MemoryStream();
        foreach (var (key, value) in pairs)
        {
            WriteString(stream, key);
            WriteString(stream, value);
        }

        return stream.ToArray();
    }

    public static Dictionary<string, string> Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (index < bytes.Length)
        {
            string key = ReadString(bytes, ref index);
            if (key.Length == 0 && index >= bytes.Length)
                break;

            string value = ReadString(bytes, ref index);
            result[key] = value;
        }

        return result;
    }

    private static void WriteString(Stream stream, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        stream.WriteByte(0);
    }

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int index)
    {
        if (index >= bytes.Length)
            return "";

        int terminatorOffset = bytes[index..].IndexOf((byte)0);
        if (terminatorOffset < 0)
            throw new InvalidOperationException("Invalid property bag blob: missing string terminator.");

        string value = Encoding.UTF8.GetString(bytes.Slice(index, terminatorOffset));
        index += terminatorOffset + 1;
        return value;
    }
}
