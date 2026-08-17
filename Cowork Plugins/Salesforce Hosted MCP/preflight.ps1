<#
.SYNOPSIS
    Preflight checks for the Salesforce (Hosted MCP) Cowork plugin.

.DESCRIPTION
    Validates the manifest against the Salesforce Hosted MCP deployment model:
    the connector must point at a Salesforce-operated MCP server URL, the OAuth
    reference must be resolved, and every agentSkills folder must exist with
    frontmatter whose name matches its folder.

    Unlike the self-hosted Salesforce plugin, this one has no Azure
    infrastructure, so there are no deployment environment variables to check.

.PARAMETER AllowPlaceholders
    Report unresolved placeholder values as warnings instead of errors. Useful
    while developing before the OAuth registration exists.

.EXAMPLE
    .\preflight.ps1
    .\preflight.ps1 -AllowPlaceholders
#>

param(
    [switch]$AllowPlaceholders
)

$ErrorActionPreference = "Stop"

$script:errors = @()
$script:warnings = @()

function Add-Error([string]$message) {
    if ($AllowPlaceholders -and $message -match 'placeholder') {
        $script:warnings += $message
    }
    else {
        $script:errors += $message
    }
}

function Add-Warning([string]$message) {
    $script:warnings += $message
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root "manifest.json"

# Salesforce-operated MCP endpoints. Anything else means the plugin is not
# actually using the hosted servers.
$hostedUrlPattern = '^https://api\.salesforce\.com/platform/mcp/v1/(sandbox/)?[A-Za-z0-9_\-/]+$'
$knownServers = @(
    "platform/sobject-reads",
    "platform/sobject-mutations",
    "platform/sobject-deletes",
    "platform/sobject-all",
    "data/data-cloud-queries",
    "data/data360",
    "analytics/tableau-next"
)

if (-not (Test-Path $manifestPath)) {
    Add-Error "manifest.json not found at plugin root."
}
else {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    # --- Identity ---

    if (-not $manifest.id -or $manifest.id -match '\{\{') {
        Add-Error "manifest 'id' still contains a placeholder value."
    }
    elseif (-not ($manifest.id -as [guid])) {
        Add-Error "manifest 'id' is not a valid GUID."
    }

    if ($manifest.manifestVersion -ne "devPreview") {
        Add-Error "manifestVersion must be 'devPreview'; Cowork drops agentConnectors on other schema versions."
    }

    if (-not $manifest.packageName) {
        Add-Error "manifest 'packageName' is required by the devPreview schema."
    }

    if ([string]::IsNullOrWhiteSpace([string]$manifest.version)) {
        Add-Error "manifest 'version' is missing."
    }

    # --- Connector ---

    $connector = $null
    if ($manifest.agentConnectors -and $manifest.agentConnectors.Count -gt 0) {
        $connector = $manifest.agentConnectors[0]
    }
    else {
        Add-Error "No connector found in manifest agentConnectors."
    }

    if ($connector) {
        $server = $connector.toolSource.remoteMcpServer
        $mcpUrl = [string]$server.mcpServerUrl
        $authType = [string]$server.authorization.type
        $referenceId = [string]$server.authorization.referenceId

        if ([string]::IsNullOrWhiteSpace($mcpUrl)) {
            Add-Error "Connector mcpServerUrl is empty."
        }
        elseif ($mcpUrl -match '\{\{|<YOUR-|replace-with') {
            Add-Error "Connector mcpServerUrl still contains a placeholder value."
        }
        elseif ($mcpUrl -notmatch $hostedUrlPattern) {
            Add-Error "Connector mcpServerUrl '$mcpUrl' is not a Salesforce hosted MCP endpoint (expected https://api.salesforce.com/platform/mcp/v1/[sandbox/]<server-name>)."
        }
        else {
            $serverName = $mcpUrl -replace '^https://api\.salesforce\.com/platform/mcp/v1/(sandbox/)?', ''
            if ($knownServers -notcontains $serverName) {
                Add-Warning "Server name '$serverName' is not one of the documented standard servers. This is expected for a custom Salesforce MCP server."
            }
            if ($mcpUrl -match '/v1/sandbox/') {
                Add-Warning "Connector points at a SANDBOX server. Use the production URL before publishing."
            }

            # A skill that needs a tool the target server does not expose will
            # fail at runtime with no useful message, so catch it here.
            $writeSkills = @("create-account", "update-account", "create-opportunity",
                             "update-opportunity", "add-contact", "update-contact",
                             "log-call-notes")
            $deleteSkills = @("delete-salesforce-record")
            $identitySkills = @("opportunity-health-summary", "pipeline-review",
                                "open-risks-and-blockers", "review-tasks",
                                "lead-followup", "case-triage")

            $declaredNames = @()
            foreach ($entry in $manifest.agentSkills) {
                $declaredNames += (Split-Path ([string]$entry.folder) -Leaf)
            }

            $unsupported = @{}
            switch ($serverName) {
                "platform/sobject-reads" {
                    foreach ($n in ($writeSkills + $deleteSkills)) {
                        if ($declaredNames -contains $n) { $unsupported[$n] = "server is read-only" }
                    }
                }
                "platform/sobject-mutations" {
                    foreach ($n in $deleteSkills) {
                        if ($declaredNames -contains $n) { $unsupported[$n] = "server cannot delete" }
                    }
                    foreach ($n in $identitySkills) {
                        if ($declaredNames -contains $n) { $unsupported[$n] = "server does not expose getUserInfo" }
                    }
                }
                "platform/sobject-deletes" {
                    foreach ($n in $writeSkills) {
                        if ($declaredNames -contains $n) { $unsupported[$n] = "server cannot create or update" }
                    }
                    foreach ($n in $identitySkills) {
                        if ($declaredNames -contains $n) { $unsupported[$n] = "server does not expose getUserInfo" }
                    }
                }
            }

            foreach ($name in $unsupported.Keys) {
                Add-Error "Skill '$name' is declared but '$serverName' cannot run it ($($unsupported[$name])). Run ./configure.ps1 to fix the skill set."
            }
        }

        if ($authType -ne "OAuthPluginVault") {
            Add-Error "Connector authorization.type must be 'OAuthPluginVault'. Salesforce does not support dynamic client registration, so an anonymous or DCR connector will never authenticate."
        }

        if ([string]::IsNullOrWhiteSpace($referenceId)) {
            Add-Error "Connector authorization.referenceId is empty."
        }
        elseif ($referenceId -match '\{\{|replace-with|registration-id') {
            Add-Error "Connector authorization.referenceId still contains a placeholder value."
        }
    }

    # --- Skills ---

    if (-not $manifest.agentSkills -or $manifest.agentSkills.Count -eq 0) {
        Add-Error "manifest declares no agentSkills."
    }
    else {
        if ($manifest.agentSkills.Count -gt 20) {
            Add-Error "agentSkills has $($manifest.agentSkills.Count) entries (max 20)."
        }

        $declared = @()
        foreach ($entry in $manifest.agentSkills) {
            $folder = [string]$entry.folder
            if ([string]::IsNullOrWhiteSpace($folder)) {
                Add-Error "An agentSkills entry has no 'folder' value."
                continue
            }

            $relative = $folder -replace '^\./', ''
            $declared += (Split-Path $relative -Leaf)
            $skillPath = Join-Path $root ($relative -replace '/', '\')
            $skillFile = Join-Path $skillPath "SKILL.md"

            if (-not (Test-Path $skillFile)) {
                Add-Error "Missing SKILL.md for declared skill '$folder'."
                continue
            }

            $content = Get-Content $skillFile -Raw
            $folderName = Split-Path $skillPath -Leaf

            if ($content -notmatch '(?m)^name:\s*(\S+)\s*$') {
                Add-Error "$folderName/SKILL.md has no 'name' in frontmatter."
            }
            elseif ($Matches[1] -ne $folderName) {
                Add-Error "$folderName/SKILL.md declares name '$($Matches[1])' which does not match its folder name."
            }

            if ($content -notmatch '(?m)^description:') {
                Add-Error "$folderName/SKILL.md has no 'description' in frontmatter."
            }

            if ($content -notmatch '## Handling Authentication') {
                Add-Warning "$folderName/SKILL.md has no '## Handling Authentication' section; the agent may retry blindly when the user is not signed in."
            }

            if ($content -match '\{\{') {
                Add-Error "$folderName/SKILL.md still contains a placeholder value."
            }
        }

        # Skill folders on disk that the manifest never declares are expected
        # after configuring for a reduced server; report them as information.
        $skillsDir = Join-Path $root "skills"
        if (Test-Path $skillsDir) {
            $undeclared = @()
            foreach ($dir in (Get-ChildItem $skillsDir -Directory)) {
                if ($declared -notcontains $dir.Name) { $undeclared += $dir.Name }
            }
            if ($undeclared.Count -gt 0) {
                Add-Warning "$($undeclared.Count) skill folder(s) on disk are not declared and will not ship: $($undeclared -join ', '). This is expected when configured for a reduced server."
            }
        }
    }
}

# --- Icons ---

foreach ($icon in @("color.png", "outline.png")) {
    if (-not (Test-Path (Join-Path $root $icon))) {
        Add-Error "Missing required icon '$icon'."
    }
}

# --- Deployment model ---

foreach ($stray in @("server", "infra", "azure.yaml")) {
    if (Test-Path (Join-Path $root $stray)) {
        Add-Warning "'$stray' exists in this plugin. The hosted MCP plugin needs no self-hosted server or Azure infrastructure."
    }
}

Write-Host "`nSalesforce (Hosted MCP) preflight results" -ForegroundColor Cyan
Write-Host ("=" * 45)

if ($warnings.Count -gt 0) {
    Write-Host "Warnings ($($warnings.Count)):" -ForegroundColor Yellow
    foreach ($w in $warnings) {
        Write-Host "  ! $w" -ForegroundColor Yellow
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Errors ($($errors.Count)):" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  x $e" -ForegroundColor Red
    }
    Write-Host "`nPreflight failed." -ForegroundColor Red
    exit 1
}

Write-Host "Preflight passed." -ForegroundColor Green
exit 0
