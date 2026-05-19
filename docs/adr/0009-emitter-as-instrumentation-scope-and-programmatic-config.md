# Emitter as InstrumentationScope + Programmatic Configuration

We unify the existing "Emitter" concept with OTel's InstrumentationScope: the emitter name becomes the scope name on the wire (shown as Category/Source in dashboards), and an optional version can be registered per emitter. We also introduce `pp_otel_config` as a generic programmatic configuration call that mirrors INI sections, enabling zero-config-file usage.

## Considered Options

**Emitter exposure:**
- Per-call parameter (chosen) — explicit emitter name on every signal call, `''` = use pipeline default. Matches the library's explicit-everything philosophy.
- Via template — overloads the template concept with scope semantics.
- Dedicated registration + implicit current emitter — contradicts explicit-everything.

**Programmatic config:**
- Purpose-built calls per concern (`pp_otel_resource`, `pp_otel_endpoint`, etc.) — discoverable but requires new exports for each INI feature.
- Generic `pp_otel_config section kvps` (chosen) — one export covers everything, mirrors INI 1:1, forward-compatible. Section name = INI section name.

**Timing:**
- Config before init, init freezes (chosen). Exception: `[emitter.*]` registrations are accepted post-init because components may load lazily.

## Consequences

- All signal call signatures gain an `emitter` parameter (breaking change — acceptable since all call sites are in this repo).
- OTLP destinations must group signals by emitter name before serializing (one InstrumentationScope container per distinct emitter in a batch).
- The `[pipeline]` INI section is introduced for pipeline-level settings (default emitter, emitter.version, flush timeout).
- Precedence layers remain: defaults → INI → env vars → builder calls (per-key merge, code wins).
