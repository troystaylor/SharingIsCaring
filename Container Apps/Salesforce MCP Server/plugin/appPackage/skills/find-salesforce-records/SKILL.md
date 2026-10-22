---
name: find-salesforce-records
description: |
  Finds and reads Salesforce records. Use when the user asks for accounts,
  contacts, leads, opportunities, cases, or any custom object, for example
  "show my open opportunities", "find the Acme account", "who owns this case",
  "how many contacts are in California", "look up record 001...", or "search
  Salesforce for Contoso".
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: retrieval
---

# Find Salesforce Records

## What This Skill Does

Answers questions about Salesforce data by querying it, and returns the records
with links that open them in Salesforce.

## When to Activate

- The user asks to list, find, count, or look up records
- The user gives a name or keyword and does not say which object holds it
- The user gives a record ID and wants its details

## Workflow

1. **Pick the right way in.**
   - Object and filter known: `soql_query`.
   - A name or keyword, object unknown: `sosl_search`, which searches several
     objects at once.
   - A record ID: `get_record`. Pass `fields` to keep the answer small.

2. **Confirm names before querying an object you are not sure of.** Call
   `list_objects` with `nameContains` to find an object's API name, then
   `describe_object` with `fieldNameContains` for field names. Custom objects and
   fields end in `__c`. A query with a wrong name fails, so checking is cheaper
   than guessing.

3. **Write a focused query.** Select only the fields the answer needs, filter in
   the `WHERE` clause, and add a `LIMIT`. Never use `SELECT *`; SOQL has no such
   form.

4. **Page only when needed.** If the result says `done: false` and includes
   `nextRecordsUrl`, pass that value unchanged to `query_more`. Do not page
   through a large result to answer a "how many" question. Use
   `SELECT COUNT() FROM ...` instead.

5. **Answer with links.** Every record includes a `recordUrl`. Include it so the
   user can open the record.

## What to Watch For

- **An empty result is an answer, not an error.** Say that nothing matched and
  offer to widen the search. Never invent a record to fill the gap.
- **`truncated: true` means records were left out** to keep the response small.
  Say so, then narrow the `WHERE`, select fewer fields, or add a `LIMIT`. When a
  result is truncated there is no `nextRecordsUrl`, because following it would
  skip the records that were dropped.
- **Results reflect the signed-in person's own Salesforce permissions.** If a record
  they expect is missing, they may not have access to it. Call
  `get_connection_info` to confirm who the request runs as before concluding the
  record does not exist.
- **Do not echo large field values** such as long text or notes unless asked.
- If the user wants to *change* data, switch to `change-salesforce-records`. If
  they ask how an object is built, switch to `understand-salesforce-schema`.
