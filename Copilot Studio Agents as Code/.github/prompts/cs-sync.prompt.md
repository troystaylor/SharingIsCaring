---
agent: agent
description: "Run the safe Copilot Studio sync ritual: preview remote changes, retrieve them, validate the local agent definition, commit, and apply."
---

# Sync this Copilot Studio agent

Walk the cloned agent in this workspace through a safe apply. Do not skip steps — apply is blocked by the extension if remote changes are outstanding, and `Get Changes` overwrites uncommitted local work.

1. **Check for remote drift.** Have me run `Copilot Studio: Preview` from the command palette, then read the Agent Changes pane. Report what changed remotely and what changed locally.

2. **Protect local work.** If there are uncommitted local changes, commit them to Git before anything is retrieved. Show me the diff summary first.

3. **Retrieve.** If remote changes exist, have me run `Copilot Studio: Get Changes`. For each conflict, show both versions and recommend which to keep, with a one-line reason. Do not choose silently.

4. **Validate.** Check the Problems pane for errors in the agent definition files. Fix any that are mine to fix — malformed YAML, unknown `kind:` values, Power Fx missing a leading `=`, duplicate node ids, undeclared variables missing an `init:` prefix. If a `kind:` is flagged and you are not certain of the correct value, say so rather than substituting a guess.

5. **Summarise before applying.** List exactly what will change in the live agent, grouped by component. Wait for my confirmation.

6. **Apply.** Have me run `Copilot Studio: Apply Changes`, then confirm the reported status.

7. **Remind me** that apply is not publish — the change is live in the environment for testing in the Copilot Studio test pane, but end users see nothing until I publish.

If the workspace has no cloned agent, stop and tell me to run `Copilot Studio: Clone Agent` first.
