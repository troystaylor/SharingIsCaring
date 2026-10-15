---
name: find-brand-assets
description: Find approved brand media in Salesforce CMS and connected digital asset systems - images, video, documents, audio - and retrieve brand application instructions. Use when asked for a logo, hero image or other brand asset, or how to apply the brand to an app or page.
---

# Find Brand Assets

## When to use this skill

- The user needs media or brand guidance from CMS.
- Cue phrases: "find a hero image of a retail store", "get our logo", "what brand guidelines apply", "search our DAM for product photos".

## How to do it

1. For brand application guidance, call `get_brand_instructions` first, before any other brand-related call, and follow what it returns.
2. Search published media with `search_media_cms_channels`, passing a few concrete nouns as `searchKeyword` and, if known, `channelIds`, `contentTypeFqn` (for example `sfdc_cms__image`) and a `taxonomyExpression` for style or mood.
3. For a natural-language search across Salesforce CMS and connected third-party DAM systems, call `search_electronic_media` with `searchQuery` and `channelType` `PublicUnauthenticated`. It needs the CMS Base Data Kit deployed.
4. Present matches with title, type, source and URL, and recommend the best fit. Use `get_published_cms_content_item_from_channel` for full details of a chosen item.

## What to watch for

- Only published media in public channels is returned; unpublished assets need the authoring tools in Find CMS Content.
- Do not claim an asset is approved for a use the metadata does not state; mention licensing or usage notes if present.
- This skill is read-only.
