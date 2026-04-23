param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$Location,

    [Parameter(Mandatory = $true)]
    [string]$FoundryEndpoint,

    [string]$FoundryModel = "phi-4",
    [string]$WorkloadName = "decisionduck",
    [string]$FoundryApiKey = "",
    [string]$ContainerImage = "",
    [string]$AcrName = ""
)

$ErrorActionPreference = "Stop"

function Test-CommandAvailable {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

Test-CommandAvailable -Name "az"

Write-Host "Setting Azure subscription..." -ForegroundColor Cyan
az account set --subscription $SubscriptionId | Out-Null

Write-Host "Ensuring resource group exists..." -ForegroundColor Cyan
az group create --name $ResourceGroupName --location $Location | Out-Null

if ([string]::IsNullOrWhiteSpace($ContainerImage)) {
    if ([string]::IsNullOrWhiteSpace($AcrName)) {
        $AcrName = ($WorkloadName -replace '[^a-zA-Z0-9]', '').ToLower()
        if ($AcrName.Length -lt 5) {
            $AcrName = ($AcrName + "duckacr").Substring(0, [Math]::Min(($AcrName + "duckacr").Length, 15))
        }

        $suffix = [Math]::Abs($ResourceGroupName.GetHashCode()).ToString().Substring(0, 4)
        $AcrName = ($AcrName + $suffix).ToLower()
        if ($AcrName.Length -gt 50) {
            $AcrName = $AcrName.Substring(0, 50)
        }
    }

    Write-Host "Ensuring Azure Container Registry '$AcrName' exists..." -ForegroundColor Cyan
    $acrExists = az acr show --name $AcrName --resource-group $ResourceGroupName --query name -o tsv 2>$null

    if (-not $acrExists) {
        az acr create --name $AcrName --resource-group $ResourceGroupName --sku Basic --admin-enabled true --location $Location | Out-Null
    }
    else {
        az acr update --name $AcrName --resource-group $ResourceGroupName --admin-enabled true | Out-Null
    }

    $acrLoginServer = az acr show --name $AcrName --resource-group $ResourceGroupName --query loginServer -o tsv
    $ContainerImage = "$acrLoginServer/decisionduck-mcp:latest"

    $projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $dockerfilePath = Join-Path $projectRoot "mcp-server\Dockerfile"

    if (-not (Test-Path $dockerfilePath)) {
        throw "Dockerfile was not found at $dockerfilePath"
    }

    Write-Host "Building and pushing container image to ACR..." -ForegroundColor Cyan
    az acr build --registry $AcrName --resource-group $ResourceGroupName --image "decisionduck-mcp:latest" "$projectRoot\mcp-server" | Out-Null

    $acrCreds = az acr credential show --name $AcrName --resource-group $ResourceGroupName -o json | ConvertFrom-Json
    $acrUsername = $acrCreds.username
    $acrPassword = $acrCreds.passwords[0].value
}
else {
    Write-Host "Using provided container image: $ContainerImage" -ForegroundColor Yellow
    $acrLoginServer = ""
    $acrUsername = ""
    $acrPassword = ""
}

$deploymentName = "decisionduck-$(Get-Date -Format 'yyyyMMddHHmmss')"

Write-Host "Deploying infrastructure with Bicep..." -ForegroundColor Cyan
az deployment group create `
  --name $deploymentName `
  --resource-group $ResourceGroupName `
  --template-file "$PSScriptRoot\main.bicep" `
  --parameters location=$Location `
               workloadName=$WorkloadName `
               containerImage=$ContainerImage `
               foundryEndpoint=$FoundryEndpoint `
               foundryModel=$FoundryModel `
               foundryApiKey=$FoundryApiKey `
               acrServer=$acrLoginServer `
               acrUsername=$acrUsername `
               acrPassword=$acrPassword | Out-Null

Write-Host "Fetching deployment outputs..." -ForegroundColor Cyan
$outputs = az deployment group show `
  --name $deploymentName `
  --resource-group $ResourceGroupName `
  --query properties.outputs -o json | ConvertFrom-Json

$mcpEndpoint = $outputs.mcpEndpoint.value
$healthEndpoint = $outputs.healthEndpoint.value
$managedIdentityResourceId = $outputs.managedIdentityResourceId.value

if ([string]::IsNullOrWhiteSpace($FoundryApiKey)) {
    Write-Host "No Foundry API key provided; attempting managed-identity role assignment..." -ForegroundColor Cyan

    $foundryAccounts = az cognitiveservices account list -o json | ConvertFrom-Json
    $normalizedFoundryEndpoint = $FoundryEndpoint.TrimEnd('/').ToLowerInvariant()
    $foundryResource = @(
        $foundryAccounts | Where-Object {
            $endpoint = $_.properties.endpoint
            -not [string]::IsNullOrWhiteSpace($endpoint) -and $endpoint.TrimEnd('/').ToLowerInvariant() -eq $normalizedFoundryEndpoint
        }
    )

    if ($foundryResource -and $foundryResource.Count -gt 0) {
        $foundryResourceId = $foundryResource[0].id
        $miPrincipalId = az identity show --ids $managedIdentityResourceId --query principalId -o tsv

        if (-not [string]::IsNullOrWhiteSpace($miPrincipalId)) {
            $existing = az role assignment list --assignee-object-id $miPrincipalId --scope $foundryResourceId --query "[?roleDefinitionName=='Cognitive Services OpenAI User'] | length(@)" -o tsv

            if ($existing -eq "0") {
                az role assignment create --assignee-object-id $miPrincipalId --role "Cognitive Services OpenAI User" --scope $foundryResourceId | Out-Null
                Write-Host "Assigned 'Cognitive Services OpenAI User' role to managed identity." -ForegroundColor Green
            }
            else {
                Write-Host "Managed identity already has OpenAI User role on target resource." -ForegroundColor Yellow
            }
        }
    }
    else {
        Write-Host "Could not resolve Foundry resource by endpoint; skipping role assignment." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Deployment complete." -ForegroundColor Green
Write-Host "Container image: $ContainerImage" -ForegroundColor Yellow
Write-Host "MCP endpoint: $mcpEndpoint" -ForegroundColor Yellow
Write-Host "Health endpoint: $healthEndpoint" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next step: update ../agent-package/plugin.json spec.url with this MCP endpoint." -ForegroundColor Cyan
