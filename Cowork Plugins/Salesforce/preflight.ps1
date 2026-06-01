<#
.SYNOPSIS
    Preflight checks for Salesforce Cowork plugin cutover.

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

function Get-AzdEnvValues {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $values = @{}
    try {
        Push-Location $WorkingDirectory
        $raw = & azd env get-values 2>$null
        Pop-Location
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
            return $values
        }

        foreach ($line in ($raw -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^[A-Za-z0-9_]+=') { continue }

            $parts = $trimmed.Split('=', 2)
            if ($parts.Count -ne 2) { continue }

            $key = $parts[0]
            $value = $parts[1].Trim().Trim('"')
            $values[$key] = $value
        }
    }
    catch {
        try { Pop-Location } catch {}
        # Ignore azd parsing failures and fall back to process env only.
    }

    return $values
}

function Get-AzdDotEnvValues {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $values = @{}
    $azureDir = Join-Path $WorkingDirectory ".azure"
    if (-not (Test-Path $azureDir)) {
        return $values
    }

    $envFiles = Get-ChildItem -Path $azureDir -Filter ".env" -Recurse -File -ErrorAction SilentlyContinue
    foreach ($envFile in $envFiles) {
        $lines = Get-Content $envFile.FullName -ErrorAction SilentlyContinue
        foreach ($line in $lines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $trimmed = $line.Trim()
            if ($trimmed.StartsWith('#')) { continue }
            if ($trimmed -notmatch '^[A-Za-z0-9_]+=') { continue }

            $parts = $trimmed.Split('=', 2)
            if ($parts.Count -ne 2) { continue }

            $key = $parts[0]
            $value = $parts[1].Trim().Trim('"')
            if (-not $values.ContainsKey($key)) {
                $values[$key] = $value
            }
        }
    }

    return $values
}

$errors = @()
$warnings = @()

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root "manifest.json"
$azdValues = Get-AzdEnvValues -WorkingDirectory $root
$azdDotEnvValues = Get-AzdDotEnvValues -WorkingDirectory $root

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
        elseif ($mcpUrl -match 'replace-with|your-salesforce-mcp-endpoint|\{\{') {
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
        "DEPLOYER_PRINCIPAL_ID",
        "SALESFORCE_BASE_URL"
    )

    foreach ($envVar in $requiredEnvVars) {
        $value = [Environment]::GetEnvironmentVariable($envVar)
        if ([string]::IsNullOrWhiteSpace($value) -and $azdValues.ContainsKey($envVar)) {
            $value = $azdValues[$envVar]
        }
        if ([string]::IsNullOrWhiteSpace($value) -and $azdDotEnvValues.ContainsKey($envVar)) {
            $value = $azdDotEnvValues[$envVar]
        }
        if ([string]::IsNullOrWhiteSpace($value)) {
            Add-Warning "Environment variable '$envVar' is not set (and no azd env value was found)."
        }
    }

    $sfApiVersion = [Environment]::GetEnvironmentVariable("SALESFORCE_API_VERSION")
    if ([string]::IsNullOrWhiteSpace($sfApiVersion) -and $azdValues.ContainsKey("SALESFORCE_API_VERSION")) {
        $sfApiVersion = $azdValues["SALESFORCE_API_VERSION"]
    }
    if ([string]::IsNullOrWhiteSpace($sfApiVersion) -and $azdDotEnvValues.ContainsKey("SALESFORCE_API_VERSION")) {
        $sfApiVersion = $azdDotEnvValues["SALESFORCE_API_VERSION"]
    }
    if ([string]::IsNullOrWhiteSpace($sfApiVersion)) {
        Add-Warning "Environment variable 'SALESFORCE_API_VERSION' is not set (and no azd env value was found); default v61.0 will be used."
    }
}

Write-Host "`nSalesforce preflight results" -ForegroundColor Cyan
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
