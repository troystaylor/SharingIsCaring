---
name: technical-design-draft
description: Help draft a technical design by retrieving Confluence engineering guidelines, templates and prior designs, then producing a sourced draft that follows them. Use when asked to write, outline or review a technical design, find design guidelines or prior art, or prepare a design page.
---

# Technical Design Draft

Mostly read-only. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The requirement or change being designed, the system or component, any related Jira issue key, and the intended audience (design review, implementation, customer).
- Which design template to follow: [[CONFIGURE: template page title or ID]]. If none is configured, list candidate templates with `listConfluenceTemplates` and ask.

## How to do it

1. **Find the rules.** Run `searchConfluence` in the guidelines space [[CONFIGURE: space key]] for engineering guidelines, naming, security, performance, logging and deployment standards. Read the authoritative pages with `getConfluenceContent` (`detail` full) and note title, page ID, version and date.
2. **Find prior art.** Search for earlier designs or implementations of the same area and for similar requirements. Read the closest two or three. Note what they reused and what differed.
3. **Read the context.** If a Jira issue is given, read it (`getJiraIssue`) and its comments for requirements and constraints.
4. **Draft the design** in Markdown following the template: purpose and scope, requirements and assumptions, current behavior, proposed design, interfaces and data, error handling, security, performance and limits, testing, rollout and rollback, open questions. Reference each guideline you applied by page title and ID. Where a guideline is silent, say so and mark the choice as an assumption to confirm.
5. **Check for conflicts.** If two guidelines disagree or a guideline is out of date, show both with citations and ask the user to decide.
6. **List open questions** that need an owner. Do not name an owner unless one is recorded.
7. **Output** the draft in chat. Only if the user asks to save it, follow `write-safety` and create it as a draft page with `createConfluenceContent` after loading authoring guidance (`getContentFormatGuide` with `toolName` set to `createConfluencePage`) and the space instructions (`getConfluenceSpace`).

## What to watch for

- Do not invent endpoints, field names or limits; take them from cited pages or mark them as unknown.
- Keep the draft proportional to the change; skip sections that do not apply and say why.
- A design draft is not an approval. Say it needs the normal review.
