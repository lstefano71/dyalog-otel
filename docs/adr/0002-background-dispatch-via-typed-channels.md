# Background dispatch via typed channels

Signal calls on the APL interpreter thread copy arguments into typed C# structs, capture a timestamp, and enqueue to a `System.Threading.Channels.Channel<T>` — one channel per signal type (log, trace, metric). Three dedicated consumer threads drain the channels, batch signals by configurable size/interval thresholds, and dispatch batches to configured destinations. The interpreter thread never blocks on I/O and never waits for export.

When a channel's bounded capacity is reached, new signals are dropped (drop-newest via `TryWrite`). An explicit `otel_flush` call blocks the interpreter thread until all channels drain — intended for clean shutdown only.

This architecture exists because the APL interpreter is single-threaded. Any I/O on the interpreter thread (HTTP, file writes, even DNS resolution) directly stalls the APL session. The only way to achieve "minimum overhead" telemetry is to separate data capture (hot path, interpreter thread) from data export (background threads). The typed-channel-per-signal-type design allows independent batching and flush intervals per signal type, matching OTel's own pipeline separation.

## Considered options

- **Synchronous export on the interpreter thread**: Simplest, but a slow or unreachable collector would freeze the APL session. Unacceptable for production use.
- **Single shared channel with discriminated union**: Simpler (one channel, one thread), but prevents independent batching tuning per signal type. Logs and metrics have very different volume and latency profiles.
- **Thread pool dispatch (`Task.Run`)**: Would work for the export side, but DWA forbids `Task.Run` in extension code, and thread pool exhaustion under load could degrade the interpreter. Dedicated threads are predictable.
