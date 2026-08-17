---
name: improve-skills
description: |
  Records feedback about how these Salesforce skills perform and reports
  accumulated insights. Use when the user says "that wasn't what I meant", "the
  wrong skill ran", "you should have known to do that", "that's not the
  Salesforce data I wanted", or rephrases a request that a Salesforce skill
  should have handled. Also use when the plugin author asks to "review skill
  feedback", "what needs improving in this plugin", or "show me skill insights".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: feedback
cowork.category: Administration
cowork.icon: Lightbulb
---

# Improve Skills

## What This Skill Does

Captures why a Salesforce skill missed or misfired, and reports the accumulated
patterns to the plugin author. This closes the loop between real usage and the
next version of the skills.

This plugin runs on Salesforce's hosted MCP server, which exposes only generic
sObject tools — there is no plugin-owned backend to store feedback in. So this
skill works in two modes:

| Mode | When | Persistence |
|---|---|---|
| **Session** | Default | Held in the conversation, reported on request |
| **Salesforce** | Org has created a feedback custom object | Stored as records |

## When to Activate

### Recording feedback

- User says the result wasn't what they asked for
- User rephrases a request because the right skill didn't activate
- User corrects the output ("no, group it by close date")
- User asks for a Salesforce capability no current skill covers
- A skill returned nothing useful and the user visibly retried

### Reviewing insights

- Author asks what feedback exists or what needs improving
- Author is preparing a new plugin version

## Workflow

### Recording feedback

1. **Classify the problem:**

   | Type | Meaning |
   |---|---|
   | `missed_activation` | A skill should have run but did not |
   | `wrong_skill` | The wrong skill ran |
   | `poor_output` | Right skill, unhelpful result |
   | `missing_feature` | Nothing covers what the user wanted |
   | `trigger_phrase` | Phrasing that should be added to a skill's triggers |

2. **Anonymize before storing anything.** Strip names, emails, record Ids, and
   account names — keep only the request shape. CRM data is sensitive and
   feedback records are not the place for it.

   > "brief me on Contoso Financial before my 3pm with Dana Reyes"
   > → "brief me on [account] before my meeting with [contact]"

3. **Check once per session whether persistence is available.** Call
   `getObjectSchema` with `object-name: Skill_Feedback__c`.

   - **Object exists** → record it:

     ```json
     {
       "sobject-name": "Skill_Feedback__c",
       "body": {
         "Skill_Name__c": "pipeline-review",
         "Feedback_Type__c": "missed_activation",
         "User_Input__c": "how much am I going to close this month",
         "Expected_Behavior__c": "Should have rolled up open pipeline for the current month",
         "Suggested_Trigger__c": "how much am I going to close"
       }
     }
     ```

   - **Object does not exist** (schema call errors with `INVALID_TYPE`) → hold
     the entry in the conversation. Do not invent a place to put it, and never
     write feedback into Task, Note, or any real CRM object as a substitute.

4. **Acknowledge in one line and move on.** "Noted — I'll flag that." Then
   answer what the user actually asked. Never let feedback capture derail the
   request that triggered it.

### Reviewing insights

1. **Pull stored feedback** if the object exists:

   ```sql
   SELECT Skill_Name__c, Feedback_Type__c, User_Input__c, Expected_Behavior__c,
          Suggested_Trigger__c, CreatedDate
   FROM Skill_Feedback__c
   WHERE CreatedDate = LAST_N_DAYS:90
   ORDER BY CreatedDate DESC LIMIT 200
   ```

   Or aggregate first for a large set:

   ```sql
   SELECT Skill_Name__c, Feedback_Type__c, COUNT(Id) Occurrences
   FROM Skill_Feedback__c WHERE CreatedDate = LAST_N_DAYS:90
   GROUP BY Skill_Name__c, Feedback_Type__c ORDER BY COUNT(Id) DESC
   ```

2. **Combine with anything captured this session.** Say which entries are
   persisted and which are session-only, since session entries vanish.

3. **Group by skill and type**, then rank by frequency.

4. **Recommend concrete edits**, naming the file to change:

   | Finding | Recommendation |
   |---|---|
   | 4 users said "how much will I close" and nothing ran | Add that phrase to `pipeline-review` description |
   | `case-triage` ran when the user wanted deal risk | Tighten both descriptions; add a cross-reference |
   | Users keep asking to convert leads | Not possible on standard servers — needs a custom server Flow |

5. **Version the plan.** Trigger-phrase edits are a patch; workflow changes or a
   new skill are a minor bump.

## Output Format

**Skill feedback — last 90 days** · 12 entries (9 stored, 3 this session)

| Skill | Missed | Wrong | Poor output | Missing | Triggers |
|---|---|---|---|---|---|
| pipeline-review | 4 | 0 | 1 | 0 | 4 |
| case-triage | 0 | 2 | 0 | 0 | 0 |

**Top missing trigger phrases**
1. "how much will I close" → `pipeline-review` (4x)
2. "who owns this account" → `account-briefing` (2x)

**Suggested updates for v1.1.0**

| Skill | Change | Type |
|---|---|---|
| pipeline-review | Add 4 trigger phrases | Patch |
| case-triage | Narrow description to service-only language | Patch |

## Enabling persistence

Feedback survives across sessions only if the Salesforce org has a custom object
for it. An admin creates `Skill_Feedback__c` with these fields:

| Field | Type |
|---|---|
| `Skill_Name__c` | Text(80) |
| `Feedback_Type__c` | Picklist — the five types above |
| `User_Input__c` | Long Text Area |
| `Expected_Behavior__c` | Long Text Area |
| `Suggested_Trigger__c` | Text(255) |

The connector must also be able to write — `sobject-all` or
`sobject-mutations`. On `sobject-reads` or `sobject-deletes`, this skill is
session-only by definition; say so rather than attempting a create that will
fail.

## Handling Edge Cases

- **Never write feedback into real CRM objects.** If `Skill_Feedback__c` does
  not exist, session-only is the correct behavior. Polluting Task or Note with
  plugin telemetry corrupts the customer's CRM.
- **Duplicate phrasings:** Report an occurrence count rather than listing the
  same phrase repeatedly.
- **Feedback about Cowork built-ins** (Email, Calendar, and similar): this
  plugin cannot change them. Point the user at Cowork's thumbs up/down.
- **Feedback about Salesforce itself** — a validation rule, a missing field, a
  permission — is not a skill problem. Say so and route it to their Salesforce
  admin.
- **Nothing recorded yet:** Tell the author the plugin needs more usage before
  patterns are meaningful; do not extrapolate from one entry.
- **Genuine platform limits are not skill gaps.** Lead conversion and record
  merge are unavailable on the standard sObject servers. Record those as
  `missing_feature` and note that a custom server Flow is the fix, so nobody
  wastes a version trying to reword a trigger.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Do NOT prompt for sign-in just to record feedback — that is disproportionate.
   Hold the entry in the session and carry on.
2. If the user was already mid-task, let the sign-in prompt for that task stand
   on its own; record the feedback once the connection exists.
3. When reviewing insights without a connection, report session entries only and
   say that stored history could not be read.
