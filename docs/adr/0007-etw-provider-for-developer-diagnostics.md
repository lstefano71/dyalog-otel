---
status: proposed
---

# ETW provider for developer diagnostics

We plan to add an opt-in, stable, process-wide ETW provider owned by the library for Windows developer and performance diagnostics. The provider will use a small stable keyword set, emit both internal dyalog-otel diagnostics and mirrored user logs/spans, keep user-signal emission on the background consumer side, represent spans as Start/Stop activities with correlated logs joining the same activity context, defer user metrics, and use a fixed event schema with serialized attributes rather than exploding dynamic attribute bags into ETW fields.

This gives the project a Windows-native structured diagnostics surface that is materially different from Event Log and JSONL, while preserving the hot-path discipline and avoiding an unstable or overly ambitious ETW schema.
