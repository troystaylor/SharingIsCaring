---
name: explore-unified-profile
description: Explore customer data in Salesforce Data 360 - find unified profiles, run Data Cloud SQL and summarize engagement. Use when asked what Data 360 knows about a customer, to query unified individuals, or to rank accounts by usage or engagement.
---

# Explore Unified Profile

## When to use this skill

- The user asks about unified customer data, engagement or behavior stored in Data 360.
- Cue phrases: "what do we know about <customer> in Data 360", "top 20 accounts by usage events", "customers who got the email but did not open it".

## How to do it

1. Call `search` with the intent (for example "query unified individual data") to find the right tool family; Query tools are `d360_query_sql`, `d360_query_sql_status` and `d360_query_sql_rows`.
2. Call `payload_examples` with `toolName` `d360_query_sql` to get the exact input schema, then discover the object names you need (data model objects end in `__dlm`).
3. Run the query with `execute`, passing `toolName` and `paramsJson` as a JSON string, for example `{"input":{"sql":"SELECT ... FROM UnifiedIndividual__dlm LIMIT 10"}}`. Always include a `LIMIT`.
4. For long-running queries, use the returned `queryId` with `d360_query_sql_status`, then page with `d360_query_sql_rows`.
5. Summarize results with the fields used and any caveats about data freshness or filters.

## What to watch for

- `paramsJson` must be a valid JSON string matching the tool schema.
- Data 360 calls count toward Connect API limits and Flex Credit usage; keep queries targeted.
- Respect privacy: report only what the user needs and avoid exposing personal data beyond the question.
- This skill is read-only; use Build Segment for creating segments.
