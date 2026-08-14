# GitHub Issue Tool Reference

Parameter notes for the issue tools in the default toolset of the remote
GitHub MCP server. All tools take `owner` and `repo`.

## `issue_read`

Read-only. One tool, five methods.

| Parameter | Required | Notes |
|---|---|---|
| `method` | Yes | `get`, `get_comments`, `get_sub_issues`, `get_parent`, `get_labels` |
| `owner`, `repo` | Yes | |
| `issue_number` | Yes | The visible issue number, not the internal ID |
| `page`, `perPage` | No | Pagination for `get_comments` and `get_sub_issues` |

Start with `method: "get"`. Follow up with `get_comments` when the discussion
matters — the body alone often omits the actual decision.

## `issue_write`

Creates and updates. Required: `method`, `owner`, `repo`.

| Parameter | Type | Notes |
|---|---|---|
| `method` | string | `create` or `update` |
| `issue_number` | number | Required for `update` |
| `title` | string | Required in practice for `create` |
| `body` | string | Markdown |
| `assignees` | string[] | GitHub usernames. Replaces the current set on update. |
| `labels` | string[] | Label names. Replaces the current set on update. |
| `milestone` | number | Milestone **number**, not title |
| `type` | string \| null | Issue type name. Only if issue types are enabled — validate with `list_issue_types` first. Pass `null` on update to clear. |
| `state` | string | `open` or `closed` |
| `state_reason` | string | `completed`, `not_planned`, or `duplicate`. Ignored unless `state` changes. |
| `duplicate_of` | number | Required when `state_reason` is `duplicate` |
| `issue_fields` | object[] | Custom fields — see below |

**`labels` and `assignees` replace, they do not merge.** To add one label to an
issue that already has two, read the current labels with `issue_read`
(`method: "get_labels"`) and send all three.

### Closing correctly

| Situation | Parameters |
|---|---|
| Work is done | `state: "closed"`, `state_reason: "completed"` |
| Won't do | `state: "closed"`, `state_reason: "not_planned"` |
| Duplicate | `state: "closed"`, `state_reason: "duplicate"`, `duplicate_of: 412` |

### `issue_fields`

Each entry needs `field_name` plus exactly one of `value`, `field_option_name`,
or `delete: true`.

```json
{ "field_name": "Priority", "field_option_name": "P1" }
{ "field_name": "Target date", "value": "2026-09-30" }
{ "field_name": "Priority", "delete": true }
```

Use `field_option_name` for single-select fields — it validates the option
before the API call. Call `list_issue_fields` first to get valid field names.

## `add_issue_comment`

| Parameter | Required | Notes |
|---|---|---|
| `owner`, `repo` | Yes | |
| `issue_number` | Yes | Works for pull requests too — a PR is an issue |
| `body` | Yes | Markdown |

## `sub_issue_write`

Required: `method`, `owner`, `repo`, `issue_number`, `sub_issue_id`.

| Parameter | Notes |
|---|---|
| `method` | `add`, `remove`, or `reprioritize` |
| `issue_number` | The **parent** issue number |
| `sub_issue_id` | The sub-issue's internal **ID**, not its issue number |
| `replace_parent` | With `add`, moves a sub-issue from its current parent |
| `after_id` / `before_id` | With `reprioritize`, one or the other |

`sub_issue_id` is the ID returned by the API, which differs from the `#123`
number users see. Get it from `issue_read` on the sub-issue before calling.

## Validation helpers

| Tool | Use for |
|---|---|
| `get_label` | Confirm a label exists before applying it |
| `list_issue_types` | Valid values for the `type` parameter |
| `list_issue_fields` | Valid `field_name` values for `issue_fields` |

## `list_issues` vs. `search_issues`

| | `list_issues` | `search_issues` |
|---|---|---|
| Scope | One repository | Any repos the user can see |
| Filtering | Structured parameters (`state`, `labels`, `since`) | GitHub search qualifiers |
| Best for | "All open bugs in this repo" | "Untriaged issues across the org" |

`search_issues` results can include pull requests unless the query includes
`is:issue`.

## Toolset boundaries

With the default connector URL (`https://api.githubcopilot.com/mcp/`) these
issue operations are **not** available:

| Operation | Requires toolset |
|---|---|
| Create or edit label definitions (`label_write`, `list_label`) | `labels` |
| Add issues to a project board (`projects_write`) | `projects` |
| Issue notification subscriptions | `notifications` |
