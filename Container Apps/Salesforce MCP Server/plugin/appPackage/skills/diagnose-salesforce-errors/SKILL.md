---
name: diagnose-salesforce-errors
description: |
  Diagnoses Salesforce errors and missing data. Use when a Salesforce tool
  returns an error, when the user says "it says insufficient access", "I can't
  see that record", "why did this fail", "invalid field", "request limit
  exceeded", "Salesforce isn't responding", or "who is this connected as".
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: troubleshooting
---

# Diagnose Salesforce Errors

## What This Skill Does

Turns a Salesforce error or an unexpected empty result into a cause and a next
step the user can act on.

## When to Activate

- A tool returns an error
- A record, field, or object the user expects is missing
- Results are empty when they should not be
- The user asks who or what the connection runs as

## First Moves

1. **Read the error.** Salesforce returns a code and a message. Quote the message,
   because it usually names the exact problem.
2. **Call `get_connection_info`** when access might be the cause. It shows the
   org, whether it is a sandbox, and the Salesforce user the request runs as: the
   person who is signed in. Their permissions are the limit of what they can see
   or change here.

## Common Errors

| What you see | Likely cause | Next step |
|---|---|---|
| `INVALID_FIELD` or `INVALID_TYPE` | A field or object name is wrong, or the signed-in user cannot see it | `describe_object` or `list_objects` with `nameContains` to find the right name |
| `INSUFFICIENT_ACCESS` or a record that "does not exist" | The signed-in user lacks permission or sharing for the record | Say so plainly and suggest asking a Salesforce administrator for access |
| `REQUEST_LIMIT_EXCEEDED` | The org's API limit is used up | `get_limits`, then wait or ask an administrator |
| `REQUIRED_FIELD_MISSING` | A required field was not supplied | `describe_object`, then ask the user for the value |
| `FIELD_CUSTOM_VALIDATION_EXCEPTION` | A validation rule rejected the data | `list_validation_rules` with `includeFormulas` and explain the condition |
| `INVALID_OR_NULL_FOR_RESTRICTED_PICKLIST` | A picklist value is not allowed | `get_picklist_values`, with `recordTypeId` if the object has record types |
| "does not support API version" | The version was retired or mistyped | `list_api_versions` and offer a supported one |
| A setup lookup returns a note | The signed-in user lacks setup permission | Report the note's reason; continue with `describe_object` |
| "no Salesforce sign-in", or "expired or been revoked" | The person's Salesforce sign-in is missing or has expired | Ask them to sign in to Salesforce again for this connection, then retry once. Do not retry in a loop |
| "Could not reach Salesforce" | Salesforce is unavailable | Say the connection is unavailable and to try again shortly |

## What to Watch For

- **Permissions belong to the signed-in person.** A missing record or a refused
  action usually means their Salesforce profile or sharing does not allow it. Say
  so plainly and suggest asking a Salesforce administrator. Do not suggest
  workarounds or other people's access.
- **Do not retry a failing call unchanged.** Change something first: a name, a
  value, a version.
- **An empty result is not an error.** It means nothing matched. Check the filter
  before assuming the data is missing.
- **Sign-in problems are for the user first.** Ask them to sign in to Salesforce
  again. If it keeps failing, a Salesforce administrator should check the
  connection. Do not guess at configuration the user cannot reach.
