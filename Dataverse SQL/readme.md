# Dataverse SQL

Query Dataverse with SQL `SELECT` statements, from both Power Automate and Copilot Studio.

The Dataverse Web API accepts a read-only T-SQL subset through the [`sql` query option](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/sql). This connector puts a usable surface on it: typed actions for flows and apps, and an MCP endpoint that gives a Copilot Studio agent discovery tools so it can write SQL against columns it has confirmed exist.

## What the connector does for you

Calling the `sql` option directly has two rough edges. This connector handles both.

**The entity set in the URL must match the base table of the query.** A query beginning `FROM account` has to be sent to `/accounts`, and `FROM msdyn_botsession` to `/msdyn_botsessions`. The connector reads the `FROM` clause, resolves the entity set from table metadata, and builds the URL. You write the table name; you never write the entity set.

**The supported subset is narrow, and a rejected statement returns a raw fault.** Every statement is checked locally first. Anything unsupported comes back as a named issue — `select_star`, `having`, `unsupported_join` — describing what to write instead, with no round trip to Dataverse.

The connector is read-only. Statements must be a single `SELECT`; write keywords are rejected before the request is built.

## Prerequisites

- A Dataverse environment and its URL, such as `https://contoso.crm.dynamics.com`
- Permission to create an app registration in your Microsoft Entra tenant
- Permission to create custom connectors in your Power Platform environment
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`)

## Setup

### 1. Register an application in Microsoft Entra ID

Create an app registration, then add the **Dynamics CRM → user_impersonation** delegated permission and grant consent. Add `https://global.consent.azure-apim.net/redirect` as a web redirect URI for now; you will add a second, connector-specific one after deploying.

Copy the **Application (client) ID** and create a client secret.

### 2. Point the connector at your environment

In `apiDefinition.swagger.json`, replace the `host` with your environment hostname:

```json
"host": "contoso.crm.dynamics.com"
```

In `apiProperties.json`, replace `[YOUR_CLIENT_ID]` with your application ID, and replace `https://org.crm.dynamics.com` in both `AzureActiveDirectoryResourceId` and `resourceUri` with your environment URL. Leave `redirectUrl` as `[YOUR_REDIRECT_URL]` — the platform generates and substitutes the real value when you deploy.

### 3. Deploy

```powershell
pac auth create --environment <your-environment-url>
pac connector create --api-definition-file apiDefinition.swagger.json `
                     --api-properties-file apiProperties.json `
                     --script-file script.csx
```

The command prints the new connector ID. Keep it for the next step.

Then supply the client secret, which `pac` has no parameter for — do it one of two ways:

- In **Power Apps → Custom connectors → Dataverse SQL → Edit → Security**, paste the secret and save.
- Or add it to a **temporary copy** of `apiProperties.json` as `clientSecret` inside `oAuthSettings`, deploy from that copy, and delete it afterwards. Never put a secret in the file you keep.

### 4. Register the generated redirect URL

The platform creates a redirect URL unique to this connector and environment. Read it back:

```powershell
pac connector download --connector-id <connector-id> --outputDirectory ./verify
```

The value is at `properties.connectionParameters.token.oAuthSettings.redirectUrl`. It encodes the publisher prefix, the connector name and the environment, with characters escaped — `_` becomes `-5f` and a space becomes `-20` — so a connector named `Dataverse SQL` under publisher `new` takes the shape:

```
https://global.consent.azure-apim.net/redirect/new-5fdataverse-20sql-5f<ENVIRONMENT_SUFFIX>
```

The trailing segment identifies your environment and is the same for every connector in it. Copy the whole value exactly as downloaded rather than assembling it by hand. Add it to your app registration's web redirect URIs alongside the generic one. OAuth consent fails until it is registered. Deploying the same connector to a second environment produces a different URL, and both must be registered.

### 5. Create a connection and test

In **Power Apps → Custom connectors**, click the **+** on the Dataverse SQL row to create a connection, and sign in. Then run **List tables** with no parameters. A list of your tables confirms auth, host and script are all wired up.

## Operations

| Operation | Purpose |
|-----------|---------|
| **Execute SQL** | Run a `SELECT` and return rows, with a next link when more are available |
| **Get next page** | Follow the next link from a previous result |
| **Count rows** | `COUNT(*)` against a table, with an optional filter |
| **Validate SQL** | Check a statement and resolve its base table without running it |
| **List tables** | Table logical names, entity set names and display names |
| **List columns** | Columns of one table, for building a `SELECT` list |
| **Invoke Dataverse SQL MCP** | JSON-RPC 2.0 endpoint for Copilot Studio |

A hidden **Get row schema** operation backs the dynamic content described below. It is marked internal and does not appear in the action list.

**Execute SQL** takes the statement, a page size, an optional row budget, and an option to include formatted values. Formatted values add display text for choice, lookup and date columns as `@OData.Community.Display.V1.FormattedValue` annotations next to the raw values.

### Dynamic content

The designer resolves the columns your statement returns and offers them as typed dynamic content, so you can pick `name` or `revenue` directly instead of writing `item()?['name']`.

Column types come from Dataverse metadata for whichever table each alias refers to, so in a join `c.birthdate` is typed from `contact` rather than the base table. Aggregates are typed from their function — `COUNT(*)` is a whole number, `SUM()` a decimal, and `MIN()`/`MAX()` inherit the type of the column they wrap.

Two points worth knowing:

- **Alias your aggregates.** Dataverse names an aggregate column after its alias, so `SELECT COUNT(*) AS total` produces a `total` field while a bare `SELECT COUNT(*)` produces a field this connector cannot name and therefore omits.
- **Dataverse decides what comes back, not your `SELECT` list.** A plain query also returns the primary key and `@odata.etag`, and some tables add more — `systemuser` and `team` return `ownerid` unselected, while `queue` and `role` do not. An aggregate query, by contrast, returns *only* its aliased columns. Because that pattern is not derivable from metadata, the connector runs your statement once for a single row at design time and reads the real column names rather than predicting them.

If the table is empty there is no row to inspect, so the connector falls back to the `SELECT` list plus the primary key and etag, and reports `sampled: false`.

Resolution happens at design time against the statement in the box. If you build the statement from an expression or dynamic content, there is nothing to inspect yet and `rows` stays untyped — that is expected, and the action still runs normally.

## Copilot Studio

Add the connector as a tool in Copilot Studio. It exposes six tools:

| Tool | Purpose |
|------|---------|
| `list_tables` | Find the logical name for a `FROM` clause |
| `describe_table` | Confirm column names and types before writing a `SELECT` |
| `validate_sql` | Check a statement without running it |
| `run_sql` | Execute and return rows, optionally collecting several pages |
| `next_page` | Continue a paged result |
| `count_rows` | Count rows, optionally filtered |

The tool descriptions carry the supported subset, so an agent generally writes a valid statement on the first attempt. When it does not, failures come back as tool results rather than protocol errors — the agent reads the issues and valid values and retries rather than stalling. An unknown table name returns near matches from your environment.

The intended path is `list_tables` → `describe_table` → `run_sql`. An agent that skips discovery and guesses column names will be corrected by the `unknown_column` error, but the round trips are avoidable.

## Supported SQL

| Feature | Supported |
|---------|-----------|
| Select | `SELECT`, `SELECT DISTINCT` |
| Joins | `INNER JOIN`, `LEFT JOIN`, across multiple tables |
| Filtering | `WHERE` with `=` `!=` `>` `<` `>=` `<=` `LIKE` `IN` `NOT IN` `IS NULL` `IS NOT NULL` `BETWEEN` `AND` `OR`, and nested parentheses |
| Aggregation | `GROUP BY` with `COUNT` `SUM` `AVG` `MIN` `MAX` |
| Sorting | `ORDER BY [ASC\|DESC]` over plain columns |
| Functions | `DATEADD` and `GETUTCDATE`, in `WHERE` and `ON` only |

Not supported: `SELECT *`, subqueries, CTEs, `HAVING`, `UNION`, `RIGHT JOIN`, `FULL JOIN`, `CROSS JOIN`, `CASE`, `COALESCE`, window functions, and string, date or math functions beyond the two above. `WHERE` cannot compare two literals or two columns, and cannot apply a function to a column value. `ORDER BY` and `GROUP BY` take columns only — including date parts, so `GROUP BY MONTH(createdon)` is rejected.

A statement is one `SELECT`. Multiple result sets are not supported.

```sql
SELECT a.name, COUNT(*) AS contact_count
FROM account AS a
INNER JOIN contact AS c ON a.accountid = c.parentcustomerid
WHERE a.createdon >= DATEADD(day, -30, GETUTCDATE())
GROUP BY a.name
ORDER BY a.name
```

## Paging

Set the page size on **Execute SQL**; it becomes `Prefer: odata.maxpagesize`. When more rows exist, `hasMore` is true and `nextLink` carries the continuation — pass it to **Get next page** unchanged. Next links are checked against your environment before they are followed.

**Get next page** returns the same shape, but `sql`, `table` and `entitySet` come back empty: a next link carries the continuation token, not the statement that produced it. Carry those values forward from the first call if you need them.

To collect several pages in one action, set **Maximum rows**. The connector then follows next links itself until it has that many rows, and `stoppedBecause` reports which limit ended the run:

| `stoppedBecause` | Meaning |
|------------------|---------|
| `complete` | No more rows exist |
| `page` | A single page was requested and more rows remain |
| `maxRows` | The row budget was reached |
| `timeBudget` | Paging stopped early to stay inside the script time limit |

The final page may carry the total slightly past **Maximum rows** — pages are never split. Whenever paging stops with rows remaining, `nextLink` is still returned, so a flow can resume exactly where it left off. Leave **Maximum rows** at 0 for a single page.

`TOP` and `OFFSET … FETCH` are accepted but flagged with a warning, because the Dataverse reference lists them as supported syntax while its paging guidance says they are not honored. Prefer the page size, or page by filtering on the last id you saw:

```sql
SELECT name, accountid
FROM account
WHERE accountid > '00000000-0000-0000-0000-000000000000'
ORDER BY accountid
```

## Throttling

Dataverse enforces [service protection limits](https://learn.microsoft.com/power-apps/developer/data-platform/api-limits) by returning `429` with a `Retry-After` header. The connector waits and retries automatically when that wait is short.

The wait scales with how demanding the previous five minutes were, so it can exceed the time a connector script is allowed to run. When it does, the call returns a `throttled` error carrying `retryAfterSeconds` rather than being killed mid-wait. Handle it in a flow with a **Delay** of that many seconds followed by a retry.

## Limits

| Limit | Value | Effect |
|-------|-------|--------|
| Script execution | 2 minutes | Bounds auto-paging and throttle retries; both stop early and hand back a next link |
| Aggregate evaluation | 50,000 records | Error `8004E023`, reported as `aggregate_limit_exceeded` |
| Page size | 5,000 rows | Values above this are clamped |
| Columns per describe | 250 | Use the search parameter on wide tables |
| Tables per list | 1,000 default, 5,000 max | Adjust with the limit parameter |

For the aggregate limit, add a filter — typically a date range or a subset of a choice column — and combine the results of several runs.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `invalid_sql` with an `issues` list | The statement uses an unsupported construct | Each issue names the rule and what to write instead |
| `entity_set_in_from` | The `FROM` clause names an entity set, such as `accounts` | Use the table name, `account`. The connector adds the entity set itself |
| `unknown_table` | No such table in this environment | Use a name from **List tables**; near matches are returned in `validValues` |
| `unknown_column` | A column in the `SELECT` list does not exist | Confirm names with **List columns** |
| `aggregate_limit_exceeded` | The aggregate evaluated more than 50,000 records | Narrow the `WHERE` clause and combine several runs |
| `throttled` | Dataverse service protection limits are in effect | Delay for `retryAfterSeconds`, then retry |
| `invalid_next_link` | The next link was edited, or came from another environment | Pass it back exactly as returned |
| No dynamic content for `rows` | The statement is built from an expression, so it cannot be inspected at design time | Expected. Parse rows with the column names you selected, or type a literal statement to pick up the fields |
| Sign-in fails during connection creation | The generated redirect URL is not on the app registration | Complete step 4 |
| `401` on every call | Missing `Dynamics CRM → user_impersonation`, or `resourceUri` does not match the environment | Check the permission and both URLs in `apiProperties.json` |

## Application Insights

Telemetry is off by default. To enable it, set `APP_INSIGHTS_ENABLED` to `true` in `script.csx` and replace `APP_INSIGHTS_KEY` with your instrumentation key. Events cover executed statements, MCP tool calls, validation failures and unhandled errors. Telemetry failures never fail an operation.

## Files

| File | Contents |
|------|----------|
| `apiDefinition.swagger.json` | Operation definitions and response schemas |
| `apiProperties.json` | OAuth configuration and script operation registration |
| `script.csx` | Validator, metadata resolution, schema inference, typed operations and MCP endpoint |

## Reference

- [Use SQL to query data with the Dataverse Web API](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/sql)
- [Page results](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/page-results)
- [Service protection API limits](https://learn.microsoft.com/power-apps/developer/data-platform/api-limits)
- [Query anti-patterns](https://learn.microsoft.com/power-apps/developer/data-platform/query-antipatterns)
- [Write code in a custom connector](https://learn.microsoft.com/connectors/custom-connectors/write-code)
- [OpenAPI extensions for custom connectors](https://learn.microsoft.com/connectors/custom-connectors/openapi-extensions)
