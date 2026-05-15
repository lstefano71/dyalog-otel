---
status: proposed
---

# Windows Event Log as an operator-facing destination

We plan to add a Windows Event Log destination aimed at operator visibility rather than full telemetry parity. The first cut writes warning-and-above log records plus a startup event to the Application log using a stable configured source, with curated fields, correlation IDs, deployment-managed source registration, and graceful degradation when the source is unavailable.

This gives Windows operators a native inspection surface without expanding the library into a broad matrix of vendor-specific integrations or forcing spans and metrics into an unnatural UI.
