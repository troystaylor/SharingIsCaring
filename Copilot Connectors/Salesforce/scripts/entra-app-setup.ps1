<#
.SYNOPSIS
    Registers a Microsoft Entra (Azure AD) application for the Salesforce Copilot Connector.

.DESCRIPTION
    Creates an app registration with the required Microsoft Graph permissions:
    - ExternalConnection.ReadWrite.OwnedBy
    - ExternalItem.ReadWrite.OwnedBy
    Then creates a client secret, updates .env.local, and instructs you to generate local.settings.json from it.

.NOTES
    Prerequisites:
    - Azure CLI installed (https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
    - Logged in: az login
    - Must be run by a user with permission to create app registrations in the tenant
    - A Global Admin or Privileged Role Administrator must grant admin consent afterward

.EXAMPLE
    .\entra-app-setup.ps1
    .\entra-app-setup.ps1 -AppName "Salesforce Copilot Connector"
#>

param(
    [string]$AppName = "Salesforce Copilot Connector"
)

$ErrorActionPreference = "Stop"

# ── Check Azure CLI ──
try {
    $null = az version 2>$null
} catch {
    Write-Error "Azure CLI not found. Install from https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
}

# ── Verify login ──
Write-Host "`n[1/6] Checking Azure CLI login..." -ForegroundColor Cyan
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in. Running 'az login'..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
$tenantId = $account.tenantId
Write-Host "  Tenant: $tenantId ($($account.user.name))" -ForegroundColor Green

# ── Microsoft Graph permission IDs ──
# These are the well-known IDs for Microsoft Graph application permissions
$graphAppId = "00000003-0000-0000-c000-000000000000" # Microsoft Graph

# Application permissions (Roles)
$permissions = @(
    @{
        Id = "f431331c-49a6-499f-be1c-62af19c34a9d" # ExternalConnection.ReadWrite.OwnedBy
        Name = "ExternalConnection.ReadWrite.OwnedBy"
    },
    @{
        Id = "8116ae0f-55c2-452d-9571-571b6138b7c3" # ExternalItem.ReadWrite.OwnedBy
        Name = "ExternalItem.ReadWrite.OwnedBy"
    }
)

# ── Create App Registration ──
Write-Host "`n[2/6] Creating app registration: '$AppName'..." -ForegroundColor Cyan
$app = az ad app create `
    --display-name $AppName `
    --sign-in-audience "AzureADMyOrg" `
    --output json | ConvertFrom-Json

$appId = $app.appId
$objectId = $app.id
Write-Host "  App ID (client_id): $appId" -ForegroundColor Green
Write-Host "  Object ID: $objectId" -ForegroundColor Green

# ── Add Required Permissions ──
Write-Host "`n[3/6] Adding Microsoft Graph permissions..." -ForegroundColor Cyan
foreach ($perm in $permissions) {
    Write-Host "  Adding $($perm.Name)..."
    az ad app permission add `
        --id $appId `
        --api $graphAppId `
        --api-permissions "$($perm.Id)=Role" `
        --output none 2>$null
}

# ── Create Service Principal ──
Write-Host "`n[4/6] Creating service principal..." -ForegroundColor Cyan
$sp = az ad sp create --id $appId --output json 2>$null | ConvertFrom-Json
if ($sp) {
    Write-Host "  Service principal created" -ForegroundColor Green
} else {
    Write-Host "  Service principal may already exist (OK)" -ForegroundColor Yellow
}

# ── Create Client Secret ──
Write-Host "`n[5/6] Creating client secret (valid 2 years)..." -ForegroundColor Cyan
$secret = az ad app credential reset `
    --id $appId `
    --display-name "copilot-connector-secret" `
    --years 2 `
    --output json | ConvertFrom-Json

$clientSecret = $secret.password

# ── Output Results ──
Write-Host "`n[6/6] Setup complete!" -ForegroundColor Cyan
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
Write-Host "║  Add these to .env.local:                                    ║" -ForegroundColor Yellow
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
Write-Host ""
Write-Host "AZURE_TENANT_ID=$tenantId"
Write-Host "AZURE_CLIENT_ID=$appId"
Write-Host "AZURE_CLIENT_SECRET=$clientSecret"
Write-Host ""

# ── Create or update .env.local ──
$envFile = Join-Path $PSScriptRoot ".." ".env.local"
$envTemplateFile = Join-Path $PSScriptRoot ".." ".env.local.template"

if (-not (Test-Path $envFile)) {
    if (-not (Test-Path $envTemplateFile)) {
        Write-Error ".env.local.template not found at $envTemplateFile"
        exit 1
    }

    Write-Host "Creating .env.local from .env.local.template..." -ForegroundColor Cyan
    Copy-Item $envTemplateFile $envFile
    Write-Host "  .env.local created" -ForegroundColor Green
}

Write-Host "Updating .env.local..." -ForegroundColor Cyan
$content = Get-Content $envFile -Raw
$content = $content -replace 'AZURE_TENANT_ID=.*', "AZURE_TENANT_ID=$tenantId"
$content = $content -replace 'AZURE_CLIENT_ID=.*', "AZURE_CLIENT_ID=$appId"
$content = $content -replace 'AZURE_CLIENT_SECRET=.*', "AZURE_CLIENT_SECRET=$clientSecret"
Set-Content $envFile -Value $content -NoNewline
Write-Host "  .env.local updated" -ForegroundColor Green

Write-Host "To generate local.settings.json for local Azure Functions runs:" -ForegroundColor Cyan
Write-Host "  npm run generate-local-settings" -ForegroundColor Green

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
Write-Host "║  IMPORTANT: Admin consent required!                          ║" -ForegroundColor Red
Write-Host "║                                                              ║" -ForegroundColor Red
Write-Host "║  A Global Admin must grant admin consent:                    ║" -ForegroundColor Red
Write-Host "║  1. Go to https://entra.microsoft.com                       ║" -ForegroundColor Red
Write-Host "║  2. App registrations > '$AppName'                          ║" -ForegroundColor Red
Write-Host "║  3. API permissions > Grant admin consent                    ║" -ForegroundColor Red
Write-Host "║                                                              ║" -ForegroundColor Red
Write-Host "║  Or run (requires Global Admin):                             ║" -ForegroundColor Red
Write-Host "║  az ad app permission admin-consent --id $appId              ║" -ForegroundColor Red
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
Write-Host ""
