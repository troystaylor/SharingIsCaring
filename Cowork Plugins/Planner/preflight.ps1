<#
.SYNOPSIS
    Preflight checks for Planner Cowork plugin cutover.

.DESCRIPTION
    Validates required deployment environment variables and checks that
    manifest connector values are no longer placeholders before upload.

.PARAMETER CheckManifestOnly
    Skip environment variable checks and validate only manifest values.
#>

param(
    [switch]$CheckManifestOnly
)

$ErrorActionPreference = "Stop"

function Add-Error([string]$message) {
    $script:errors += $message
}

function Add-Warning([string]$message) {
    $script:warnings += $message
}

$errors = @()
$warnings = @()

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root "manifest.json"

if (-not (Test-Path $manifestPath)) {
    Add-Error "manifest.json not found at plugin root."
}
else {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    $connector = $null
    if ($manifest.agentConnectors -and $manifest.agentConnectors.Count -gt 0) {
        $connector = $manifest.agentConnectors[0]
    }
    else {
        Add-Error "No connector found in manifest agentConnectors."
    }

    if ($connector) {
        $mcpUrl = [string]$connector.toolSource.remoteMcpServer.mcpServerUrl
        $referenceId = [string]$connector.toolSource.remoteMcpServer.authorization.referenceId
        $version = [string]$manifest.version

        if ([string]::IsNullOrWhiteSpace($mcpUrl)) {
            Add-Error "Connector mcpServerUrl is empty."
        }
        elseif ($mcpUrl -notmatch '^https://') {
            Add-Error "Connector mcpServerUrl must be HTTPS."
        }
        elseif ($mcpUrl -match 'replace-with|contoso\.com|\{\{') {
            Add-Error "Connector mcpServerUrl still contains a placeholder value."
        }

        if ([string]::IsNullOrWhiteSpace($referenceId)) {
            Add-Error "Connector authorization.referenceId is empty."
        }
        elseif ($referenceId -match 'replace-with|registration-id|\{\{') {
            Add-Error "Connector authorization.referenceId still contains a placeholder value."
        }

        if ([string]::IsNullOrWhiteSpace($version)) {
            Add-Error "Manifest version is missing."
        }
    }
}

if (-not $CheckManifestOnly) {
    $requiredEnvVars = @(
        "AZURE_ENV_NAME",
        "AZURE_LOCATION",
        "DEPLOYER_PRINCIPAL_ID"
    )

    foreach ($envVar in $requiredEnvVars) {
        $value = [Environment]::GetEnvironmentVariable($envVar)
        if ([string]::IsNullOrWhiteSpace($value)) {
            Add-Warning "Environment variable '$envVar' is not set."
        }
    }

    $tenant = [Environment]::GetEnvironmentVariable("PLANNER_TENANT_ID")
    if ([string]::IsNullOrWhiteSpace($tenant)) {
        Add-Warning "Environment variable 'PLANNER_TENANT_ID' is not set; ensure Graph auth is configured in your server deployment."
    }
}

Write-Host "`nPlanner preflight results" -ForegroundColor Cyan
Write-Host ("=" * 40)

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
