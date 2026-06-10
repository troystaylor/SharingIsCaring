---
name: upload-to-sharepoint
description: |
  Upload a file from a public HTTPS URL into a SharePoint document library.
  Use when the user asks "upload this file to SharePoint", "save this URL
  to SharePoint", "put this file in the Documents library", "download and
  store this in SharePoint", or "transfer this file to our team site".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: CloudArrowUp
---

# Upload to SharePoint

1. Ask the user for the source URL (must be public HTTPS, reachable without auth).
2. If the user hasn't specified a destination, use `list_sites` to find the target SharePoint site. Confirm the site with the user.
3. Use `list_drives` to show available document libraries and let the user pick one.
4. Optionally use `list_folder` to let the user choose a subfolder, or default to the library root.
5. Confirm the upload details: source URL, destination site, library, and path.
6. Call `upload_from_url` with the drive ID, destination path, and source URL.
7. If the file is large (250 MB+), inform the user it will upload in the background. Offer to check status later with `get_upload_status`.
8. If the upload completes inline, report the file name, size, and web URL.
9. Offer to generate a sharing link with `create_link` or set metadata with `set_metadata`.
