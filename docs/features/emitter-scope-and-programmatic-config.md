# Feature: Emitter-Scoped Signals + Programmatic Configuration

## Summary

Two related capabilities:

1. **Emitter as InstrumentationScope** — Every signal call accepts an explicit emitter name that maps to OTLP's InstrumentationScope.name. Dashboards (Aspire, Grafana, etc.) display this as the log Category and trace Source, enabling per-module identification within a service.

2. **Programmatic configuration via `pp_otel_config`** — A single generic call that mirrors the INI file structure, enabling full pipeline configuration from APL code without external files.

## Motivation

- The Aspire dashboard shows "unknown" for Category/Source because the library previously hardcoded a single scope with no way to vary it.
- Small APL applications want to send telemetry without managing an INI file — just point at an OTLP endpoint from code.
- As Dyalog moves toward componentised applications, different modules need to identify themselves with distinct emitter names and versions.

## API Design

### New Export: `pp_otel_config`

```apl
⎕NA 'I4 ',dll,'|pp_otel_config I4 <PP <PP'
⍝ rc←pp_otel_config pipeline section kvps
```

- `pipeline` — pipeline handle (0 = singleton)
- `section` — INI section name: `'pipeline'`, `'resource'`, `'destination.otlp'`, `'batch'`, `'emitter.X'`
- `kvps` — flat alternating key-value vector: `('key1' 'val1' 'key2' 'val2')`
- Returns: 0=ok, 1=unknown section, 2=called too late (frozen)

**Timing rules:**
- `pipeline`, `resource`, `destination.*`, `batch` — must be called before `pp_otel_init`. Frozen at init.
- `emitter.*` — can be called anytime (components load lazily).

**Precedence (per-key merge, higher wins):**
1. Hardcoded defaults
2. INI file
3. `OTEL_*` environment variables
4. `pp_otel_config` builder calls

### Modified Export: `pp_otel_init`

```apl
⎕NA 'I4 ',dll,'|pp_otel_init <PP >I4'
⍝ (status handle)←pp_otel_init ⍬ 0
```

- Returns status (0=ok, non-zero=error) as explicit result
- Returns pipeline handle as output parameter
- Freezes all config except `emitter.*`

### Modified Signal Exports (emitter parameter added)

```apl
⎕NA dll,'|pp_otel_log I4 I4 <PP <PP <PP <PP'
⍝ pp_otel_log pipeline severity message emitter templateName attrs

⎕NA dll,'|pp_otel_log_span I4 I4 <PP I4 <PP <PP <PP'
⍝ pp_otel_log_span pipeline severity message spanHandle emitter templateName attrs

⎕NA dll,'|pp_otel_span_start I4 <PP I4 <PP <PP <PP >PP'
⍝ handle←pp_otel_span_start pipeline name parentHandle emitter templateName attrs 0

⎕NA dll,'|pp_otel_metric I4 <PP <PP I4 <PP <PP <PP'
⍝ pp_otel_metric pipeline metricName value metricType emitter templateName attrs
```

- `emitter` — string name of the emitter (e.g., `'myapp.orders'`). Pass `''` for pipeline default.

### INI Configuration

```ini
[pipeline]
emitter = myapp.main          ; default emitter name (used when '' is passed)
emitter.version = 1.0.0       ; default emitter version

[resource]
service.name = myapp
deployment.environment = production

[destination.otlp]
endpoint = http://localhost:4318
protocol = protobuf
signals = log,span,metric

[emitter.myapp.calc]
version = 2.0.0               ; registered emitter with specific version

[emitter.myapp.io]
version = 1.3.0

[batch]
log.size = 512
log.interval = 5000
```

### Wire Behavior

- Each signal record carries an `Emitter` string field (null/empty = pipeline default).
- OTLP destinations group signals by emitter name before serializing.
- Each distinct emitter produces a separate `ScopeLogs`/`ScopeSpans`/`ScopeMetrics` container with its own `InstrumentationScope { name, version }`.
- Version lookup: registered emitter → its version; unregistered emitter → empty string; pipeline default emitter → `emitter.version` from `[pipeline]`.

### Programmatic Example (zero INI file)

```apl
dll←'path/to/dyalog.otel.dll'
⎕NA 'I4 ',dll,'|pp_otel_config I4 <PP <PP'
⎕NA 'I4 ',dll,'|pp_otel_init <PP >I4'
⎕NA dll,'|pp_otel_log I4 I4 <PP <PP <PP <PP'
⎕NA dll,'|pp_otel_shutdown I4'

pp_otel_config 0 'resource' ('service.name' 'my-apl-app')
pp_otel_config 0 'pipeline' ('emitter' 'myapp' 'emitter.version' '1.0.0')
pp_otel_config 0 'destination.otlp' ('endpoint' 'http://localhost:4318' 'protocol' 'protobuf' 'signals' 'log,span,metric')

(status handle)←pp_otel_init ⍬ 0

pp_otel_log handle 9 'Order placed' 'myapp.orders' '' ⍬
pp_otel_log handle 9 'Cache hit' '' '' ⍬   ⍝ uses default emitter 'myapp'

pp_otel_shutdown handle
```

## Acceptance Criteria

1. Signals appear in Aspire/Grafana with correct Category/Source matching the emitter name.
2. `pp_otel_config` can fully configure a pipeline without any INI file.
3. `pp_otel_config` with `emitter.*` sections works after init.
4. `pp_otel_config` with frozen sections after init returns error code 2.
5. OTLP output correctly groups signals by emitter into separate InstrumentationScope containers.
6. Existing INI-based configuration continues to work (backward-compatible).
7. Per-key merge precedence: INI < env vars < builder calls.
