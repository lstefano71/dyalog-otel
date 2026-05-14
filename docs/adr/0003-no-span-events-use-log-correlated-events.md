# No span events API — use log-correlated events

We do not implement span events (`Span.AddEvent`, `Span.RecordException`). Events within a span are emitted as log records correlated with the span via the span handle argument to `otel_log`.

OpenTelemetry is officially deprecating the Span Event API in favour of log-based events emitted via the Logs API and correlated with traces through context (see [OTel blog post, May 2026](https://opentelemetry.io/blog/2026/deprecating-span-events/)). The rationale: having two overlapping ways to emit events (span events and log-based events) splits guidance, duplicates concepts, and slows evolution. The community is converging on "events are logs with names."

Since we're building a greenfield library, we adopt the new model from the start rather than implementing a deprecated API that would need migration later. The `otel_log` call already accepts a span handle (via the pipeline/handle argument), which stamps the log record with trace_id and span_id on the hot path. This gives full span-event semantics without a separate API surface.
