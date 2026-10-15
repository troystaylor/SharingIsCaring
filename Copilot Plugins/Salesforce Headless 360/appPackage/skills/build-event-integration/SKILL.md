---
name: build-event-integration
description: Plan and set up an event-driven Salesforce integration - platform events, Change Data Capture and event relays to an external system - through Headless 360 with confirmation at each step. Use when asked to send events when records change or to stream Salesforce changes to AWS or another service.
---

# Build Event Integration

## When to use this skill

- The user wants Salesforce changes or events delivered to another system.
- Cue phrases: "send events to AWS when Account records change", "set up change data capture for Opportunity", "create an event relay".

## How to do it

1. Clarify the source (which object or custom platform event), the destination (for example an AWS EventBridge account or another relay target) and the events needed (create, update, delete).
2. Call `discover` with the goal and prefer `resultType` `sor` for a multi-step recipe, falling back to `endpoint` operations. Call `describe` on the best match to get the steps, dependencies and required inputs.
3. Check current state with `dispatch_readonly`: existing change data capture selections, platform events, event relays and named credentials for the destination.
4. Present the plan in order (for example enable CDC on the object, create or confirm the credential, create the relay, start it), marking which steps change the org.
5. After explicit confirmation, run steps one at a time with `dispatch`, verify each with `dispatch_readonly`, and stop on the first error.
6. Summarize the configuration, how to test it, and how to disable it.

## What to watch for

- Streaming records out of Salesforce exposes data; confirm the object and fields with the user and consider which data leaves the org.
- Event delivery can generate volume and cost on both sides; mention limits.
- Prefer a sandbox. Headless 360 is Beta, so rely on what `discover` returns, not assumptions.
