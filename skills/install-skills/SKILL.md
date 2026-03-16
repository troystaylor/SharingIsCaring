---
name: install-skills
description: 'Install Copilot skills from GitHub repositories. Use when: adding skills, cloning skill repos, configuring chat.agentSkillsLocations, setting up skills for Copilot Studio.'
argument-hint: 'Optionally provide repo URL and skills subfolder path'
---

# Install Copilot Skills

Install skills from any GitHub repository and automatically configure VS Code settings.

## When to Use

- Installing skills from a GitHub repository
- Adding new skill sources to VS Code
- Setting up Copilot Studio skills
- Configuring `chat.agentSkillsLocations`

## Procedure

1. Run the install script with appropriate parameters:

```powershell
# Default - global install (Copilot Studio skills)
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1"

# Install to current workspace instead of globally
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1" -Workspace

# Custom repository - global
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1" -RepoUrl "https://github.com/org/repo"

# Custom repository - workspace
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1" -RepoUrl "https://github.com/org/repo" -Workspace

# Custom repository with different skills location
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1" -RepoUrl "https://github.com/org/repo" -SkillsSubPath "src/skills"

# Repository with skills at root
& "$env:USERPROFILE\.copilot\.copilot\install-skills.ps1" -RepoUrl "https://github.com/org/repo" -SkillsSubPath ""
```

2. Restart VS Code to load the new skills

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-RepoUrl` | `https://github.com/microsoft/skills-for-copilot-studio` | GitHub repository URL |
| `-SkillsSubPath` | `skills` | Subfolder containing skills (use `""` for root) |
| `-Workspace` | (switch) | Install to `.github/skills/` in current directory |

## What It Does

**Global mode (default):**
1. Clones the repository to `~\.copilot\skills\<repo-name>\`
2. Updates VS Code `settings.json` to add the skills path to `chat.agentSkillsLocations`
3. Handles both VS Code Insiders and stable editions

**Workspace mode (`-Workspace`):**
1. Clones the repository to `.github\skills\<repo-name>\` in current directory
2. Copies individual skill folders to `.github\skills\` for auto-discovery
3. Cleans up the clone directory
4. No settings changes needed - VS Code auto-discovers workspace skills

## Script Location

[install-skills.ps1](../install-skills.ps1)
