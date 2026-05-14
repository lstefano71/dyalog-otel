# Dyalog OTel

An APL telemetry library built as a DWA extension, producing logs, traces, and metrics with minimum interpreter-thread overhead via background dispatch.

## Language

**Signal**:
A single telemetry datum — a log record, a span event, or a metric measurement. The unit of work the APL developer hands to the library.
_Avoid_: event (overloaded), record (ambiguous with log record specifically)

**Pipeline**:
A configured set of channels and consumer threads that accepts signals and dispatches them to destinations. One implicit singleton pipeline exists (lazy-init); additional pipelines can be created with explicit handles.
_Avoid_: provider, exporter (those are parts of a pipeline, not the whole)

**Hot path**:
The code that executes on the APL interpreter thread during a signal call: read Localp arguments, build a typed struct, capture timestamp, enqueue to channel. Must never block or perform I/O.
_Avoid_: fast path (too generic)

**Attribute**:
A string-keyed, APL-typed value attached to a signal. Passed as a nested vector of key-value pairs. The background thread maps APL element types to OTel attribute types at serialization time.
_Avoid_: tag, label, property

**Template**:
A named, immutable snapshot of key-value pairs (resource and/or signal attributes) that can be referenced by signal calls to avoid repeating common attributes. Created via builder calls, cloned-on-derive. Parent-child links exist for lifecycle only (delete parent cascades to children); derivation does not imply dynamic inheritance.
_Avoid_: preset, context (overloaded), scope

**Destination**:
A configured output target that receives batched signals from a consumer thread. Examples: OTLP/protobuf endpoint, file, custom JSON+HTTP.
_Avoid_: exporter (OTel-specific term — we may support non-OTel destinations), sink

**Span handle**:
An opaque integer returned by `otel_span_start`, identifying an active span. Parenting is always explicit (pass parent handle at creation) — never inferred from an implicit stack. Must be closed with `otel_span_end`.
_Avoid_: span context, trace handle

**Scope-bound span**:
A span whose handle is localised in the calling tradfn via the `⎕SHADOW`/dfn-execute trick, so it is released when the tradfn's scope exits. Only works within a single tradfn's lifetime.
_Avoid_: auto-span, managed span

**Cross-function span**:
A span opened in one function and closed in another. Requires explicit handle passing — the `⎕SHADOW` trick cannot help. Leaks are possible if `otel_span_end` is never reached.
_Avoid_: long-lived span

**Message template**:
A log message string containing `{Placeholder}` tokens that are bound to positional filler values. Produces both a human-readable rendered string and structured named attributes. Parsed once and cached implicitly by the template string.
_Avoid_: format string (no positional `{0}` numbering — names are semantic), log template

**Config file**:
An INI-format file that configures pipelines, destinations, resources, and batching. Discovered via `DYALOG_OTEL_CONFIG` environment variable or conventional location. The primary configuration mechanism for non-programmatic setup.
_Avoid_: settings file, properties file

**Resource**:
A set of attributes describing the entity producing telemetry (service name, host, APL version, etc.). Frozen at pipeline start. Auto-detected where possible (host name, PID, APL version via DWA `Workspace` API), overridable via builder calls.
_Avoid_: metadata, identity

## Relationships

- A **pipeline** owns three channels (log, trace, metric), each with a consumer thread
- A **signal** is enqueued to the appropriate channel within a **pipeline**
- A consumer thread batches **signals** and dispatches them to one or more **destinations**
- An **attribute** bag is the variable part of a **signal**; fixed fields (severity, message, span name, etc.) are positional
- A **template** is referenced by name during signal calls; the hot path resolves it to an immutable snapshot via dictionary lookup
- A **span handle** is returned by span-start and consumed by span-end; parenting is explicit via a parent handle argument

## Flagged ambiguities

- "handle" in this library means a pipeline handle (integer, 0 = singleton) or a span handle (integer). In the DWA bridge context, "handle" typically refers to a database connection. These are different concepts sharing a name. Pipeline handles and span handles are also distinct — pipeline handles route signals to a pipeline; span handles identify an active span within a pipeline.
- APL has no deterministic scope-exit mechanism (`using`, `try-finally`). Span lifecycle is inherently leaky. Scope-bound spans via `⎕SHADOW` work for single-tradfn scopes; cross-function spans require discipline. A timeout-based reaper on the background thread auto-closes long-open spans as a safety net.
