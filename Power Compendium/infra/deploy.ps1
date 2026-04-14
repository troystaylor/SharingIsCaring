<#
.SYNOPSIS
    Deploys Power Compendium to Azure.

.DESCRIPTION
    Provisions Azure infrastructure via Bicep, builds and pushes the container image,
    assigns RBAC for Azure OpenAI, configures EasyAuth, and verifies the deployment.

.PARAMETER ResourceGroup
    Name of the Azure resource group (created if it doesn't exist).

.PARAMETER Location
    Azure region. Default: westus2.

.PARAMETER OpenAiResourceGroup
    Resource group containing the Azure OpenAI resource.

.PARAMETER OpenAiAccountName
    Name of the existing Azure OpenAI resource.

.PARAMETER OpenAiDeploymentName
    Chat completion model deployment name. Default: gpt-4o.

.PARAMETER SkipInfra
    Skip Bicep deployment (use when infra already exists).

.PARAMETER SkipBuild
    Skip container image build (use when image already exists).

.PARAMETER ImageTag
    Container image tag. Default: latest.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-power-compendium -OpenAiResourceGroup myResourceGroup -OpenAiAccountName my-openai
#>

param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $Location = "westus2",
    [Parameter(Mandatory)] [string] $OpenAiResourceGroup,
    [Parameter(Mandatory)] [string] $OpenAiAccountName,
    [string] $OpenAiDeploymentName = "gpt-4o",
    [switch] $SkipInfra,
    [switch] $SkipBuild,
    [string] $ImageTag = "latest"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$containerAppDir = Join-Path $scriptDir "container-app"
$bicepFile = Join-Path $scriptDir "infra\main.bicep"

Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Power Compendium — Azure Deployment" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════`n" -ForegroundColor Cyan

# ── Preflight ──

Write-Host "[1/8] Preflight checks..." -ForegroundColor Yellow
$account = az account show -o json | ConvertFrom-Json
Write-Host "  Subscription: $($account.name) ($($account.id))"
Write-Host "  User: $($account.user.name)"

$openAiEndpoint = "https://${OpenAiAccountName}.openai.azure.com/"
$openAi = az cognitiveservices account show --name $OpenAiAccountName -g $OpenAiResourceGroup -o json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Azure OpenAI resource '$OpenAiAccountName' not found in resource group '$OpenAiResourceGroup'."
    exit 1
}
Write-Host "  Azure OpenAI: $OpenAiAccountName ($openAiEndpoint)"

# ── Resource Group ──

Write-Host "`n[2/8] Resource group..." -ForegroundColor Yellow
az group create --name $ResourceGroup --location $Location -o none
Write-Host "  $ResourceGroup in $Location"

# ── Bicep Deployment ──

if (-not $SkipInfra) {
    Write-Host "`n[3/8] Deploying infrastructure (Bicep)..." -ForegroundColor Yellow
    $deployment = az deployment group create `
        --resource-group $ResourceGroup `
        --template-file $bicepFile `
        --parameters openAiEndpoint=$openAiEndpoint openAiDeploymentName=$OpenAiDeploymentName `
        --query "properties.outputs" -o json | ConvertFrom-Json

    $acrName = $deployment.acrName.value
    $acrLoginServer = $deployment.acrLoginServer.value
    $acaAppName = $deployment.acaAppName.value
    $acaFqdn = $deployment.acaFqdn.value
    $acaPrincipalId = $deployment.acaPrincipalId.value
    $storageName = $deployment.storageAccountName.value

    Write-Host "  ACR: $acrLoginServer"
    Write-Host "  ACA: https://$acaFqdn"
    Write-Host "  Storage: $storageName"
} else {
    Write-Host "`n[3/8] Skipping infrastructure (--SkipInfra)" -ForegroundColor DarkGray
    $acrName = az acr list -g $ResourceGroup --query "[0].name" -o tsv
    $acrLoginServer = az acr show --name $acrName --query loginServer -o tsv
    $acaAppName = "compendium-api"
    $acaFqdn = az containerapp show --name $acaAppName -g $ResourceGroup --query "properties.configuration.ingress.fqdn" -o tsv
    $acaPrincipalId = az containerapp identity show --name $acaAppName -g $ResourceGroup --query principalId -o tsv
    $storageName = az storage account list -g $ResourceGroup --query "[0].name" -o tsv
}

# ── Build & Push Container Image ──

$imageName = "${acrLoginServer}/compendium-api:${ImageTag}"

if (-not $SkipBuild) {
    Write-Host "`n[4/8] Building container image..." -ForegroundColor Yellow
    Push-Location $containerAppDir
    az acr build --registry $acrName --image "compendium-api:${ImageTag}" --file Dockerfile . 2>&1 | Select-String "Successfully|Run ID|error"
    Pop-Location
    Write-Host "  Image: $imageName"
} else {
    Write-Host "`n[4/8] Skipping build (--SkipBuild)" -ForegroundColor DarkGray
}

# ── Update Container App Image ──

Write-Host "`n[5/8] Deploying container..." -ForegroundColor Yellow
az containerapp update --name $acaAppName -g $ResourceGroup --image $imageName -o none
Write-Host "  Deployed: $imageName"

# ── Azure OpenAI RBAC ──

Write-Host "`n[6/8] Assigning Azure OpenAI RBAC..." -ForegroundColor Yellow
$openAiId = az cognitiveservices account show --name $OpenAiAccountName -g $OpenAiResourceGroup --query id -o tsv
az role assignment create `
    --assignee-object-id $acaPrincipalId `
    --assignee-principal-type ServicePrincipal `
    --role "Cognitive Services OpenAI User" `
    --scope $openAiId -o none 2>&1 | Out-Null
Write-Host "  Cognitive Services OpenAI User assigned"

# ── Verify Storage Access ──

Write-Host "`n[7/8] Verifying storage configuration..." -ForegroundColor Yellow
$publicAccess = az storage account show --name $storageName -g $ResourceGroup --query publicNetworkAccess -o tsv
if ($publicAccess -ne "Enabled") {
    Write-Host "  WARNING: publicNetworkAccess is '$publicAccess' — re-enabling..." -ForegroundColor Red
    az storage account update --name $storageName -g $ResourceGroup --public-network-access Enabled -o none
    Write-Host "  Fixed: publicNetworkAccess set to Enabled"
} else {
    Write-Host "  publicNetworkAccess: Enabled (OK)"
}

# ── EasyAuth Setup ──

Write-Host "`n[8/8] Configuring EasyAuth..." -ForegroundColor Yellow
$existingAuth = az containerapp auth microsoft show --name $acaAppName -g $ResourceGroup -o json 2>&1
if ($existingAuth -eq "{}") {
    Write-Host "  No Entra ID provider configured."
    Write-Host "  To enable EasyAuth, run:" -ForegroundColor DarkGray
    Write-Host "    1. Create an app registration:" -ForegroundColor DarkGray
    Write-Host "       az ad app create --display-name 'Power Compendium API' --sign-in-audience AzureADMyOrg" -ForegroundColor DarkGray
    Write-Host "    2. Configure the provider:" -ForegroundColor DarkGray
    Write-Host "       az containerapp auth microsoft update --name $acaAppName -g $ResourceGroup --client-id <APP_ID> --client-secret <SECRET> --tenant-id <TENANT_ID> --yes" -ForegroundColor DarkGray
    Write-Host "    3. Enable auth:" -ForegroundColor DarkGray
    Write-Host "       az containerapp auth update --name $acaAppName -g $ResourceGroup --unauthenticated-client-action Return401 --enabled true" -ForegroundColor DarkGray
} else {
    Write-Host "  Entra ID provider already configured"
    az containerapp auth update --name $acaAppName -g $ResourceGroup --enabled true -o none 2>&1
    Write-Host "  EasyAuth enabled"
}

# ── Summary ──

Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Deployment Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "  API:     https://$acaFqdn"
Write-Host "  REST:    https://$acaFqdn/api/book/pages"
Write-Host "  MCP:     https://$acaFqdn/api/mcp"
Write-Host "  ACR:     $acrLoginServer"
Write-Host "  Storage: $storageName"
Write-Host ""
Write-Host "  Test (no auth):" -ForegroundColor DarkGray
Write-Host "    curl https://$acaFqdn/api/book/pages?scope=org" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Test (with auth):" -ForegroundColor DarkGray
Write-Host "    `$t = az account get-access-token --scope 'api://<CLIENT_ID>/.default' --query accessToken -o tsv" -ForegroundColor DarkGray
Write-Host "    Invoke-WebRequest https://$acaFqdn/api/book/pages?scope=org -Headers @{Authorization=`"Bearer `$t`"}" -ForegroundColor DarkGray
Write-Host ""
