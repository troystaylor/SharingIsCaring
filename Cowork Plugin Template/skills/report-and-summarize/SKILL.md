---
name: report-and-summarize
description: |
  Generates reports, summaries, and analysis from {{Service Name}} data.
  Use when the user asks to "summarize", "report on", "give me an overview",
  "analyze", "break down", "how are we doing on", "what's the trend",
  or "weekly/monthly update" for {{Service Name}} data.
metadata:
  author: "{{Your Name}}"
  version: "1.0"
  pattern: aggregation
---

# Report and Summarize {{Service Name}} Data

## What This Skill Does

Transforms raw {{Service Name}} data into executive summaries, trend analysis,
and structured reports. Designed for users who need to understand what's
happening without querying dashboards or pulling exports themselves.

## When to Activate

- User asks for a summary, overview, or status report
- User wants to know trends, patterns, or distributions
- User asks "how are we doing" on a metric or category
- User needs a weekly, monthly, or ad-hoc report
- User asks to compare periods, teams, or categories

## Workflow

1. **Clarify the report scope.** Determine:
   - **What entity:** tickets, deals, orders, customers, etc.
   - **Time range:** last week, this month, quarter-to-date, custom range
   - **Grouping:** by status, assignee, category, priority, region
   - **Audience:** is this for the user, their manager, or a team?

   If the user says "give me a summary," default to the current week
   and ask if they want a different range.

2. **Retrieve the data.** Use the `search_{{entity}}` tool with date
   filters and relevant grouping parameters. Pull enough data to
   provide meaningful aggregation.

3. **Analyze and aggregate.** Calculate:
   - Totals and counts by category
   - Changes from the previous period (week-over-week, month-over-month)
   - Top items (highest priority, most recent, largest value)
   - Items needing attention (overdue, stalled, unassigned)

4. **Present the report.** Structure output for the stated audience.
   Lead with the headline number, then drill into details.

5. **Suggest actions.** Based on what the data shows, recommend
   concrete next steps.

## Output Format

### Executive Summary (Default)

**{{Service Name}} Weekly Summary** — Apr 28 – May 4, 2026

**Headline:** 47 new tickets this week (+12% vs. last week). 8 are
unresolved past SLA.

| Category | This Week | Last Week | Change |
|----------|-----------|-----------|--------|
| New | 47 | 42 | +12% |
| Resolved | 39 | 45 | -13% |
| Open (total) | 83 | 75 | +11% |
| Past SLA | 8 | 3 | +167% |

**Top Issues:**
1. **Login failures** — 12 tickets, all High priority, assigned to Platform team
2. **Billing discrepancies** — 8 tickets, 3 past SLA
3. **API timeout errors** — 6 tickets, started May 1

**Recommended Actions:**
- Escalate the 8 past-SLA tickets — want me to reassign or notify owners?
- Login failures cluster started May 1 — may be related to the API timeouts
- Schedule a review of the billing process (recurring pattern, 3rd week in a row)

### Detailed Breakdown (When Asked)

Provide per-assignee, per-category, or per-priority breakdowns as additional
tables. Keep each table focused on one dimension.

### Trend Report (For Period Comparisons)

Use a text-based trend format since Cowork doesn't render charts:

```
New tickets per week (last 4 weeks):
Apr 7:  ████████████████████████ 38
Apr 14: ██████████████████████████ 41
Apr 21: ██████████████████████████████ 42
Apr 28: ██████████████████████████████████ 47
```

## Handling Edge Cases

- **No data in range:** Report it clearly. Suggest expanding the date
  range or checking if the filters are too narrow.
- **Insufficient data for trends:** If fewer than 2 periods exist,
  skip the comparison. Say "Not enough history for trend analysis yet."
- **Large datasets:** Aggregate server-side when possible. If the API
  doesn't support aggregation, retrieve up to 200 records and note
  that the report covers a sample.
- **User asks for a chart or graph:** Explain that Cowork displays
  text-based reports. Offer to format the data as a table that can be
  pasted into Excel, or suggest creating a document with the data.

## Handling Authentication

If a tool call fails because the user hasn't connected to {{Service Name}} yet:

1. Tell the user: "I need to connect to {{Service Name}} to pull the data
   for this report. You should see a sign-in prompt — please complete it
   and I'll try again."
2. Do NOT retry the data retrieval until the user confirms sign-in.
3. If partial data was already retrieved before the auth failure, present
   what you have and note which sections are incomplete.
