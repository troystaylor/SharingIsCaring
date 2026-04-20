# HVE Guidance Manual Evaluation Pack

Use these scenarios to manually evaluate connector quality in the connector test pane or Copilot Studio. For each scenario, capture:

- User prompt
- Tool selected
- Input arguments
- Whether the first tool call was appropriate
- Whether the top result was useful
- Any missing metadata, weak reasoning, or ranking issues

## Discovery

### 1. Find Review Guidance

Prompt:

`Find HVE guidance for code review workflows in a regulated environment.`

Expected:

- Primary tool: `recommend_assets_for_task`
- Good signals: top results should emphasize review, governance, or security-oriented assets
- Failure signals: generic onboarding assets rank above review-specific content

### 2. Browse Instructions

Prompt:

`List instruction assets in HVE Guidance and show the most relevant ones for repository standards.`

Expected:

- Primary tool: `list_assets`
- Optional follow-up: `recommend_assets_for_task`
- Good signals: returned assets include metadata, summaries, and clear asset types

## Retrieval

### 3. Inspect a Specific Asset

Prompt:

`Open a specific instruction and tell me what it is for, when not to use it, and what related assets I should inspect next.`

Expected:

- Primary tool: `get_asset`
- Good signals: response includes intended use, when not to use, key constraints, and related assets

### 4. Search Skills

Prompt:

`Search HVE skills for security or OWASP-related content.`

Expected:

- Primary tool: `search_assets`
- Good signals: results show skill paths and metadata that align to security intents

## Recommendation Quality

### 5. Onboarding Scenario

Prompt:

`Recommend the best HVE assets for onboarding a new contributor to an existing repository.`

Expected:

- Primary tool: `recommend_assets_for_task`
- Good signals: onboarding/getting-started assets rank highly
- Failure signals: implementation-only assets outrank onboarding assets

### 6. Refactor Scenario

Prompt:

`What HVE assets should I use for a risky multi-file refactor?`

Expected:

- Primary tool: `recommend_assets_for_task`
- Good signals: planning, research, and review intents appear in results and reasons

### 7. Governance Scenario

Prompt:

`Recommend HVE assets for adding governance and auditability to agent workflows.`

Expected:

- Primary tool: `recommend_assets_for_task`
- Good signals: governance and policy-oriented assets rank highly with explicit reasoning

## Validation

### 8. Validate Instruction

Prompt:

`Validate this instruction content and tell me exactly what to fix.`

Arguments:

- Use intentionally weak content missing frontmatter and examples

Expected:

- Primary tool: `validate_instruction`
- Good signals: findings include `message`, `why`, and `fix`

### 9. Validate Prompt

Prompt:

`Check whether this prompt is well-structured and safe.`

Arguments:

- Use a short ambiguous prompt and one with bypass language

Expected:

- Primary tool: `validate_prompt`
- Good signals: unsafe language is flagged as an issue, not just a warning

### 10. Validate Agent Config

Prompt:

`Validate this agent config and show the most important fixes.`

Arguments:

- Use malformed JSON once
- Use valid JSON missing `name` and `description` once

Expected:

- Primary tool: `validate_agent_config`
- Good signals: parse failures are actionable and missing fields produce clear fixes

## Change Intelligence

### 11. Recent Standards Changes

Prompt:

`Summarize recent changes in HVE instructions from the last 30 days.`

Expected:

- Primary tool: `summarize_asset_changes`
- Good signals: commit list is scoped to instruction paths and dates are recent

### 12. Compare Versions

Prompt:

`Compare two versions of an HVE instruction file and tell me whether it changed materially.`

Expected:

- Primary tool: `compare_asset_versions`
- Good signals: changed status and estimated line deltas are returned

## Release Tracking

### 13. Release Highlights

Prompt:

`What are the latest HVE Guidance release highlights?`

Expected:

- Primary tool: `get_release_highlights`
- Good signals: recent releases, tag names, and body previews are returned

## Adoption Planning

### 14. Team Rollout Plan

Prompt:

`Create an adoption plan for the API Platform team to standardize agent prompts and instructions over 8 weeks.`

Expected:

- Primary tool: `generate_adoption_plan`
- Good signals: phased rollout, milestones, and relevant tool suggestions

## Scoring Rubric

Score each scenario on a 1-5 scale for:

1. Tool selection correctness
2. Result usefulness
3. Explanation quality
4. Ranking quality
5. Actionability

Track recurring misses and use them to tune:

- intent labels
- recommendation scoring
- validation rules
- related asset heuristics
- fallback search behavior
