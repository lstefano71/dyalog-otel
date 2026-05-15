# dyalog-otel

High-performance telemetry library for Dyalog APL — logs, traces, and metrics with minimum interpreter-thread overhead.

Built as a NativeAOT C# DLL using the [DWA Kit](https://github.com/user/bridge-dwa) for zero-copy data access. The hot path (called from APL) only parses arguments and enqueues to bounded channels; all I/O happens on background threads.

## Features

- **Logs** — plain text or Serilog-style message templates (`'Order {OrderId} placed for {Amount}'`)
- **Traces** — explicit span start/end with parent-child nesting, log↔span correlation
- **Metrics** — counters, gauges, histograms
- **Destinations** — `text` and `console` in the core DLL, plus opt-in companion DLL families for JSONL (`file`, `http`) and OTLP (`otlp`)
- **Config** — INI file with 4-layer precedence (defaults → file → env vars → builder calls)
- **Attribute templates** — pre-built frozen snapshots for recurring key-value sets

## Prerequisites

- Dyalog APL 20.0+ (64-bit, Windows)
- .NET 10 SDK
- The [bridge-dwa](https://github.com/user/bridge-dwa) repo cloned as a sibling directory

## Build

```powershell
dotnet publish src\Dyalog.OTel\Dyalog.OTel.csproj -c Release
```

This produces the publish layout in `src\Dyalog.OTel\bin\Release\net10.0\win-x64\publish\`:
- `Dyalog.OTel.dll` — C shim (APL loads this via `⎕NA`)
- `Dyalog.OTel_impl.dll` — NativeAOT implementation
- `Dyalog.OTel.Destinations.Jsonl.dll` — native companion family for `destination.file` and `destination.http`
- `Dyalog.OTel.Destinations.Otlp.dll` — native companion family for `destination.otlp`

The config surface is unchanged: you still write `type=file|http|otlp|text|console`. The core DLL discovers companion DLLs by fixed name beside `Dyalog.OTel.dll`.

## Run tests

```powershell
.\run_apl.ps1 test_otel.apls
.\run_apl.ps1 test_text.apls
```

`test_otel.apls` (59 checks) exercises:
- Lifecycle: version, init, status, shutdown
- Logs: plain text, message templates with fillers, span-correlated logs
- Traces: span start/end, nested spans, parent-child relationships
- Metrics: counter, gauge, histogram
- Templates: create, use, delete
- Flush: drain all channels, verify stats
- **Content verification**: reads the JSONL output file after flush and checks every field (body, severity, timestamps, trace/span IDs, metric names/values/types, parent-child links)

`test_text.apls` adds a 17-check smoke test for the human-readable text destination.

Uses `test_otel.ini` / `test_text.ini` (auto-detected by `run_apl.ps1` as companion configs).

## Run benchmark

```powershell
.\run_apl.ps1 bench_otel.apls 120
```

Measures per-call hot-path overhead in microseconds for:
- Plain log
- Log with message template + positional fillers
- Log with key-value attributes
- Span start + end (round-trip)
- Metric counter increment

Reports net overhead (subtracting a bare-loop baseline), throughput in calls/sec, and drop statistics.

Uses `bench_otel.ini` (auto-detected).

## Test OTLP/Protobuf with Fluent Bit

Download and unzip [Fluent Bit](https://fluentbit.io/). Then:

```powershell
# Terminal 1 — start Fluent Bit as an OTLP collector
D:\path\to\fluent-bit\bin\fluent-bit.exe -c test_otlp_fluentbit.conf

# Terminal 2 — send logs, spans, and metrics via OTLP/Protobuf
.\run_apl.ps1 test_otlp_fluentbit.apls
```

Fluent Bit's terminal will show the received data: log bodies with severity and template-extracted attributes, span traces with IDs and durations, and metric values.

Uses `test_otlp_fluentbit.ini` (auto-detected) which configures an `otlp` destination with `protocol=protobuf` pointing at `localhost:4318`.

## Benchmark OTLP/Protobuf throughput

With Fluent Bit running (see above):

```powershell
.\run_apl.ps1 bench_otlp.apls 120
```

Sends 250,000 signal calls (logs, spans, metrics) through the full OTLP/Protobuf pipeline to the collector. Reports per-call overhead, aggregate throughput, and drop statistics.

Uses `bench_otlp.ini` (auto-detected).

## Configuration

Copy `otel.ini.sample` beside `Dyalog.OTel.dll`, into your working directory, or set the `DYALOG_OTEL_CONFIG` environment variable. See the sample for all options.

Standard `OTEL_*` environment variables are also supported (`OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_EXPORTER_OTLP_ENDPOINT`, etc.).

### Destination companion DLLs

The destination split is packaging-only; config names stay the same:

- `destination.text` and the `console` fallback stay in the core DLL
- `destination.file` and `destination.http` require `Dyalog.OTel.Destinations.Jsonl.dll`
- `destination.otlp` requires `Dyalog.OTel.Destinations.Otlp.dll`

Configured companion destinations are not optional at runtime. If a pipeline is configured with `file`, `http`, or `otlp` and the corresponding companion DLL is missing or broken beside `Dyalog.OTel.dll`, `pp_otel_init` fails pipeline startup.

### `destination.text`

`destination.text` is a human-readable operator log. It is separate from `destination.file`, which remains JSONL. Raw log lines render as:

```text
{timestamp} {emitter} {severity?} {message} {promoted attributes?}
```

- `timestamp` is UTC ISO-8601 with a `Z` suffix
- `emitter` comes from the canonical `emitter` signal attribute (usually supplied via templates)
- `severity` is only shown for warnings, errors, and fatal logs
- promoted attributes come from an explicit allowlist and render as `Label: value`
- multiline raw messages are split and re-prefixed

If you configure summary metrics, `destination.text` also emits periodic histogram summary blocks grouped by emitter. Summary metrics require `signals = log,metric`.

Example:

```ini
[destination.text]
path = logs/operator.log
rotate = monthly
signals = log,metric
startup = true
promote = process.pid, apl.version
label.process.pid = Process ID
label.apl.version = APL Version
summary.interval = 1h
summary.percentiles = 50,90,95,99
summary.metric.request_latency.name = request.latency
summary.metric.request_latency.bars = true
summary.metric.calc_latency.name = calc.latency
```

`summary.metric.<alias>.name` declares a histogram metric to summarize. Optional per-metric overrides:

- `summary.metric.<alias>.percentiles = 75,95,99`
- `summary.metric.<alias>.bars = true|false`
- `summary.metric.<alias>.interval = 5m|1h|1d|hourly|daily`

## License

[Unlicense](UNLICENSE)
