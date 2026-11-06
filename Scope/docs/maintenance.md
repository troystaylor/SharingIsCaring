# Maintaining this folder

This folder is an overlay on [microsoft/scope](https://github.com/microsoft/scope). It holds documentation, tooling, and one configuration patch, and it is pinned to the upstream commit in [upstream.json](../upstream.json). Two scripts keep it in sync with a working clone:

| Script | Direction |
|--------|-----------|
| `Apply-Overlay.ps1` | This folder to a Scope clone. |
| `Export-Overlay.ps1` | A Scope clone back to this folder. |

## What is exported

`Export-Overlay.ps1` compares the clone with its `HEAD` and writes:

- `overlay/`: whole files for `README.md`, everything under `docs/`, and everything under `scripts/local/`, whether modified or new.
- `patches/local-changes.patch`: a patch for every other modified tracked file, such as `pnpm-workspace.yaml`.

New files outside the overlay paths are listed and skipped, because the overlay is meant to stay small. If you want to ship another kind of file, add its path to `$OverlayPaths` in the script. The overlay can't delete files that upstream has, so the script warns when it finds one deleted.

`-WhatIf` lists what would be exported without changing anything.

## Everyday workflow

1. Edit files in your Scope clone, for example the readme, the docs, or `scripts/local/scope-local.ps1`.
2. Run `.\Export-Overlay.ps1 -ScopePath <scope-path>`.
3. Review the result with `git diff` and commit this folder.
4. Run `.\tests\Invoke-Tests.ps1`.

Evaluation files under `evaluations/` live only in this folder and aren't part of the overlay.

## Move to a newer upstream commit

The overlay replaces whole upstream files, so a new upstream version of `README.md` or a doc we changed would be overwritten. Rebase deliberately:

1. In your clone, save your changes: `git stash --include-untracked`.
2. Check out the new upstream commit: `git fetch origin` then `git checkout <commit>`.
3. Re-apply the overlay and see what differs. For each overlay file, compare upstream's new version with ours and carry the new upstream content into ours:
   - `git diff <old-commit> <new-commit> -- README.md docs/ scripts/` shows what upstream changed in the paths we replace.
4. Apply the overlay and patch: `.\Apply-Overlay.ps1 -ScopePath <scope-path>`. If the patch doesn't apply cleanly, make its changes by hand (see `local-changes.patch`).
5. Run `.\Export-Overlay.ps1 -ScopePath <scope-path> -UpdateCommit`. This regenerates the overlay and patch and writes the new commit to `upstream.json`.
6. Check the stack still works: `scope-local.ps1 up`, `credential`, then `smoke -Model <model>`.
7. Run the tests and commit.

## Tests

`.\tests\Invoke-Tests.ps1` runs the tests with Pester 3.4 or 5. They build throwaway git repositories in the temp folder, so they never touch a real Scope clone or this folder's overlay. They cover:

- Export and apply, including a round trip into a fresh clone, applying twice, and `-SkipPatch`.
- The pinned-commit warning, `-UpdateCommit`, skipped files, and `-WhatIf`.
- The evaluation script's helpers: criteria parsing, environment-variable expansion, profile hashing, and the report's pass-rate arithmetic.
- That every script parses, has no PSScriptAnalyzer errors, and contains no personal paths or secrets.

## Things to know when changing the scripts

- `Read-RunLog` in `evaluations/Invoke-ScopeEval.ps1` parses Scope's run log text: timestamps, `Tool call:` lines, MCP tools named `mcp-gateway-<server>__<tool>`, and `Criteria DAG:` summaries. If a newer Scope changes that format, the report's active seconds, MCP and web counts, and criteria summaries go blank or wrong. Update the parser and the sample lines in its tests, which come from real runs.
- The scripts support Windows PowerShell 5.1, which is the version that ships with Windows. In 5.1, a native command that writes to stderr, such as `git apply --check` on an already-applied patch, throws when `$ErrorActionPreference` is `Stop`. The scripts set it to `Continue` and `throw` explicitly instead.
- Also in 5.1, `ConvertFrom-Json` returns a JSON array as a single object. Pipe it through `ForEach-Object { $_ }` to get the items.
- PowerShell variable names are case-insensitive: a local `$model` hides a parameter named `-Model`.
