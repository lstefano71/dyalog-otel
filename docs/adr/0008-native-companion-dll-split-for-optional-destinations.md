# Native companion DLL split for optional destinations

We split non-core destinations out of the main NativeAOT DLL into native companion DLL families loaded beside the core library at runtime. The core DLL keeps `text` and `console`; the JSONL companion family owns `file` and `http`; the OTLP companion family owns `otlp`.

This exists because NativeAOT does not support managed runtime plugin loading via `Assembly.Load*`, so the runtime seam cannot be a managed `IDestination` plugin model. The split uses a pure native ABI instead: exported family entry points, opaque handles, blittable batch structs, and UTF-8 property bags. Existing destination config stays stable as `type=file|http|otlp|text|console`, and companion discovery is by fixed naming convention beside `Dyalog.OTel.dll`.

If a configured companion destination is missing or fails to load, pipeline startup fails. We chose family-level DLLs instead of one DLL per destination type so the packaging split reduces core footprint without multiplying duplicated NativeAOT runtime payload.

## Considered options

- **Keep the monolithic DLL**: Simplest packaging, but every deployment pays for all destination code and dependencies even when only one destination family is used.
- **Managed runtime plugins (`Assembly.Load*`)**: Attractive shape in normal .NET, but incompatible with NativeAOT for this project.
- **One native companion DLL per destination type**: Maximally granular, but likely duplicates AOT runtime payload and undercuts the size goal.
