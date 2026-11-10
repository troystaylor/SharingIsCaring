---
name: c3-change-data
description: Create, update or delete records in a C3 application with a preview, confirmation and verification. Use for "update these assets", "add a record", "set the status to...", or "remove these test records". Requires entityDataModifier or a custom write tool.
---

# Change C3 data

Always follow `c3-write-safety`. Use the C3 App MCP connector.

## Steps

1. **Check the tool.** Confirm `entityDataModifier`, or a custom create or update tool, is in the live tool list. If not, tell the user an App Admin must enable it, and stop.
2. **Identify the records.** Use `c3-explore-data` to find the type, its fields and the exact records. Only write fields that exist and aren't read-only.
3. **Preview.** Show a table: record, field, current value, new value. For creates, show every field you'll set and the ones the type will default.
4. **Confirm.** Wait for an explicit yes.
5. **Write.** Call the tool once for the approved set. Write by record id.
6. **Verify.** Re-read the records with `entityDataRetriever` and show the final values.

## Rules

- Respect type validation. If C3 rejects a value, show the error and propose a corrected value. Don't coerce silently.
- Don't change many records on one approval. Split anything over 25 records into batches, each confirmed.
- Don't delete as a shortcut for fixing a bad create. Update it, or ask.
- Data in C3 may also be loaded by pipelines from source systems, and a manual change can be overwritten on the next load. If the type looks pipeline-fed (for example it has a source system or data source field), warn the user before changing it.
