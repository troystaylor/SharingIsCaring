---
name: spec-verify
description: Verify the facts a generated specification or configuration JSON depends on - endpoints, owners, payload shapes, queues and routing - against Confluence before it is produced, and report what is verified, conflicting or missing. Use when about to generate or review a spec, integration or routing JSON, or when asked to verify endpoints, owners, queues or payloads.
---

# Spec Verify

Read-only gate that runs before JSON is generated. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- What is being specified and its target system or component.
- The facts the JSON will contain. Extract them from the request into a checklist: endpoints or URLs, HTTP methods, request and response payload fields and types, queue or topic names, routing rules, owners or teams, environments, authentication method, limits.

## How to do it

1. **Search for authority.** For each fact group, run `searchConfluence` with precise terms (endpoint path, queue name, component) in the relevant spaces [[CONFIGURE: integration and architecture space keys]]. Prefer approved, current pages over drafts and comments.
2. **Read the pages** with `getConfluenceContent` (`detail` full) and extract the exact values. Record page title, page ID, version and last-modified date for each.
3. **Cross-check.** Compare each checklist item to the extracted value. If a Jira issue is the source of the request, read it for stated constraints (`getJiraIssue`).
4. **Classify each item**:
   - **Verified**: matches an authoritative source (cite it).
   - **Conflict**: sources differ (cite both and say which is newer).
   - **Not found**: no source located (state what was searched).
   - **Restricted**: a source exists but could not be read.
5. **Return a verification table**: item, proposed value, status, source, note. Follow it with a one-line verdict: ready to generate, ready with stated assumptions, or blocked.
6. **Block on critical gaps.** If an endpoint, payload field, queue or routing rule that the JSON depends on is conflicting, not found or restricted, say generation should wait and list what is needed. Never fill a gap with a plausible value.
7. **Owners.** Record an owner only if a source states it; otherwise write `Owner unknown`.
8. When generation proceeds, pass only the verified values and the explicit assumptions, and keep the table with the output.

## What to watch for

- Match exactly: a near-identical queue name or path is a conflict, not a match.
- Check dates; a page older than the related Jira change may be stale.
- Do not write to Confluence or Jira from this skill.
