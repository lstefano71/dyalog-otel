# Aggregate counters and gauges on the consumer thread before export

Counters and gauges were previously passed through to destinations as individual raw data points (one per `otel_metric` call). This produced noisy delta exports in dashboards — many `+1` increments instead of a single summed delta per collection interval.

We now aggregate all metric types on the consumer thread before export, consistent with OTel SDK behaviour (.NET, Java, Go): counters are summed per series per batch interval (Delta temporality), gauges keep last-value-wins, and histograms continue using the existing bucket/percentile aggregation. The aggregation key is metric name + canonicalized merged attributes (template + inline, sorted by key). Aggregation interval equals the batch interval — no additional config knob. Flush and shutdown force a snapshot-and-reset so no accumulated data is lost.

## Considered Options

- **Cumulative temporality**: track a running total since pipeline start. Rejected because it adds state management for resets and provides no benefit over Delta for our target backends (Aspire, OTLP collectors).
- **Raw pass-through with client-side rate computation**: let dashboards figure it out from individual deltas. Rejected because it produces noisy exports and most dashboards expect pre-aggregated points.
- **Separate aggregation interval**: decouple collection period from batch flush cadence. Rejected as unnecessary complexity — increasing the batch interval achieves the same effect.

## Consequences

- Destinations now receive one data point per unique metric series per flush, not one per call.
- `MetricPoint` gains an optional `StartTimeUnixNano` field for interval boundaries.
- `HistogramAggregator` is replaced by a unified `MetricAggregator` handling all three types.
- Future extension point: raw gauge observations for destinations that need spike detection (same pattern as `IRawHistogramObservationDestination`). Not built now.
