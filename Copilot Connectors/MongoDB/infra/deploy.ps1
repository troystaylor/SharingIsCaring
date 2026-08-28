<#
.SYNOPSIS
    Builds, pushes, and deploys the MongoDB Copilot connector heads.

.DESCRIPTION
    Deploys two Azure Container Apps: the federated MCP server (scales to zero) and the
    indexer (always on, because the change stream tail holds an open cursor).

    Run this after editing config/profiles.json. Both containers validate profiles at
    startup and refuse to run if a collection is missing an Access rule or, when
    RequireUrl is true, a resolvable citation URL.

.EXAMPLE
    ./deploy.ps1 -ResourceGroup rg-mongo-copilot -RegistryName mongocopilotacr `
        -TenantId 00000000-0000-0000-0000-000000000000 `
        -AppClientId 11111111-1111-1111-1111-111111111111 `
        -MongoConnectionString 'mongodb+srv://reader:...@cluster.mongodb.net' `
        -MongoDatabase helpdesk -UiBaseUrl https://helpdesk.contoso.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $RegistryName,
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $AppClientId,
    [Parameter(Mandatory)] [string] $MongoConnectionString,
    [Parameter(Mandatory)] [string] $MongoDatabase,
    [Parameter(Mandatory)] [string] $UiBaseUrl,

    [ValidateSet('ConnectionString', 'ManagedIdentity')]
    [string] $MongoAuthMode = 'ConnectionString',

    # Required for Atlas managed identity (the Application ID URI backing workload identity
    # federation). Left empty for Azure DocumentDB, which has a documented default.
    [string] $MongoTokenResource = '',

    [string] $Name = 'mongo-copilot',
    [string] $Location = 'eastus',
    [string] $Tag = (Get-Date -Format 'yyyyMMddHHmmss'),
    [string] $InfrastructureSubnetId = '',
    [string[]] $PrivateDnsZoneIds = @(),

    # Set only to deliberately publish items with no citation URL.
    [switch] $AllowUncitedItems
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> Validating profiles" -ForegroundColor Cyan
$profilePath = Join-Path $root 'config/profiles.json'
if (-not (Test-Path $profilePath)) {
    throw "config/profiles.json not found. Copy config/profiles.example.json and edit it for your database."
}

# Azure Cosmos DB for MongoDB on the request-unit model does not emit change-stream delete
# events, so indexed content would keep returning documents deleted upstream. The apps
# refuse to start in that configuration; catching it here avoids a wasted image build.
if ($MongoConnectionString -match '\.mongo\.cosmos\.azure\.com') {
    $profileJson = Get-Content $profilePath -Raw | ConvertFrom-Json
    $indexed = $profileJson.Mongo.Collections.PSObject.Properties |
        Where-Object { $_.Value.Mode -in @('Indexed', 'Both') }

    if ($indexed) {
        throw ("The connection string points at Azure Cosmos DB for MongoDB (request-unit model), " +
               "which does not emit change-stream delete events. Collections " +
               "[$($indexed.Name -join ', ')] are set to Indexed or Both. Use Azure DocumentDB " +
               "(Cosmos DB for MongoDB vCore) or MongoDB Atlas, or set them to Mode 'Live'.")
    }
}

if ($MongoAuthMode -eq 'ManagedIdentity' -and $MongoConnectionString -match '@') {
    throw "MongoAuthMode is ManagedIdentity but the connection string contains credentials. Remove the username and password."
}

Write-Host "==> Building images ($Tag)" -ForegroundColor Cyan
$federatedImage = "$RegistryName.azurecr.io/$Name-mcp:$Tag"
$indexerImage = "$RegistryName.azurecr.io/$Name-indexer:$Tag"

az acr build --registry $RegistryName --image "$Name-mcp:$Tag" `
    --file (Join-Path $root 'src/Mongo.Copilot.Federated/Dockerfile') $root
if ($LASTEXITCODE -ne 0) { throw "Federated image build failed." }

az acr build --registry $RegistryName --image "$Name-indexer:$Tag" `
    --file (Join-Path $root 'src/Mongo.Copilot.Indexer/Dockerfile') $root
if ($LASTEXITCODE -ne 0) { throw "Indexer image build failed." }

Write-Host "==> Deploying infrastructure" -ForegroundColor Cyan
$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file (Join-Path $PSScriptRoot 'main.bicep') `
    --parameters `
        name=$Name `
        location=$Location `
        registryName=$RegistryName `
        federatedImage=$federatedImage `
        indexerImage=$indexerImage `
        tenantId=$TenantId `
        appClientId=$AppClientId `
        mongoConnectionString=$MongoConnectionString `
        mongoAuthMode=$MongoAuthMode `
        mongoTokenResource=$MongoTokenResource `
        mongoDatabase=$MongoDatabase `
        uiBaseUrl=$UiBaseUrl `
        requireUrl=$(-not $AllowUncitedItems) `
        infrastructureSubnetId=$InfrastructureSubnetId `
        privateDnsZoneIds=$($PrivateDnsZoneIds | ConvertTo-Json -Compress -AsArray) `
    --query properties.outputs -o json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) { throw "Deployment failed." }

$mcpUrl = $deployment.mcpBaseUrl.value
$indexerPrincipal = $deployment.indexerPrincipalId.value

Write-Host ""
Write-Host "Deployed." -ForegroundColor Green
Write-Host "  MCP base URL:        $mcpUrl"
Write-Host "  Indexer principal:   $indexerPrincipal"
Write-Host ""
Write-Host "Remaining manual steps:" -ForegroundColor Yellow
Write-Host "  1. Grant the indexer principal these Microsoft Graph application permissions,"
Write-Host "     then grant admin consent:"
Write-Host "       ExternalConnection.ReadWrite.OwnedBy   (create the connection and schema)"
Write-Host "       ExternalItem.ReadWrite.OwnedBy         (publish and delete items)"
Write-Host "       User.Read.All, Group.Read.All          (resolve emails and team names to"
Write-Host "                                              Entra object IDs for item ACLs)"
if ($MongoAuthMode -eq 'ManagedIdentity') {
    Write-Host "  2. Grant both principals database access:" -ForegroundColor Yellow
    Write-Host "     Azure DocumentDB:"
    Write-Host "       az documentdb mongocluster microsoft-entra-user assign --type ManagedIdentity \"
    Write-Host "         --object-id <principal-id> --cluster-name <cluster> --resource-group <rg> \"
    Write-Host "         --role db=$MongoDatabase role=read"
    Write-Host "     MongoDB Atlas: add a database user of type Workload Identity Federation"
    Write-Host "       mapped to each principal's object ID, with a read-only role."
    Write-Host "  3. Register Entra SSO for $AppClientId in the Teams Developer Portal."
    Write-Host "  4. In the M365 admin center, Copilot > Connectors > Gallery >"
    Write-Host "     Create a new connector > Connect to MCP server:"
    Write-Host "       Base URL:        $mcpUrl"
    Write-Host "       Registration ID: (from step 3)"
    Write-Host "  5. Roll out to a test group before enabling for all users."
} else {
    Write-Host "  2. Register Entra SSO for $AppClientId in the Teams Developer Portal and note"
    Write-Host "     the registration ID."
    Write-Host "  3. In the M365 admin center, go to Copilot > Connectors > Gallery >"
    Write-Host "     Create a new connector > Connect to MCP server, and enter:"
    Write-Host "       Base URL:        $mcpUrl"
    Write-Host "       Registration ID: (from step 2)"
    Write-Host "  4. Roll out to a test group before enabling for all users."
}
