---
name: set-up-cms-workspace
description: Set up Salesforce CMS structure - a new enhanced workspace, channels, folders and folder sharing - from a short brief, confirming each change. Use when asked to create a workspace for a campaign or team, add a channel, or organize and share content folders.
---

# Set Up CMS Workspace

## When to use this skill

- The user wants a new CMS workspace, channel or folder structure.
- Cue phrases: "create a Partner Enablement workspace and channel", "add a folder", "share this folder with the team".

## How to do it

1. Check what exists with `get_cms_workspaces`, `get_cms_channels` and `get_cms_folder` to avoid duplicates.
2. Propose a plan: workspace name, API name, `spaceType`, default and supported languages, channels (`type` such as `PublicUnauthenticated` or `Community`), folders, and who gets access. Show it and ask for corrections.
3. After explicit confirmation, run steps in order: `create_cms_workspace`, then `create_cms_channel` as needed (`update_cms_channel` to adjust an existing channel's name, domain or cache settings), `update_cms_workspace_channels` to associate channels, `create_cms_folder`, and finally `update_cms_folder_sharing_settings` for sharing.
4. For a UI Bundle that needs content, `get_or_create_cms_workspace_and_web_app_channel` gets or creates the workspace and Web App channel together.
5. Summarize what was created with Ids and what remains.

## What to watch for

- Channel type decides who can see published content; confirm the audience before creating public channels.
- Do not change sharing settings without naming the users, groups or roles affected.
- Users need appropriate CMS permissions; report permission errors verbatim.
- Content Write is currently supported only with Agentforce Vibes per Salesforce.
