---
name: worklog-signals
description: Surface signals from Jira worklogs and recent issue activity to help the user reconstruct their own work for time entry. Optional; only the signed-in user's own activity unless they are authorized to see more. Use when asked what they worked on, to prepare timesheet suggestions, or to list logged time and recent activity.
---

# Worklog Signals

Read-only and optional. It produces signals and suggestions, not timesheet entries. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The date range and time zone (default: the current week), and whose activity. Default is the signed-in user (`atlassianUserInfo`). Do not look up other people's worklogs unless the user asks and Atlassian returns them under their own permissions; say that results depend on those permissions.
- The Jira project(s) in scope, if limited.

## How to do it

1. **Find candidate issues** with `searchJiraIssuesUsingJql`: issues with the user's worklogs in the range (`worklogAuthor = currentUser() AND worklogDate >= <start> AND worklogDate <= <end>`), plus issues the user updated, transitioned or was assigned in the range (`assignee = currentUser() AND updated >= <start>`; use `listJiraIssueChangelogs` on a few to see their own status changes).
2. **Read worklogs** for each candidate with `listJiraIssueWorklogs` (via `discover` and `executeRead`). Keep only entries in the range and by the user. Record issue key, date, time spent and comment.
3. **Summarize** per day and per issue: logged time with sources, plus **unlogged activity signals** (issues with changes by the user but no worklog that day). Label these clearly as signals, not time spent.
4. **Suggest** entries only as proposals: issue key, day, "no time recorded in Jira", and the activity that suggests work. Do not estimate durations; ask the user for them.
5. **State limits**: Jira shows only logged time and recorded changes, not meetings, email, code review or work in other systems; other sources (calendar, Teams, Bitbucket) are not covered here.

## What to watch for

- Totals come from worklog values returned by the tool; show the arithmetic and the issues behind each number.
- Do not infer hours from timestamps.
- Do not post worklogs or edit issues from this skill. Writing a worklog needs the Jira write permission and `write-safety`, and is outside this skill's scope.
