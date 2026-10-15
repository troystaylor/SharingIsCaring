---
name: publish-or-unpublish-content
description: Publish approved Salesforce CMS content to its channels or unpublish it, with explicit confirmation and a summary of exactly what becomes public or goes offline. Use when asked to publish, go live with, unpublish or take down CMS content.
---

# Publish Or Unpublish Content

## When to use this skill

- The user wants content made available to, or removed from, delivery channels.
- Cue phrases: "publish this to the public site", "unpublish the old pricing page".

## How to do it

1. Identify the content with `get_cms_content_item` or `search_content_cms_workspaces`. Read the current version and its variants.
2. Find where it would be delivered: `get_cms_channels_for_workspace` for the workspace's channels, and `get_cms_channel` or `get_cms_channel_delivery_detail` if the audience matters (for example a public unauthenticated channel).
3. State plainly the action, items, languages and channels, and who can see the result. For publishing, show the content body one last time.
4. After explicit confirmation, call `publish_cms_content` with `contentIds` (all variants) or `variantIds`, setting `contextContentSpaceId` when content is shared across workspaces. For takedowns call `unpublish_cms_content` with `contentIds`.
5. Report the per-channel success or error result, and verify with `get_published_cms_content_item_from_channel` when appropriate.

## What to watch for

- Publishing to public channels exposes content to anyone; never publish without a clear request and confirmation.
- Unpublishing reverts content to draft status and removes it from external viewers.
- Do not publish content the user has not reviewed.
- Content Write is currently supported only with Agentforce Vibes per Salesforce.
