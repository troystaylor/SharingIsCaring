<#
.SYNOPSIS
    Provisions the infrastructure, builds the image in ACR, and rolls the container app to it.

.DESCRIPTION
    1. Deploys infra/main.bicep with the current image (the quickstart placeholder on first run).
    2. Builds the Dockerfile in Azure Container Registry. No local Docker is needed.
    3. Deploys again with the new image, which switches the probes to /healthz and /ready.

    Re-running is safe: an existing app keeps its current image through step 1.

    There are no secrets to supply. The server holds no Salesforce credential: each caller signs in to
    Salesforce and the server uses their own access token.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-sf-mcp -SalesforceInstanceUrl https://acme.my.salesforce.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [ValidatePattern('^https://[^/]+$')] [string] $SalesforceInstanceUrl,
    [string] $Location = 'eastus2',
    [string] $AppName = 'sfmcp',
    [string] $SalesforceApiVersion = 'v66.0',
    [switch] $EnableWriteTools,
    [switch] $DisableToolingTools,
    [ValidateRange(0, 86400)] [int] $SchemaCacheSeconds = 300,
    [string[]] $AllowedIpRanges = @(),
    [string[]] $CorsAllowedOrigins = @(),
    [int] $MinReplicas = 1,
    [string] $ImageTag = (Get-Date -Format 'yyyyMMddHHmmss')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bicep = Join-Path $PSScriptRoot 'main.bicep'
$placeholder = 'mcr.microsoft.com/k8se/quickstart:latest'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

function Deploy-Infra([string] $image) {
    $parameters = @{
        '$schema'      = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters     = @{
            appName               = @{ value = $AppName }
            containerImage        = @{ value = $image }
            salesforceInstanceUrl = @{ value = $SalesforceInstanceUrl }
            salesforceApiVersion  = @{ value = $SalesforceApiVersion }
            enableWriteTools      = @{ value = [bool] $EnableWriteTools }
            enableToolingTools    = @{ value = -not $DisableToolingTools }
            schemaCacheSeconds    = @{ value = $SchemaCacheSeconds }
            allowedIpRanges       = @{ value = @($AllowedIpRanges) }
            corsAllowedOrigins    = @{ value = @($CorsAllowedOrigins) }
            minReplicas           = @{ value = $MinReplicas }
        }
    }

    $file = Join-Path ([System.IO.Path]::GetTempPath()) "$([guid]::NewGuid()).json"
    try {
        $parameters | ConvertTo-Json -Depth 10 | Set-Content -Path $file -Encoding utf8
        Invoke-Az deployment group create --resource-group $ResourceGroup --template-file $bicep `
            --parameters "@$file" --query properties.outputs -o json | ConvertFrom-Json
    }
    finally {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Ensuring resource group $ResourceGroup in $Location" -ForegroundColor Cyan
Invoke-Az group create --name $ResourceGroup --location $Location --output none

$currentImage = & az containerapp show --name $AppName --resource-group $ResourceGroup `
    --query 'properties.template.containers[0].image' -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentImage)) { $currentImage = $placeholder }

Write-Host "Provisioning infrastructure (image: $currentImage)" -ForegroundColor Cyan
$outputs = Deploy-Infra $currentImage

$registry = $outputs.containerRegistryName.value
$loginServer = $outputs.containerRegistryEndpoint.value
$image = "$loginServer/${AppName}:$ImageTag"

Write-Host "Building $image in ACR" -ForegroundColor Cyan
Invoke-Az acr build --registry $registry --image "${AppName}:$ImageTag" --file (Join-Path $root 'Dockerfile') $root

Write-Host 'Rolling the container app to the new image' -ForegroundColor Cyan
$outputs = Deploy-Infra $image

Write-Host ''
Write-Host "MCP endpoint: $($outputs.mcpEndpoint.value)" -ForegroundColor Green
Write-Host "Health check: https://$($outputs.containerAppFqdn.value)/healthz"
Write-Host "Readiness (reports Degraded if Salesforce is unreachable): https://$($outputs.containerAppFqdn.value)/ready"
Write-Host "Resource metadata: https://$($outputs.containerAppFqdn.value)/.well-known/oauth-protected-resource/mcp"
