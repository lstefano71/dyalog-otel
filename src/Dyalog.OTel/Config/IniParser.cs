namespace Dyalog.OTel.Config;

/// <summary>
/// Minimal INI parser. Supports [sections], key=value pairs, # and ; comments,
/// comma-separated lists, and blank lines. No dependencies.
/// </summary>
public static class IniParser
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";
        result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                continue;

            // Section header
            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            // Key = Value
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                result[currentSection][key] = value;
            }
        }

        return result;
    }

    public static Dictionary<string, Dictionary<string, string>> ParseFile(string path)
    {
        return Parse(File.ReadAllText(path));
    }
}
