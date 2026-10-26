<#
.SYNOPSIS
    Creates the Microsoft Entra app registration for the Copilot Studio plugin and gateway.

.DESCRIPTION
    One app serves two roles:
      - the plugin's sign-in client: a public client (OAuth with PKCE, so no client secret) with the Teams OAuth
        redirect URI. It is not a single-page application: Teams redeems the authorization code from its backend,
        and Entra refuses that for the SPA platform (AADSTS9002327), and
      - the gateway's API: it exposes the delegated scope access_as_user and issues v2 access tokens.

    The gateway exchanges each user's token for a Power Platform token on their behalf, so the app also requests
    two delegated Power Platform API permissions: CopilotFlows.ChatAssistant.Execute and
    EnvironmentManagement.Environments.Read. Optionally grants admin consent for all of them.

    Run once per tenant. Requires `az login` as someone allowed to register applications. Granting consent
    also requires an admin role.

.EXAMPLE
    .\New-PluginClientApp.ps1 -GrantAdminConsent
#>
[CmdletBinding()]
param(
    [string] $DisplayName = 'Copilot Studio MCP for Microsoft 365 Copilot',
    [string] $ScopeName = 'access_as_user',
    [switch] $GrantAdminConsent
)

$ErrorActionPreference = 'Stop'

# The Power Platform API and its delegated permissions.
$powerPlatformApi = '8578e004-a5c6-46e7-913e-12f58912df43'
$powerPlatformScopes = 'CopilotFlows.ChatAssistant.Execute', 'EnvironmentManagement.Environments.Read'
$redirectUri = 'https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

function Update-Application([string] $ObjectId, [hashtable] $Body) {
    $file = Join-Path ([System.IO.Path]::GetTempPath()) "$([guid]::NewGuid()).json"
    try {
        $Body | ConvertTo-Json -Depth 10 | Set-Content -Path $file -Encoding utf8
        Invoke-Az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$ObjectId" `
            --headers 'Content-Type=application/json' --body "@$file" | Out-Null
    }
    finally {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

$tenantId = Invoke-Az account show --query tenantId -o tsv

# A tenant that has never used the Power Platform API has no service principal for it yet.
$null = & az ad sp show --id $powerPlatformApi 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Creating the Power Platform API service principal in this tenant' -ForegroundColor Cyan
    Invoke-Az ad sp create --id $powerPlatformApi | Out-Null
}

$available = Invoke-Az ad sp show --id $powerPlatformApi --query 'oauth2PermissionScopes[].{id:id,value:value}' -o json | ConvertFrom-Json
$powerPlatformPermissions = foreach ($name in $powerPlatformScopes) {
    $scope = $available | Where-Object value -eq $name
    if (-not $scope) { throw "The Power Platform API in this tenant does not expose the permission $name." }
    "$($scope.id)=Scope"
}

$app = Invoke-Az ad app create --display-name $DisplayName --sign-in-audience AzureADMyOrg --query '{id:id,appId:appId}' -o json | ConvertFrom-Json
Invoke-Az ad app update --id $app.appId --identifier-uris "api://$($app.appId)" | Out-Null

$scopeId = [guid]::NewGuid().ToString()
Update-Application $app.id @{
    publicClient = @{ redirectUris = @($redirectUri) }
    api = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes      = @(@{
            id                      = $scopeId
            value                   = $ScopeName
            type                    = 'User'
            isEnabled               = $true
            adminConsentDisplayName = 'Use Copilot Studio as you'
            adminConsentDescription = 'Allow the Copilot Studio plugin to call Copilot Studio on your behalf.'
            userConsentDisplayName  = 'Use Copilot Studio as you'
            userConsentDescription  = 'Allow the Copilot Studio plugin to call Copilot Studio on your behalf.'
        })
    }
}

Invoke-Az ad app permission add --id $app.appId --api $powerPlatformApi --api-permissions @powerPlatformPermissions 2>$null | Out-Null
Invoke-Az ad app permission add --id $app.appId --api $app.appId --api-permissions "$scopeId=Scope" 2>$null | Out-Null
Invoke-Az ad sp create --id $app.appId | Out-Null

if ($GrantAdminConsent) {
    Invoke-Az ad app permission admin-consent --id $app.appId | Out-Null
    Write-Host 'Admin consent granted.' -ForegroundColor Green
}
else {
    Write-Host "Ask an admin to run: az ad app permission admin-consent --id $($app.appId)" -ForegroundColor Yellow
}

Write-Host ''
Write-Host "COPILOT_STUDIO_TENANT_ID=$tenantId"
Write-Host "COPILOT_STUDIO_CLIENT_ID=$($app.appId)"
Write-Host "Scope the plugin requests: api://$($app.appId)/$ScopeName"
