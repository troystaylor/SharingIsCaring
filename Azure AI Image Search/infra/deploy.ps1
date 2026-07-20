# Deploy Azure AI Image Search to ACA
# Prerequisites: az login, Docker, ACR access

param(
    [string]$ResourceGroup = "rg-ai-image-search",
    [string]$Location = "westus2",
    [string]$AcrName = "acacodeinterpreter",
    [string]$ImageTag = "latest"
)

$ErrorActionPreference = "Stop"
$ImageName = "ai-image-search"
$FullImage = "$AcrName.azurecr.io/${ImageName}:${ImageTag}"

Write-Host "=== Azure AI Image Search Deployment ===" -ForegroundColor Cyan

# 1. Build and push container image
Write-Host "`n[1/4] Building container image..." -ForegroundColor Yellow
Push-Location "$PSScriptRoot\..\server"
docker build -t $FullImage .
Pop-Location

Write-Host "[1/4] Pushing to ACR..." -ForegroundColor Yellow
az acr login --name $AcrName
docker push $FullImage

# 2. Create resource group
Write-Host "`n[2/4] Creating resource group..." -ForegroundColor Yellow
az group create --name $ResourceGroup --location $Location -o none

# 3. Generate API key
$ApiKey = [System.Guid]::NewGuid().ToString("N")
Write-Host "`n[3/4] Generated API key (save this): $ApiKey" -ForegroundColor Green

# 4. Deploy infrastructure
Write-Host "`n[4/4] Deploying Bicep template..." -ForegroundColor Yellow
$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot\main.bicep" `
    --parameters containerImage=$FullImage apiKey=$ApiKey `
    --query "properties.outputs" `
    -o json | ConvertFrom-Json

$AppUrl = $deployment.appUrl.value
$SearchEndpoint = $deployment.searchEndpoint.value

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Green
Write-Host "App URL:         $AppUrl"
Write-Host "Search Endpoint: $SearchEndpoint"
Write-Host "API Key:         $ApiKey"
Write-Host "`nNext steps:"
Write-Host "  1. Upload images to the blob container"
Write-Host "  2. Create a multimodal search index (see readme)"
Write-Host "  3. Update script.csx BACKEND_HOST with: $($AppUrl -replace 'https://','')"
Write-Host "  4. Deploy the connector with PAC CLI"
