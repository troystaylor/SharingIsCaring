---
name: query-crm-analytics-dataset
description: Run a SAQL query against a CRM Analytics dataset by first inspecting its schema, then returning structured results. Use when asked for numbers from a CRM Analytics dataset such as top accounts by revenue, pipeline by region, or rep performance versus quota.
---

# Query CRM Analytics Dataset

## When to use this skill

- The user wants figures from CRM Analytics data rather than a Tableau Next semantic model.
- Cue phrases: "top 10 accounts by revenue last quarter", "total pipeline by region", "show rep performance versus quota".

## How to do it

1. Find the dataset with `list_datasets` (use `q` for keywords, `folderId` to scope to an app from `list_folders`; `get_folder` shows an app's details and permissions).
2. Call `get_dataset` and capture the dataset Id and `currentVersionId`.
3. Call `get_xmd` with that dataset and version to get exact field API names, types and labels. Never guess field names.
4. Write the SAQL. The load stage must be `q = load "<datasetId>/<currentVersionId>";` and the query must end with a `limit`, for example `q = limit q 100;`.
5. Run it with `execute_query`, setting `timezone` to the user's IANA timezone when dates matter.
6. Present results as a table with a one-line takeaway, and show the SAQL used so it can be reused.

## What to watch for

- CRM Analytics tools are read-only; do not claim anything was created or changed.
- If assets are missing or lists look short, the user probably lacks access to them.
- Queries count against the org's Wave API limits; keep them targeted.
- CRM Analytics here is a Beta server.
