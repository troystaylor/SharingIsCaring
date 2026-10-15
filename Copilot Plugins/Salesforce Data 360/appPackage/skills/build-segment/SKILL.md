---
name: build-segment
description: Design and create a Data 360 segment from a plain-language audience description, checking the data model first and confirming before creating or publishing. Use when asked to create, size, update or publish a customer segment.
---

# Build Segment

## When to use this skill

- The user wants to define an audience in Data 360 such as "customers with high spend in the last 90 days".
- Cue phrases: "create a segment", "how many people match", "publish this segment".

## How to do it

1. Call `search` with "create and manage a segment" to see the Segment family tools (for example `d360_segment_create`, `d360_segment_count`, `d360_segment_get`, `d360_segment_list`, `d360_segment_publish`).
2. Check for an existing similar segment with `d360_segment_list` via `execute` before creating a new one.
3. Call `payload_examples` for `d360_segment_create` to get the schema and an example payload. Confirm the target data model object and the fields you will filter on.
4. Draft the criteria in plain language and show the planned payload. After explicit confirmation, call `execute` with `d360_segment_create`.
5. Use `d360_segment_count` to report the audience size. Only after the user approves, publish with `d360_segment_publish`.

## What to watch for

- Creating, updating, deactivating, deleting and publishing change the org; confirm each one separately and never delete without an explicit request.
- Prefer the dev or sandbox environment for experiments.
- Report API errors verbatim and adjust the payload using `payload_examples`.
- Calls count toward Connect API limits and Flex Credit usage.
