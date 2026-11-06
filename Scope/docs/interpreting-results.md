# Interpreting results

An evaluation report from [Invoke-ScopeEval.ps1](../evaluations/Invoke-ScopeEval.ps1) is a starting point, not a verdict. This guide explains what each number means and what to do next.

## Outcomes

Each run ends in one of four outcomes:

| Outcome | Meaning | Counts toward pass rate |
|---------|---------|-------------------------|
| `passed` | The agent finished and the Judge found every criterion satisfied. | Yes |
| `failed` | The agent finished and at least one criterion wasn't satisfied. | Yes |
| `invalid` | The Judge itself failed, so the run says nothing about the agent. | No |
| `incomplete` | The run didn't finish before the timeout, or its result couldn't be read. | No |

### Judge failures are not agent failures

Scope marks a Judge problem as an "infrastructure error" and says so in the error text, for example:

```text
Judge infrastructure error (evaluation could not run - not a criteria failure) ...
```

The report treats these as `invalid`. The common causes in a local setup are:

| Detail in the error | Cause and fix |
|---------------------|---------------|
| `Criteria '<id>' not found via API` | A criterion named by the scenario doesn't exist in your Scope project. Run the evaluation without `-SkipSetup`. |
| `fetch failed` or `ECONNREFUSED` | The Judge can't reach the token manager, usually because it is still starting. Run `scope-local.ps1 status` and wait. |
| `No valid keys available for capability 'copilot-sdk'` | Register a credential with `scope-local.ps1 credential`. |
| `Model "<name>" is not available` | The Judge model isn't available to your account. Use `scope-local.ps1 judge-model`. |

Fix the cause and run the evaluation again. Many invalid runs in a row mean the setup is broken, not that the agent is.

## The report

The summary table has one row per profile:

- **Runs, Passed, Failed, Invalid, Incomplete**: counts of each outcome.
- **Pass rate**: passed divided by passed plus failed.
- **Median turns**: how many Judge feedback turns the run took. A run that passes on turn 1 succeeded without help; a pass on turn 3 needed two rounds of feedback.
- **Median active seconds**: the median time the worker spent on the run, taken from its log lines. It excludes time spent waiting in the queue. Use it to spot a profile where the agent does much more work, for example because it searches for what it needs.
- **Runs using MCP tools**: how many runs called at least one tool from an MCP server. If a profile lists an MCP server and this shows `0 of N`, the agent never used it, which is a finding about the tool's name, description, or discoverability.
- **Runs with web access**: how many runs fetched a URL or ran `curl` or `wget`. See [Check that the baseline can fail](#check-that-the-baseline-can-fail).

Both counts come from each run's trace. `results.json` also lists which MCP tools each run called and how often.

When the first profile (the baseline) has runs with web access and some of them passed, the report adds a **Check your baseline** note, because those passes may come from public information.

The **Runs to review** table lists every failed, invalid, or incomplete run with its criteria summary, such as `1 passed, 1 failed, 0 skipped`, so you can see how close it was before you open it.

When there is more than one profile, the report shows each profile's pass-rate change against the first. Put your baseline first.

## How much to trust a difference

The Judge is a language model and agents vary from run to run, so a single run proves little.

- With 3 runs per profile, a difference of one run is noise. With 10, a difference of two or three is still weak.
- Look for a large, consistent gap, or repeat the evaluation and check the result holds.
- A profile that passes 3 of 3 and a baseline that passes 0 of 3 is strong evidence. 3 of 3 against 2 of 3 is not.
- Read the traces of failures, and of a few passes. The Judge can pass something it shouldn't and fail something it should pass.

## Check that the baseline can fail

An evaluation only measures a tool if the agent needs the tool to pass. Before you trust a comparison, confirm the baseline fails for the reason you expect. Two things make a baseline pass when you don't expect it:

- **The baseline isn't isolated.** The worker has network access, so an agent without your tool can still search the web or read public GitHub repositories. If the facts your criteria check are public, a baseline agent may find them.
- **Scope retries after feedback.** By default a run is multi-turn: when the Judge fails a criterion, the agent gets the feedback and tries again. A baseline that gets it wrong on turn 1 and right on turn 2 counts as passed.

In a first trial of this tooling, the baseline passed every run, twice. The first criterion asked for a skill's name, which was easy to guess from the task. The second asked for a version number and a platform list that exist only in the tool's output, but the baseline agent found them on the web, and its second turn fixed what it got wrong on the first. Its runs also took far longer than the runs with the tool.

What to do about it:

- **Look at turns and time, not only pass rate.** The report shows median turns and active seconds. A profile that passes on turn 1 in a minute beats one that passes on turn 3 after 13 minutes.
- **Read the web-access column.** The report counts runs that fetched from the web, and warns when a baseline that did so also passed.
- **Measure the first attempt.** Run with `-MaxIterations 1`, so a run passes only if the agent succeeds without Judge feedback.
- **Check the trace.** If a baseline passes, open a run and see how. Look for web or GitHub fetches.
- **Use facts the agent can't look up.** Information that is private to your tool, or that you generate for the evaluation, can't be found on the web.

Good criteria:

- Require information or behavior that only the tool provides.
- Are specific enough that a plausible guess fails.
- Check the workspace, not the agent's description of what it did.

## Reading a failed run

Open the run in the Scope Portal (Runs, then the run ID from the report). Look at:

1. **The criteria results**: which criterion failed and the Judge's reasoning. If a parent criterion failed, its children are not judged.
2. **The agent trace**: which tools the agent called, with what arguments, and what came back. This shows whether the agent found your tool, called it correctly, and used the result.
3. **The workspace**: the files the agent produced.

Typical findings and what they suggest:

| What you see | What to consider |
|--------------|------------------|
| The agent never called the tool | The tool's name or description doesn't match how a developer phrases the task. |
| The agent called the tool with wrong arguments | The input schema or parameter descriptions are unclear. |
| The tool returned an error and the agent gave up | The error message isn't actionable. |
| The agent used the tool but ignored the result | The result is too long, poorly structured, or buried the answer. |
| The agent did the task without the tool | The scenario doesn't require the tool. See the baseline check above. |

## Cost and runtime

Each run uses model capacity for the agent and for the Judge. The worker runs only a few runs at a time, so a batch queues, and the time from submission includes waiting. The report's active seconds measures the worker's own time from its log lines. `results.json` keeps both.

In a local setup with a small model, a trivial one-file task took roughly a minute. An agent that had to search the web for what it needed took more than ten minutes per run, and a run that called an MCP tool directly took seconds. Time is a useful signal of how much work the agent did, as well as a cost.

To control cost:

- Develop scenarios with a small model and a count of 1 to 3. Increase the count when you need to decide.
- Keep tasks short and criteria few.
- Reuse profiles. The script names profiles by their definition, so an unchanged profile isn't recreated.

Results are stored in your local Scope database, and `scope-local.ps1 down` deletes it. The report in `evaluations/.results/` is a plain file and stays.
