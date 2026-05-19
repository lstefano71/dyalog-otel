---
status: proposed
---

# Durable delivery mode for destinations

We plan to add durable spool-and-forward as a delivery mode on `destination.otlp`, `destination.http`, and `destination.file` (cooperative mode), not as a standalone destination. The first cut stores serialized outbound requests per destination, defines success as remote acceptance (for network destinations) or successful lock-acquire-and-flush (for file destinations), preserves queued backlog when the spool fills, and makes `Flush` and shutdown guarantee durable local handoff rather than remote drain.

For file destinations in cooperative mode, durable delivery covers the case where a lock cannot be acquired within the timeout — the batch is spooled locally and retried with exponential backoff rather than dropped immediately.

This keeps the feature focused on transient outages and tightens delivery semantics without inventing a second generic export model or making shutdown depend on network recovery.
