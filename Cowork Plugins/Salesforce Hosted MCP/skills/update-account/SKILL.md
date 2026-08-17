---
name: update-account
description: |
  Updates fields on an existing Salesforce account. Use when the user asks
  "update [account] in Salesforce", "change the account owner", "fix the
  address on this Salesforce account", "set account type to customer", or
  "correct the industry in Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Account

## What This Skill Does

Edits an existing Salesforce Account after showing the current values and
getting explicit confirmation on the change.

## When to Activate

- User asks to change, correct, or update account details
- User asks to reassign account ownership
- User asks to change account type, industry, or address

## Workflow

1. **Resolve the account** with `find`:

   ```
   FIND {Acme*} IN NAME FIELDS RETURNING Account(Id, Name, Type, Industry, BillingCity, Owner.Name LIMIT 10)
   ```

   If more than one match, list them and ask. Never update on a guess.

2. **Read current values** for the fields being changed:

   ```sql
   SELECT Id, Name, Type, Industry, Website, Phone, AnnualRevenue,
          NumberOfEmployees, BillingStreet, BillingCity, BillingState,
          BillingPostalCode, BillingCountry, Description, OwnerId, Owner.Name
   FROM Account WHERE Id = '001XXXXXXXXXXXXXXX'
   ```

3. **Validate picklist values** with `getObjectSchema` and
   `object-name: Account` before sending `Type` or `Industry`.

4. **Resolve an owner change** to a User Id first:

   ```sql
   SELECT Id, Name, Email, IsActive FROM User WHERE Name LIKE '%Chen%' AND IsActive = true LIMIT 10
   ```

5. **Show a before/after diff and get explicit confirmation.** List only the
   fields that change. Do not proceed on an implied yes.

6. **Apply the update** with `updateSobjectRecord`:

   ```json
   {
     "sobject-name": "Account",
     "id": "001XXXXXXXXXXXXXXX",
     "body": {
       "Type": "Customer - Direct",
       "Industry": "Technology",
       "BillingCity": "San Francisco",
       "BillingState": "CA"
     }
   }
   ```

   Send only the fields that change — never resend the whole record.

7. **Confirm the result** and re-read the record if the user needs to see the
   saved state.

## Output Format

**Updated: Acme Corporation** — Id `001XXXXXXXXXXXXXXX`

| Field | Before | After |
|---|---|---|
| Type | Prospect | Customer - Direct |
| Billing City | Oakland | San Francisco |

Unchanged fields were not touched.

## Handling Edge Cases

- **Read-only fields:** `Id`, `CreatedDate`, `LastModifiedDate`, and formula or
  roll-up summary fields cannot be written. If the user asks to change one,
  explain why it is not editable.
- **Clearing a field:** Send `null` — an empty string is not equivalent for
  non-text field types.
- **Ownership changes:** Reassigning `OwnerId` can change record visibility for
  other users and may trigger org automation. Call this out before confirming.
- **Validation rules:** On `FIELD_CUSTOM_VALIDATION_EXCEPTION`, relay the org's
  message verbatim rather than paraphrasing, and ask how to proceed.
- **Bulk updates:** If the user asks to update many accounts, list every record
  that would change, get one explicit confirmation for the whole set, then call
  `updateSobjectRecord` once per record and report per-record success or
  failure. Stop and report if more than two consecutive calls fail.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to make that change. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, explain that the signed-in Salesforce
   user lacks edit access to that record or field — the connector always acts as
   them, never with elevated rights.
