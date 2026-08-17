---
name: find-contacts
description: |
  Finds Salesforce contacts and leads by name, account, title, or email and
  returns reachable details. Use when the user asks "find [person] in
  Salesforce", "who do we know at [company]", "contacts at [account]", "get me
  [person]'s email from Salesforce", "who is the decision maker at [account]",
  or "look up this lead in Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Person
---

# Find Contacts

## What This Skill Does

Locates people in Salesforce — contacts and leads — and returns who they are,
how to reach them, and what is currently open with them.

## When to Activate

- User asks to find a person, contact, or lead
- User asks who they know at a company
- User asks for someone's email, phone, or title
- User asks who the decision maker or champion is at an account

## Workflow

1. **Search across people objects** with `find` (SOSL) when given a name or
   partial term — it searches Contact and Lead in one call:

   ```
   FIND {Reyes*} IN NAME FIELDS RETURNING Contact(Id, Name, Title, Email, Phone, Account.Name, LastActivityDate LIMIT 20), Lead(Id, Name, Title, Email, Phone, Company, Status LIMIT 20)
   ```

   For an email search, use `IN EMAIL FIELDS`. For a broad term, use
   `IN ALL FIELDS`.

2. **Search by account** when the user names a company rather than a person.
   Resolve the account first, then:

   ```sql
   SELECT Id, Name, Title, Department, Email, Phone, MobilePhone,
          ReportsToId, ReportsTo.Name, LastActivityDate
   FROM Contact WHERE AccountId = '001XXXXXXXXXXXXXXX'
   ORDER BY LastActivityDate DESC NULLS LAST LIMIT 50
   ```

3. **Filter by title** when the user asks for a role:

   ```sql
   SELECT Id, Name, Title, Email, Phone, Account.Name
   FROM Contact
   WHERE AccountId = '001XXXXXXXXXXXXXXX'
     AND (Title LIKE '%Chief%' OR Title LIKE '%VP%' OR Title LIKE '%Director%')
   ORDER BY Name LIMIT 25
   ```

4. **Identify deal involvement** for the contacts found — this is what makes
   the answer useful rather than a phone book lookup:

   ```sql
   SELECT ContactId, Contact.Name, Opportunity.Name, Opportunity.StageName,
          Opportunity.Amount, Role, IsPrimary
   FROM OpportunityContactRole
   WHERE ContactId IN ('003XXXXXXXXXXXXXXX') AND Opportunity.IsClosed = false
   ```

5. **Report** with contact details plus context: their role on open deals, last
   activity date, and any open tasks assigned to them.

## Output Format

For multiple matches:

| Name | Title | Account | Email | Last Activity |
|---|---|---|---|---|
| Dana Reyes | VP Engineering | Acme Corp | dana@acme.com | 2026-08-04 |
| Sam Reyes | Lead — Director IT | Globex (Lead) | sam@globex.com | — |

For a single person:

**Dana Reyes** — VP Engineering at Acme Corp
- **Email:** dana@acme.com · **Phone:** +1 415-555-0142 · **Mobile:** +1 415-555-0188
- **Reports to:** Chris Vale (CTO)
- **Open deals:** Acme Renewal ($450K, Negotiation) — role: Decision Maker, primary
- **Last activity:** 2026-08-04 — "Redline review call"

Always mark leads clearly as leads; they are not yet contacts on an account.

## Handling Edge Cases

- **SOSL minimum length:** Search terms must be at least two characters. Wrap
  multi-word terms in quotes: `FIND {"Dana Reyes"}`.
- **Apostrophes and reserved characters:** Escape `\ ? & | ! { } [ ] ( ) ^ ~ * : " ' + -`
  in SOSL with a backslash. In SOQL string literals, escape `'` as `\'`.
- **No results:** Try a broader search (`IN ALL FIELDS`, shorter stem, wildcard
  suffix) before reporting nothing found. Then offer to create the contact,
  which hands off to the `add-contact` skill.
- **Duplicates:** Salesforce orgs commonly hold duplicate people. Show all
  matches with Id and last activity so the user can pick the live record.
- **Person Accounts:** In orgs with Person Accounts enabled, individuals may
  exist as Accounts. If a name search returns nothing in Contact, also try
  `RETURNING Account(Id, Name, PersonEmail, PersonMobilePhone)` — check the
  schema first with `getObjectSchema` since those fields only exist when the
  feature is on.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to look that person up. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Contact visibility follows the signed-in user's sharing rules — a person may
   exist in Salesforce but not be visible to them.
