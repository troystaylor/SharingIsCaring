---
name: copy-across-sites
description: |
  Copy files or folders between SharePoint sites or document libraries.
  Use when the user asks "copy this file to another site", "duplicate
  this document to the other library", "copy from Site A to Site B",
  "replicate this folder to the team site", "clone this file to another
  SharePoint library", or "move this to another site" (cross-site move
  is copy + delete).
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: Copy
---

# Copy Across Sites

1. Identify the source item. If not already known, use `list_sites`, `list_drives`, and `list_folder` to navigate to the file or folder. Capture the source `drive_id` and `item_id`.
2. Identify the destination. Use `list_sites` and `list_drives` to find the target site and library. Use `list_folder` to choose a destination folder. Capture the `dest_drive_id` and `dest_folder_id`.
3. Confirm the copy details with the user: source item, destination site/library/folder, and optional new name.
4. Call `copy_item` with the source and destination IDs. Optionally provide `new_name` and `conflict_behavior` (rename, replace, or fail).
5. The copy runs asynchronously. Inform the user and offer to check progress.
6. Use `check_copy_status` with the returned `monitor_url` to track progress. Report `percentageComplete` and `status`.
7. Once complete, offer to generate a sharing link with `create_link` or set metadata with `set_metadata` on the copied item.
8. If the user wanted a cross-site **move** (not copy), offer to delete the original item after confirming the copy succeeded.
