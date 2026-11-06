<#
.SYNOPSIS
    Registers an OAuth client for the Atlassian MCP server (v2) through dynamic client registration.

.DESCRIPTION
    Atlassian has no developer-console app for its MCP server. Clients are created through the
    authorization server's dynamic client registration (DCR) endpoint, which this script
    discovers from the MCP server's metadata and calls for you.

    By default the client is a public client (no secret), which is what the oauth2pkcewithdcr
    identity provider expects. Use -Confidential to get a client secret for the oauth2pkce
    identity provider; the secret is copied to your clipboard and never printed.

.PARAMETER RedirectUrl
    The exact callback URL of your deployed connector, for example the redirectUrl value in the
    apiProperties.json returned by `pac connector download`.

.PARAMETER ClientName
    Name shown on the Atlassian consent screen.

.PARAMETER Confidential
    Request a client secret (client_secret_post) instead of a public client.

.EXAMPLE
    .\Register-AtlassianClient.ps1 -RedirectUrl "https://global.consent.azure-apim.net/redirect/<your-connector-redirect>"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$RedirectUrl,

    [string]$ClientName = 'Atlassian MCP V2 for Power Platform',

    [switch]$Confidential
)

$ErrorActionPreference = 'Stop'

$resource = Invoke-RestMethod -Uri 'https://mcp.atlassian.com/.well-known/oauth-protected-resource/v2/mcp'
$issuer = $resource.authorization_servers[0].TrimEnd('/')
$metadata = Invoke-RestMethod -Uri "$issuer/.well-known/oauth-authorization-server"

if (-not $metadata.registration_endpoint) {
    throw "The authorization server at $issuer does not advertise a registration endpoint."
}

$body = @{
    client_name                = $ClientName
    redirect_uris              = @($RedirectUrl)
    grant_types                = @('authorization_code', 'refresh_token')
    response_types             = @('code')
    token_endpoint_auth_method = $(if ($Confidential) { 'client_secret_post' } else { 'none' })
} | ConvertTo-Json

$client = Invoke-RestMethod -Method Post -Uri $metadata.registration_endpoint `
    -ContentType 'application/json' -Body $body

Write-Host "client_id:  $($client.client_id)"
Write-Host "redirect:   $($client.redirect_uris -join ', ')"

if ($Confidential) {
    $client.client_secret | Set-Clipboard
    Write-Host 'client_secret copied to the clipboard. Enter it in the connection or Security tab, then clear the clipboard.'
}
else {
    Write-Host 'Public client: no client secret is needed.'
}
