<#
.SYNOPSIS
    Deploys a federated MCP connector to Azure Container Apps.

.DESCRIPTION
    Builds the container image, pushes to Azure Container Registry,
    and deploys to Azure Container Apps using Bicep.

.PARAMETER ConnectorName
    Unique name for this connector (used in all resource names).

.PARAMETER ResourceGroup
    Azure resource group name.

.PARAMETER Location
    Azure region (default: westus2).

.PARAMETER RegistryName
    Azure Container Registry name (without .azurecr.io).

.PARAMETER TenantId
    Microsoft Entra ID tenant ID.

.PARAMETER AppClientId
    App registration client ID for JWT audience validation.

.PARAMETER UpstreamBaseUrl
    Base URL of the upstream API this connector proxies.

.EXAMPLE
    .\deploy.ps1 -ConnectorName "hubspot" -ResourceGroup "rg-mcp-connectors" `
        -RegistryName "mcpregistry" -TenantId "00000000-..." -AppClientId "11111111-..."
#>

param(
    [Parameter(Mandatory)] [string] $ConnectorName,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $Location = "westus2",
    [Parameter(Mandatory)] [string] $RegistryName,
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $AppClientId,
    [string] $UpstreamBaseUrl = "https://api.example.com"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

# ── Git identity ─────────────────────────────────────────────────────────────
git config user.name "Troy Simeon Taylor"
git config user.email "44444967+troystaylor@users.noreply.github.com"

# ── Step 1: Login ────────────────────────────────────────────────────────────
Write-Host "`n── Step 1: Verify Azure login ──" -ForegroundColor Cyan
$account = az account show --query "{subscription:name, tenant:tenantId}" -o json | ConvertFrom-Json
Write-Host "  Subscription: $($account.subscription)"
Write-Host "  Tenant:       $($account.tenant)"

# ── Step 2: Ensure resource group ────────────────────────────────────────────
Write-Host "`n── Step 2: Ensure resource group ──" -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --output none
Write-Host "  Resource group: $ResourceGroup ($Location)"

# ── Step 3: Ensure container registry ────────────────────────────────────────
Write-Host "`n── Step 3: Ensure container registry ──" -ForegroundColor Cyan
$registryExists = az acr show --name $RegistryName --query "name" -o tsv 2>$null
if (-not $registryExists) {
    Write-Host "  Creating registry: $RegistryName"
    az acr create --resource-group $ResourceGroup --name $RegistryName --sku Basic --output none
} else {
    Write-Host "  Registry exists: $RegistryName"
}

# ── Step 4: Build and push container image ───────────────────────────────────
Write-Host "`n── Step 4: Build and push container image ──" -ForegroundColor Cyan
$imageTag = "$RegistryName.azurecr.io/${ConnectorName}-mcp:latest"
Write-Host "  Image: $imageTag"

az acr build `
    --registry $RegistryName `
    --image "${ConnectorName}-mcp:latest" `
    --file "$projectDir/Dockerfile" `
    "$projectDir"

# ── Step 5: Deploy infrastructure ────────────────────────────────────────────
Write-Host "`n── Step 5: Deploy infrastructure ──" -ForegroundColor Cyan
$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$scriptDir/main.bicep" `
    --parameters `
        connectorName=$ConnectorName `
        containerImage=$imageTag `
        registryName=$RegistryName `
        tenantId=$TenantId `
        appClientId=$AppClientId `
        upstreamBaseUrl=$UpstreamBaseUrl `
    --query "properties.outputs" -o json | ConvertFrom-Json

$mcpBaseUrl = $deployment.mcpBaseUrl.value
$principalId = $deployment.identityPrincipalId.value

# ── Step 6: Grant ACR pull to managed identity ───────────────────────────────
Write-Host "`n── Step 6: Grant ACR pull access ──" -ForegroundColor Cyan
$registryId = az acr show --name $RegistryName --query "id" -o tsv
az role assignment create `
    --assignee-object-id $principalId `
    --assignee-principal-type ServicePrincipal `
    --role "AcrPull" `
    --scope $registryId `
    --output none
Write-Host "  Granted AcrPull to managed identity"

# ── Done ─────────────────────────────────────────────────────────────────────
Write-Host "`n── Deployment complete ──" -ForegroundColor Green
Write-Host ""
Write-Host "  MCP Base URL:  $mcpBaseUrl" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Cyan
Write-Host "  1. Register auth in Teams Developer Portal (https://dev.teams.microsoft.com)"
Write-Host "     - For Entra SSO: Tools > OAuth Client Registration > SSO"
Write-Host "     - For OAuth 2.0: Tools > OAuth Client Registration > OAuth"
Write-Host "  2. Create federated connector in M365 admin center (https://admin.microsoft.com)"
Write-Host "     - Copilot > Connectors > Gallery > Create a new connector"
Write-Host "     - Base URL: $mcpBaseUrl"
Write-Host "     - Enter your registration ID from step 1"
Write-Host "  3. Stage rollout to test users before deploying to all"
Write-Host ""
