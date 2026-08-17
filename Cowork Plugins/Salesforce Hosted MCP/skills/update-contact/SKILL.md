---
name: update-contact
description: |
  Updates fields on an existing Salesforce contact. Use when the user asks
  "update [person] in Salesforce", "fix this contact's email", "change the
  title on this Salesforce contact", "this person changed roles", "correct the
  phone number in Salesforce", or "move this contact to another account".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Contact

## What This Skill Does

Edits an existing Salesforce Contact after showing current values and getting
explicit confirmation on the change.

## When to Activate

- User asks to correct or update someone's contact details
- User reports a title, role, or company change for a known contact
- User asks to change a contact's account or reporting line

## Workflow

1. **Resolve the contact:**

   ```
   FIND {"Dana Reyes"} IN NAME FIELDS RETURNING Contact(Id, Name, Title, Email, Phone, Account.Name LIMIT 10)
   ```

   If several match, show account, title, and email and ask which. Duplicate
   people are common — confirm you are editing the live record.

2. **Read current values:**

   ```sql
   SELECT Id, FirstName, LastName, Name, AccountId, Account.Name, Title,
          Department, Email, Phone, MobilePhone, ReportsToId, ReportsTo.Name,
          LeadSource, MailingCity, MailingState, MailingCountry, Description,
          OwnerId, Owner.Name
   FROM Contact WHERE Id = '003XXXXXXXXXXXXXXX'
   ```

3. **Resolve reference fields** before writing them. For `ReportsToId`, look up
   the manager's Contact Id; for `AccountId`, resolve the target account; for
   `OwnerId`, resolve the User:

   ```sql
   SELECT Id, Name, Title FROM Contact WHERE AccountId = '001XXXXXXXXXXXXXXX' AND Name LIKE '%Vale%' LIMIT 10
   ```

4. **Show before/after and get explicit confirmation.** List only the changing
   fields.

5. **Apply the update** with `updateSobjectRecord`, sending only changed fields:

   ```json
   {
     "sobject-name": "Contact",
     "id": "003XXXXXXXXXXXXXXX",
     "body": {
       "Title": "SVP Engineering",
       "Email": "dana.reyes@acme.com",
       "MobilePhone": "+1 415-555-0188"
     }
   }
   ```

6. **Report the before/after summary.**

## Output Format

**Updated: Dana Reyes** — Id `003XXXXXXXXXXXXXXX` · Acme Corporation

| Field | Before | After |
|---|---|---|
| Title | VP Engineering | SVP Engineering |
| Email | dana@acme.com | dana.reyes@acme.com |

## Handling Edge Cases

- **`Name` is read-only.** To change a name, write `FirstName` and `LastName`.
- **Contact moved to a new employer:** Changing `AccountId` rewrites history —
  the contact's past activities follow them to the new account. Ask whether the
  user would rather create a new contact under the new account and mark the old
  one inactive. Confirm the choice before writing.
- **Clearing a field:** Send `null`, not an empty string.
- **Contact roles are separate:** Editing a contact does not change their role
  on any opportunity. If the user means the deal role, update
  `OpportunityContactRole` instead:

  ```json
  {
    "sobject-name": "OpportunityContactRole",
    "id": "00KXXXXXXXXXXXXXXX",
    "body": { "Role": "Economic Buyer", "IsPrimary": true }
  }
  ```

- **Validation and duplicate rules:** Relay
  `FIELD_CUSTOM_VALIDATION_EXCEPTION` and `DUPLICATES_DETECTED` messages
  verbatim and ask how to proceed.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to update that contact. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, the signed-in user cannot edit that
   contact or that specific field under their field-level security.
