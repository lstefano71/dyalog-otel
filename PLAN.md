# Dyalog OTel — Design Plan

## Problem

Dyalog APL has no native observability library. Getting logs, traces, and metrics out of an APL session today requires ad-hoc file I/O or shelling out to external tools — both slow and unstructured. We want an OpenTelemetry-compatible telemetry library that:

- Runs as a DWA extension (NativeAOT C# DLL loaded via `⎕NA`)
- Adds minimum overhead to the interpreter thread (no I/O, no blocking)
- Supports configurable destinations (OTLP/protobuf, OTLP/JSON, JSONL files, JSONL+HTTP)
- Feels natural from APL (positional args for fixed fields, nested pairs for attributes)

## Architecture

```
APL interpreter thread              Background threads (3)
─────────────────────              ──────────────────────
⎕NA call                           
  → DWA export (hot path)          
    → read Localp args             
    → build typed struct            
    → capture timestamp            
    → Channel<T>.TryWrite ──────→  Consumer thread (per signal type)
    → return to APL                   → batch by size/interval
                                      → serialize (LightProto or JSONL)
                                      → POST to destination(s)
```

### Key design decisions

| Decision | Choice | ADR |
|----------|--------|-----|
| Protobuf library | LightProto (AOT source-gen, no reflection) | [0001](docs/adr/0001-lightproto-for-otlp-serialization.md) |
| Dispatch model | Background channels, one per signal type | [0002](docs/adr/0002-background-dispatch-via-typed-channels.md) |
| Span events | Not implemented; use log-correlated events | [0003](docs/adr/0003-no-span-events-use-log-correlated-events.md) |
| Backpressure | Drop-newest + explicit `otel_flush` for shutdown | — |
| Pipeline lifecycle | Lazy singleton (handle 0) + explicit named pipelines | — |
| Span management | Explicit handles, explicit parenting, no implicit stack | — |
| HTTP transport | `HttpClient` with keep-alive, gRPC deferred | — |
| Configuration | INI config file (primary) + env vars + builder calls | — |
| Config precedence | hardcoded defaults → config file → `OTEL_*` env vars → builder calls | — |
| Serialization | LightProto for OTLP/protobuf, System.Text.Json/JSONL for file & HTTP | 0001 |
| Structured logging | Serilog/Seq-style message templates with implicit cache | — |
| Metrics | Counter, Gauge, Histogram (all day one). HdrHistogram.NET for distributions | — |
| File rotation | Time-based (hourly/daily/monthly/none), default monthly | — |

## API Surface (DWA exports)

All exports use `pp_otel_` prefix. Signal calls are void (no `>PP`); data-returning calls use `>PP`.

### Lifecycle

| Export | Signature | Description |
|--------|-----------|-------------|
| `pp_otel_init` | `>PP` | Lazy-init singleton, returns 0 |
| `pp_otel_create` | `<PP >PP` | Create explicit pipeline from config, returns handle |
| `pp_otel_shutdown` | `<PP` | Flush + tear down pipeline (0 = singleton) |
| `pp_otel_flush` | `<PP` | Block until channels drain (0 = singleton) |

### Configuration (builder phase, before start)

| Export | Signature | Description |
|--------|-----------|-------------|
| `pp_otel_add_dest` | `<PP <PP <PP` | pipeline, type, params |
| `pp_otel_resource` | `<PP <PP` | pipeline, key-value pairs |
| `pp_otel_batch_config` | `<PP <PP I4 I4` | pipeline, signal_type, max_size, interval_ms |
| `pp_otel_start` | `<PP` | Freeze config, start consumer threads |

### Signals (hot path, void)

| Export | Signature | Description |
|--------|-----------|-------------|
| `pp_otel_log` | `<PP <PP <PP <PP <PP` | pipeline, severity, message, template_name, attrs/fillers |
| `pp_otel_span_start` | `<PP <PP <PP <PP <PP >PP` | pipeline, name, parent_handle, template_name, attrs → span_handle |
| `pp_otel_span_end` | `<PP <PP <PP` | pipeline, span_handle, attrs |
| `pp_otel_metric` | `<PP <PP <PP <PP <PP <PP` | pipeline, metric_name, value, metric_type, template_name, attrs |

### Templates

| Export | Signature | Description |
|--------|-----------|-------------|
| `pp_otel_tpl_create` | `<PP <PP` | name, attrs → (void, name is the key) |
| `pp_otel_tpl_derive` | `<PP <PP <PP` | name, parent_name, extra_attrs |
| `pp_otel_tpl_delete` | `<PP` | name (cascades to children) |

### Diagnostics

| Export | Signature | Description |
|--------|-----------|-------------|
| `pp_otel_status` | `<PP >PP` | pipeline → 0/1/2 (healthy/degraded/down) |
| `pp_otel_stats` | `<PP >PP` | pipeline → result triple of counters |

## APL Cover Namespace

Thin `OTel` namespace shipped as a `.dyalog` file:

```apl
⍝ Monadic (singleton pipeline)
OTel.Log severity message attrs
OTel.SpanStart name parent attrs        ⍝ → span_handle
OTel.SpanEnd handle attrs

⍝ Dyadic (explicit pipeline)
handle OTel.Log severity message attrs
handle OTel.SpanStart name parent attrs ⍝ → span_handle
```

Template name can be passed where `attrs` goes (string = template name, nested = inline attrs). The cover distinguishes by type.

## Configuration

INI format, hand-rolled parser. Discovered via search order: `DYALOG_OTEL_CONFIG` env var → `otel.ini` beside the DLL → `otel.ini` in CWD → hardcoded defaults (console).

Example:

```ini
[resource]
service.name = trading-engine
environment = production

[destination.file]
path = C:\logs\trading.jsonl
signals = log, metric, span
rotate = monthly

[destination.http]
url = https://logs.internal/ingest
signals = log

[destination.otlp]
endpoint = http://collector:4318
protocol = http/protobuf

[batch]
log.size = 512
log.interval = 5000
metric.size = 256
metric.interval = 60000
span.size = 256
span.interval = 2000
```

## C# Internal Architecture

```
Dyalog.OTel (NativeAOT DLL)
├── Exports/                    ← [DwaExport] methods (hot path)
│   ├── LogExports.cs
│   ├── SpanExports.cs
│   ├── MetricExports.cs
│   └── LifecycleExports.cs
├── Pipeline/
│   ├── Pipeline.cs             ← Owns 3 channels + consumer threads
│   ├── PipelineRegistry.cs     ← Singleton + handle-keyed lookup
│   └── PipelineBuilder.cs      ← Incremental config builder
├── Channels/
│   ├── LogRecord.cs            ← Typed signal structs
│   ├── SpanRecord.cs
│   └── MetricPoint.cs
├── Templates/
│   ├── TemplateSnapshot.cs     ← Immutable attribute set
│   └── TemplateRegistry.cs     ← ConcurrentDictionary<string, TemplateSnapshot>
├── Destinations/
│   ├── IDestination.cs         ← interface: Init, WriteBatch, Flush, Shutdown
│   ├── OtlpProtobufDestination.cs
│   ├── OtlpJsonDestination.cs
│   ├── JsonlFileDestination.cs ← time-based rotation (hourly/daily/monthly/none)
│   └── JsonlHttpDestination.cs
├── Config/
│   ├── IniParser.cs            ← Hand-rolled INI parser (~50 lines)
│   ├── ConfigLoader.cs         ← Search order: env var → beside DLL → CWD → defaults
│   └── ConfigModel.cs          ← Typed config sections
├── MessageTemplates/
│   ├── MessageTemplate.cs      ← Parsed template (placeholder names + segments)
│   └── TemplateCache.cs        ← Dictionary<string, MessageTemplate>, size-capped
├── Serialization/
│   ├── OtlpProto/              ← LightProto-generated from OTLP .proto files
│   └── JsonModels/             ← System.Text.Json source-gen models
├── Resources/
│   └── ResourceDetector.cs     ← Auto-detect host, PID, APL version, etc.
└── Diagnostics/
    └── InternalMetrics.cs      ← Atomic counters: enqueued, exported, dropped
```

## Resolved Decisions

- **Channel capacity**: 8,192 per channel. Configurable via builder. Tune with real data.
- **Structured message templates**: Serilog/Seq-style. Implicit cache (parse once per unique template string). Fillers passed in the attrs `<PP` slot — if message has `{placeholders}`, attrs are positional fillers; if plain string, attrs are key-value pairs.
- **Init / start lifecycle**: Lazy init reads config file → env vars → console defaults. Explicit mode: `otel_init` → builder calls → `otel_start`.
- **Config file search order**: `DYALOG_OTEL_CONFIG` env var → `otel.ini` beside DLL → `otel.ini` in CWD → defaults.
- **Wire format**: JSONL for file and HTTP destinations. LightProto for OTLP/protobuf. OTLP/JSON as fallback.
- **File rotation**: Time-based, configurable (hourly/daily/monthly/none). Default monthly. Timestamp suffix on filename.
- **Log ↔ span correlation**: Resolved on hot path. `otel_log` with span handle looks up trace_id/span_id immediately.
- **Span events**: Not implemented (OTel deprecating). Use `otel_log` with span handle instead.
- **Span links**: Deferred.
- **gRPC**: Deferred. OTLP/HTTP only for now.
- **Destination plugins**: Internal `IDestination` interface, closed DLL. Runtime loading deferred.
- **APL covers**: Shipped alongside raw `⎕NA` docs. Dyadic left arg = pipeline handle.
