# Evaluations

An evaluation answers one question: can an agent finish a task using what you ship, and does a change to your tools, skills, or guidance improve that? Each evaluation is a folder that [Invoke-ScopeEval.ps1](./Invoke-ScopeEval.ps1) runs against a local Scope stack. The script runs the same task several times for each profile you want to compare, then writes a report.

Evaluation results and your own evaluations stay out of source control. The `evaluations/local/` and `evaluations/.results/` folders are ignored by git, so put evaluations for your own products in `local/`.

## Folder layout

```text
evaluations/
  Invoke-ScopeEval.ps1       Runs an evaluation and writes a report
  _template/                 Copy this to start a new evaluation
  examples/health-route/     A runnable example that needs no external tools
  local/                     Your evaluations (ignored by git)
  .results/                  Reports and results (ignored by git)
```

An evaluation folder contains:

| File | Required | Purpose |
|------|----------|---------|
| `scenario.yaml` | Yes | The task prompt and the IDs of the criteria that judge it. |
| `criteria/*.yaml` | Yes | One criterion per file. |
| `profiles.json` | No | The profiles to compare. Without it, the script runs a single `baseline` profile. |
| `mcp-servers.json` | No | MCP servers to register so a profile can use them. |

## Write an evaluation

1. Copy `_template` to `local/<name>`.
2. Write the task in `scenario.yaml` the way a developer would ask for it. Don't name the tool or skill you are testing unless a real user would.
3. Write criteria that the Judge can check. Prefer observable statements about the workspace, such as "`server.js` implements a `GET /health` route that returns JSON", over opinions such as "the code is good". Use `depends_on` so a later criterion is judged only if an earlier one passes.
4. List the profiles to compare in `profiles.json`. Change one thing at a time between profiles, such as adding an MCP server or a skill, so you can attribute the difference.
5. If a profile uses an MCP server, describe it in `mcp-servers.json`.

### Criteria files

The script creates criteria with the Scope CLI, because the CLI's `criteria import` command doesn't send a project ID. It reads a simple YAML shape, one criterion per file:

```yaml
id: my_check
prompt: |
  The workspace contains a file named result.md that lists the three risks.
depends_on:
  - earlier_check
gates:
  - select
```

Use `depends_on: []` for a criterion with no parents. Criteria that already exist in your project are left alone, so to change a criterion, edit it in the Portal or delete it first.

### Profiles

```json
[
  { "name": "baseline" },
  { "name": "with-mcp", "mcpServers": ["my-server"] },
  { "name": "with-skill", "skills": ["github/<owner>/<repo>/<skill-path>"] }
]
```

Each entry can also set `worker` and `model`. Scope profile versions are immutable, so the script names each profile `<evaluation>-<name>-<hash>` and creates a new one when the definition changes.

### MCP servers

```json
[
  {
    "id": "my-server",
    "name": "My server",
    "type": "http",
    "url": "http://host.docker.internal:8080/mcp",
    "headers": ["Authorization: Bearer ${env:MY_MCP_TOKEN}"]
  }
]
```

- Scope's workers run in containers, so a server running on your machine is reachable at `host.docker.internal`, not `localhost`.
- Write secrets as `${env:NAME}`. The script reads the value from your environment when it registers the server, so the file never contains it. The registered server, including its headers, is stored in your local Scope database.
- Skills must be reachable from the worker. A skill is a path in a GitHub repository that the worker can read.
- A worker has network access, so a baseline agent can search the web. See [Check that the baseline can fail](../docs/interpreting-results.md#check-that-the-baseline-can-fail).

## Run an evaluation

Start Scope and register a credential first (see the [folder readme](../README.md)). Then:

```powershell
.\evaluations\Invoke-ScopeEval.ps1 -Path .\evaluations\examples\health-route -Model <model> -Count 3
```

| Parameter | Purpose |
|-----------|---------|
| `-Model` | The model for the coding agent. List the models with `pnpm cli agent list` in your Scope clone. |
| `-Count` | Runs per profile. Default 3. Use more for decisions. |
| `-MaxIterations` | The most Judge feedback turns per run. By default the agent can retry after feedback. Use `1` to measure first-attempt success. |
| `-ProfileName` | Run only the named profiles. |
| `-Worker` | The worker. Default `coder-acp-copilot`. |
| `-ScopePath` | Your Scope clone, used when the `scope` CLI isn't installed. Defaults to `$env:SCOPE_REPO`, then `C:\src\scope`. |
| `-ApiUrl`, `-Project` | Another Scope API or project. Defaults to `http://localhost:3100` and your first project. |
| `-OutDir` | Where reports go. Defaults to `evaluations\.results`. |
| `-SkipSetup` | Skip creating criteria and registering MCP servers. |

The script prints progress and writes `report.md` and `results.json` to `.results/<evaluation>/<timestamp>/`. [Interpreting results](../docs/interpreting-results.md) explains how to read them.

## Limitations

- The criteria reader understands only the shape above.
- A run's per-criterion reasoning and agent trace are in the Portal. The report lists failed runs with their IDs so you can open them.
- The Judge is an LLM. Read the traces of failures before you change a product because of a result.
