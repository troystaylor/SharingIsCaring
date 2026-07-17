---
name: create-document
description: |
  Generate Word, Excel, and PowerPoint documents from structured data or instructions.
  Use when the user asks to create a report, spreadsheet, or presentation.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: create-and-update
---

# Create Document

## When to Activate

- "Create a Word document", "make a spreadsheet", "build a presentation"
- "Generate a report about [topic]", "put this data in Excel"
- "Create slides for [meeting]"

## Workflow

### Word Documents
1. Structure the content into title + markdown body
2. `create_word_doc(title, content, filename)`
3. Return the file with download option

### Excel Spreadsheets
1. Organize data into sheets with headers and rows
2. `create_excel(sheets: [{name, headers, rows}], filename)`
3. Return the file

### PowerPoint
1. Structure into title + slides with bullets and notes
2. `create_powerpoint(title, slides: [{title, bullets, notes}], filename)`
3. Return the file

## Combining with Research

For "research [topic] and create a report":
1. Use web-research workflow to gather data
2. Pass synthesized findings to `create_word_doc` or `create_powerpoint`

## Missing Tools

- To save to OneDrive: "Please enable Work IQ OneDrive in Cowork Settings > Tools."
- To create native Word docs with formatting: "Please enable Work IQ Word in Cowork Settings > Tools."
- To email the document: "Please enable Work IQ Mail in Cowork Settings > Tools."
