---
name: audit-cms-access
description: Review who can access Salesforce CMS content - workspaces, folder sharing and which channels deliver content publicly. Use when asked who has access to a workspace or folder, what is exposed publicly, or to check CMS permissions before a change.
---

# Audit CMS Access

## When to use this skill

- The user wants to understand CMS exposure and permissions.
- Cue phrases: "who can see the Marketing folder", "what is public", "audit our CMS channels", "check sharing before we publish".

## How to do it

1. List workspaces with `get_cms_workspaces` (filter by `spaceType` or `nameFragment`) and read details with `get_cms_workspace`.
2. For each workspace, list its channels with `get_cms_channels_for_workspace`, then describe each channel with `get_cms_channel` (type, domain, configuration) and `get_cms_channel_delivery_detail` (how consumers see it). Note which are public, such as `PublicUnauthenticated`.
3. For folders of interest, call `get_cms_folder` and `get_cms_folder_sharing_details` to list users, groups and roles and their permission levels.
4. See what is live: `get_published_cms_content_from_channel` (or the site equivalents `get_published_cms_content_from_site` and `get_published_cms_collection_from_site`) for public channels, summarizing counts and types rather than dumping content.
5. Report a table per workspace: channels and audience, folder sharing, notable exposure (public channels with sensitive-looking content, broad sharing), and recommended follow-ups.

## What to watch for

- You only see workspaces and folders the signed-in user can access; say so, because absence is not proof of no access.
- Do not change sharing or channels in this skill; use Set Up CMS Workspace for changes, with confirmation.
- This skill is read-only.
