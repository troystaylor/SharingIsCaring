---
name: apply-skill
description: |
  Loads an agentskills.io skill and executes its steps in an isolated ACA
  Sandbox. Use when the user asks to "apply", "run", "execute", "set up",
  "scaffold", "create a project using", or wants to follow a skill's
  instructions with actual command execution. Creates an isolated environment,
  runs the commands, and returns results.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
---

# Apply a Skill (Execute in Sandbox)

## What This Skill Does

Loads an agentskills.io skill, extracts its executable steps, and runs them
in an isolated Azure Container Apps Sandbox. The sandbox provides a fresh
Linux environment with the tools needed (dotnet, node, python, etc.).
Commands run sequentially; output is returned to the user.

## When to Activate

- User says "apply that skill" or "run that skill"
- User asks to "set up a project" or "scaffold" something
- User says "execute the steps" from a skill
- User wants to follow a skill's workflow with real execution
- User asks to "try it" after seeing skill guidance

## Workflow

1. **Load the skill.** Use `get_skill` to fetch the full SKILL.md:

   > `get_skill(owner: "dotnet", repo: "skills", skill_path: "plugins/dotnet-ai/skills/technology-selection")`

2. **Parse executable steps.** Read the skill content and identify:
   - Shell commands (in code blocks marked as `bash`, `shell`, `powershell`)
   - Package installs (`dotnet add package`, `npm install`, `pip install`)
   - File creation steps
   - Configuration commands

3. **Choose the right sandbox image.** Based on the skill's requirements:
   - `.NET` skills → image: `ubuntu` (has dotnet SDK)
   - `Node.js` skills → image: `ubuntu` (has node)
   - `Python` skills → image: `ubuntu` (has python)
   - General → image: `ubuntu`

4. **Confirm with the user.** Before executing, show what will be run:

   "I'll execute these commands in an isolated sandbox:
   1. `dotnet new webapi -n MyApi`
   2. `cd MyApi && dotnet add package Microsoft.Extensions.AI`
   3. `dotnet build`

   Proceed?"

5. **Execute commands.** Use `execute_in_sandbox`:

   > `execute_in_sandbox(commands: ["dotnet new webapi -n MyApi", "cd MyApi && dotnet add package Microsoft.Extensions.AI", "cd MyApi && dotnet build"], image: "ubuntu")`

6. **Report results.** For each command, show:
   - Whether it succeeded (exit code 0) or failed
   - Relevant stdout output (trimmed to key information)
   - Any errors with suggestions

7. **Offer follow-up actions:**
   - "Want me to run more commands in the same sandbox?"
   - "Should I check the generated files?"
   - "Want me to clean up the sandbox?"

## Output Format

### Execution results

**Applying skill: technology-selection**

| # | Command | Status | Output |
|---|---------|--------|--------|
| 1 | `dotnet new webapi -n MyApi` | ✓ | Project created |
| 2 | `dotnet add package Microsoft.Extensions.AI` | ✓ | Package added (v9.1.0) |
| 3 | `dotnet build` | ✓ | Build succeeded, 0 warnings |

**Sandbox ID:** `sb-a1b2c3` (active for 10 more minutes)

Want me to:
- Run additional commands in this sandbox?
- Show the project structure?
- Clean up the sandbox?

### On failure

| # | Command | Status | Output |
|---|---------|--------|--------|
| 1 | `dotnet new webapi -n MyApi` | ✓ | Project created |
| 2 | `dotnet add package NonExistent.Package` | ✗ | Package not found |

**Stopped at step 2.** The package `NonExistent.Package` doesn't exist.

Suggestions:
- Check the package name on nuget.org
- The skill may reference a preview package — try adding `--prerelease`

## Safety Rules

- **Always confirm before executing.** Show the commands and ask for approval.
- **Stop on first failure.** Don't continue if a command fails (exit code ≠ 0).
- **Sandbox is isolated.** Nothing in the sandbox can affect the user's machine
  or other systems. It's a throwaway environment.
- **Auto-cleanup.** Sandboxes auto-delete after 10 minutes of idle time.
- **No secrets.** Never pass user credentials, tokens, or API keys to sandbox
  commands. If a skill requires auth, note this and let the user decide.
