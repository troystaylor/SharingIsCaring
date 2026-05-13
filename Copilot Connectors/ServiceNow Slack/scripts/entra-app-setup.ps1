# ── Entra App Registration for ServiceNow Slack Copilot Connector ──
# Creates an app registration with Graph external connector permissions.

param(
    [string]$AppName = "ServiceNow Slack Copilot Connector",
    [string]$EnvFile = "../.env.local"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== Creating Entra App Registration ===" -ForegroundColor Cyan

# Check if template exists and create .env.local from it if needed
$templateFile = Join-Path $PSScriptRoot "../.env.local.template"
$envFilePath = Join-Path $PSScriptRoot $EnvFile

if (-not (Test-Path $envFilePath) -and (Test-Path $templateFile)) {
    Copy-Item $templateFile $envFilePath
    Write-Host "Created .env.local from template"
}

# Create the app registration
$app = az ad app create `
    --display-name $AppName `
    --sign-in-audience "AzureADMyOrg" `
    --output json | ConvertFrom-Json

$appId = $app.appId
$objectId = $app.id

Write-Host "App created: $AppName"
Write-Host "  Client ID: $appId"
Write-Host "  Object ID: $objectId"

# Add Graph API permissions
# ExternalConnection.ReadWrite.OwnedBy = f431331c-49a6-499f-be1c-62af19c34a9d
# ExternalItem.ReadWrite.OwnedBy = 8116ae0f-55c2-452d-9571-d9b8cdc97987
$graphResourceId = "00000003-0000-0000-c000-000000000000"

az ad app permission add `
    --id $appId `
    --api $graphResourceId `
    --api-permissions "f431331c-49a6-499f-be1c-62af19c34a9d=Role" `
    2>$null

az ad app permission add `
    --id $appId `
    --api $graphResourceId `
    --api-permissions "8116ae0f-55c2-452d-9571-d9b8cdc97987=Role" `
    2>$null

Write-Host "  Permissions added: ExternalConnection.ReadWrite.OwnedBy, ExternalItem.ReadWrite.OwnedBy"

# Create client secret
$secret = az ad app credential reset `
    --id $appId `
    --display-name "connector-secret" `
    --years 2 `
    --output json | ConvertFrom-Json

$clientSecret = $secret.password
$tenantId = $secret.tenant

Write-Host "  Client secret created (2-year expiry)"

# Update .env.local
if (Test-Path $envFilePath) {
    $content = Get-Content $envFilePath -Raw
    $content = $content -replace 'AZURE_TENANT_ID=.*', "AZURE_TENANT_ID=$tenantId"
    $content = $content -replace 'AZURE_CLIENT_ID=.*', "AZURE_CLIENT_ID=$appId"
    $content = $content -replace 'AZURE_CLIENT_SECRET=.*', "AZURE_CLIENT_SECRET=$clientSecret"
    Set-Content $envFilePath $content
    Write-Host "  Updated .env.local with Entra credentials"
}

Write-Host "`n=== Admin Consent Required ===" -ForegroundColor Yellow
Write-Host "A Global Admin must grant consent. Use this URL:"
Write-Host "  https://login.microsoftonline.com/$tenantId/adminconsent?client_id=$appId"
Write-Host ""

# Configure git identity per repo instructions
git config user.name "Troy Simeon Taylor"
git config user.email "44444967+troystaylor@users.noreply.github.com"
