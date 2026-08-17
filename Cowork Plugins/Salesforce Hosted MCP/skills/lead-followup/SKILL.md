---
name: lead-followup
description: |
  Triages Salesforce leads — new, untouched, and aging — and drafts the next
  outreach. Use when the user asks "what leads do I have in Salesforce", "new
  leads this week", "which leads haven't been contacted", "follow up on
  Salesforce leads", "lead status update", or "who should I call from my
  Salesforce leads".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: PersonAdd
---

# Lead Follow-Up

## What This Skill Does

Reviews Salesforce leads assigned to the user, separates untouched from
working from stale, and recommends who to contact next and why.

## When to Activate

- User asks about their leads, new leads, or lead queue
- User asks which leads need follow-up or have gone cold
- User asks to update a lead's status after an outreach

## Workflow

1. **Get the user's Id** with `getUserInfo`.

2. **Pull open leads:**

   ```sql
   SELECT Id, Name, Company, Title, Email, Phone, Status, Rating, LeadSource,
          Industry, AnnualRevenue, NumberOfEmployees, State, Country,
          CreatedDate, LastActivityDate, Owner.Name
   FROM Lead
   WHERE IsConverted = false AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY CreatedDate DESC LIMIT 100
   ```

3. **Isolate untouched leads** — created but never worked:

   ```sql
   SELECT Id, Name, Company, Title, Email, Phone, Status, LeadSource, CreatedDate
   FROM Lead
   WHERE IsConverted = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND LastActivityDate = null AND CreatedDate = LAST_N_DAYS:30
   ORDER BY CreatedDate ASC LIMIT 50
   ```

4. **Isolate stale working leads** — engaged once, then dropped:

   ```sql
   SELECT Id, Name, Company, Status, LastActivityDate, CreatedDate
   FROM Lead
   WHERE IsConverted = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND Status = 'Working - Contacted' AND LastActivityDate < LAST_N_DAYS:14
   ORDER BY LastActivityDate ASC LIMIT 50
   ```

5. **Check for an existing relationship** before treating a lead as cold —
   the company may already be an account:

   ```sql
   SELECT Id, Name, Owner.Name, Type FROM Account WHERE Name LIKE '%Globex%' LIMIT 5
   ```

   If it matches, flag it: the lead may be a duplicate or belong to an existing
   account team.

6. **Prioritize** by rating, company size, source quality, and age. Surface
   untouched leads inside the standard response window first.

7. **Offer to act:**
   - Update `Status` and log outreach — confirm, then `updateSobjectRecord` on
     `Lead` and `createSobjectRecord` on `Task` with `WhoId` set to the Lead Id.
   - Draft the outreach email or call opener as text for the user to send.
   - Conversion is not available through these tools — see the note below.

## Output Format

**Leads — 23 open · 8 untouched**

**Untouched (respond first)**

| Lead | Company | Title | Source | Age | Rating |
|---|---|---|---|---|---|
| Sam Reyes | Globex | Director IT | Webinar | 3 days | Hot |

**Going stale**

| Lead | Company | Status | Last touched |
|---|---|---|---|
| Alex Kim | Initech | Working - Contacted | 2026-07-20 (28 days) |

**Flags** — duplicates against existing accounts, missing email or phone,
leads older than 90 days that should be disqualified.

**Recommended next 5 contacts** — named, with the reason each is ranked there.

## Handling Edge Cases

- **Custom lead statuses:** Confirm real picklist values with `getObjectSchema`
  and `object-name: Lead` before filtering on `Status`.
- **Lead conversion is unavailable.** The standard hosted sObject servers expose
  no lead-convert operation. If the user wants to convert, tell them plainly,
  point them to the Convert button on the lead record, and offer to prepare the
  account, contact, and opportunity details in advance. An admin can
  alternatively expose a conversion Flow, `@InvocableMethod` Apex action, or
  `@AuraEnabled` method through a custom Salesforce MCP server — if that tool is
  present in `tools/list`, use it instead.
- **Missing contact details:** Call out leads with no email and no phone — they
  cannot be worked and should be enriched or disqualified.
- **Converted leads:** Always filter `IsConverted = false`; converted leads
  remain queryable and will otherwise pollute the list.
- **No leads:** Say so and offer a contact or account view instead.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to pull your leads. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Leads sitting in a queue rather than assigned to the user may not appear —
   mention this if the count looks lower than expected.
