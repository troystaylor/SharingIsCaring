---
name: execute-code
description: |
  Execute code in isolated ACA Sandbox microVMs. Python (default), bash, JavaScript,
  TypeScript, and .NET. Use when the user wants to run code, process data, transform
  files, test scripts, or perform calculations.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: create-and-update
---

# Execute Code

## When to Activate

- "Run this code", "execute [code]", "process this data"
- "Calculate", "transform", "parse", "analyze"
- User provides code or asks for a script to be written and run

## Workflow

1. **Execute**: `execute_code(code, language)` — auto-creates a session if needed
2. **Upload files** if user provides data: `upload_file(session_id, file_name, content)`
3. **Review output**: Check `exit_code`, `stdout`, `stderr`
4. **Download artifacts** if code generated files: `download_artifact(session_id, file_path)`
5. **Save to memory** if results should persist: `save_memory(key, value)`

## Supported Languages

python (default), bash, javascript, typescript, dotnet

## Pre-installed Python Libraries

All these are available without `pip install`. They are also auto-imported in Python sessions — you can use them directly (e.g., `pd.DataFrame(...)` works without `import pandas as pd`).

**Document formats:** python-docx, openpyxl, xlsxwriter, xlrd, python-pptx, pypdf, reportlab, extract-msg, olefile, mammoth, icalendar, vobject

**Data & analysis:** pandas (as `pd`), numpy (as `np`), matplotlib.pyplot (as `plt`), pillow (as `PIL`), pydantic

**Productivity:** jinja2, python-dateutil, qrcode, humanize, tabulate, cryptography

**Web & parsing:** requests, beautifulsoup4 (as `bs4`), lxml, feedparser, pyyaml (as `yaml`)

**MCP & developer:** mcp, httpx, fastapi, uvicorn, jsonschema

**R:** tidyverse, jsonlite, openxlsx (use `language: "bash"` with `Rscript -e "..."`)

## Output Format

Present results in a fenced code block. For errors, show stderr with suggestions.

## Saving Files

To save generated files to OneDrive, use `save_to_onedrive(session_id, file_path)`.
