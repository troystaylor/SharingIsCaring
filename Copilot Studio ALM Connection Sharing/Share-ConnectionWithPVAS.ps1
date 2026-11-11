<#
.SYNOPSIS
    Shares a Power Platform connection with the Power Virtual Agents Service
    service principal so Copilot Studio agents can use it at runtime.

.DESCRIPTION
    Requires an authenticated Azure CLI session (az login or an AzureCLI@2 task) whose
    identity can read service principals in Entra ID and administer connections in the
    target environment.

    The principal type in the request body is "ServicePrincipal". Confirm it by sharing a
    connection with Power Virtual Agents Service in make.powerapps.com while capturing the
    modifyPermissions request in your browser's developer tools.

.PARAMETER EnvironmentId
    Power Platform environment ID (GUID).

.PARAMETER ConnectorName
    Connector API name, for example shared_databricks.

.PARAMETER ConnectionName
    Connection ID that your connection reference points to.

.PARAMETER RoleName
    CanView ("Can use") or CanViewWithShare ("Can use + share").

.PARAMETER PrincipalObjectId
    Optional. Object ID of the Power Virtual Agents Service enterprise application in this
    tenant. Looked up by display name when omitted.
#>
param(
    [Parameter(Mandatory)] [string] $EnvironmentId,
    [Parameter(Mandatory)] [string] $ConnectorName,
    [Parameter(Mandatory)] [string] $ConnectionName,
    [ValidateSet('CanView', 'CanViewWithShare')] [string] $RoleName = 'CanView',
    [string] $PrincipalObjectId
)

$ErrorActionPreference = 'Stop'

if (-not $PrincipalObjectId) {
    $PrincipalObjectId = az ad sp list --display-name "Power Virtual Agents Service" --query "[0].id" -o tsv
    if (-not $PrincipalObjectId) {
        throw "Power Virtual Agents Service was not found in this tenant."
    }
}

$token = az account get-access-token --resource https://service.powerapps.com/ --query accessToken -o tsv
$uri = "https://api.powerapps.com/providers/Microsoft.PowerApps/scopes/admin/environments/$EnvironmentId/apis/$ConnectorName/connections/$ConnectionName/modifyPermissions?api-version=2017-06-01"

$body = @{
    put = @(@{
        properties = @{
            roleName  = $RoleName
            principal = @{ id = $PrincipalObjectId; type = 'ServicePrincipal' }
        }
    })
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post -Uri $uri -Body $body -ContentType 'application/json' `
    -Headers @{ Authorization = "Bearer $token" } | Out-Null

Write-Host "Shared connection '$ConnectionName' with Power Virtual Agents Service ($PrincipalObjectId) as $RoleName."
