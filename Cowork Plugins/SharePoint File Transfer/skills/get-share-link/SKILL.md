---
name: get-share-link
description: |
  Generate a shareable link for a file or folder in SharePoint. Use when
  the user asks "share this file", "get a link to this document", "create
  a sharing link", "send someone access to this file", or "make this
  file accessible".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: Link
---

# Get Share Link

1. If the user hasn't identified the file, use `list_sites`, `list_drives`, and `list_folder` to navigate to it.
2. Use `get_item` to confirm the file or folder and retrieve its item ID.
3. Ask the user for link preferences: type (view, edit, or embed), scope (organization or anonymous), optional expiration date, and optional password.
4. Call `create_link` with the drive ID, item ID, and chosen settings.
5. Return the sharing link URL and confirm the access level granted.
