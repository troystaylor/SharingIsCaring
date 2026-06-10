---
name: create-destination-folder
description: |
  Create a new folder in a SharePoint document library and optionally move
  items into it. Use when the user asks "create a folder in SharePoint",
  "make a new folder", "organize files into a folder", "add a subfolder
  to the library", or "set up a folder structure".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: FolderAdd
---

# Create Destination Folder

1. If the user hasn't specified a site, use `list_sites` to find and confirm the target site.
2. Use `list_drives` to identify the document library.
3. Optionally use `list_folder` to let the user choose a parent folder.
4. Confirm the folder name and location with the user.
5. Call `create_folder` with the drive ID, parent path, and folder name.
6. Report the new folder's name and web URL.
7. Offer to move existing items into the new folder with `move_item` or upload files with `upload_from_url`.
