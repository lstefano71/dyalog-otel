using Dyalog.OTel.Config;

namespace Dyalog.OTel.Destinations;

internal sealed class TextDestinationOptions
{
    public required string Path { get; init; }
    public RotationPeriod Rotation { get; init; } = RotationPeriod.Monthly;
    public SharingMode Sharing { get; init; } = SharingMode.Cooperative;
    public FlushMode Flush { get; init; } = FlushMode.Batch;
    public LockStyle LockStyle { get; init; } = LockStyle.Inline;
    public int LockTimeoutMs { get; init; } = 5000;
    public bool EmitStartupBlock { get; init; }
    public bool AcceptLogs { get; init; } = true;
    public bool AcceptMetricSummaries { get; init; }
    public string MissingEmitterPlaceholder { get; init; } = "???";
    public IReadOnlyList<TextPromotedAttribute> PromotedAttributes { get; init; } = Array.Empty<TextPromotedAttribute>();
    public IReadOnlyList<TextSummaryMetricRule> SummaryRules { get; init; } = Array.Empty<TextSummaryMetricRule>();
}

internal sealed record TextPromotedAttribute(string Key, string Label);

internal sealed record TextSummaryMetricRule(
    string Name,
    TimeSpan Interval,
    IReadOnlyList<double> Percentiles,
    bool IncludeBars);

internal static class TextDestinationConfig
{
    private const string SummaryMetricPrefix = "summary.metric.";

    public static TextDestinationOptions Parse(DestinationConfig config)
    {
        if (!config.Properties.TryGetValue("path", out var rawPath) || string.IsNullOrWhiteSpace(rawPath))
            throw new InvalidOperationException("destination.text requires a non-empty path.");

        bool acceptLogs = config.Signals.Contains("log");
        bool acceptMetrics = config.Signals.Contains("metric");

        var promotedAttributes = ParsePromotedAttributes(config.Properties);
        var summaryRules = ParseSummaryRules(config.Properties);

        if (!acceptLogs && summaryRules.Count == 0)
            throw new InvalidOperationException("destination.text must accept logs, summaries, or both.");

        if (summaryRules.Count > 0 && !acceptMetrics)
            throw new InvalidOperationException("destination.text requires metric signals when summary metrics are configured.");

        return new TextDestinationOptions
        {
            Path = Path.GetFullPath(rawPath),
            Rotation = ParseRotation(config.Properties.GetValueOrDefault("rotate", "monthly")),
            Sharing = ParseSharing(config.Properties.GetValueOrDefault("sharing", "cooperative")),
            Flush = ParseFlush(config.Properties.GetValueOrDefault("flush", "batch")),
            LockStyle = ParseLockStyle(config.Properties.GetValueOrDefault("lock_style", "inline")),
            LockTimeoutMs = int.TryParse(config.Properties.GetValueOrDefault("lock_timeout", "5000"), out var lt) ? lt : 5000,
            EmitStartupBlock = ParseBoolean(config.Properties, "startup", defaultValue: false),
            AcceptLogs = acceptLogs,
            AcceptMetricSummaries = acceptMetrics && summaryRules.Count > 0,
            PromotedAttributes = promotedAttributes,
            SummaryRules = summaryRules
        };
    }

    private static List<TextPromotedAttribute> ParsePromotedAttributes(Dictionary<string, string> properties)
    {
        var keys = ParseList(properties.GetValueOrDefault("promote", ""));
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in properties)
        {
            if (!key.StartsWith("label.", StringComparison.OrdinalIgnoreCase))
                continue;

            string attributeKey = key["label.".Length..];
            if (!string.IsNullOrWhiteSpace(attributeKey))
                labels[attributeKey] = value;
        }

        var result = new List<TextPromotedAttribute>(keys.Count);
        foreach (var key in keys)
            result.Add(new TextPromotedAttribute(key, labels.GetValueOrDefault(key, key)));

        return result;
    }

    private static List<TextSummaryMetricRule> ParseSummaryRules(Dictionary<string, string> properties)
    {
        TimeSpan defaultInterval = ParseInterval(properties.GetValueOrDefault("summary.interval", "1h"));
        IReadOnlyList<double> defaultPercentiles = ParsePercentiles(properties.GetValueOrDefault("summary.percentiles", "50,90,95,99"));
        var builders = new Dictionary<string, SummaryRuleBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in properties)
        {
            if (!key.StartsWith(SummaryMetricPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string remainder = key[SummaryMetricPrefix.Length..];
            int separator = remainder.LastIndexOf('.');
            if (separator <= 0 || separator == remainder.Length - 1)
                throw new InvalidOperationException($"Invalid destination.text summary setting '{key}'.");

            string alias = remainder[..separator];
            string setting = remainder[(separator + 1)..];

            if (!builders.TryGetValue(alias, out var builder))
            {
                builder = new SummaryRuleBuilder();
                builders[alias] = builder;
            }

            switch (setting.ToLowerInvariant())
            {
                case "name":
                    builder.Name = value;
                    break;
                case "bars":
                    builder.IncludeBars = ParseBoolean(value);
                    break;
                case "percentiles":
                    builder.Percentiles = ParsePercentiles(value);
                    break;
                case "interval":
                    builder.Interval = ParseInterval(value);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported destination.text summary setting '{key}'.");
            }
        }

        var rules = new List<TextSummaryMetricRule>(builders.Count);
        var metricNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, builder) in builders.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(builder.Name))
                throw new InvalidOperationException("Each destination.text summary metric rule requires a non-empty .name value.");

            if (!metricNames.Add(builder.Name))
                throw new InvalidOperationException($"Duplicate destination.text summary metric '{builder.Name}'.");

            rules.Add(new TextSummaryMetricRule(
                builder.Name,
                builder.Interval ?? defaultInterval,
                builder.Percentiles ?? defaultPercentiles,
                builder.IncludeBars));
        }

        return rules;
    }

    private static RotationPeriod ParseRotation(string value) => value.Trim().ToLowerInvariant() switch
    {
        "hourly" => RotationPeriod.Hourly,
        "daily" => RotationPeriod.Daily,
        "monthly" => RotationPeriod.Monthly,
        "none" => RotationPeriod.None,
        _ => throw new InvalidOperationException($"Unsupported destination.text rotation '{value}'.")
    };

    private static SharingMode ParseSharing(string value) => value.Trim().ToLowerInvariant() switch
    {
        "cooperative" or "coop" => SharingMode.Cooperative,
        "exclusive" => SharingMode.Exclusive,
        _ => throw new InvalidOperationException($"Unsupported sharing mode '{value}'. Use 'cooperative' or 'exclusive'.")
    };

    private static FlushMode ParseFlush(string value) => value.Trim().ToLowerInvariant() switch
    {
        "batch" => FlushMode.Batch,
        "shutdown" => FlushMode.Shutdown,
        _ => throw new InvalidOperationException($"Unsupported flush mode '{value}'. Use 'batch' or 'shutdown'.")
    };

    private static LockStyle ParseLockStyle(string value) => value.Trim().ToLowerInvariant() switch
    {
        "inline" => LockStyle.Inline,
        "sidecar" => LockStyle.Sidecar,
        _ => throw new InvalidOperationException($"Unsupported lock_style '{value}'. Use 'inline' or 'sidecar'.")
    };

    private static bool ParseBoolean(Dictionary<string, string> properties, string key, bool defaultValue)
    {
        if (!properties.TryGetValue(key, out var rawValue))
            return defaultValue;

        return ParseBoolean(rawValue);
    }

    private static bool ParseBoolean(string rawValue) => rawValue.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => throw new InvalidOperationException($"Invalid boolean value '{rawValue}'.")
    };

    private static IReadOnlyList<string> ParseList(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var part in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(part))
                result.Add(part);
        }

        return result;
    }

    private static IReadOnlyList<double> ParsePercentiles(string rawValue)
    {
        var values = new List<double>();
        foreach (var part in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percentile))
                throw new InvalidOperationException($"Invalid percentile '{part}'.");

            if (percentile <= 0 || percentile > 100)
                throw new InvalidOperationException($"Percentile '{part}' must be > 0 and <= 100.");

            values.Add(percentile);
        }

        if (values.Count == 0)
            throw new InvalidOperationException("At least one percentile must be configured.");

        return values
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    private static TimeSpan ParseInterval(string rawValue)
    {
        string value = rawValue.Trim().ToLowerInvariant();
        if (value == "hourly")
            return TimeSpan.FromHours(1);
        if (value == "daily")
            return TimeSpan.FromDays(1);

        if (value.Length >= 2)
        {
            string numberPart = value[..^1];
            char suffix = value[^1];
            if (int.TryParse(numberPart, out var number) && number > 0)
            {
                return suffix switch
                {
                    'm' => TimeSpan.FromMinutes(number),
                    'h' => TimeSpan.FromHours(number),
                    'd' => TimeSpan.FromDays(number),
                    _ => throw new InvalidOperationException($"Unsupported destination.text summary interval '{rawValue}'.")
                };
            }
        }

        throw new InvalidOperationException($"Unsupported destination.text summary interval '{rawValue}'.");
    }

    private sealed class SummaryRuleBuilder
    {
        public string Name { get; set; } = "";
        public bool IncludeBars { get; set; }
        public IReadOnlyList<double>? Percentiles { get; set; }
        public TimeSpan? Interval { get; set; }
    }
}
