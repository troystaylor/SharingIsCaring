<#
.SYNOPSIS
    Provisions and deploys the Copilot Studio Azure Function middleware.

.DESCRIPTION
    Automates the middleware deployment steps from the README:
      - Creates the resource group and storage account (if they do not exist)
      - Creates a Linux Consumption Function App (Node 20, Functions v4)
      - Sets the required app settings (tenant, client, secret, Direct Connect URL, base URL)
      - Configures CORS for your Salesforce org domains
      - Publishes the function code from streaming-middleware/
      - Prints the Function App URL and host key for the Salesforce Custom Metadata

    Requires Azure CLI (az) and Azure Functions Core Tools (func) v4.

.PARAMETER ResourceGroup
    Resource group to deploy into. Created if it does not exist.

.PARAMETER FunctionAppName
    Globally unique Function App name (also the *.azurewebsites.net host).

.PARAMETER Location
    Azure region. Defaults to eastus.

.PARAMETER StorageAccountName
    Storage account name (3-24 lowercase alphanumeric). Defaults to a name derived from the Function App.

.PARAMETER TenantId
    Entra ID tenant ID (from Register-EntraApp.ps1).

.PARAMETER ClientId
    App registration client ID (from Register-EntraApp.ps1).

.PARAMETER ClientSecret
    App registration client secret (from Register-EntraApp.ps1).

.PARAMETER DirectConnectUrl
    Copilot Studio Direct Connect URL (Channels -> Native app).

.PARAMETER SalesforceOrgDomains
    One or more Salesforce origins to allow via CORS,
    e.g. https://acme.my.salesforce.com, https://acme.lightning.force.com.

.EXAMPLE
    ./Deploy-Middleware.ps1 -ResourceGroup rg-copilot -FunctionAppName acme-copilot-mw `
        -TenantId <t> -ClientId <c> -ClientSecret <s> `
        -DirectConnectUrl <url> `
        -SalesforceOrgDomains 'https://acme.my.salesforce.com','https://acme.lightning.force.com'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ResourceGroup,
    [Parameter(Mandatory = $true)][string]$FunctionAppName,
    [string]$Location = 'eastus',
    [string]$StorageAccountName,
    [Parameter(Mandatory = $true)][string]$TenantId,
    [Parameter(Mandatory = $true)][string]$ClientId,
    [Parameter(Mandatory = $true)][string]$ClientSecret,
    [Parameter(Mandatory = $true)][string]$DirectConnectUrl,
    [Parameter(Mandatory = $true)][string[]]$SalesforceOrgDomains
)

$ErrorActionPreference = 'Stop'

function Assert-Tool {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not installed. $InstallHint"
    }
}

Assert-Tool -Name az -InstallHint "Install it from https://aka.ms/azcli and run 'az login'."
Assert-Tool -Name func -InstallHint "Install Azure Functions Core Tools v4: https://learn.microsoft.com/azure/azure-functions/functions-run-local"

try {
    az account show --only-show-errors 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) { throw }
}
catch {
    throw "Not signed in to Azure CLI. Run 'az login' first."
}

# Derive a storage account name if not supplied (must be 3-24 lowercase alphanumeric).
if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
    $base = ($FunctionAppName -replace '[^a-z0-9]', '').ToLower()
    if ($base.Length -gt 18) { $base = $base.Substring(0, 18) }
    $StorageAccountName = "st$base"
    if ($StorageAccountName.Length -gt 24) { $StorageAccountName = $StorageAccountName.Substring(0, 24) }
}

$middlewareDir = Join-Path $PSScriptRoot '..\streaming-middleware' | Resolve-Path
Write-Host "Deploying middleware from $middlewareDir" -ForegroundColor Cyan

Write-Host "Ensuring resource group '$ResourceGroup' ($Location)..." -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --only-show-errors 1>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to create/verify resource group." }

Write-Host "Ensuring storage account '$StorageAccountName'..." -ForegroundColor Cyan
az storage account create `
    --name $StorageAccountName `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --only-show-errors 1>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to create storage account." }

Write-Host "Creating Function App '$FunctionAppName' (Node 20, Functions v4, Consumption)..." -ForegroundColor Cyan
az functionapp create `
    --name $FunctionAppName `
    --resource-group $ResourceGroup `
    --storage-account $StorageAccountName `
    --consumption-plan-location $Location `
    --runtime node `
    --runtime-version 20 `
    --functions-version 4 `
    --os-type Linux `
    --only-show-errors 1>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to create the Function App." }

$baseUrl = "https://$FunctionAppName.azurewebsites.net"

Write-Host "Configuring app settings..." -ForegroundColor Cyan
az functionapp config appsettings set `
    --name $FunctionAppName `
    --resource-group $ResourceGroup `
    --settings `
        "AZURE_TENANT_ID=$TenantId" `
        "AZURE_CLIENT_ID=$ClientId" `
        "AZURE_CLIENT_SECRET=$ClientSecret" `
        "COPILOT_DIRECT_CONNECT_URL=$DirectConnectUrl" `
        "MIDDLEWARE_BASE_URL=$baseUrl" `
    --only-show-errors 1>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to set app settings." }

Write-Host "Configuring CORS for Salesforce origins..." -ForegroundColor Cyan
foreach ($origin in $SalesforceOrgDomains) {
    az functionapp cors add --name $FunctionAppName --resource-group $ResourceGroup --allowed-origins $origin --only-show-errors 1>$null
}

Write-Host "Publishing function code (this can take a couple of minutes)..." -ForegroundColor Cyan
Push-Location $middlewareDir
try {
    npm install --omit=dev
    if ($LASTEXITCODE -ne 0) { throw "npm install failed." }
    func azure functionapp publish $FunctionAppName --javascript
    if ($LASTEXITCODE -ne 0) { throw "func publish failed." }
}
finally {
    Pop-Location
}

Write-Host "Retrieving Function App host key..." -ForegroundColor Cyan
$hostKey = az functionapp keys list --name $FunctionAppName --resource-group $ResourceGroup --query "functionKeys.default" -o tsv --only-show-errors

Write-Host ""
Write-Host "Middleware deployed." -ForegroundColor Green
Write-Host "----------------------------------------------------------------"
Write-Host "Function App URL : $baseUrl"
Write-Host "Health check     : $baseUrl/api/health"
Write-Host "Host key         : $hostKey"
Write-Host "----------------------------------------------------------------"
Write-Host "Next steps in Salesforce:" -ForegroundColor Yellow
Write-Host "  1. Custom Metadata 'CopilotStudio Settings' -> Default:"
Write-Host "       Token_Endpoint__c = $baseUrl"
Write-Host "       Function_Key__c   = <host key above>"
Write-Host "  2. Update the AgentMiddleware CSP Trusted Site and StreamingMiddleware Remote Site URLs to $baseUrl"
Write-Host "  3. Ensure the Entra app redirect URI is $baseUrl/api/auth/callback"

[PSCustomObject]@{
    FunctionAppUrl = $baseUrl
    HealthUrl      = "$baseUrl/api/health"
    HostKey        = $hostKey
}
