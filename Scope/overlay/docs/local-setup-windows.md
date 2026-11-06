# Local setup on Windows

This guide covers running Scope locally on Windows, including a helper script and the
local deviations from the upstream setup. It complements the
[Quick start](../README.md#quick-start-local) in the root README.

The helper adds files only. It doesn't change upstream scripts, Compose files, or
configuration.

## Helper script

[scripts/local/scope-local.ps1](../scripts/local/scope-local.ps1) wraps the upstream
`pnpm docker:dev:copilot` flow. Run it from the repository root in Windows PowerShell or
PowerShell 7.

| Command | What it does |
|---------|--------------|
| `.\scripts\local\scope-local.ps1 check` | Verifies Node 22, pnpm, a running Docker daemon, mkcert, a signed-in GitHub CLI, Git Bash ahead of WSL bash, and installed dependencies. |
| `.\scripts\local\scope-local.ps1 up` | Runs the checks, starts the stack, and waits until it's ready. Recovers if Compose aborts (see below). |
| `.\scripts\local\scope-local.ps1 status` | Shows container state and readiness checks. |
| `.\scripts\local\scope-local.ps1 credential` | Registers your `gh` token as a Scope secret if none has the `copilot-sdk` capability. |
| `.\scripts\local\scope-local.ps1 judge-model -JudgeModel <model>` | Sets `JUDGE_MODEL`, `FEEDBACK_MODEL`, and `REPORT_MODEL` in `.env.local` and recreates the Judge and report generator. |
| `.\scripts\local\scope-local.ps1 smoke -Model <model>` | Submits a trivial evaluation and reports whether it passed. Exits non-zero on failure. |
| `.\scripts\local\scope-local.ps1 down` | Stops the stack. |

Typical first run:

```powershell
.\scripts\local\scope-local.ps1 check
.\scripts\local\scope-local.ps1 up
.\scripts\local\scope-local.ps1 credential
.\scripts\local\scope-local.ps1 judge-model -JudgeModel <model>   # only if the default Judge model isn't available to you
.\scripts\local\scope-local.ps1 smoke -Model <model>
```

Optional parameters: `-Worker` (default `coder-acp-copilot`), `-ProjectName` (default
`scope-mt-app`, the Compose project name), `-ApiPort` (default `3100`), `-MaxAttempts`
(default `3`), and `-TimeoutMinutes` (default `30`). Compose logs are written to
`%TEMP%\scope-local`.

`down` removes the stack's volumes, so stored data is lost, including projects,
criteria, and secrets. Run `credential` again after the next `up`.

## What the helper changes and why

| Behavior | Reason |
|----------|--------|
| Puts Git Bash ahead of `bash.exe` in System32 and sets `npm_config_script_shell` | The upstream scripts are Bash. On Windows, `bash` often resolves to WSL, and pnpm defaults to `cmd.exe`, so scripts fail or run in the wrong environment. |
| Sets `COMPOSE_PARALLEL_LIMIT=1` | Parallel image builds hit DNS resets (`EAI_AGAIN`), `ECONNRESET`, and 502 errors from a package registry proxy. Sequential builds complete. |
| Retries after Compose exits | The Rust gateway compiles from source on its first dev start, which can outlast its healthcheck. Compose then aborts and stops dependent services. The helper waits for the gateway to become healthy and starts Compose again, up to `-MaxAttempts` times. |
| Restarts stopped services during `up` | A service such as the token manager can be left stopped after an aborted start. |
| Waits for the Judge to reach the token manager | The Judge and token manager compile in watch mode and take a few minutes after a (re)start. Runs submitted earlier fail with `fetch failed`. |
| `credential` and `judge-model` | The Judge needs a `copilot-sdk` credential, and its default model may not be available to your account. |

## Local deviations from upstream

These changes exist only in this working copy. Keep them out of upstream pull requests.

| File | Change | Reason |
|------|--------|--------|
| `pnpm-workspace.yaml` | Added `networkConcurrency: 4`, `fetchRetries: 8`, and `fetchRetryMintimeout: 5000`. | The Docker image builds copy this file, so these settings throttle and retry package downloads inside builds. Without them, builds fail on flaky DNS or proxy connections. |
| `.env.local` (ignored by git) | `JUDGE_MODEL`, `FEEDBACK_MODEL`, `REPORT_MODEL`, written by `judge-model`. | The upstream default model may not be available to your account. |
| `scripts/local/scope-local.ps1` | New file. | The helper described above. |
| `docs/local-setup-windows.md` | New file. | This guide. |

To revert the `pnpm-workspace.yaml` change, run `git checkout -- pnpm-workspace.yaml`.

## Machine-level changes

Running Scope locally changes your machine in these ways:

- `mkcert -install` adds a local root CA to your Windows and browser trust store. Remove it
  with `mkcert -uninstall`.
- Node.js 22 must be active (for example `nvm use 22`).
- Your `gh auth token` is passed to the Copilot worker as `GITHUB_TOKEN` and stored in
  Scope's local secret store by `credential`. Both live only in local containers and
  volumes, and `down` deletes the stored secret.

## Troubleshooting

See [Troubleshooting local setup](../README.md#troubleshooting-local-setup) in the root
README for symptoms and fixes. Run `.\scripts\local\scope-local.ps1 status` first: it shows
which service isn't ready.
