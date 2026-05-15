---
status: proposed
---

# Durable delivery mode for remote destinations

We plan to add durable spool-and-forward as a delivery mode on `destination.otlp` and `destination.http`, not as a standalone destination. The first cut stores serialized outbound requests per remote destination, defines success as remote acceptance, preserves queued backlog when the spool fills, and makes `Flush` and shutdown guarantee durable local handoff rather than remote drain.

This keeps the feature focused on transient outages and tightens delivery semantics without inventing a second generic export model or making shutdown depend on network recovery.
