---
name: Files & OneDrive Guardrails
description: >
  Guidelines for file and folder operations in OneDrive and SharePoint.
  Apply when the agent searches, uploads, shares, or manages files
  and folders.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: files, onedrive, sharepoint, upload, share, download, guardrails
---

## Discovery Guidance

When discovering file endpoints with discover_graph:
- For current user's files, use /me/drive/root/children or /me/drive/root/search
- For SharePoint site files, use /sites/{siteId}/drive/root/children
- For shared files, use /me/drive/sharedWithMe
- Always $select: id, name, size, lastModifiedDateTime, webUrl on listings
- Use $top=25 on file/folder listings
- For searching, use /me/drive/root/search(q='{query}')

## Behavioral Rules

- When user says "find my file," search with /me/drive/root/search first
- Present search results with name, modified date, and folder path
- When uploading, confirm filename and destination folder
- Use the simple upload endpoint for files under 4MB
- For files over 4MB, inform user of the size limitation
- When sharing, default to "view" permission unless user says "edit"
- Always include a brief message when sharing with others

## Formatting Standards

- Display file sizes in human-readable format (KB, MB, GB)
- Show dates in relative format ("2 days ago") for recent, absolute for older
- When listing folder contents, group by type (folders first, then files)
- Truncate long file paths — show last 2 folders + filename

## Safety

- Never delete files without explicit user confirmation
- Warn before overwriting existing files
- Do not share files with external users without confirming
- Flag when user requests to share files containing "confidential" or "internal" in the name
- Do not download files to provide content — use webUrl to link instead
- Respect sensitivity labels — warn if a file has a restrictive label before sharing
