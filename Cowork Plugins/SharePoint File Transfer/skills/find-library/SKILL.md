---
name: find-library
description: |
  Browse SharePoint sites and document libraries to find where files are
  stored. Use when the user asks "find a SharePoint library", "which site
  has the documents", "show me my SharePoint sites", "list document
  libraries", "browse SharePoint", or "where is the Documents library".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Files
cowork.icon: Search
---

# Find Library

1. Use `list_sites` with an optional search query to find SharePoint sites matching the user's request.
2. Present the matching sites with their names and URLs. Let the user pick one.
3. Use `list_drives` to list document libraries in the selected site.
4. If the user wants to explore further, use `list_folder` to browse folders within a library.
5. Use `get_item` to retrieve metadata for a specific file or folder if requested.
6. Summarize the site name, library name, and path for the user's next action (upload, share link, etc.).
