# SOQL and SOSL Cookbook

Reference for building correct queries against the Salesforce-hosted
`platform/sobject-all` MCP server.

## Tool parameters

| Tool | Parameters |
|---|---|
| `getObjectSchema` | `object-name` (optional — omit for the full object index) |
| `soqlQuery` | `query` — one SOQL statement |
| `find` | `search` — one SOSL statement |
| `getUserInfo` | none |
| `listRecentSobjectRecords` | `sobject-name` |
| `getRelatedRecords` | `sobject-name`, `id`, `relationship-path` |
| `createSobjectRecord` | `sobject-name`, `body` |
| `updateSobjectRecord` | `sobject-name`, `id`, `body` |
| `updateRelatedSobjectRecord` | `sobject-name`, `id`, `relationship-path`, `body` |
| `deleteSobjectRecord` | `sobject-name`, `id` |
| `deleteRelatedSobjectRecord` | `sobject-name`, `id`, `relationship-path` |

## SOQL basics

```sql
SELECT Id, Name, StageName, Amount
FROM Opportunity
WHERE IsClosed = false AND Amount > 50000
ORDER BY Amount DESC NULLS LAST
LIMIT 25
```

Rules that trip up generated queries:

- `SELECT *` does not exist. List every field explicitly.
- Always include `LIMIT`.
- Null tests use `= null` / `!= null`, never `IS NULL`.
- String literals use single quotes; escape an embedded quote as `\'`.
- Date and datetime literals are **unquoted**: `CloseDate > 2026-01-31`,
  `CreatedDate > 2026-01-31T00:00:00Z`.
- `LIKE` supports `%` (any sequence) and `_` (single character), and works on
  text fields only.
- `IN` takes a quoted, comma-separated list: `WHERE Id IN ('006aaa', '006bbb')`.
- Boolean fields compare to `true` / `false` without quotes.
- `NULLS FIRST` / `NULLS LAST` control null placement in `ORDER BY`.
- `OFFSET` is capped at 2,000; prefer narrowing filters over deep paging.

## Date literals

`TODAY`, `YESTERDAY`, `TOMORROW`, `THIS_WEEK`, `LAST_WEEK`, `NEXT_WEEK`,
`THIS_MONTH`, `LAST_MONTH`, `NEXT_MONTH`, `THIS_QUARTER`, `LAST_QUARTER`,
`NEXT_QUARTER`, `THIS_YEAR`, `LAST_YEAR`, `THIS_FISCAL_QUARTER`,
`LAST_FISCAL_QUARTER`, `NEXT_FISCAL_QUARTER`, `THIS_FISCAL_YEAR`,
`LAST_N_DAYS:n`, `NEXT_N_DAYS:n`, `LAST_N_WEEKS:n`, `LAST_N_MONTHS:n`,
`NEXT_N_MONTHS:n`, `LAST_N_QUARTERS:n`, `LAST_N_FISCAL_QUARTERS:n`.

Fiscal literals follow the org's fiscal calendar; the non-fiscal versions are
calendar-based. Always tell the user which one you used.

## Relationships

**Parent traversal (child → parent)** — up to five levels:

```sql
SELECT Id, Name, Account.Name, Account.Owner.Name, Owner.Profile.Name
FROM Opportunity WHERE Id = '006XXXXXXXXXXXXXXX'
```

Custom lookups traverse with `__r`:

```sql
SELECT Id, Name, Project__r.Status__c FROM Task__c LIMIT 10
```

**Child subquery (parent → children)** — one level:

```sql
SELECT Id, Name,
       (SELECT Id, Name, StageName, Amount FROM Opportunities WHERE IsClosed = false),
       (SELECT Id, Name, Title, Email FROM Contacts LIMIT 10)
FROM Account WHERE Id = '001XXXXXXXXXXXXXXX'
```

The child relationship name is plural for standard objects (`Opportunities`,
`Contacts`, `Cases`, `Tasks`) and ends in `__r` for custom objects
(`Projects__r`). Confirm the exact name with `getObjectSchema`.

The `getRelatedRecords` tool is a simpler alternative for a single hop:
`sobject-name: Account`, `id: 001...`, `relationship-path: Opportunities`.

**Semi-joins:**

```sql
SELECT Id, Name FROM Account
WHERE Id IN (SELECT AccountId FROM Opportunity WHERE IsClosed = false AND Amount > 100000)
```

## Aggregates

```sql
SELECT StageName, COUNT(Id) DealCount, SUM(Amount) TotalAmount,
       AVG(Amount) AvgAmount, MAX(CloseDate) LatestClose
FROM Opportunity
WHERE IsClosed = false
GROUP BY StageName
HAVING SUM(Amount) > 100000
ORDER BY SUM(Amount) DESC
```

- Alias every aggregate, or the result keys come back as `expr0`, `expr1`, ...
- Every non-aggregated `SELECT` field must appear in `GROUP BY`.
- `ORDER BY` may only reference grouped fields or aggregate expressions.
- `HAVING` filters aggregates; `WHERE` filters rows before grouping.
- `COUNT()` with no argument returns just a row count.
- `GROUP BY ROLLUP(...)` and `GROUP BY CUBE(...)` add subtotal rows.
- Date grouping helpers: `CALENDAR_YEAR(CloseDate)`, `CALENDAR_MONTH(...)`,
  `FISCAL_QUARTER(...)`, `FISCAL_YEAR(...)`.

## SOSL (`find` tool)

```
FIND {Acme*} IN NAME FIELDS RETURNING
  Account(Id, Name, Industry, Owner.Name LIMIT 10),
  Contact(Id, Name, Title, Email, Account.Name LIMIT 10),
  Lead(Id, Name, Company, Status LIMIT 10)
```

- Search term goes in braces; the term must be at least two characters.
- Scopes: `IN ALL FIELDS`, `IN NAME FIELDS`, `IN EMAIL FIELDS`,
  `IN PHONE FIELDS`, `IN SIDEBAR FIELDS`.
- Wildcards: `*` (any sequence, not allowed as the first character) and `?`
  (single character).
- Multi-word phrases need quotes: `FIND {"Dana Reyes"}`.
- Logical operators inside braces: `AND`, `OR`, `AND NOT`.
- Escape these characters with a backslash: `? & | ! { } [ ] ( ) ^ ~ * : \ " ' + -`
- Returns at most 2,000 records total across all objects.
- SOSL is the right tool for "find something named roughly X"; SOQL `LIKE` is
  right when you already know the object and want precise filtering.

## Writes

`createSobjectRecord` and `updateSobjectRecord` take a `body` object of API
field name to value:

```json
{
  "sobject-name": "Opportunity",
  "body": {
    "Name": "Acme Platform Expansion",
    "AccountId": "001XXXXXXXXXXXXXXX",
    "StageName": "Prospecting",
    "CloseDate": "2026-12-31",
    "Amount": 250000
  }
}
```

- Dates are `YYYY-MM-DD`; datetimes are ISO-8601 UTC (`2026-12-31T17:00:00Z`).
- Numbers and booleans are unquoted JSON values, not strings.
- Never write formula, roll-up, or system fields (`Id`, `CreatedDate`,
  `LastModifiedDate`, `IsClosed`, `IsWon`, `Name` on Contact, `CaseNumber`).
- To clear a field, set it to `null`.
- Picklist values must match the org's configured values exactly, including
  case and punctuation. Verify with `getObjectSchema`.

## Common errors

| Error | Cause | Fix |
|---|---|---|
| `INVALID_FIELD` | Field API name wrong or not visible | Re-read `getObjectSchema` |
| `INVALID_TYPE` | Object API name wrong or no access | List objects with `getObjectSchema` |
| `MALFORMED_QUERY` | Syntax error, unescaped quote, quoted date literal | Rebuild the query |
| `REQUIRED_FIELD_MISSING` | Missing required field on create | Check schema for required fields |
| `FIELD_CUSTOM_VALIDATION_EXCEPTION` | Org validation rule rejected the write | Report the rule's message to the user verbatim |
| `INSUFFICIENT_ACCESS_OR_READONLY` | User lacks permission, or the field is read-only | Explain it is a Salesforce permission, not a connector fault |
| `DUPLICATES_DETECTED` | Org duplicate rule blocked the create | Show the existing record and ask how to proceed |

## Limits

- SOQL: 50,000 records maximum per transaction.
- SOSL: 2,000 records maximum per `find` call.
- `OFFSET`: 2,000 maximum.
- Deleted records go to the Recycle Bin with a 15-day recovery window; there is
  no undelete tool on this server.
