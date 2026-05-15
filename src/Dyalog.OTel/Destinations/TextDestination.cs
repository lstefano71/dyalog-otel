using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Destinations;

public sealed class TextDestination : IDestination, IResourceAwareDestination, IRawHistogramObservationDestination
{
    private readonly TextDestinationOptions _options;
    private readonly TextLineRenderer _renderer;
    private readonly TextHistogramSummaryEngine _summaryEngine;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _resource = new();

    private StreamWriter? _writer;
    private string _currentPeriodKey = "";
    private bool _missingEmitterWarningWritten;

    internal TextDestination(TextDestinationOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _renderer = new TextLineRenderer(options);
        _summaryEngine = new TextHistogramSummaryEngine(options.SummaryRules);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "text";

    public void SetResource(Dictionary<string, string> resource)
    {
        lock (_sync)
        {
            _resource.Clear();
            foreach (var kv in resource)
                _resource[kv.Key] = kv.Value;
        }
    }

    public void Init()
    {
        lock (_sync)
        {
            EnsureWriter(GetUtcNow());
            if (_options.EmitStartupBlock)
                WriteBlock(_renderer.RenderStartupBlock(GetUtcNow(), _resource, _options.Path));
        }
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        lock (_sync)
        {
            EmitReadySummaries();
            if (!_options.AcceptLogs || batch.Length == 0)
                return;

            foreach (var record in batch)
            {
                string emitter = _renderer.ResolveEmitter(record, out var missingEmitter);
                WarnOnceForMissingEmitter(missingEmitter);
                WriteBlock(_renderer.RenderLog(record, emitter));
            }
        }
    }

    public void WriteSpans(ReadOnlySpan<SpanRecord> batch)
    {
    }

    public void WriteMetrics(ReadOnlySpan<MetricPoint> batch)
    {
    }

    void IRawHistogramObservationDestination.WriteRawHistogramMetrics(ReadOnlySpan<MetricPoint> batch)
    {
        lock (_sync)
        {
            EmitReadySummaries();
            if (!_options.AcceptMetricSummaries || batch.Length == 0)
                return;

            foreach (var point in batch)
            {
                string emitter = _renderer.ResolveEmitter(point, out var missingEmitter);
                WarnOnceForMissingEmitter(missingEmitter);
                _summaryEngine.Observe(point, emitter);
            }

            EmitReadySummaries();
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            EmitReadySummaries();
            _writer?.Flush();
        }
    }

    public void Shutdown()
    {
        lock (_sync)
        {
            EmitReadySummaries();

            if (_options.AcceptMetricSummaries)
            {
                foreach (var summary in _summaryEngine.DrainPartial(GetUtcNow()))
                    WriteBlock(_renderer.RenderSummary(summary));
            }

            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _currentPeriodKey = "";
        }
    }

    public void Dispose() => Shutdown();

    private void EmitReadySummaries()
    {
        if (!_options.AcceptMetricSummaries || !_summaryEngine.HasRules)
            return;

        foreach (var summary in _summaryEngine.DrainReady(GetUtcNow()))
            WriteBlock(_renderer.RenderSummary(summary));
    }

    private void WarnOnceForMissingEmitter(bool missingEmitter)
    {
        if (!missingEmitter || _missingEmitterWarningWritten)
            return;

        Console.Error.WriteLine("[dyalog-otel] WARNING: destination.text received a signal without the canonical 'emitter' attribute. Rendering placeholder output.");
        _missingEmitterWarningWritten = true;
    }

    private void WriteBlock(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return;

        EnsureWriter(GetUtcNow());
        foreach (var line in lines)
            _writer!.WriteLine(line);
    }

    private void EnsureWriter(DateTimeOffset nowUtc)
    {
        string periodKey = GetPeriodKey(nowUtc.UtcDateTime);
        if (_writer != null && periodKey == _currentPeriodKey)
            return;

        _writer?.Flush();
        _writer?.Dispose();

        _currentPeriodKey = periodKey;
        string filePath = BuildRotatedPath(periodKey);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _writer = new StreamWriter(filePath, append: true) { AutoFlush = false };
    }

    private string BuildRotatedPath(string periodKey)
    {
        if (_options.Rotation == RotationPeriod.None)
            return _options.Path;

        string directory = Path.GetDirectoryName(_options.Path) ?? ".";
        string name = Path.GetFileNameWithoutExtension(_options.Path);
        string extension = Path.GetExtension(_options.Path);
        return Path.Combine(directory, $"{name}-{periodKey}{extension}");
    }

    private string GetPeriodKey(DateTime utcNow) => _options.Rotation switch
    {
        RotationPeriod.Hourly => utcNow.ToString("yyyy-MM-dd-HH"),
        RotationPeriod.Daily => utcNow.ToString("yyyy-MM-dd"),
        RotationPeriod.Monthly => utcNow.ToString("yyyy-MM"),
        _ => ""
    };

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();
}
