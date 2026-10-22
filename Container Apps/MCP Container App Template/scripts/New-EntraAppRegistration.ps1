<#
.SYNOPSIS
    Creates the Microsoft Entra app registration that represents the MCP server.

.DESCRIPTION
    Creates a single-tenant app, sets its Application ID URI to api://<appId>, exposes one delegated
    scope, forces v2 access tokens (the server validates the v2 issuer), and creates the service principal.
    Prints the values deploy.ps1 and the connector need.

    Run once per environment. Requires `az login` as someone allowed to register applications.

.EXAMPLE
    .\New-EntraAppRegistration.ps1 -DisplayName 'MCP Container App'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DisplayName,
    [string] $ScopeName = 'access_as_user'
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

$tenantId = Invoke-Az account show --query tenantId -o tsv
$appId = Invoke-Az ad app create --display-name $DisplayName --sign-in-audience AzureADMyOrg --query appId -o tsv
$objectId = Invoke-Az ad app show --id $appId --query id -o tsv

Invoke-Az ad app update --id $appId --identifier-uris "api://$appId" | Out-Null

$body = @{
    api = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes      = @(
            @{
                id                      = [guid]::NewGuid().ToString()
                value                   = $ScopeName
                type                    = 'User'
                isEnabled               = $true
                adminConsentDisplayName = "Access $DisplayName"
                adminConsentDescription = "Allow the app to call $DisplayName on behalf of the signed-in user."
                userConsentDisplayName  = "Access $DisplayName"
                userConsentDescription  = "Allow the app to call $DisplayName on your behalf."
            }
        )
    }
}

$file = Join-Path ([System.IO.Path]::GetTempPath()) "$([guid]::NewGuid()).json"
try {
    $body | ConvertTo-Json -Depth 10 | Set-Content -Path $file -Encoding utf8
    Invoke-Az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$objectId" `
        --headers 'Content-Type=application/json' --body "@$file" | Out-Null
}
finally {
    Remove-Item $file -Force -ErrorAction SilentlyContinue
}

Invoke-Az ad sp create --id $appId | Out-Null

Write-Host ''
Write-Host "Tenant ID:        $tenantId"
Write-Host "Client ID:        $appId"
Write-Host "Application URI:  api://$appId"
Write-Host "Scope:            api://$appId/$ScopeName"
