<#
.SYNOPSIS
    Deploys OpenDataLoader PDF API to Azure Container Apps.

.DESCRIPTION
    Provisions Azure infrastructure via Bicep, builds and pushes the container image,
    and optionally configures EasyAuth (Entra ID).

.PARAMETER ResourceGroup
    Name of the Azure resource group (created if it doesn't exist).

.PARAMETER Location
    Azure region. Default: westus2.

.PARAMETER ApiKey
    API key for the service. Auto-generated if not provided.

.PARAMETER SkipInfra
    Skip Bicep deployment (use when infra already exists).

.PARAMETER SkipBuild
    Skip container image build (use when image already exists).

.PARAMETER ImageTag
    Container image tag. Default: latest.

.PARAMETER UseGhcrImage
    Use the pre-built image from ghcr.io instead of building in ACR.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-opendataloader

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-opendataloader -ApiKey "my-secret-key" -UseGhcrImage
#>

param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $Location = "westus2",
    [string] $ApiKey = "",
    [switch] $SkipInfra,
    [switch] $SkipBuild,
    [string] $ImageTag = "latest",
    [switch] $UseGhcrImage
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$containerAppDir = Join-Path (Split-Path $scriptDir) "container-app"
$bicepFile = Join-Path $scriptDir "main.bicep"

Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  OpenDataLoader PDF — Azure Deployment" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════`n" -ForegroundColor Cyan

# ── Step 1: Preflight ──

Write-Host "[1/6] Preflight checks..." -ForegroundColor Yellow
$account = az account show -o json | ConvertFrom-Json
Write-Host "  Subscription: $($account.name) ($($account.id))"
Write-Host "  User: $($account.user.name)"

# Generate API key if not provided
if (-not $ApiKey) {
    $ApiKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
    Write-Host "  Generated API key: $ApiKey" -ForegroundColor Green
    Write-Host "  (Save this — you'll need it for the connector)" -ForegroundColor Green
} else {
    Write-Host "  Using provided API key"
}

# ── Step 2: Resource Group ──

Write-Host "`n[2/6] Resource group..." -ForegroundColor Yellow
az group create --name $ResourceGroup --location $Location -o none
Write-Host "  $ResourceGroup in $Location"

# ── Step 3: Bicep Deployment ──

if (-not $SkipInfra) {
    Write-Host "`n[3/6] Deploying infrastructure (Bicep)..." -ForegroundColor Yellow
    $deployment = az deployment group create `
        --resource-group $ResourceGroup `
        --template-file $bicepFile `
        --parameters apiKey=$ApiKey `
        --query "properties.outputs" -o json | ConvertFrom-Json

    $acrName = $deployment.acrName.value
    $acrLoginServer = $deployment.acrLoginServer.value
    $acaAppName = $deployment.acaAppName.value
    $acaFqdn = $deployment.acaFqdn.value
    $appInsightsKey = $deployment.appInsightsInstrumentationKey.value

    Write-Host "  ACR: $acrLoginServer"
    Write-Host "  ACA: https://$acaFqdn"
    Write-Host "  App Insights Key: $appInsightsKey"
} else {
    Write-Host "`n[3/6] Skipping infrastructure (--SkipInfra)" -ForegroundColor DarkGray
    $acrName = az acr list -g $ResourceGroup --query "[0].name" -o tsv
    $acrLoginServer = az acr show --name $acrName --query loginServer -o tsv
    $acaAppName = "opendataloader-pdf-api"
    $acaFqdn = az containerapp show --name $acaAppName -g $ResourceGroup --query "properties.configuration.ingress.fqdn" -o tsv
}

# ── Step 4: Build & Push Container Image ──

if ($UseGhcrImage) {
    $imageName = "ghcr.io/troystaylor/opendataloader-pdf-api:${ImageTag}"
    Write-Host "`n[4/6] Using pre-built GHCR image..." -ForegroundColor Yellow
    Write-Host "  Image: $imageName"
} elseif (-not $SkipBuild) {
    $imageName = "${acrLoginServer}/opendataloader-pdf-api:${ImageTag}"
    Write-Host "`n[4/6] Building container image..." -ForegroundColor Yellow
    Push-Location $containerAppDir
    az acr build --registry $acrName --image "opendataloader-pdf-api:${ImageTag}" --file Dockerfile . 2>&1 | Select-String "Successfully|Run ID|error"
    Pop-Location
    Write-Host "  Image: $imageName"
} else {
    $imageName = "${acrLoginServer}/opendataloader-pdf-api:${ImageTag}"
    Write-Host "`n[4/6] Skipping build (--SkipBuild)" -ForegroundColor DarkGray
}

# ── Step 5: Update Container App ──

Write-Host "`n[5/6] Updating container app..." -ForegroundColor Yellow
az containerapp update `
    --name $acaAppName `
    --resource-group $ResourceGroup `
    --image $imageName `
    -o none
Write-Host "  Updated to: $imageName"

# ── Step 6: Verify ──

Write-Host "`n[6/6] Verifying deployment..." -ForegroundColor Yellow
$healthUrl = "https://${acaFqdn}/health"
Write-Host "  Health check: $healthUrl"

$retries = 0
$maxRetries = 12
$healthy = $false
while ($retries -lt $maxRetries -and -not $healthy) {
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 10 -ErrorAction SilentlyContinue
        if ($response.status -eq "healthy") {
            $healthy = $true
        }
    } catch {
        $retries++
        if ($retries -lt $maxRetries) {
            Write-Host "  Waiting for container to start... ($retries/$maxRetries)" -ForegroundColor DarkGray
            Start-Sleep -Seconds 10
        }
    }
}

if ($healthy) {
    Write-Host "  Service is healthy!" -ForegroundColor Green
} else {
    Write-Host "  Service did not respond to health check (may still be starting)" -ForegroundColor DarkYellow
}

# ── Summary ──

Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Deployment Complete" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Service URL:  https://$acaFqdn" -ForegroundColor Green
Write-Host "  API Key:      $ApiKey" -ForegroundColor Green
Write-Host "  MCP Endpoint: https://$acaFqdn/mcp" -ForegroundColor Green
Write-Host ""
Write-Host "  Connector Setup:" -ForegroundColor Yellow
Write-Host "    1. Update apiDefinition.swagger.json host to: $acaFqdn"
Write-Host "    2. Deploy connector: pac connector create --settings-file apiProperties.json --api-definition apiDefinition.swagger.json --script script.csx"
Write-Host "    3. Create connection using API key: $ApiKey"
if ($appInsightsKey) {
    Write-Host ""
    Write-Host "  App Insights:" -ForegroundColor Yellow
    Write-Host "    Update script.csx APP_INSIGHTS_KEY to: $appInsightsKey"
}
Write-Host ""
