---
name: find-dashboards
description: Find and describe Salesforce analytics dashboards, visualizations, lenses and workspaces across Tableau Next and CRM Analytics. Use when asked what dashboards exist, to find one by topic, or to see what a dashboard tracks.
---

# Find Dashboards

## When to use this skill

- The user wants to discover analytics assets or understand what a dashboard shows.
- Cue phrases: "find dashboards about pipeline", "what analytics do we have for support", "what does this dashboard track".

## How to do it

1. Tableau Next: call `search_assets` with the topic (filter `type` to `dashboard` or `visualization` when helpful), or `list_dashboards`, `list_visualizations`, `list_workspaces` and `list_workspace_assets` to browse.
2. For a chosen item, call `get_dashboard` or `get_visualization` and summarize contained visualizations, data source and purpose.
3. CRM Analytics: call `list_dashboards` with `q` or `folderId` (get apps via `list_folders`), then `get_dashboard`. Use `list_lenses` and `get_lens` for saved explorations.
4. Present results grouped by product with name, owner or app, and a one-line description. Recommend the best match and offer to pull the underlying numbers.

## What to watch for

- Tableau Next and CRM Analytics are separate; say which product each result comes from.
- Missing items usually mean the user lacks permission.
- These tools return metadata only; to get data, use Ask Analytics Question or Query CRM Analytics Dataset.
