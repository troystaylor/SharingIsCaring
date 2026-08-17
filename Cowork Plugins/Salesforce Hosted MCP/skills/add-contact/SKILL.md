---
name: add-contact
description: |
  Creates a new contact under a Salesforce account. Use when the user asks "add
  a contact in Salesforce", "create [person] as a contact", "add this person to
  [account] in Salesforce", "save this new stakeholder to the CRM", or "add the
  people from this meeting to Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: PersonAdd
---

# Add Contact

## What This Skill Does

Creates a Contact under an existing Salesforce Account, checking for duplicates
first and optionally attaching the person to an open opportunity as a contact
role.

## When to Activate

- User asks to add or create a contact or stakeholder in Salesforce
- User shares meeting attendees or business-card details to store in the CRM

## Workflow

1. **Check for an existing person** before creating anything:

   ```
   FIND {"Dana Reyes"} IN NAME FIELDS RETURNING Contact(Id, Name, Title, Email, Account.Name LIMIT 10), Lead(Id, Name, Company, Status LIMIT 10)
   ```

   Also check by email when you have one — `FIND {dana@acme.com} IN EMAIL FIELDS`.
   If the person already exists, offer to update them instead
   (`update-contact` skill).

2. **Resolve the account:**

   ```
   FIND {Acme*} IN NAME FIELDS RETURNING Account(Id, Name, Type, Owner.Name LIMIT 10)
   ```

   If the account does not exist, offer to create it first (`create-account`).

3. **Gather fields.** Only `LastName` is required:

   | Field | Notes |
   |---|---|
   | `LastName` | Required |
   | `FirstName` | |
   | `AccountId` | Strongly recommended |
   | `Title` | |
   | `Email` | |
   | `Phone` / `MobilePhone` | |
   | `Department` | |
   | `ReportsToId` | Another Contact Id |
   | `LeadSource` | Picklist |
   | `MailingCity` / `MailingState` / `MailingCountry` | |
   | `Description` | |

4. **Confirm before writing.** Show the full record and ask for an explicit yes.

5. **Create it** with `createSobjectRecord`:

   ```json
   {
     "sobject-name": "Contact",
     "body": {
       "FirstName": "Dana",
       "LastName": "Reyes",
       "AccountId": "001XXXXXXXXXXXXXXX",
       "Title": "VP Engineering",
       "Email": "dana@acme.com",
       "Phone": "+1 415-555-0142"
     }
   }
   ```

6. **Offer to link the contact to an open deal** — this is what turns a contact
   record into pipeline signal:

   ```json
   {
     "sobject-name": "OpportunityContactRole",
     "body": {
       "OpportunityId": "006XXXXXXXXXXXXXXX",
       "ContactId": "003XXXXXXXXXXXXXXX",
       "Role": "Decision Maker",
       "IsPrimary": false
     }
   }
   ```

7. **Report** the new Contact Id and any role created.

## Output Format

**Created: Dana Reyes** — Id `003XXXXXXXXXXXXXXX`

| Field | Value |
|---|---|
| Account | Acme Corporation |
| Title | VP Engineering |
| Email | dana@acme.com |
| Phone | +1 415-555-0142 |

Linked to **Acme Renewal** as Decision Maker.

## Handling Edge Cases

- **`Name` is read-only.** Write `FirstName` and `LastName`; `Name` is derived
  and cannot be set.
- **Only a full name given:** Split it sensibly, but confirm the split with the
  user for names where the boundary is ambiguous.
- **Duplicate rules:** On `DUPLICATES_DETECTED`, show the matching record and
  ask whether to use or update it. Do not force the create.
- **Contact without an account:** Salesforce allows it ("private" contacts) but
  they are hard to find later. Warn the user and confirm before creating one.
- **Existing lead with the same person:** Point it out — creating a contact
  alongside an unconverted lead creates a data-quality problem. Offer to note
  the lead for conversion in Salesforce instead.
- **Multiple contacts at once:** List them all, take one confirmation, then
  create them one at a time and report each result.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to add that contact. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, their Salesforce profile does not
   permit creating contacts on that account.
