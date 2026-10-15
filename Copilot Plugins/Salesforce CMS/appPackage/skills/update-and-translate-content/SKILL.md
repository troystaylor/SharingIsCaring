---
name: update-and-translate-content
description: Revise existing Salesforce CMS content, add translated variants, or clone an item - showing the current body and the proposed change first and confirming before saving. Use when asked to edit, rewrite, translate or duplicate CMS content.
---

# Update And Translate Content

## When to use this skill

- The user wants to change, translate or copy an existing content item.
- Cue phrases: "update the pricing article", "add a Spanish version", "rewrite this in a friendlier tone", "clone this page".

## How to do it

1. Find the item with `search_content_cms_workspaces` or `get_cms_content_item`. Read its current body and variants (`get_cms_content_variant` for one language) and confirm the right item and language.
2. Call `get_content_types_for_workspace` if you need the content type schema so the new body matches it.
3. Choose the operation:
   - Revise a variant: `update_cms_content_variant`. It replaces the whole body, so send a complete body. Alternatively pass `prompt` (not both) to have Salesforce refine the body from an instruction.
   - Translate: `create_cms_content_variant` with `managedContentKeyOrId`, `variantTitle`, `languageCode` and the translated body. Use text the user provided or approved.
   - Copy: `clone_cms_content` with an optional `title`, target folder and `includeVariants`.
4. Show the current text beside the proposed text (or the prompt that will be used). After explicit confirmation, run the call and report the resulting variant Id and version.

## What to watch for

- Changes do not publish automatically; publishing is a separate confirmed step (Publish Or Unpublish Content skill).
- Preserve structure, links and required fields; partial bodies are not supported.
- Do not machine-translate sensitive or legal text without telling the user it needs review.
- Content Write is currently supported only with Agentforce Vibes per Salesforce.
