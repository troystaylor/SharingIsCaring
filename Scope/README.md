# Scope

> **Unofficial.** This folder is a personal collection of documentation updates and tooling for running [Scope](https://github.com/microsoft/scope). It isn't part of the Scope project, and Microsoft doesn't maintain or support it. Scope's own code and documentation are in the [microsoft/scope](https://github.com/microsoft/scope) repository, and the files in `overlay/` are derived from them under Scope's MIT license (see `LICENSE`). The scripts are provided as is, and they change settings on your machine, such as installing a local certificate authority through `mkcert`. Read `overlay/docs/local-setup-windows.md` before you run them.

[Scope](https://github.com/microsoft/scope) is Microsoft's open-source platform for evaluating how AI coding agents use your product. You define a task and success criteria, run GitHub Copilot or Claude Code against it in a containerized worker, and a Judge service scores the result against a criteria graph. This folder doesn't contain Scope itself. It contains the documentation updates, Windows setup tooling, and evaluation tooling that I use to run Scope locally and compare agent setups, such as the same task with and without an MCP server.

Scope is a research preview, so treat its API and CLI as subject to change.

## What's in this folder

| Path | Purpose |
|------|---------|
| `overlay/` | Files that replace the matching files in a Scope clone: a complete project readme, a Windows setup guide, the `scope-local.ps1` helper, and fixes for stale documentation links. |
| `patches/local-changes.patch` | Throttles and retries package downloads inside Docker image builds. Needed on networks with a package registry proxy. |
| `Apply-Overlay.ps1` | Copies the overlay into a Scope clone and applies the patch. |
| `Export-Overlay.ps1` | The reverse: regenerates the overlay and patch from a working Scope clone. |
| `upstream.json` | The upstream Scope commit the overlay was made against. |
| `evaluations/` | `Invoke-ScopeEval.ps1`, which runs an evaluation across profiles and writes a report, plus a template and a runnable example. See [evaluations/README.md](./evaluations/README.md). |
| `docs/` | [Interpreting results](./docs/interpreting-results.md) and [Maintaining this folder](./docs/maintenance.md). |
| `tests/` | Tests for the scripts. Run `.\tests\Invoke-Tests.ps1`. |
| `LICENSE` | Scope's MIT license. The overlay documents are derived from Scope's, so the license and copyright notice apply to them. |

Links inside the `overlay/` documents are relative to a Scope clone, so they resolve after you apply the overlay, not in this folder.

## Set up Scope locally

You need Git, Node.js 22, pnpm 10.29.1, Docker Desktop with Compose v2, [mkcert](https://github.com/FiloSottile/mkcert), and the [GitHub CLI](https://cli.github.com/) signed in to an account with a GitHub Copilot entitlement.

1. Clone Scope at the pinned commit (the one in `upstream.json`), outside any OneDrive-synced folder. Use a short path such as `C:\src\scope`: upstream contains very long file names, and the checkout fails on Windows with "Filename too long" in a deeper path unless you also pass `-c core.longpaths=true`.

   ```powershell
   git -c core.longpaths=true clone https://github.com/microsoft/scope.git <scope-path>
   git -C <scope-path> -c core.longpaths=true checkout <commit>
   ```

2. Apply the overlay from this folder:

   ```powershell
   .\Apply-Overlay.ps1 -ScopePath <scope-path>
   ```

   Add `-SkipPatch` if your network reaches the public npm registry without problems.

3. Install dependencies and start Scope:

   ```powershell
   cd <scope-path>
   pnpm install --frozen-lockfile
   .\scripts\local\scope-local.ps1 check
   .\scripts\local\scope-local.ps1 up
   ```

   The first start builds images and compiles the gateway, so it can take 20 minutes or more.

4. Register a credential for the Judge, optionally pick a model you can use for the Judge, and run the smoke test:

   ```powershell
   .\scripts\local\scope-local.ps1 credential
   .\scripts\local\scope-local.ps1 judge-model -JudgeModel <model>
   .\scripts\local\scope-local.ps1 smoke -Model <model>
   ```

   Skip `judge-model` if the default Judge model works for your account. A passing smoke test confirms the agent, Judge, and credentials work end to end.

5. Open the Portal at `http://localhost:5100` with `pnpm open:portal`. Local sign-in uses seeded emulator users, not your Entra tenant.

Stop the stack with `.\scripts\local\scope-local.ps1 down`. This also removes the stack's volumes, so stored projects, criteria, and secrets are deleted.

See `overlay/docs/local-setup-windows.md` for what the helper does, the local deviations from upstream, and the changes it makes to your machine, including a local certificate authority from `mkcert -install`.

## Evaluate your tools

With Scope running, compare agent setups on the same task:

```powershell
.\evaluations\Invoke-ScopeEval.ps1 -Path .\evaluations\examples\health-route -Model <model> -Count 3
```

The script creates the criteria, registers any MCP servers, creates a profile for each variant, runs the task several times per profile, and writes `report.md` with pass rates. To test your own MCP server or skill:

1. Copy `evaluations/_template` to `evaluations/local/<name>`.
2. Write the task, criteria, and profiles. Make sure the baseline can fail without your tool.
3. Run the script on it.

`evaluations/local/` and `evaluations/.results/` are ignored by git, so your evaluations and their results stay out of commits. See [evaluations/README.md](./evaluations/README.md) for the file formats and [interpreting results](./docs/interpreting-results.md) for how to read a report.

## Keep this folder current

When you change files in your Scope clone, run `Export-Overlay.ps1 -ScopePath <scope-path>` to regenerate `overlay/` and `patches/`, review the diff, and run the tests. To move to a newer upstream commit, follow [Maintaining this folder](./docs/maintenance.md).

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| Checkout fails with "Filename too long" | Clone to a short path, or pass `-c core.longpaths=true` to git. |
| Image builds fail with `ECONNRESET`, `EAI_AGAIN`, or 502 errors | Parallel downloads overwhelm DNS or a registry proxy. Apply the patch and use `scope-local.ps1`, which builds images one at a time. |
| Compose stops because the gateway is unhealthy | The gateway compiles from source on first start and outlasts its healthcheck. `scope-local.ps1 up` waits for it and starts Compose again. |
| Runs fail with `fetch failed` | The Judge can't reach the token manager yet. Run `scope-local.ps1 status` and wait until every check passes. |
| `No valid keys available for capability 'copilot-sdk'` | Run `scope-local.ps1 credential`. |
| `Model "..." is not available` from the Judge | Run `scope-local.ps1 judge-model -JudgeModel <model>` with a model your account can use. |
| An evaluation reports many `invalid` runs | The Judge failed, not the agent. See [interpreting results](./docs/interpreting-results.md#judge-failures-are-not-agent-failures). |
| An MCP server on your machine isn't reachable from a run | Containers can't use `localhost`. Use `host.docker.internal` in the server's URL. |

More symptoms and fixes are in `overlay/README.md`.
