---
name: inventory-data-model
description: Inventory what is configured in Salesforce Data 360 - data lake objects, data model objects, data streams, mappings, calculated insights, segments and data spaces. Use when asked what exists in Data 360, how data flows, or to document the data model.
---

# Inventory Data Model

## When to use this skill

- The user wants an overview or documentation of Data 360 configuration.
- Cue phrases: "what is in our Data 360", "list data streams and mappings", "which DMOs do we have", "show calculated insights".

## How to do it

1. Call `search` for each area of interest (for example "list data model objects", "list data streams", "list calculated insights") to learn the family tool names.
2. For each tool, call `payload_examples` if you are unsure of inputs, then call `execute` with `toolName` and `paramsJson`. Use the `list` and `get` style tools only.
3. Cover, as relevant: data spaces, data lake objects, data model objects, DLO-to-DMO mappings, data streams, transforms, calculated insights, segments, activations and semantic data models.
4. Present a structured inventory: a table per area with name, API name, status and last modified, followed by a short narrative of how data flows from streams to DLOs to DMOs to segments.

## What to watch for

- This skill is read-only; never call create, update or delete tools while inventorying.
- Page through large lists rather than assuming the first page is complete.
- Calls count toward Connect API limits and Flex Credit usage.
