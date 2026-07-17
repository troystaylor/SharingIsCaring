---
name: screenshot-and-pdf
description: |
  Capture screenshots and generate PDF exports of web pages for visual documentation,
  reporting, and archival. Use when the user wants to see what a page looks like or
  save it as a document.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: create-and-update
---

# Screenshot and PDF

## When to Activate

- "Take a screenshot of [URL]", "show me what [site] looks like"
- "Generate a PDF of [URL]", "save [page] as PDF"
- "Capture the full page"

## Workflow

1. **Create session**: `create_browser_session()`
2. **Navigate**: `navigate(session_id, url, wait_until: "networkidle")` — use networkidle for full render
3. **Capture**:
   - Screenshot: `screenshot(session_id, full_page: true)` or `screenshot(session_id, selector: "...")`
   - PDF: `generate_pdf(session_id, format: "A4")`
4. **Return** the base64 content and file path
5. **Destroy session**: `destroy_session(session_id)`

## Saving Files

To save screenshots/PDFs to the user's OneDrive, use `download_artifact(session_id, file_path)` to get the file content, then write it to the `output/` folder using CopyArtifact. The output/ folder syncs to the user's OneDrive Cowork folder automatically. Do NOT use save_to_onedrive.
