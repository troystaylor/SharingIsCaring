<#
.SYNOPSIS
    Registers the Entra ID application used by the Copilot Studio middleware.

.DESCRIPTION
    Automates the Entra ID app registration steps from the README:
      - Creates a single-tenant app registration
      - Adds the Power Platform API delegated permission CopilotStudio.Copilots.Invoke
      - Grants admin consent (requires a Global Admin / Privileged Role Admin)
      - Sets the /api/auth/callback web redirect URI
      - Enables ID token issuance
      - Creates a client secret

    Outputs the Tenant ID, Client ID, and Client Secret needed by Deploy-Middleware.ps1
    and streaming-middleware/local.settings.json.

.PARAMETER FunctionAppName
    The Function App name that will host the middleware. Used to build the redirect URI
    https://<FunctionAppName>.azurewebsites.net/api/auth/callback. Provide this OR -RedirectUri.

.PARAMETER RedirectUri
    Full redirect URI to register. Use for local dev (e.g. http://localhost:7071/api/auth/callback)
    or when the Function App URL is not the default *.azurewebsites.net.

.PARAMETER DisplayName
    Display name for the app registration. Defaults to "Copilot Studio Salesforce Middleware".

.PARAMETER SecretYears
    Client secret lifetime in years (1 or 2). Defaults to 1.

.EXAMPLE
    ./Register-EntraApp.ps1 -FunctionAppName my-copilot-middleware

.EXAMPLE
    ./Register-EntraApp.ps1 -RedirectUri http://localhost:7071/api/auth/callback
#>
[CmdletBinding(DefaultParameterSetName = 'FunctionApp')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'FunctionApp')]
    [string]$FunctionAppName,

    [Parameter(Mandatory = $true, ParameterSetName = 'RedirectUri')]
    [string]$RedirectUri,

    [string]$DisplayName = 'Copilot Studio Salesforce Middleware',

    [ValidateRange(1, 2)]
    [int]$SecretYears = 1
)

$ErrorActionPreference = 'Stop'

# Power Platform API — CopilotStudio.Copilots.Invoke delegated permission
$PowerPlatformApiAppId = '8578e004-a5c6-46e7-913e-12f58912df43'
$InvokeScopeId = '204440d3-c1d0-4826-b570-99eb6f5e2aeb'

function Assert-AzCli {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI (az) is not installed. Install it from https://aka.ms/azcli and run 'az login'."
    }
    try {
        az account show --only-show-errors 1>$null 2>$null
        if ($LASTEXITCODE -ne 0) { throw }
    }
    catch {
        throw "Not signed in to Azure CLI. Run 'az login' first."
    }
}

Assert-AzCli

if ($PSCmdlet.ParameterSetName -eq 'FunctionApp') {
    $RedirectUri = "https://$FunctionAppName.azurewebsites.net/api/auth/callback"
}

Write-Host "Registering Entra ID app '$DisplayName'..." -ForegroundColor Cyan
Write-Host "  Redirect URI: $RedirectUri"

$appId = az ad app create `
    --display-name $DisplayName `
    --sign-in-audience AzureADMyOrg `
    --web-redirect-uris $RedirectUri `
    --enable-id-token-issuance true `
    --query appId -o tsv --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($appId)) {
    throw "Failed to create the app registration."
}
Write-Host "  Created app registration. Client ID: $appId" -ForegroundColor Green

Write-Host "Adding Power Platform API delegated permission (CopilotStudio.Copilots.Invoke)..." -ForegroundColor Cyan
az ad app permission add `
    --id $appId `
    --api $PowerPlatformApiAppId `
    --api-permissions "$InvokeScopeId=Scope" `
    --only-show-errors 1>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to add the API permission." }

Write-Host "Granting admin consent (requires admin privileges)..." -ForegroundColor Cyan
# Consent can briefly 404 while the service principal propagates — retry a few times.
$consented = $false
for ($i = 1; $i -le 5; $i++) {
    az ad app permission admin-consent --id $appId --only-show-errors 2>$null
    if ($LASTEXITCODE -eq 0) { $consented = $true; break }
    Start-Sleep -Seconds 5
}
if (-not $consented) {
    Write-Warning "Admin consent did not complete automatically. Grant it in the Azure Portal: App registrations -> $DisplayName -> API permissions -> Grant admin consent."
}
else {
    Write-Host "  Admin consent granted." -ForegroundColor Green
}

Write-Host "Creating client secret..." -ForegroundColor Cyan
$secret = az ad app credential reset `
    --id $appId `
    --append `
    --display-name 'middleware-secret' `
    --years $SecretYears `
    --query password -o tsv --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($secret)) {
    throw "Failed to create the client secret."
}

$tenantId = az account show --query tenantId -o tsv --only-show-errors

Write-Host ""
Write-Host "App registration complete." -ForegroundColor Green
Write-Host "----------------------------------------------------------------"
Write-Host "AZURE_TENANT_ID    = $tenantId"
Write-Host "AZURE_CLIENT_ID    = $appId"
Write-Host "AZURE_CLIENT_SECRET= $secret"
Write-Host "----------------------------------------------------------------"
Write-Host "Store the secret now — it cannot be retrieved again." -ForegroundColor Yellow
Write-Host "Pass these to Deploy-Middleware.ps1 or paste into streaming-middleware/local.settings.json."

# Return an object so the values can be captured programmatically.
[PSCustomObject]@{
    TenantId     = $tenantId
    ClientId     = $appId
    ClientSecret = $secret
    RedirectUri  = $RedirectUri
}
