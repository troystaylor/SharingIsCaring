---
name: change-salesforce-records
description: |
  Creates, updates, or deletes Salesforce records. Use when the user says
  "create an account", "add a contact", "update this opportunity", "change the
  stage to Closed Won", "set the owner", "delete this record", or asks to log,
  edit, or remove Salesforce data. Requires the server's write tools, which are
  off unless an administrator turned them on.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: guarded-action
---

# Change Salesforce Records

## What This Skill Does

Makes changes in Salesforce safely: it checks what the object allows, confirms
the change with the user, makes it once, and reports exactly what happened.

## Before Anything Else

The tools `create_record`, `update_record`, and `delete_record` exist only when an
administrator enabled them on the server. If they are not available, say so
plainly: this connection is read-only. Do not look for a workaround.

## Workflow

1. **Understand the object.** Call `describe_object` and check that the object is
   `createable` or `updateable` as needed, and which fields are `required`.

2. **Check the values.**
   - Objects with record types: call `get_record_types`, and set the right
     record type when the user cares which one.
   - Picklist fields: call `get_picklist_values` (with `recordTypeId` when
     relevant). A value outside the list is rejected.
   - Call `list_validation_rules` when the object has rules that could reject
     the change, so you can warn the user up front.

3. **For an update or delete, read the record first** with `get_record`. Show the
   user the current values so they can see what will change.

4. **Confirm before writing.** State the object, the record (name and
   `recordUrl`), and every field and value that will be written. Wait for a
   clear yes. Treat a delete as needing explicit confirmation every time.

5. **Make the change once.** Call `create_record`, `update_record`, or
   `delete_record`.

6. **Report the outcome precisely.** Give the record link for a create or update.
   For a delete, say it was deleted and that an administrator may be able to
   restore it from the Recycle Bin for a limited time.

## What to Watch For

- **Never call `create_record` twice for the same record.** Each call makes a new
  one. If a create returns an error or no answer, search for the record with
  `soql_query` before trying again.
- **Salesforce reports why a write failed** with an error code and message, such
  as `REQUIRED_FIELD_MISSING` or `FIELD_CUSTOM_VALIDATION_EXCEPTION`. Quote the
  message, fix the input, and confirm again before retrying.
- **Updates overwrite.** Only the fields you pass change, but each replaces its
  current value. Never send fields the user did not ask to change.
- **Do not guess IDs, values, or field names.** Look them up or ask.
- **Changes are made as the signed-in person.** Salesforce's history attributes
  them to that person, and Salesforce's own permissions apply: a change they are
  not allowed to make fails with a permission error. Report it plainly and
  suggest asking a Salesforce administrator. Do not suggest workarounds.
- Deleting can also delete related records. Mention that when the object is a
  parent such as an account.
