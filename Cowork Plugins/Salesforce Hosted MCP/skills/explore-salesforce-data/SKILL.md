---
name: explore-salesforce-data
description: |
  Queries any Salesforce object, including custom objects, and discovers schema
  on demand. Use when the user asks about Salesforce data that standard sales
  skills don't cover — "query [custom object] in Salesforce", "what fields are
  on [object]", "run a SOQL query", "show me Salesforce records where...",
  "what objects exist in Salesforce", or "report on [custom object]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Search
---

# Explore Salesforce Data

## What This Skill Does

The catch-all for Salesforce data work: discovers the org's schema, then builds
and runs correct SOQL against any standard or custom object. Use this whenever
the request falls outside the account, opportunity, contact, lead, case, and
task skills.

## When to Activate

- User asks about a custom object or a field the other skills do not cover
- User asks what objects or fields exist in their org
- User asks for an ad-hoc query, count, or report
- Another skill's query failed with `INVALID_FIELD` or `INVALID_TYPE`

## Workflow

1. **Discover the object.** Call `getObjectSchema` with no parameters to list
   every object in the org. Match the user's wording to an API name — custom
   objects end in `__c` (for example `Project__c`, `Renewal__c`).

2. **Read the field schema.** Call `getObjectSchema` with `object-name` set to
   the API name. Never guess field API names — custom fields end in `__c`, and
   labels rarely match API names exactly.

3. **Confirm intent before querying** when the request is loose. Restate what
   you are about to retrieve, including filters and the row limit.

4. **Build and run the query** with `soqlQuery`:

   ```sql
   SELECT Id, Name, Status__c, Amount__c, Account__r.Name, CreatedDate
   FROM Project__c
   WHERE Status__c = 'Active' AND CreatedDate = LAST_N_DAYS:90
   ORDER BY CreatedDate DESC LIMIT 50
   ```

   Always include an explicit `LIMIT`. Always project only the fields you need.

5. **Aggregate instead of listing** when the user wants a count or total:

   ```sql
   SELECT Status__c, COUNT(Id) RecordCount, SUM(Amount__c) TotalAmount
   FROM Project__c WHERE CreatedDate = THIS_YEAR GROUP BY Status__c
   ```

6. **Use text search** when the user has a term rather than a filter:

   ```
   FIND {renewal} IN ALL FIELDS RETURNING Project__c(Id, Name, Status__c LIMIT 20)
   ```

7. **Present results** as a table, state the row count and whether the limit
   was hit, and show the SOQL you ran so the user can verify or reuse it.

8. **Writes require confirmation.** If the exploration leads to a change, state
   the object, record Id, and exact field values, get an explicit yes, then call
   `createSobjectRecord` or `updateSobjectRecord`. Deletions belong to the
   `delete-salesforce-record` skill.

## Output Format

**Query:** `SELECT ... FROM Project__c WHERE ... LIMIT 50`
**Returned:** 12 of 12 matching records

| Name | Status | Amount | Account | Created |
|---|---|---|---|---|
| Acme Migration | Active | $120,000 | Acme Corp | 2026-06-04 |

If the limit was reached, say so: "Showing the first 50; there are more."

## Handling Edge Cases

- **`INVALID_FIELD`:** Re-read the schema with `getObjectSchema` and correct
  the API name. Do not retry the same query.
- **Relationship syntax:** Traversing a lookup uses `__r`, not `__c` —
  `Account__r.Name`, `Custom_Lookup__r.Field__c`. Standard lookups use the
  relationship name: `Owner.Name`, `Account.Industry`.
- **Child records:** Either use a subquery
  (`SELECT Id, (SELECT Id, Name FROM Contacts) FROM Account WHERE ...`) or the
  `getRelatedRecords` tool with `sobject-name`, `id`, and `relationship-path`.
- **Escaping:** In SOQL string literals escape `'` as `\'`. Never interpolate
  raw user text into a query without escaping it.
- **Null checks:** SOQL uses `= null` and `!= null`, not `IS NULL`.
- **Aggregate aliases:** Alias aggregates (`COUNT(Id) RecordCount`), otherwise
  results come back as `expr0`.
- **Limits:** A transaction returns at most 50,000 records via SOQL, and `find`
  (SOSL) returns at most 2,000. Aggregate or narrow the filter rather than
  paging through everything.
- **Ambiguous object names:** If both `Project__c` and `Project_Plan__c` exist,
  list them and ask which the user means.
- **Custom server tools:** The org may run a custom MCP server that adds tools
  beyond the sObject set — Flows, Apex invocable actions, `@AuraEnabled`
  methods, Apex REST, or Connect API endpoints. If a request cannot be served by
  SOQL alone, check the available tools before telling the user it is
  impossible; a purpose-built tool may already exist.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to run that query. You should
   see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Schema discovery only returns objects and fields the signed-in user can see.
   If an expected object is missing, their profile or field-level security is
   the likely cause, not the connector.

## Additional Resources

- **`references/soql-cookbook.md`** — SOQL and SOSL syntax, date literals,
  aggregate patterns, relationship traversal, escaping rules, and limits.
