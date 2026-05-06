---
name: create-and-update
description: |
  Creates new records or updates existing ones in {{Service Name}}. Use when
  the user asks to "create", "add", "submit", "update", "change", "modify",
  "assign", "close", or "reopen" something in {{Service Name}}.
metadata:
  author: "{{Your Name}}"
  version: "1.0"
  pattern: mutation
---

# Create and Update Records in {{Service Name}}

## What This Skill Does

Handles all write operations — creating new records and modifying existing ones.
Translates business-language instructions into structured API calls, confirms
details before committing, and reports the outcome clearly.

## When to Activate

- User asks to create, add, or submit a new record
- User wants to update, change, or modify an existing record
- User asks to assign, reassign, close, reopen, or change status
- User provides information and expects it to be saved somewhere

## Workflow

### For Creating New Records

1. **Identify the entity type.** Determine what the user wants to create
   (e.g., ticket, customer, order, task).

2. **Gather required fields.** Check the API field reference for mandatory
   fields. Ask the user for any required information not provided:
   - Required fields: ask explicitly
   - Optional fields with sensible defaults: fill automatically, mention
     what you defaulted
   - Optional fields without defaults: skip unless the user mentioned them

3. **Confirm before creating.** Summarize what will be created and ask
   for confirmation. Note: Cowork has its own approval system for
   sensitive actions (sending emails, posting to Teams, creating events).
   For actions that Cowork already gates with an approval prompt, you
   don't need a separate confirmation step — let the platform handle it.
   Only add your own confirmation for API-specific mutations where
   Cowork wouldn't know the action is significant.

   > "I'll create a new support ticket:
   > - **Title:** Login page returns 500 error
   > - **Priority:** High
   > - **Assigned to:** Platform team
   > - **Category:** Bug
   >
   > Should I go ahead?"

4. **Create the record.** Use the `create_{{entity}}` tool. Report the
   result with the new record ID.

5. **Offer follow-up actions.** "Want me to assign this to someone
   specific?" or "Should I add any attachments or notes?"

### For Updating Existing Records

1. **Identify the record.** If the user provides an ID, use it directly.
   If they provide a name or description, search first to confirm the
   right record.

2. **Determine the changes.** Map the user's request to specific field
   updates. For status changes, validate the transition is allowed
   (e.g., you can't close a ticket that's already closed).

3. **Confirm before updating.** Show current values and proposed changes:

   > "Updating ticket TKT-4421:
   > - **Status:** Open → In Progress
   > - **Assigned to:** (unassigned) → Jamie Chen
   >
   > Proceed?"

4. **Apply the update.** Use the `update_{{entity}}` tool. Report success
   and the updated state.

## Output Format

After a successful create:

**Created:** {{Entity}} {{ID}}
- **Title:** Login page returns 500 error
- **Status:** Open
- **Link:** {{URL to record}}

After a successful update:

**Updated:** {{Entity}} {{ID}}
- **Changed:** Status (Open → In Progress), Assigned to (→ Jamie Chen)

## Handling Edge Cases

- **Missing required fields:** List exactly which fields are needed.
  Don't guess values for critical fields like customer name or priority.
- **Record not found:** If the user references something that doesn't
  exist, confirm the identifier and search before giving up.
- **Permission errors:** Report clearly: "I don't have permission to
  update this record. You may need to ask [owner] or contact your admin."
- **Validation failures:** Translate API validation errors into plain
  language. "The priority must be Low, Medium, High, or Critical" is
  better than "400: invalid enum value for field 'priority'."
- **Bulk operations:** If the user asks to update multiple records,
  list them all and confirm once rather than confirming each individually.

## Handling Authentication

If a tool call fails because the user hasn't connected to {{Service Name}} yet:

1. Tell the user: "I need to connect to {{Service Name}} before I can create
   or update records. You should see a sign-in prompt — please complete it
   and I'll try again."
2. Do NOT attempt the create/update again until the user confirms sign-in.
   Never silently retry mutations.
3. If the user was mid-confirmation when auth failed, re-present the
   confirmation summary after reconnecting so nothing is lost.
