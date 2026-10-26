<#
.SYNOPSIS
    Provisions the infrastructure, builds the image in ACR, and rolls the container app to it.

.DESCRIPTION
    1. Deploys infra/main.bicep with the current image (the quickstart placeholder on first run).
    2. Builds the Dockerfile in Azure Container Registry. No local Docker is needed.
    3. Deploys again with the new image, which switches the probes to /healthz and /ready.

    Re-running is safe: an existing app keeps its current image through step 1.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-copilot-studio-mcp -TenantId <guid> -ClientId <guid>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $ClientId,
    [string] $Location = 'eastus2',
    [string] $AppName = 'cstudiomcp',
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
            appName            = @{ value = $AppName }
            containerImage     = @{ value = $image }
            authTenantId       = @{ value = $TenantId }
            authClientId       = @{ value = $ClientId }
            allowedIpRanges    = @{ value = @($AllowedIpRanges) }
            corsAllowedOrigins = @{ value = @($CorsAllowedOrigins) }
            minReplicas        = @{ value = $MinReplicas }
        }
    }

    # Parameters go through a temp file rather than the command line.
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

# The server proves it is the Entra app with its managed identity's token, so no client secret exists. The app
# trusts that identity through a federated credential. This is idempotent and safe to re-run.
$principalId = $outputs.managedIdentityPrincipalId.value
$credentialName = "$AppName-managed-identity"
$existing = Invoke-Az ad app federated-credential list --id $ClientId --query "[?name=='$credentialName'].subject" -o tsv
if ($existing -ne $principalId) {
    if ($existing) { Invoke-Az ad app federated-credential delete --id $ClientId --federated-credential-id $credentialName | Out-Null }
    $credential = @{
        name      = $credentialName
        issuer    = "https://login.microsoftonline.com/$TenantId/v2.0"
        subject   = $principalId
        audiences = @('api://AzureADTokenExchange')
    }
    $file = Join-Path ([System.IO.Path]::GetTempPath()) "$([guid]::NewGuid()).json"
    try {
        $credential | ConvertTo-Json | Set-Content -Path $file -Encoding utf8
        Write-Host "Trusting the managed identity on app $ClientId" -ForegroundColor Cyan
        Invoke-Az ad app federated-credential create --id $ClientId --parameters "@$file" | Out-Null
    }
    finally {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}
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

