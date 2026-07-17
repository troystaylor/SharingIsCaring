---
name: browse-and-extract
description: |
  Navigate to websites, interact with page elements, and extract structured data
  using Playwright browser automation in isolated sandboxes. Use when the user asks
  to visit a website, get content from a page, fill a form, or interact with a web app.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Browse and Extract

## When to Activate

- User asks "go to [URL]", "what's on [website]", "extract [data] from [URL]"
- User wants to interact with a web page (click, fill, submit)
- User asks to "scrape", "get the content", or "read from [site]"

## Workflow

1. **Create session** if none exists: `create_browser_session()`
2. **Navigate**: `navigate(session_id, url, wait_until: "domcontentloaded")`
3. **Interact** if needed: `click(session_id, selector)` or `fill(session_id, fields)`
4. **Extract**: `extract(session_id, selector, extract: "textContent", all: true)`
5. **Present** results as markdown table or summary
6. **Destroy** session when done: `destroy_session(session_id)`

## Output Format

For structured data, present as a table. For text, present as a clean summary citing the source URL.

## Missing Tools

If a Work IQ tool is needed but unavailable (e.g., saving results to OneDrive):
- Tell the user: "To save this to OneDrive, please enable Work IQ OneDrive in Cowork Settings > Tools, then try again."
