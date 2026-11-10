---
name: c3-explore-data
description: Find out what is in a C3 application and answer questions about its data - list entity types, inspect fields, filter, count and aggregate records, with evidence. Use for "what types does this app have", "show me the assets where...", "how many work orders are open", or "what fields does X have".
---

# Explore C3 data

Read-only. Use the C3 App MCP connector. Apply `c3-access` first.

## Steps

1. **Find the type.** Use `getEntityTypes` to list types, and match the user's words to a type name. If several fit, list the candidates and ask which one. If `getEntityTypes` isn't enabled, ask the user for the type name rather than guessing.
2. **Learn the fields.** Use `entityFieldRetriever` for the type's public fields. Use `getC3TypeInformation` if you need relationships, calculated fields or documentation. Only filter and select fields you've seen listed.
3. **Query.** Use `entityDataRetriever` with the narrowest filter and a field list. Start with a count or a small page (10 to 25 rows) and widen only if needed. Use its aggregation support for totals, averages and groupings instead of pulling rows and counting them yourself.
4. **Answer.** Lead with the answer, then show the filter you used and the row count. Offer the next cut, such as a breakdown by site or status.

## Rules

- Don't use `runPyCode` or `runJsCode` for a question `entityDataRetriever` can answer. Code runs are harder to audit.
- If a result is truncated or paged, say so, and don't present a partial count as a total.
- If a field comes back empty for every row, say it may be unpopulated or hidden by the user's roles, and don't assume zero.
- Don't show raw ids or system fields unless the user asked. Show names and business fields.
- If the user wants to change the data, switch to `c3-change-data`. For metrics over time, switch to `c3-metrics`.
