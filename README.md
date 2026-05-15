# dyalog-otel

High-performance telemetry library for Dyalog APL — logs, traces, and metrics with minimum interpreter-thread overhead.

Built as a NativeAOT C# DLL using the [DWA Kit](https://github.com/user/bridge-dwa) for zero-copy data access. The hot path (called from APL) only parses arguments and enqueues to bounded channels; all I/O happens on background threads.

## Features

- **Logs** — plain text or Serilog-style message templates (`'Order {OrderId} placed for {Amount}'`)
- **Traces** — explicit span start/end with parent-child nesting, log↔span correlation
- **Metrics** — counters, gauges, histograms
- **Destinations** — JSONL file (with time-based rotation), JSONL-over-HTTP, OTLP/Protobuf (default), OTLP/JSON, console fallback
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

This produces two DLLs in `src\Dyalog.OTel\bin\Release\net10.0\win-x64\publish\`:
- `Dyalog.OTel.dll` — C shim (APL loads this via `⎕NA`)
- `Dyalog.OTel_impl.dll` — NativeAOT implementation

## Run tests

```powershell
.\run_apl.ps1 test_otel.apls
```

The test suite (54 checks) exercises:
- Lifecycle: version, init, status, shutdown
- Logs: plain text, message templates with fillers, span-correlated logs
- Traces: span start/end, nested spans, parent-child relationships
- Metrics: counter, gauge, histogram
- Templates: create, use, delete
- Flush: drain all channels, verify stats
- **Content verification**: reads the JSONL output file after flush and checks every field (body, severity, timestamps, trace/span IDs, metric names/values/types, parent-child links)

Uses `test_otel.ini` (auto-detected by `run_apl.ps1` as a companion config).

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

Copy `otel.ini.sample` beside the DLL, into your working directory, or set the `DYALOG_OTEL_CONFIG` environment variable. See the sample for all options.

Standard `OTEL_*` environment variables are also supported (`OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_EXPORTER_OTLP_ENDPOINT`, etc.).

## License

[Unlicense](UNLICENSE)
