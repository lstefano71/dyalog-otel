using System.Collections;
using System.Globalization;
using Dyalog.OTel.Channels;
using Dyalog.OTel.Templates;

namespace Dyalog.OTel.Destinations;

internal sealed class TextLineRenderer
{
    private const string StartupEmitter = "OTEL";

    private readonly TextDestinationOptions _options;

    public TextLineRenderer(TextDestinationOptions options)
    {
        _options = options;
    }

    public string ResolveEmitter(LogRecord record, out bool missing) =>
        ResolveEmitter(record.Attributes, record.Template, out missing);

    public string ResolveEmitter(MetricPoint point, out bool missing) =>
        ResolveEmitter(point.Attributes, point.Template, out missing);

    public IReadOnlyList<string> RenderLog(LogRecord record, string emitter)
    {
        string timestamp = FormatTimestamp(FromUnixNano(record.TimestampUnixNano));
        string? severity = GetDisplayedSeverity(record.SeverityNumber);
        string[] messageLines = SplitLines(record.Body);
        string? attributeTail = BuildPromotedAttributeTail(record.Attributes, record.Template);
        var lines = new List<string>(messageLines.Length);

        for (int i = 0; i < messageLines.Length; i++)
        {
            string? tail = i == 0 ? attributeTail : null;
            lines.Add(BuildLine(timestamp, emitter, severity, messageLines[i], tail));
        }

        return lines;
    }

    public IReadOnlyList<string> RenderSummary(TextSummaryBlock summary)
    {
        string timestamp = FormatTimestamp(summary.TimestampUtc);
        string headerLabel = summary.IsPartial
            ? $"Final partial {FormatIntervalLabel(summary.Interval)} for {summary.MetricName}:"
            : $"{FormatIntervalLabel(summary.Interval)} for {summary.MetricName}:";

        var lines = new List<string>
        {
            BuildLine(timestamp, summary.Emitter, null, headerLabel, null),
            BuildLine(timestamp, summary.Emitter, null,
                $"Count: {summary.Count}, min: {FormatNumber(summary.Min)}, max: {FormatNumber(summary.Max)}",
                null)
        };

        string percentileText = string.Join(", ", summary.Percentiles.Select(
            p => $"p{FormatPercentile(p.Percentile)}: {FormatNumber(p.Value)}"));
        lines.Add(BuildLine(timestamp, summary.Emitter, null, $"Percentiles: {percentileText}", null));

        if (summary.IncludeBars)
        {
            long maxBucket = summary.BucketCounts.DefaultIfEmpty(0).Max();
            for (int i = 0; i < summary.BucketCounts.Length; i++)
            {
                long count = summary.BucketCounts[i];
                if (count == 0)
                    continue;

                (double lower, double upper) = GetBucketRange(summary.ExplicitBounds, i);
                string bars = BuildBars(count, maxBucket, width: 60);
                lines.Add(BuildLine(
                    timestamp,
                    summary.Emitter,
                    null,
                    $"| {FormatNumber(lower),7} - {FormatNumber(upper),7}: {bars}",
                    null));
            }
        }

        return lines;
    }

    public IReadOnlyList<string> RenderStartupBlock(DateTimeOffset timestampUtc, IReadOnlyDictionary<string, string> resource, string outputPath)
    {
        string timestamp = FormatTimestamp(timestampUtc);
        var lines = new List<string>
        {
            BuildLine(timestamp, StartupEmitter, null, "Text destination started", null),
            BuildLine(timestamp, StartupEmitter, null, $"Text stream: {outputPath}", null)
        };

        if (resource.TryGetValue("service.name", out var serviceName))
            lines.Add(BuildLine(timestamp, StartupEmitter, null, $"Service: {serviceName}", null));
        if (resource.TryGetValue("host.name", out var hostName))
            lines.Add(BuildLine(timestamp, StartupEmitter, null, $"Host: {hostName}", null));
        if (resource.TryGetValue("process.pid", out var processId))
            lines.Add(BuildLine(timestamp, StartupEmitter, null, $"Process ID: {processId}", null));
        if (resource.TryGetValue("apl.version", out var aplVersion))
            lines.Add(BuildLine(timestamp, StartupEmitter, null, $"APL Version: {aplVersion}", null));

        return lines;
    }

    private string ResolveEmitter(OTelAttribute[]? attributes, TemplateSnapshot? template, out bool missing)
    {
        if (TextAttributeResolver.TryGetStringAttribute(attributes, template, "emitter", out var emitter)
            && !string.IsNullOrWhiteSpace(emitter))
        {
            missing = false;
            return emitter.Trim();
        }

        missing = true;
        return _options.MissingEmitterPlaceholder;
    }

    private string? BuildPromotedAttributeTail(OTelAttribute[]? attributes, TemplateSnapshot? template)
    {
        if (_options.PromotedAttributes.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var promoted in _options.PromotedAttributes)
        {
            if (promoted.Key.Equals("emitter", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TextAttributeResolver.TryGetAttribute(attributes, template, promoted.Key, out var value))
                continue;

            parts.Add($"{promoted.Label}: {FormatAttributeValue(value)}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static string? GetDisplayedSeverity(int severityNumber) => severityNumber switch
    {
        >= 21 => "FATAL",
        >= 17 => "ERROR",
        >= 13 => "WARNING",
        _ => null
    };

    private static string BuildLine(string timestamp, string emitter, string? severity, string message, string? tail)
    {
        var parts = new List<string> { timestamp, emitter };
        if (!string.IsNullOrEmpty(severity))
            parts.Add(severity);
        parts.Add(message);
        if (!string.IsNullOrEmpty(tail))
            parts.Add(tail);

        return string.Join(" ", parts);
    }

    private static string[] SplitLines(string value)
    {
        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] parts = normalized.Split('\n');
        if (parts.Length > 1 && parts[^1].Length == 0)
            return parts[..^1];

        return parts.Length == 0 ? [""] : parts;
    }

    private static string FormatTimestamp(DateTimeOffset timestampUtc)
        => timestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromUnixNano(long unixNano)
    {
        long ticks = unixNano / 100;
        return new DateTimeOffset(DateTime.UnixEpoch.Ticks + ticks, TimeSpan.Zero);
    }

    private static string FormatAttributeValue(object? value) => value switch
    {
        null => "\"\"",
        string s => QuoteIfNeeded(s),
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "\"\"",
        Array array => "[" + string.Join(", ", array.Cast<object?>().Select(FormatAttributeValue)) + "]",
        IEnumerable sequence => "[" + string.Join(", ", sequence.Cast<object?>().Select(FormatAttributeValue)) + "]",
        _ => QuoteIfNeeded(value.ToString() ?? "")
    };

    private static string QuoteIfNeeded(string value)
    {
        string escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return escaped.IndexOfAny([',', ':', '"', ' ']) >= 0 ? $"\"{escaped}\"" : escaped;
    }

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatPercentile(double value) =>
        value.ToString(value % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);

    private static string FormatIntervalLabel(TimeSpan interval)
    {
        if (interval == TimeSpan.FromHours(1))
            return "Last hour";
        if (interval == TimeSpan.FromDays(1))
            return "Last day";
        if (interval.TotalHours >= 1 && interval.TotalHours % 1 == 0)
            return $"Last {interval.TotalHours:0} hours";
        return $"Last {interval.TotalMinutes:0} minutes";
    }

    private static (double Lower, double Upper) GetBucketRange(double[] bounds, int index)
    {
        double lower = index == 0 ? 0 : bounds[index - 1];
        double upper = index < bounds.Length ? bounds[index] : bounds[^1];
        return (lower, upper);
    }

    private static string BuildBars(long count, long maxBucket, int width)
    {
        if (count <= 0 || maxBucket <= 0)
            return "";

        int barLength = Math.Max(1, (int)Math.Round((double)count / maxBucket * width));
        return new string('*', barLength);
    }
}

internal static class TextAttributeResolver
{
    public static bool TryGetStringAttribute(OTelAttribute[]? attributes, TemplateSnapshot? template, string key, out string value)
    {
        if (TryGetAttribute(attributes, template, key, out var rawValue) && rawValue != null)
        {
            value = rawValue.ToString() ?? "";
            return true;
        }

        value = "";
        return false;
    }

    public static bool TryGetAttribute(OTelAttribute[]? attributes, TemplateSnapshot? template, string key, out object? value)
    {
        if (attributes != null)
        {
            for (int i = attributes.Length - 1; i >= 0; i--)
            {
                if (attributes[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = attributes[i].Value;
                    return true;
                }
            }
        }

        if (template != null)
        {
            foreach (var pair in template.Attributes)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
