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

**Text destination**:
A destination that renders raw log records and selected synthesized summaries as human-readable text into a text stream.
_Avoid_: file destination, console fallback

**Event Log destination**:
A Windows operator-facing destination that writes selected log records to the Application event log using a configured source.
_Avoid_: debug viewer, console fallback

**ETW provider**:
An opt-in EventSource/TraceLogging surface exposed by the library for developer and performance diagnostics on Windows.
_Avoid_: destination, operator log

**ETW destination**:
The user-signal path that mirrors selected logs and spans into the ETW provider.
_Avoid_: whole ETW surface, internal diagnostics stream

**Analytics handoff format**:
A local structured format written for downstream analysis tools such as DuckDB, optimized for append-friendly export rather than built-in query UX.
_Avoid_: query store, warehouse

**Debug feed**:
A bounded local output target intended for developer inspection during local runs, separate from operator-facing production outputs.
_Avoid_: viewer, inspector, destination UI

**Debug viewer**:
A local tool or UI that reads from a debug feed and presents live telemetry to a developer.
_Avoid_: destination, sink

**Durable delivery mode**:
A delivery mode for a remote destination that persists serialized outbound requests locally until the remote endpoint accepts them.
_Avoid_: spool destination, offline mode

**Local durable handoff**:
The point at which in-memory telemetry has been durably written to the local spool, even if the remote endpoint has not yet accepted it.
_Avoid_: delivered, remotely flushed

**Remote acceptance**:
The point at which a remote endpoint confirms receipt of a batch; the moment durable remote delivery counts as success.
_Avoid_: enqueue success, attempted export

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

**Emitter**:
A named logical producer within a resource (e.g. `'app.calc'`, `'app.io'`). Maps to OTLP InstrumentationScope.name on the wire. The same string appears as the source/category in dashboards and as the origin label in text destinations. Passed as a per-call parameter to signal exports (empty string = pipeline default). Version is resolved from the emitter registry at serialization time.
_Avoid_: component, scope (overloaded in OTel), source, category

**Summary block**:
A periodic human-readable text group synthesized by a destination from aggregated metrics rather than emitted directly by application code.
_Avoid_: report, dashboard, log record

**Text stream**:
A concrete human-readable output file or stream selected per process; multiple emitters may write to the same text stream, and its filename need not match any emitter code.
_Avoid_: component log, source file

**Startup block**:
A one-time human-readable text group emitted at pipeline start from known process and resource metadata.
_Avoid_: banner, bootstrap log spam

## Relationships

- A **pipeline** owns three channels (log, trace, metric), each with a consumer thread
- A **signal** is enqueued to the appropriate channel within a **pipeline**
- A consumer thread batches **signals** and dispatches them to one or more **destinations**
- An **attribute** bag is the variable part of a **signal**; fixed fields (severity, message, span name, etc.) are positional
- A **template** is referenced by name during signal calls; the hot path resolves it to an immutable snapshot via dictionary lookup
- A **span handle** is returned by span-start and consumed by span-end; parenting is explicit via a parent handle argument
- A **resource** may contain multiple **emitters**
- A **signal** may carry an **emitter** naming its logical producer within the **resource**
- A **destination** may render a **signal** directly or synthesize a **summary block** from aggregated metrics
- A **destination** may route output into one or more **text streams**
- A **text destination** writes to a **text stream**
- A **text destination** may emit a **startup block** and **summary blocks**
- An **Event Log destination** is a **destination** aimed at operator visibility on Windows
- An **ETW provider** may emit both internal diagnostics and mirrored user telemetry
- An **ETW destination** is one path into an **ETW provider**
- An **analytics handoff format** is consumed by downstream analysis tools rather than queried by the library itself
- A **debug viewer** reads from a **debug feed**
- A **debug feed** is the local output target; a **debug viewer** is the developer-facing reader
- A **durable delivery mode** belongs to a remote **destination**
- **Local durable handoff** happens before **remote acceptance**
- Multiple **emitters** may share a **text stream**

## Flagged ambiguities

- "handle" in this library means a pipeline handle (integer, 0 = singleton) or a span handle (integer). In the DWA bridge context, "handle" typically refers to a database connection. These are different concepts sharing a name. Pipeline handles and span handles are also distinct — pipeline handles route signals to a pipeline; span handles identify an active span within a pipeline.
- APL has no deterministic scope-exit mechanism (`using`, `try-finally`). Span lifecycle is inherently leaky. Scope-bound spans via `⎕SHADOW` work for single-tradfn scopes; cross-function spans require discipline. A timeout-based reaper on the background thread auto-closes long-open spans as a safety net.
- "resource" was used to mean both pipeline identity and the short origin printed in human-readable logs — resolved: use **resource** for pipeline identity and **emitter** for the short per-signal origin code (`SRV`, `CAL`).
- "log" can mean a raw log record or a human-readable periodic digest — resolved: use **log record** for raw emitted logs and **summary block** for synthesized periodic metric output.
- A text filename can look like an emitter name but is only a routing artifact — resolved: use **text stream** for the per-process output target and **emitter** for the per-signal origin code.
- "file destination" and human-readable text output are different concepts — resolved: use **file destination** for JSONL-style structured output and **text destination** for operator-facing text output.
- "destination" was starting to mean both a local output target and the UI used to inspect it — resolved: use **debug feed** for the bounded local output target and **debug viewer** for the tool or UI that reads it.
- "spool" can sound like a standalone output target — resolved: use **durable delivery mode** for the opt-in delivery layer applied to remote destinations.
- "flush" can mean either local persistence or confirmed remote delivery — resolved: use **local durable handoff** for persistence to the local spool and **remote acceptance** for confirmed delivery by the remote endpoint.
- "queryable local format" can mean either a built-in store or a downstream-friendly export — resolved: use **analytics handoff format** for the append-friendly local format written for external tools such as DuckDB.
- "ETW destination" was starting to mean both the whole EventSource surface and the mirrored user-signal path into it — resolved: use **ETW provider** for the overall diagnostics surface and **ETW destination** for the user-signal mirroring path.
