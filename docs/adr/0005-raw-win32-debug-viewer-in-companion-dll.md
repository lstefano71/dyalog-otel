---
status: proposed
---

# Raw Win32 debug viewer in a companion DLL

We plan to add an opt-in, Windows-only debug viewer implemented via raw Win32/PInvoke in a companion DLL loaded alongside the main telemetry library. The first cut uses one process-wide window with pipeline tabs backed by an in-process bounded ring buffer, showing a mixed chronological stream with single-line rows and a details pane.

This keeps the viewer feeling built-in while respecting NativeAOT size and trimming constraints better than a richer managed UI stack or a separate-process tool.
