---
name: agent-evaluation
description: Create, run and interpret Copilot Studio agent evaluation suites - design test cases from the agent's purpose, run the suite, and report pass rates and failures honestly. Use when the user asks to test, evaluate, score, benchmark or regression-check a Copilot Studio agent, or to check an agent before publishing.
---

# Agent Evaluation

## When to use this skill

- "Evaluate my agent", "write test cases for...", "run the evals", "is this agent ready to publish", "did my change break anything".

## How to do it

1. Apply `copilot-studio-access`. Call `get_copilot_studio_skills` and read the guidance for evaluation if it is offered. Use `describe_actions` on `manage_agents` to confirm the evaluation actions and their inputs.
2. Get the agent's current draft with `manage_agents` and read its instructions, tools and knowledge. Test cases come from what the agent is supposed to do.
3. List existing evaluation suites for the agent. Reuse or extend a suite instead of creating a duplicate.
4. Propose test cases that cover: typical requests, requests that should be declined or handed off, ambiguous or incomplete requests, requests that need each tool or knowledge source, and one or two adversarial prompts. Show the cases and the expected behavior for each, then follow `write-safety` before saving the suite.
5. Running a suite consumes credits and may call tools with side effects. Say so, name the agent and suite, and get confirmation before running.
6. Report results as counts from the run: total, passed, failed, and the failing cases with the actual response beside the expected behavior. Do not round up, and do not call a run passing if cases failed or did not complete.
7. For each failure, say whether the cause looks like the agent's instructions, a missing tool or knowledge source, or a weak test case. Suggest a fix as a draft change through `agent-authoring`. Fix the agent rather than loosening the test.

## What to watch for

- Do not delete failing cases or change expected results to get a pass unless the user explains the test was wrong.
- Results from a saved draft are not results for the published agent. Say which was tested.
