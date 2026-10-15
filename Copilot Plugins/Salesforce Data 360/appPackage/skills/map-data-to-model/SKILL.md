---
name: map-data-to-model
description: Review and create Data 360 mappings from data lake objects to data model objects, including field mappings, so ingested data lands in the unified model. Use when asked how a data stream is mapped, which fields are unmapped, or to map a new source to the data model.
---

# Map Data To Model

## When to use this skill

- The user wants to understand or build the mapping between ingested data and the Data 360 data model.
- Cue phrases: "how is this data stream mapped", "which fields are unmapped", "map this DLO to the Individual DMO".

## How to do it

1. Call `search` for "data lake object", "data model object", "data stream" and "mapping" to learn the exact tool names, then `payload_examples` for each tool you will call.
2. Inspect the source: use the list and get tools through `execute` (`toolName` plus `paramsJson`) for the data lake object (fields and types) and its data stream (source, refresh, status).
3. Inspect the target: get the data model object and its fields, and list existing DLO-to-DMO mappings so you do not duplicate one.
4. Propose the mapping as a table: source field, target field, type, and any gaps or type mismatches. Highlight required target fields that have no source, and fields that look like identity keys.
5. For a new or changed mapping, show the planned payload and, after explicit confirmation, call the create or update mapping tool via `execute`. Re-read the mapping and report the result.

## What to watch for

- Mappings affect identity resolution and downstream segments; call this out before changing an existing mapping.
- Confirm field types are compatible before writing; report API errors verbatim.
- Read-only inspection needs no confirmation; any create, update or delete does.
- Calls count toward Connect API limits and Flex Credit usage.
