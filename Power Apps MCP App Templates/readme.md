# Power Apps MCP App Templates

Reusable widget templates and tool prompt patterns for Power Apps custom tools in Microsoft 365 Copilot.

## Naming: Widgets vs MCP Apps

Microsoft uses two terms interchangeably:

- **Widgets** — the self-contained HTML files that render a tool's JSON output visually inside a chat conversation. This is the most common term in [Power Apps documentation](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/generate-mcp-app-widgets).
- **MCP apps** — the protocol extension (`@modelcontextprotocol/ext-apps`) that enables MCP servers to deliver interactive UIs to hosts. This term comes from the [MCP specification](https://apps.extensions.modelcontextprotocol.io/api/documents/Overview.html).

In this kit, **widget** refers to an individual HTML file you attach to a custom tool. **MCP app** refers to the protocol those widgets use to communicate with the host.

## What Is App MCP

Model-driven Power Apps can generate an MCP server and declarative agent from your app. Users interact with app data directly from Microsoft 365 Copilot. Custom tools extend the built-in grid and form tools with:

1. **Prompt-based instructions** — natural language that tells the AI model how to query Dataverse and shape JSON output
2. **Widgets** (optional) — HTML files that render the tool's JSON output as interactive visuals inside chat

No external hosting required. The MCP server runs inside Power Apps.

## Prerequisites

- A model-driven Power App
- Microsoft 365 Copilot license
- Permission to upload custom apps in Microsoft Teams
- (Optional) GitHub Copilot CLI or Claude Code with the `generate-mcp-app-ui` skill

## Widget Templates

All widgets follow the official MCP apps protocol and are ready to paste into the tool editor's UX field.

| Widget | File | Data Pattern | Use Case |
|--------|------|-------------|----------|
| Base Template | `base-widget.html` | Any JSON | Starting point — copy and customize |
| KPI Dashboard | `kpi-dashboard.html` | Aggregate metrics | Revenue, case counts, pipeline totals |
| Record Timeline | `record-timeline.html` | Date-sequenced events | Case history, activity feed, audit trail |
| Status Pipeline | `status-pipeline.html` | Stage-grouped records | Opportunity pipeline, case funnel |
| Comparison Grid | `comparison-grid.html` | Multi-option scored matrix | Vendor comparison, option analysis |
| Data Table | `data-table.html` | Rows + columns (sortable) | Account list, query results, any tabular data |
| Approval Card | `approval-card.html` | Single record + actions | Expense approval, change request, access review |
| Progress Tracker | `progress-tracker.html` | Sequential steps | BPF stages, onboarding checklist, multi-step approval |
| Alert List | `alert-list.html` | Severity-prioritized items | SLA breaches, overdue tasks, data quality flags |
| Detail Card | `detail-card.html` | Sectioned record profile | Account/contact summary, case detail, asset profile |
| Donut Chart | `donut-chart.html` | Labeled segments | Cases by priority, leads by source, ticket distribution |
| Bar Chart | `bar-chart.html` | Category bars | Revenue by region, count by team, product volume |
| Metric Sparkline | `metric-sparkline.html` | Value + trend line | Monthly revenue trend, case volume over time |
| Kanban Board | `kanban-board.html` | Cards grouped by column | Cases by status, opportunities by stage, task boards |
| Hierarchy Tree | `hierarchy-tree.html` | Parent-child nesting | Account hierarchy, BU structure, territory tree |
| Calendar Heat Map | `calendar-heatmap.html` | Date × intensity grid | Case creation density, activity patterns by day |
| Before/After | `before-after.html` | Field-level diffs | Audit history, change tracking, config review |
| Calendar | `calendar.html` | Month grid with events | Appointments, task due dates, close dates, SLA deadlines |

### How to Use a Widget

1. **Create a custom tool** in your model-driven app's App MCP tab
2. **Write prompt instructions** that produce JSON matching the widget's contract (documented in each HTML file's header comment)
3. **Test the tool** and verify the JSON output
4. **Open the widget HTML** from this kit that matches your data pattern
5. **Paste the HTML** into the tool's UX field (step 2 of the tool editor)
6. **Download and re-upload** the app package to Teams or M365 Agents

### Generating Custom Widgets

For data shapes not covered by this kit, use the `/generate-mcp-app-ui` skill:

```
/generate-mcp-app-ui Show a bar chart of revenue by region. Tool output: {"regions":[{"name":"West","revenue":340000},{"name":"East","revenue":520000}]}
```

Install via: `/plugin marketplace add microsoft/power-platform-skills`

## Widget Architecture

Every widget follows the same structure:

```
┌─ <head> ─────────────────────────────────────────┐
│  Fluent Web Components (UMD)                      │
│  CSS using Fluent design tokens                   │
└───────────────────────────────────────────────────┘
┌─ <body> ─────────────────────────────────────────┐
│  #loading  — <fluent-spinner> + contextual msg    │
│  #content  — rendered visualization               │
│  #error    — error message + retry button         │
└───────────────────────────────────────────────────┘
┌─ <script type="module"> ─────────────────────────┐
│  Import App class + Fluent tokens (ESM)           │
│  show() / showError() / applyTheme() / esc()      │
│  render(data) — widget-specific                   │
│  Protocol handlers → connect()                    │
│  Optional: callServerTool for interactivity       │
│  Optional: fallback data for local preview        │
└───────────────────────────────────────────────────┘
```

### CDN Dependencies

| Package | Format | CDN | Purpose |
|---------|--------|-----|---------|
| `@modelcontextprotocol/ext-apps` | ESM | cdn.jsdelivr.net | App class (protocol) |
| `@fluentui/tokens` | ESM | cdn.jsdelivr.net | Theme tokens (light/dark) |
| `@fluentui/web-components@beta` | UMD | unpkg.com | Fluent UI elements |

### Color Tokens

Only these tokens should be used. Never hardcode hex or RGB values.

| Use | Token |
|-----|-------|
| Primary text | `var(--colorNeutralForeground1)` |
| Secondary text | `var(--colorNeutralForeground2)` |
| Primary background | `var(--colorNeutralBackground1)` |
| Card background | `var(--colorNeutralBackground2)` |
| Brand/accent | `var(--colorBrandBackground)` |
| Text on brand | `var(--colorNeutralForegroundOnBrand)` |
| Borders | `var(--colorNeutralStroke1)` |
| Error | `var(--colorStatusDangerForeground1)` |
| Success | `var(--colorStatusSuccessForeground1)` |

### Local Preview

Every widget includes a commented-out fallback section at the bottom of the `<script>` block. Uncomment it to render sample data in a plain browser — no MCP host required.

```js
// --- Local preview (uncomment to test in a browser) ---
const SAMPLE = {
  "title": "My Dashboard",
  "metrics": [{ "label": "Open Cases", "value": 42, "format": "number" }]
};
render(SAMPLE); show('content');
```

1. Open the `.html` file directly in a browser
2. Uncomment the `SAMPLE` block and replace the JSON with your test data
3. Verify the layout and formatting
4. Re-comment the block before pasting into Power Apps

Theme tokens won't resolve in a plain browser (no host context), so colors fall back to the CSS defaults.

### Interactive Callbacks (`callServerTool`)

Widgets can call back into the MCP server to trigger actions — not just display data. The Approval Card widget demonstrates this pattern.

**How it works:**

1. The widget defines a `TOOL_NAME` constant pointing to a second custom tool on the same MCP server
2. When the user clicks a button (e.g., Approve / Reject), the widget calls:
   ```js
   const result = await app.callServerTool({
     name: TOOL_NAME,
     arguments: { action: "approve", id: record.id }
   });
   ```
3. The MCP server executes the target tool, which updates Dataverse, and returns a result
4. The widget shows success/failure feedback inline

**Setting up the server-side tool:**

1. Create a second custom tool in the same app (e.g., "Process Approval")
2. In its prompt instructions, handle the `action` and `id` parameters:
   ```
   Update the {approval_table} record with id {id}.
   If action is "approve", set statuscode to 'Approved'.
   If action is "reject", set statuscode to 'Rejected'.
   Return JSON with "success" (boolean) and "message" (string).
   ```
3. In the Approval Card widget HTML, set `TOOL_NAME` to that tool's name:
   ```js
   const TOOL_NAME = 'process_approval';
   ```

When `TOOL_NAME` is `null` (the default), button clicks show visual feedback only without calling the server.

## Tool Prompt Recipes

These prompt patterns produce JSON that pairs with the widgets in this kit. Paste them into the tool's instructions field in the Power Apps designer.

### Aggregation (→ KPI Dashboard)

```
Query the {table} table. Count all records where statecode eq 0.
Sum the {column} column. Calculate the average {column}.
Count records created in the last 30 days.

Return JSON with "title" (string) and "metrics" (array).
Each metric has: "label" (string), "value" (number),
"format" ("currency", "percent", or "number"),
"trend" (number, percent change from previous period, positive = improvement),
"target" (number, optional goal for progress bar).
```

### Chronological Events (→ Record Timeline)

```
Query the {table} table. Select {date_column}, {title_column},
{description_column}, {status_column}.
Filter to records related to the input parameter {record_id}.
Order by {date_column} descending. Top 20.

Return JSON with "title" (string) and "events" (array).
Each event has: "date" (ISO 8601), "title" (string),
"description" (string), "type" ("success", "warning", "error", or "info").
Map status: Resolved→success, Escalated→warning, Failed→error, other→info.
```

### Grouped by Stage (→ Status Pipeline)

```
Query the {table} table. Group records by {stage_column}.
For each group, count records and sum {value_column}.

Return JSON with "title" (string) and "stages" (array).
Each stage has: "name" (string), "count" (number), "value" (number).
Order stages by natural progression (e.g., Qualify→Develop→Propose→Close).
```

### Side-by-Side Comparison (→ Comparison Grid)

```
Query the {table} table for records specified by the input parameter.
Evaluate each record against these criteria: {criteria_list}.
Score each criterion 1-10 based on the record's data.

Return JSON with "title" (string), "criteria" (string array),
and "options" (array of objects).
Each option has: "name" (string), "scores" (object mapping criterion→number 1-10),
"recommendation" (optional boolean, true for best overall option).
```

### Tabular Results (→ Data Table)

```
Query the {table} table. Select {columns}.
Filter: {filter_expression}. Order by {sort_column} descending. Top 25.

Return JSON with "title" (string), "columns" (array), and "rows" (array).
Each column has: "key" (field name), "label" (display name),
"format" ("currency", "percent", "date", or "text").
Each row is an object with keys matching column keys.
```

### Pending Approval (→ Approval Card)

```
Query the {approval_table} table where statuscode eq 'Pending'
and ownerid eq the current user. Select id, submitter name, amount,
category, description, and submitted date.
Return the first record.

Return JSON with "title" (string) and "record" (object).
The record has: "id" (string, record identifier),
plus any other fields as key-value pairs.
Fields containing "amount" or "cost" are formatted as currency.
Fields with ISO 8601 date strings are formatted as dates.
```

### Process Stages (→ Progress Tracker)

```
For the given record, query the business process flow stages.
Determine which stages are completed (all required fields filled),
which is the active stage, and which are still pending.

Return JSON with "title" (string) and "steps" (array).
Each step has: "name" (string), "status" ("completed", "active", or "pending"),
"detail" (string, optional context like date completed or assignee).
```

### Priority Alerts (→ Alert List)

```
Query {table} where SLA status is 'Nearing Noncompliance' or 'Noncompliant',
or where {due_date} is in the past and statecode eq 0.
Order by severity (critical first), then by due date ascending.

Return JSON with "title" (string) and "alerts" (array).
Each alert has: "severity" ("critical", "warning", or "info"),
"title" (string), "detail" (string), "time" (ISO 8601).
Map: SLA breached→critical, SLA at-risk→warning, overdue→warning, new→info.
```

### Record Profile (→ Detail Card)

```
Query {table} by record ID. Include related {lookup} records.
Return JSON with "heading" (record name), "subtitle" (type · location),
"icon" (emoji), "tags" (status badges as string array),
and "sections" (array of field groups).
Each section has "label" (string) and "fields" (array).
Each field has "label" (string), "value" (any), "format" (optional).
```

### Distribution Breakdown (→ Donut Chart)

```
Query {table} where statecode eq 0. Group by {category_column}.
Count records per group. Sum total.

Return JSON with "title" (string), "segments" (array),
"centerLabel" (total count as string), "centerSubtitle" ("Total").
Each segment has: "label" (string), "value" (number).
```

### Category Comparison (→ Bar Chart)

```
Query {table}. Group by {group_column}. Sum {value_column} per group.
Order by value descending. Top 10.

Return JSON with "title" (string), "bars" (array),
and "format" ("currency", "percent", or "number").
Each bar has: "label" (string), "value" (number).
```

### Trend Line (→ Metric Sparkline)

```
Query {table} grouped by month for the last {N} months.
For each month, calculate {metric} (count, sum, or average).
Also calculate percent change between the last two periods.

Return JSON with "title" (string) and "metrics" (array).
Each metric has: "label" (string), "current" (latest value),
"format" ("currency", "percent", or "number"),
"trend" (number, percent change), "points" (array of values oldest→newest).
```

### Task Board (→ Kanban Board)

```
Query {table} assigned to the current user. Group by {status_column}.
For each record return ticket number (id), subject (title),
customer or parent name (subtitle), and priority (badge).

Return JSON with "title" (string) and "columns" (array).
Each column has: "name" (status label) and "cards" (array).
Each card has: "id", "title", "subtitle", "badge" (optional).
Order columns by workflow progression.
```

### Organization Tree (→ Hierarchy Tree)

```
Query {table} starting from the given root record ID.
Follow {parent_lookup} relationships recursively (max 4 levels).
For each node include name and a detail string.

Return JSON with "title" (string) and "root" (object).
Each node has: "name" (string), "detail" (optional string),
"children" (array of nodes, same shape).
```

### Activity Density (→ Calendar Heat Map)

```
Query {table} created in the last {N} weeks.
Group by createdon date (truncated to YYYY-MM-DD).
Count records per day.

Return JSON with "title" (string) and "days" (array).
Each day has: "date" (YYYY-MM-DD string), "count" (number).
```

### Change Tracking (→ Before/After)

```
Query the audit table for record ID {input}.
Get the most recent audit entry. For each changed attribute,
include the field display name, old value, and new value.

Return JSON with "title" (string), "recordName" (string),
"changedBy" (modifier name), "changedOn" (ISO 8601),
and "changes" (array).
Each change has: "field" (string), "before" (any), "after" (any).
```

### Month View (→ Calendar)

```
Query activities (appointments, tasks, emails) for the current month
where ownerid eq the current user. Include subject, scheduledstart
or scheduledend, and activitytypecode.

Return JSON with "year" (number), "month" (number, 1-12),
and "events" (array).
Each event has: "date" (YYYY-MM-DD), "title" (string),
"type" ("meeting", "deadline", "task", or "event").
Map: appointment→meeting, task→task, email→event.
```

### Tool Chaining Input (→ Any Widget)

When defining a tool with input parameters for dynamic chaining:

```
This tool accepts a {ParameterName} input containing structured data.
Transform the input into the following JSON format:
{ "title": "...", "items": [...] }
Use the input data to populate each item. Return the transformed JSON.
```

## Beyond the Kit

These niche visualizations can be generated on demand with `/generate-mcp-app-ui`:

| Widget | JSON Shape | Use Case |
|--------|-----------|----------|
| Geo Map | `{ points: [{ lat, lng, label, detail }] }` | Account locations, service territory |
| Gantt Timeline | `{ tasks: [{ name, start, end, status }] }` | Project schedule, case SLA tracking |
| Sankey Diagram | `{ nodes: [...], links: [{ source, target, value }] }` | Lead flow, process transitions |
| Scatter Plot | `{ points: [{ x, y, label }] }` | Revenue vs. deal count, risk matrix |
| Stacked Bar | `{ categories: [{ label, segments: [{ label, value }] }] }` | Revenue by region broken down by product |

## Hybrid Pattern: App MCP + External MCP

A model-driven app's declarative agent can combine built-in App MCP tools with external MCP servers:

- **App MCP custom tools** handle Dataverse queries and visualizations (no hosting)
- **External MCP server** (Power Platform custom connector) handles external API calls (Salesforce, HubSpot, etc.)

The declarative agent manifest supports multiple plugin references pointing to different MCP endpoints. This means your existing custom connectors (like the Dataverse Power Orchestration or HubSpot MCP connectors in this repo) can work alongside App MCP tools in the same agent.

## References

- [Enable your app and custom tools in Microsoft 365 Copilot](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/enable-your-app-copilot)
- [Generate MCP app widgets with AI code generation tools](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/generate-mcp-app-widgets)
- [MCP Apps protocol specification](https://apps.extensions.modelcontextprotocol.io/api/documents/Overview.html)
- [MCP Apps now available in Copilot Chat](https://devblogs.microsoft.com/microsoft365dev/mcp-apps-now-available-in-copilot-chat/)
- [Power Platform Skills repository](https://github.com/microsoft/power-platform-skills)
- [Custom tools and rich UI blog post](https://www.microsoft.com/en-us/power-platform/blog/2026/04/22/custom-tools-and-rich-ui-for-app-based-conversations-are-now-in-public-preview/)
