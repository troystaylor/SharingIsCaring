---
name: c3-metrics
description: Read time series metrics from a C3 application - list the metrics a type supports, then evaluate them over a date range and interval. Use for trends, sensor readings, "how did X change over the last month", or "what can I measure on this asset".
---

# C3 metrics and time series

Read-only. Use the C3 App MCP connector. Apply `c3-access` first.

## Steps

1. **Pick the type.** Follow `c3-explore-data` to identify the entity type and, if the user named a specific asset, the record.
2. **List metrics.** Use `listTypeMetrics` to see which metrics the type exposes. Match the user's words to a metric name and confirm with them if more than one fits.
3. **Evaluate.** Use an enabled metric-evaluation tool from the live tool list, with the metric name, the target records, a start and end date, and an interval such as `HOUR`, `DAY` or `MONTH`. If no evaluation tool is enabled and the user has `runPyCode`, offer to run a small read-only evaluation and show the code first, under `c3-write-safety`.
4. **Report.** Give the time window and interval, then the headline (latest, min, max, trend). Show the series as a table when it is short, and summarize when it isn't. Name gaps in the data rather than treating them as zero.

## Rules

- Always state the time zone you used. If the user didn't give one, say the data is in the application's default.
- Ask for a bounded date range. Don't evaluate "all time" at fine intervals.
- Don't extrapolate or forecast unless the application has a forecasting tool enabled. Say that a trend is descriptive only.
- If `listTypeMetrics` returns nothing, the type has no time series metrics. Say so and offer `c3-explore-data`.
