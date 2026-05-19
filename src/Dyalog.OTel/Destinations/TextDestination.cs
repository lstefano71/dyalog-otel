using System.Text;
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
    private readonly CooperativeFileWriter _fileWriter;

    private bool _missingEmitterWarningWritten;

    internal TextDestination(TextDestinationOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _renderer = new TextLineRenderer(options);
        _summaryEngine = new TextHistogramSummaryEngine(options.SummaryRules);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileWriter = new CooperativeFileWriter(
            options.Path,
            options.Rotation,
            options.Sharing,
            options.Flush,
            options.LockStyle,
            options.LockTimeoutMs);
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
            _fileWriter.Open();
            if (_options.EmitStartupBlock)
            {
                var sb = new StringBuilder();
                AppendBlock(sb, _renderer.RenderStartupBlock(GetUtcNow(), _resource, _options.Path));
                _fileWriter.Write(sb);
            }
        }
    }

    public void WriteLogs(ReadOnlySpan<LogRecord> batch)
    {
        lock (_sync)
        {
            var sb = new StringBuilder();
            AppendReadySummaries(sb);
            if (!_options.AcceptLogs || batch.Length == 0)
            {
                if (sb.Length > 0) _fileWriter.Write(sb);
                return;
            }

            foreach (var record in batch)
            {
                string emitter = _renderer.ResolveEmitter(record, out var missingEmitter);
                WarnOnceForMissingEmitter(missingEmitter);
                AppendBlock(sb, _renderer.RenderLog(record, emitter));
            }

            _fileWriter.Write(sb);
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
            var sb = new StringBuilder();
            AppendReadySummaries(sb);
            if (!_options.AcceptMetricSummaries || batch.Length == 0)
            {
                if (sb.Length > 0) _fileWriter.Write(sb);
                return;
            }

            foreach (var point in batch)
            {
                string emitter = _renderer.ResolveEmitter(point, out var missingEmitter);
                WarnOnceForMissingEmitter(missingEmitter);
                _summaryEngine.Observe(point, emitter);
            }

            AppendReadySummaries(sb);
            if (sb.Length > 0) _fileWriter.Write(sb);
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            var sb = new StringBuilder();
            AppendReadySummaries(sb);
            if (sb.Length > 0) _fileWriter.Write(sb);
            _fileWriter.Flush();
        }
    }

    public void Shutdown()
    {
        lock (_sync)
        {
            var sb = new StringBuilder();
            AppendReadySummaries(sb);

            if (_options.AcceptMetricSummaries)
            {
                foreach (var summary in _summaryEngine.DrainPartial(GetUtcNow()))
                    AppendBlock(sb, _renderer.RenderSummary(summary));
            }

            if (sb.Length > 0) _fileWriter.Write(sb);
            _fileWriter.Shutdown();
        }
    }

    public void Dispose() => Shutdown();

    private void AppendReadySummaries(StringBuilder sb)
    {
        if (!_options.AcceptMetricSummaries || !_summaryEngine.HasRules)
            return;

        foreach (var summary in _summaryEngine.DrainReady(GetUtcNow()))
            AppendBlock(sb, _renderer.RenderSummary(summary));
    }

    private static void AppendBlock(StringBuilder sb, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
            sb.AppendLine(line);
    }

    private void WarnOnceForMissingEmitter(bool missingEmitter)
    {
        if (!missingEmitter || _missingEmitterWarningWritten)
            return;

        Console.Error.WriteLine("[dyalog-otel] WARNING: destination.text received a signal without the canonical 'emitter' attribute. Rendering placeholder output.");
        _missingEmitterWarningWritten = true;
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();
}
