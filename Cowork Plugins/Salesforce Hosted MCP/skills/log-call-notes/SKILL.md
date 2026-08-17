---
name: log-call-notes
description: |
  Logs calls, meetings, and follow-ups into Salesforce as activities. Use when
  the user asks "log this call in Salesforce", "save my meeting notes to
  Salesforce", "record what we discussed", "create a follow-up task in
  Salesforce", "log an activity on [account]", or "capture these notes to the
  CRM".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Note
---

# Log Call Notes

## What This Skill Does

Turns a conversation summary into Salesforce activity records — a completed
Task for what happened, plus follow-up Tasks for what was committed — attached
to the right account, contact, and opportunity.

## When to Activate

- User asks to log a call, meeting, or conversation in Salesforce
- User shares notes and wants them stored against a customer record
- User asks to create a follow-up or reminder task

## Workflow

1. **Identify the related records.** Resolve the account, the person, and the
   deal so the activity is filed where it will be found:

   ```
   FIND {Acme*} IN NAME FIELDS RETURNING Account(Id, Name LIMIT 5), Contact(Id, Name, Title, Account.Name LIMIT 10), Opportunity(Id, Name, StageName, Amount, CloseDate LIMIT 10)
   ```

   `WhoId` takes a Contact or Lead Id. `WhatId` takes an Account, Opportunity,
   or Case Id — prefer the Opportunity when the conversation was about a deal,
   so it shows on the deal's activity timeline.

2. **Structure the notes** before writing. A useful `Description` covers:
   what was discussed, decisions made, objections or risks raised, and
   committed next steps with owners and dates.

3. **Confirm the record set** with the user: the completed activity plus each
   follow-up task, with due dates. Get an explicit yes before writing.

4. **Create the completed activity** with `createSobjectRecord`:

   ```json
   {
     "sobject-name": "Task",
     "body": {
       "Subject": "Call — Acme redline review",
       "Description": "Discussed legal redlines on the MSA. Dana confirmed budget is approved. Objection: 30-day security review required before signature. Next: legal returns redlines by 8/24; security questionnaire due 8/27.",
       "ActivityDate": "2026-08-17",
       "Status": "Completed",
       "Priority": "Normal",
       "TaskSubtype": "Call",
       "WhoId": "003XXXXXXXXXXXXXXX",
       "WhatId": "006XXXXXXXXXXXXXXX"
     }
   }
   ```

5. **Create each follow-up** as a separate open Task:

   ```json
   {
     "sobject-name": "Task",
     "body": {
       "Subject": "Send security questionnaire to Acme",
       "ActivityDate": "2026-08-27",
       "Status": "Not Started",
       "Priority": "High",
       "WhoId": "003XXXXXXXXXXXXXXX",
       "WhatId": "006XXXXXXXXXXXXXXX"
     }
   }
   ```

6. **Log a scheduled meeting as an Event** rather than a Task when it is a
   future calendar item:

   ```json
   {
     "sobject-name": "Event",
     "body": {
       "Subject": "Acme security review",
       "StartDateTime": "2026-08-27T16:00:00Z",
       "EndDateTime": "2026-08-27T17:00:00Z",
       "WhoId": "003XXXXXXXXXXXXXXX",
       "WhatId": "006XXXXXXXXXXXXXXX"
     }
   }
   ```

7. **Offer to update the deal** if the conversation changed the stage, amount,
   close date, or next step — that hands off to `update-opportunity`.

8. **Report** everything created with Ids.

## Output Format

**Logged to Salesforce**

| Record | Type | Related to | Date |
|---|---|---|---|
| Call — Acme redline review | Task (Completed) | Acme Renewal · Dana Reyes | 2026-08-17 |
| Send security questionnaire to Acme | Task (Not Started, High) | Acme Renewal · Dana Reyes | 2026-08-27 |
| Acme security review | Event | Acme Renewal · Dana Reyes | 2026-08-27 16:00 UTC |

Deal next step is now stale — want me to update it to "Security review
completes 8/27"?

## Handling Edge Cases

- **Timezones:** `StartDateTime` and `EndDateTime` are ISO-8601 UTC. Convert
  from the user's stated local time and state the timezone you used in the
  confirmation.
- **`ActivityDate` is a date, not a datetime** — `YYYY-MM-DD` only.
- **`AccountId` on Task is read-only** — it is derived from `WhatId`/`WhoId`.
  Do not write it.
- **No matching contact:** Log against the Account or Opportunity via `WhatId`
  and leave `WhoId` empty, or offer to create the contact first (`add-contact`).
- **Long notes:** `Description` on Task holds up to 32,000 characters, but keep
  the entry structured and scannable rather than a raw transcript dump.
- **Multiple attendees:** A Task has one `WhoId`. Name the other attendees in
  the `Description`, or create an Event and note that adding formal invitees
  requires `EventRelation` records, which are best managed in Salesforce.
- **Sensitive content:** If the notes contain compensation, legal, or personal
  data, ask before writing it into a shared CRM record.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to log those notes. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm. Keep
   the drafted notes so nothing is lost while they sign in.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Activities are created as the signed-in Salesforce user and are owned by
   them.
