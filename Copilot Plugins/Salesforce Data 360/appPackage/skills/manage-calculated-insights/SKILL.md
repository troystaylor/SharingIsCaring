---
name: manage-calculated-insights
description: Create, inspect, update and run Data 360 calculated insights - metrics computed over unified data such as lifetime value or engagement scores - discovering the right tools first and confirming before any change. Use when asked about calculated insights or to define a new metric in Data 360.
---

# Manage Calculated Insights

## When to use this skill

- The user wants to see, define, change or refresh a Data 360 calculated insight.
- Cue phrases: "what calculated insights exist", "create a lifetime value insight", "recalculate this insight", "update the insight SQL".

## How to do it

1. Call `search` with the intent (for example "list calculated insights" or "create a calculated insight") to learn the tool family and exact tool names. Do not guess tool names.
2. Call `payload_examples` for each tool you plan to use to get its input schema and an example payload.
3. Read before writing: use the list and get tools through `execute` (`toolName` plus `paramsJson`) to see existing insights, their definitions, status and last run.
4. To define or change an insight, draft the expression or SQL, the dimensions and measures, and the schedule. Check the source data model objects with a read-only `d360_query_sql` query (always with a `LIMIT`) so the logic is valid. Show the planned payload.
5. After explicit confirmation, call `execute` with the create or update tool. Report the result and, if a run is triggered, check its status with the matching get tool.

## What to watch for

- `execute` can create, update and delete; confirm every change and never delete without an explicit request.
- Insights read from data model objects, so check field names before writing logic.
- Prefer a sandbox or non-production data space for experiments.
- Calls count toward Connect API limits and Flex Credit usage.
