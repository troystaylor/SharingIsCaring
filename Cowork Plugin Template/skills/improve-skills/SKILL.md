---
name: improve-skills
description: |
  Records skill performance feedback and retrieves improvement insights.
  Use when the user says "that wasn't what I meant", "the wrong skill activated",
  "you should have known to do X", "add this to your skills", or when the user
  corrects or rephrases a request that a skill should have handled.
  Also use when the plugin author asks to "review skill feedback",
  "what needs improving", or "show me skill insights".
metadata:
  author: "{{Your Name}}"
  version: "1.0"
  pattern: feedback
cowork.category: Administration
---

# Improve Skills

## What This Skill Does

Captures feedback about how skills perform in real conversations and surfaces
accumulated insights for skill authors. This creates a feedback loop that
makes the plugin better over time without requiring the author to monitor
every conversation.

## When to Activate

### Recording feedback (during user conversations)

- User says something like "that's not what I asked for"
- User rephrases a request because the right skill didn't activate
- User explicitly corrects the output ("no, I wanted it grouped by priority")
- A skill produces results that don't match what the user expected
- User suggests a capability the plugin should have

### Reviewing insights (during skill development)

- Plugin author asks "what feedback do we have?"
- Author asks to "review skill performance" or "what needs improving"
- Author is preparing a new plugin version

## Workflow

### When recording feedback

1. **Identify the skill and problem type.** Determine which skill was
   involved (or should have been) and classify the issue:

   | Type | When to use |
   |------|-------------|
   | `missed_activation` | User asked something a skill should handle but it didn't activate |
   | `wrong_skill` | The wrong skill activated for the user's request |
   | `poor_output` | The right skill activated but the output wasn't useful |
   | `missing_feature` | User asked for something no current skill covers |
   | `trigger_phrase` | User used phrasing that should be added to a skill's triggers |

2. **Anonymize the user's input.** Strip any personal information, names,
   specific record IDs, or sensitive data. Keep only the request pattern.

   > "Find Jamie Chen's overdue tickets" → "find [person]'s overdue tickets"

3. **Record the feedback.** Use the `record_skill_feedback` tool:

   > `record_skill_feedback(skill_name: "search-and-explore", feedback_type: "missed_activation", user_input: "show me overdue tickets", expected_behavior: "Should have searched for tickets past SLA")`

4. **Acknowledge briefly.** Don't interrupt the user's workflow. Say
   something like "Noted — I'll flag that for improvement" and continue
   helping with their actual request.

### When reviewing insights

1. **Retrieve feedback.** Use the `get_skill_insights` tool with optional
   filters for skill name, feedback type, or date range.

2. **Summarize findings.** Group by skill and type. Highlight:
   - Most common missed trigger phrases
   - Skills with the most "poor output" feedback
   - Feature gaps (things users ask for that no skill handles)

3. **Recommend changes.** For each finding, suggest a concrete update:

   | Finding | Recommendation |
   |---------|---------------|
   | 5 users said "overdue tickets" but search skill didn't activate | Add "overdue tickets" to search-and-explore description |
   | 3 users wanted results grouped by assignee | Add a grouping step to report-and-summarize workflow |
   | Users keep asking about "customer history" | Consider a new `customer-timeline` skill |

4. **Present as an action plan.** Format as a versioned update plan:

   **Suggested updates for v1.1.0:**

   | Skill | Change | Type |
   |-------|--------|------|
   | search-and-explore | Add trigger phrases: "overdue", "past SLA", "stale tickets" | Patch |
   | report-and-summarize | Add group-by-assignee step to workflow | Minor |
   | (new) customer-timeline | New skill for account history queries | Minor |

## Output Format

### After recording feedback

> "Got it — recorded that 'show me overdue tickets' should activate the
> search skill. This will be reviewed for the next update."

### When presenting insights

**Skill Improvement Report** — Since Apr 1, 2026

**12 feedback entries** across 3 skills

| Skill | Missed | Wrong | Poor Output | Missing | Triggers |
|-------|--------|-------|-------------|---------|----------|
| search-and-explore | 5 | 0 | 2 | 0 | 3 |
| report-and-summarize | 0 | 1 | 1 | 0 | 0 |
| create-and-update | 0 | 0 | 0 | 0 | 0 |

**Top suggested trigger phrases:**
1. "overdue tickets" → search-and-explore (3 occurrences)
2. "past SLA" → search-and-explore (2 occurrences)

**Recommended actions:**
1. Add 5 trigger phrases to search-and-explore
2. Restructure report-and-summarize output to include assignee grouping
3. No changes needed for create-and-update

## Handling Authentication

If a tool call fails because the user hasn't connected to {{Service Name}} yet:

1. Tell the user: "I need to connect to {{Service Name}} to record this
   feedback. You should see a sign-in prompt — please complete it and I'll
   save it."
2. If the feedback is about a missed activation, the feedback itself can
   still be noted in the conversation even without the MCP connection —
   just let the user know it won't persist across sessions.

## Handling Edge Cases

- **Duplicate feedback:** If the same trigger phrase appears multiple times,
  the MCP server should count occurrences rather than storing duplicates.
- **Feedback about built-in skills:** If the user's complaint is about a
  Cowork built-in skill (Email, Calendar, etc.), note that this plugin
  can't modify built-in skills and suggest the user provide feedback
  through Cowork's thumbs up/down buttons.
- **No feedback recorded yet:** If `get_skill_insights` returns empty,
  tell the author the plugin needs more usage before patterns emerge.
