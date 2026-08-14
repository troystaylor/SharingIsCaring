# GitHub Search Syntax Reference

The search tools accept GitHub's standard search qualifier syntax in the
`query` parameter. Combining qualifiers is almost always better than a longer
free-text query.

## Shared qualifiers

| Qualifier | Meaning | Example |
|---|---|---|
| `org:NAME` | Limit to an organization | `org:contoso` |
| `user:NAME` | Limit to a user's repos | `user:troystaylor` |
| `repo:OWNER/NAME` | Limit to one repository | `repo:contoso/billing-api` |
| `created:>=YYYY-MM-DD` | Created on or after | `created:>=2026-01-01` |
| `updated:>=YYYY-MM-DD` | Updated on or after | `updated:>=2026-08-01` |
| `NOT term` / `-term` | Exclude | `refund -test` |

## `search_repositories`

| Qualifier | Example |
|---|---|
| `language:` | `language:typescript` |
| `topic:` | `topic:mcp` |
| `stars:>N` | `stars:>100` |
| `archived:` | `archived:false` |
| `is:private` / `is:public` | `org:contoso is:private` |
| `in:name` / `in:description` / `in:readme` | `billing in:name` |

> `search_repositories(query: "org:contoso language:typescript archived:false billing")`

## `search_code`

Code search runs against default branches only. Forks and very large files are
generally not indexed.

| Qualifier | Example |
|---|---|
| `path:` | `path:src/config` |
| `language:` | `language:go` |
| `extension:` | `extension:yml` |
| `filename:` | `filename:Dockerfile` |
| `symbol:` | `symbol:parseInvoice` |
| `content:` | `content:MAX_RETRY_COUNT` |

> `search_code(query: "org:contoso language:typescript MAX_RETRY_COUNT")`

Quote phrases that contain spaces or punctuation: `"api-key: "`.

## `search_commits`

| Qualifier | Example |
|---|---|
| `author:` | `author:jchen` |
| `committer-date:` | `committer-date:>=2026-08-01` |
| `merge:true` / `merge:false` | `merge:false` |
| `hash:` | `hash:a1b2c3d` |

> `search_commits(query: "repo:contoso/billing-api author:jchen committer-date:>=2026-08-01")`

## `search_issues`

| Qualifier | Example |
|---|---|
| `is:issue is:open` | required to exclude PRs |
| `label:` | `label:bug label:"needs triage"` |
| `assignee:` / `no:assignee` | `no:assignee` |
| `author:` | `author:mpatel` |
| `milestone:` | `milestone:"Q3 2026"` |
| `state:` | `state:closed` |
| `comments:>N` | `comments:>5` |
| `type:` | `type:Bug` (issue types, if the org uses them) |

> `search_issues(query: "org:contoso is:issue is:open label:bug no:assignee sort:created-asc")`

## `search_pull_requests`

| Qualifier | Example |
|---|---|
| `is:pr is:open` / `is:merged` | `is:pr is:merged` |
| `review:required` / `review:approved` / `review:changes_requested` | `review:required` |
| `review-requested:USER` | `review-requested:@me` |
| `reviewed-by:USER` | `reviewed-by:jchen` |
| `draft:true` / `draft:false` | `draft:false` |
| `merged:>=YYYY-MM-DD` | `merged:>=2026-07-01` |
| `base:` / `head:` | `base:main` |

> `search_pull_requests(query: "org:contoso is:pr is:open draft:false review-requested:@me")`

## Sorting

Append `sort:` and `order:` to any search:

- `sort:updated`, `sort:created`, `sort:comments`, `sort:reactions`
- `sort:created-asc` (oldest first — useful for finding stale work)
- `order:desc` (default) or `order:asc`

## Dates

Absolute dates only — GitHub search does not understand "last week". Compute
the ISO date first, then build the qualifier. Ranges use `..`:

`merged:2026-07-01..2026-07-31`

## Practical recipes

| Goal | Query |
|---|---|
| Untriaged bugs across the org | `org:contoso is:issue is:open no:label sort:created-asc` |
| Stale open PRs | `org:contoso is:pr is:open draft:false updated:<2026-07-15` |
| What one person shipped last month | `org:contoso is:pr is:merged author:jchen merged:2026-07-01..2026-07-31` |
| PRs waiting on me | `is:pr is:open review-requested:@me` |
| Everything I opened that is still open | `is:open author:@me` |
| Where a secret pattern appears | `org:contoso extension:env content:"API_KEY="` |
