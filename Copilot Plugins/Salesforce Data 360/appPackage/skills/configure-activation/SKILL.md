---
name: configure-activation
description: Set up or review Data 360 activations that publish a segment to a target such as a marketing or ad platform, with the audience, attributes and refresh confirmed first. Use when asked to activate a segment, list activations or targets, or change an activation.
---

# Configure Activation

## When to use this skill

- The user wants to send a Data 360 segment to a downstream target or review existing activations.
- Cue phrases: "activate this segment to our marketing platform", "list activation targets", "what is this activation sending".

## How to do it

1. Call `search` with "activation" and "activation target" to see the Activation family (including `d360_activation_create`, `d360_activation_get`, `d360_activation_list` and `d360_activation_update`) and the activation target tools. Call `payload_examples` for each tool you will use.
2. Review what exists with the list and get tools through `execute` (`toolName` plus `paramsJson`): activation targets and current activations, their segments, status and refresh settings.
3. For a new activation, confirm the segment (`d360_segment_get`, with a size from `d360_segment_count`), the target, the contact points and attributes to send, and the refresh schedule.
4. Present the plan, including how many people will be sent and which personal data attributes leave Data 360. After explicit confirmation, call the create or update tool via `execute` and report the result.

## What to watch for

- Activations send customer data outside Data 360; state exactly what is sent and to where, and respect consent and privacy requirements.
- Never publish or activate a segment the user has not reviewed.
- Deleting or changing a live activation can stop downstream campaigns; confirm separately.
- Calls count toward Connect API limits and Flex Credit usage.
