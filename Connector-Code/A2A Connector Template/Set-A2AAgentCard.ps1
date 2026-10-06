#Requires -Version 7.0
<#
.SYNOPSIS
    Points an A2A connector definition at an agent's endpoint, and optionally embeds the agent card.

.DESCRIPTION
    Reads an A2A agent card from a URL or a file, takes the agent's endpoint from the card, and
    updates the single A2A operation in apiDefinition.swagger.json:

      - host and path are set from the agent's endpoint
      - x-ms-a2a-card-endpoint is set to the card URL
      - x-ms-a2a-card-json is set to the card when -EmbedCard is used, and to "null" otherwise

    The endpoint is read from the card's top-level "url" (A2A 0.3). If the card has none, the
    first JSON-RPC entry in "supportedInterfaces" (A2A 1.0) is used.

.PARAMETER CardUrl
    The agent card URL, for example https://agent.example.com/a2a/.well-known/agent-card.json.

.PARAMETER BearerToken
    Access token to send when the card endpoint requires authentication.

.PARAMETER CardFile
    Path to an agent card you already saved. Use with -CardUrl to also record where the card lives.

.PARAMETER Endpoint
    The agent's A2A endpoint. Overrides the endpoint in the card, or replaces the card entirely.

.PARAMETER EmbedCard
    Embed the card in x-ms-a2a-card-json.

.PARAMETER DefinitionPath
    The connector definition to update. Defaults to apiDefinition.swagger.json in the current folder.

.EXAMPLE
    ./Set-A2AAgentCard.ps1 -CardUrl https://agent.example.com/a2a/.well-known/agent-card.json

.EXAMPLE
    ./Set-A2AAgentCard.ps1 -CardUrl $cardUrl -BearerToken $token -EmbedCard

.EXAMPLE
    ./Set-A2AAgentCard.ps1 -Endpoint https://agent.example.com/a2a
#>
[CmdletBinding()]
param(
    [string]$CardUrl,
    [string]$BearerToken,
    [string]$CardFile,
    [string]$Endpoint,
    [switch]$EmbedCard,
    [string]$DefinitionPath = (Join-Path (Get-Location) 'apiDefinition.swagger.json')
)

$ErrorActionPreference = 'Stop'

if (-not $CardUrl -and -not $CardFile -and -not $Endpoint) {
    throw 'Provide -CardUrl, -CardFile, or -Endpoint.'
}
if ($EmbedCard -and -not $CardUrl -and -not $CardFile) {
    throw '-EmbedCard needs a card. Provide -CardUrl or -CardFile.'
}
if (-not (Test-Path $DefinitionPath)) {
    throw "Connector definition not found: $DefinitionPath"
}

$cardText = $null
if ($CardFile) {
    $cardText = (Get-Content $CardFile -Raw).Trim()
}
elseif ($CardUrl) {
    $headers = @{ Accept = 'application/json'; 'Accept-Encoding' = 'identity' }
    if ($BearerToken) { $headers.Authorization = "Bearer $BearerToken" }

    $response = Invoke-WebRequest -Uri $CardUrl -Headers $headers -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) {
        $hint = if ($response.StatusCode -in 401, 403) {
            if ($BearerToken) { ' Check that the token is valid, unexpired, and issued for the agent.' }
            else { ' The card endpoint requires authentication. Pass -BearerToken.' }
        } else { '' }
        throw "Reading the agent card returned HTTP $($response.StatusCode).$hint"
    }
    $cardText = ([string]$response.Content).Trim()
}

$card = $null
if ($cardText) {
    try { $card = $cardText | ConvertFrom-Json }
    catch { throw "The agent card isn't valid JSON: $($_.Exception.Message)" }
}

if (-not $Endpoint) {
    if ($card.url) {
        $Endpoint = $card.url
    }
    elseif ($card.supportedInterfaces) {
        $interface = @($card.supportedInterfaces | Where-Object { $_.protocolBinding -eq 'JSONRPC' })[0]
        if (-not $interface) { $interface = @($card.supportedInterfaces)[0] }
        $Endpoint = $interface.url
    }
    if (-not $Endpoint) {
        throw 'The agent card has no "url" or "supportedInterfaces" endpoint. Pass -Endpoint.'
    }
}

$endpointUri = [Uri]$Endpoint
if ($endpointUri.Scheme -ne 'https') {
    throw "The agent endpoint must use HTTPS: $Endpoint"
}
if ($endpointUri.Query) {
    Write-Warning "Ignoring the query string on the agent endpoint: $($endpointUri.Query)"
}

$definition = Get-Content $DefinitionPath -Raw | ConvertFrom-Json
$a2aPaths = @($definition.paths.PSObject.Properties | Where-Object {
    $_.Value.post -and $_.Value.post.'x-ms-agentic-protocol' -eq 'a2a-1.0'
})
if ($a2aPaths.Count -ne 1) {
    throw "Expected one operation with x-ms-agentic-protocol 'a2a-1.0' in $DefinitionPath, found $($a2aPaths.Count)."
}
$operation = $a2aPaths[0].Value

$path = $endpointUri.AbsolutePath
if (-not $path) { $path = '/' }

if (-not $CardUrl) {
    $CardUrl = $Endpoint.TrimEnd('/') + '/.well-known/agent-card.json'
}

$operation.post.'x-ms-a2a-card-endpoint' = $CardUrl
$operation.post.'x-ms-a2a-card-json' = if ($EmbedCard) { $cardText } else { 'null' }

$definition.host = $endpointUri.Authority
$definition.basePath = '/'
$definition.paths = [pscustomobject]@{ $path = $operation }

$definition | ConvertTo-Json -Depth 100 | Set-Content $DefinitionPath -Encoding utf8

[pscustomobject]@{
    Endpoint     = "https://$($endpointUri.Authority)$path"
    CardEndpoint = $CardUrl
    CardEmbedded = [bool]$EmbedCard
    AgentName    = $card.name
    Definition   = (Resolve-Path $DefinitionPath).Path
}
