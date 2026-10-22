<#
.SYNOPSIS
    Provisions the infrastructure, builds the image in ACR, and rolls the container app to it.

.DESCRIPTION
    1. Deploys infra/main.bicep with the current image (the quickstart placeholder on first run).
    2. Builds the Dockerfile in Azure Container Registry. No local Docker is needed.
    3. Deploys again with the new image, which switches the probes to /healthz and /ready.

    Re-running is safe: an existing app keeps its current image through step 1.

    There are no secrets to supply. The server holds no Atlassian or Bitbucket credential: each caller signs in
    and the server uses their own access token.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-atlassian-mcp -AtlassianSiteUrl https://acme.atlassian.net

.EXAMPLE
    .\deploy.ps1 -ResourceGroup rg-bitbucket-mcp -DisableJira -DisableConfluence
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [ValidatePattern('^https://[^/]+$')] [string] $AtlassianSiteUrl,
    [string] $Location = 'eastus2',
    [string] $AppName = 'atlmcp',
    [switch] $DisableJira,
    [switch] $DisableConfluence,
    [switch] $DisableBitbucket,
    [switch] $EnableJiraWriteTools,
    [switch] $EnableConfluenceWriteTools,
    [switch] $EnableBitbucketWriteTools,
    [string] $BitbucketWorkspace = '',
    [string[]] $AllowedIpRanges = @(),
    [string[]] $CorsAllowedOrigins = @(),
    [int] $MinReplicas = 1,
    [string] $ImageTag = (Get-Date -Format 'yyyyMMddHHmmss')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bicep = Join-Path $PSScriptRoot 'main.bicep'
$placeholder = 'mcr.microsoft.com/k8se/quickstart:latest'

if ($DisableJira -and $DisableConfluence -and $DisableBitbucket) {
    throw 'Leave at least one of Jira, Confluence, or Bitbucket enabled.'
}

if (-not ($DisableJira -and $DisableConfluence) -and [string]::IsNullOrWhiteSpace($AtlassianSiteUrl)) {
    throw 'Pass -AtlassianSiteUrl (for example https://acme.atlassian.net) when Jira or Confluence is enabled.'
}

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
            atlassianSiteUrl   = @{ value = [string] $AtlassianSiteUrl }
            enableJira         = @{ value = -not $DisableJira }
            enableConfluence   = @{ value = -not $DisableConfluence }
            enableBitbucket    = @{ value = -not $DisableBitbucket }
            enableJiraWriteTools       = @{ value = [bool] $EnableJiraWriteTools }
            enableConfluenceWriteTools = @{ value = [bool] $EnableConfluenceWriteTools }
            enableBitbucketWriteTools  = @{ value = [bool] $EnableBitbucketWriteTools }
            bitbucketWorkspace = @{ value = $BitbucketWorkspace }
            allowedIpRanges    = @{ value = @($AllowedIpRanges) }
            corsAllowedOrigins = @{ value = @($CorsAllowedOrigins) }
            minReplicas        = @{ value = $MinReplicas }
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
foreach ($name in 'jiraEndpoint', 'confluenceEndpoint', 'bitbucketEndpoint') {
    if ($outputs.$name.value) { Write-Host ("{0,-22} {1}" -f $name, $outputs.$name.value) -ForegroundColor Green }
}

$fqdn = $outputs.containerAppFqdn.value
if ($outputs.atlassianAuthorizationUrl.value) {
    Write-Host ("{0,-22} {1}" -f 'atlassianAuthorizeUrl', $outputs.atlassianAuthorizationUrl.value) -ForegroundColor Green
    Write-Host '  Use this as the authorization URL in your Teams or Power Platform sign-in. Atlassian requires an audience parameter that those clients cannot send; this endpoint adds it.'
}
Write-Host "Health check: https://$fqdn/healthz"
Write-Host "Readiness (reports Degraded if a provider is unreachable): https://$fqdn/ready"
foreach ($path in @(@{ On = -not $DisableJira; P = '/jira/mcp' }, @{ On = -not $DisableConfluence; P = '/confluence/mcp' }, @{ On = -not $DisableBitbucket; P = '/bitbucket/mcp' })) {
    if ($path.On) { Write-Host "Resource metadata: https://$fqdn/.well-known/oauth-protected-resource$($path.P)" }
}
