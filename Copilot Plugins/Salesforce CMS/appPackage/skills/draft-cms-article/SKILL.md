---
name: draft-cms-article
description: Draft a Salesforce CMS content item from source material, creating it as unpublished content in the right workspace or folder, and optionally adding translated variants. Use when asked to write up an article or create content in Salesforce CMS.
---

# Draft CMS Article

## When to use this skill

- The user wants new CMS content created from notes, a brief or pasted copy.
- Cue phrases: "create a news article in the Marketing workspace", "add this to CMS", "create a French variant".

## How to do it

1. Identify the target with `get_cms_workspaces` and, if needed, `get_cms_folder`. Confirm the workspace or folder.
2. Call `get_content_types_for_workspace` for that workspace and choose the content type FQN (for example `sfdc_cms__news`). Ask if it is unclear.
3. Draft the title and `contentBody` so it matches the content type schema (a JSON object string). Show the draft to the user.
4. After explicit confirmation, call `create_cms_content` with `contentType`, `title`, `contentBody` and `contentSpaceOrFolderId`. Report the content key and variant Id.
5. For translations, call `create_cms_content_variant` with `managedContentKeyOrId`, `variantTitle`, `languageCode` and the provided translated body. Use `update_cms_content_variant` to revise a variant, and `clone_cms_content` to duplicate an item.

## What to watch for

- Creating content does not publish it; publishing is a separate, confirmed step (Publish Or Unpublish Content skill).
- Base the draft only on the supplied material; do not invent facts, prices or dates.
- `update_cms_content_variant` replaces the whole body; fetch the current body first and show the diff.
- Content Write is currently supported only with Agentforce Vibes per Salesforce; verify support in your client.
