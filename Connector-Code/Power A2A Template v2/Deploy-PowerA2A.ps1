<#
.SYNOPSIS
    Deploys the Power A2A Template v2 connector with PAC CLI, from a temporary copy.

.DESCRIPTION
    Builds the connector files in a temporary folder, deploys them, and deletes the folder. The files in this
    folder are never changed, so keys and secrets never reach a file you commit.

    The script:
      * applies the sign-in option you choose from the auth folder
      * keeps the connector's name, description, and host in step with the constants in script.csx
      * sets HOST_SIGNING_KEY to a new random value, unless you pass one, and fills in the model keys if you ask
        for the intent resolver
      * with -Demo, swaps in a demo's skills and settings, such as demo\DummyJson.skills.txt
      * runs pac connector create, or update when you pass -ConnectorId

    Use -DryRun to build the files and see what would be deployed, without calling pac. The temporary folder
    is kept in that case so that you can read the files, and it holds the keys, so delete it when you're done.

.PARAMETER Environment
    The Power Platform environment ID. Required unless you use -DryRun.

.PARAMETER Auth
    The sign-in option: None, ApiKey, or EntraId. Required unless you use -Demo, which defaults to None.

.PARAMETER Demo
    The name of a demo in the demo folder, for example DummyJson. It replaces the skills and the agent settings
    in a copy of script.csx. The released script is never changed.

.PARAMETER ConnectorId
    Update this connector instead of creating a new one.

.PARAMETER ApiHost
    The host to put in the connector definition. Defaults to the host of HOST_API_BASE_URL.

.PARAMETER SigningKey
    The value for HOST_SIGNING_KEY. A random one is generated if you don't pass it. Pass the same value on every
    deployment if you don't want pending confirmations to stop working when you redeploy.

.PARAMETER ApiKeyHeader
    With -Auth ApiKey, the header that carries the key to your API. Defaults to X-API-Key.

.PARAMETER ClientId
    With -Auth EntraId, the Application (client) ID of your app registration.

.PARAMETER ResourceUri
    With -Auth EntraId, the Application ID URI of the API the connector calls, for example api://your-api.

.PARAMETER Scope
    With -Auth EntraId, the delegated permission that API requires.

.PARAMETER ClientSecret
    With -Auth EntraId and -ConnectorId, the client secret to store on the connector. Register the redirect URL
    first. See the readme.

.PARAMETER IntentResolver
    off, fallback, or always. fallback and always also need -LlmEndpoint and -LlmApiKey.

.PARAMETER LlmEndpoint
    The full chat-completions URL for the intent resolver.

.PARAMETER LlmApiKey
    The key for the intent resolver's endpoint.

.PARAMETER LlmKeyHeader
    api-key for Azure OpenAI (the default), or Authorization for OpenAI-compatible endpoints.

.PARAMETER LlmModel
    The model name, for OpenAI-compatible endpoints.

.PARAMETER DryRun
    Build the files and report, without calling pac.

.EXAMPLE
    .\Deploy-PowerA2A.ps1 -Environment <id> -Demo DummyJson

.EXAMPLE
    .\Deploy-PowerA2A.ps1 -Environment <id> -Auth EntraId -ClientId <id> -ResourceUri api://my-api -Scope user_impersonation

.EXAMPLE
    .\Deploy-PowerA2A.ps1 -Auth ApiKey -ApiKeyHeader X-Acme-Key -DryRun
#>
[CmdletBinding()]
param(
    [string]$Environment,
    [ValidateSet('None', 'ApiKey', 'EntraId')][string]$Auth,
    [string]$Demo,
    [string]$ConnectorId,
    [string]$ApiHost,
    [string]$SigningKey,
    [string]$ApiKeyHeader = 'X-API-Key',
    [string]$ClientId,
    [string]$ResourceUri,
    [string]$Scope,
    [string]$ClientSecret,
    [ValidateSet('off', 'fallback', 'always')][string]$IntentResolver = 'off',
    [string]$LlmEndpoint,
    [string]$LlmApiKey,
    [string]$LlmKeyHeader = 'api-key',
    [string]$LlmModel,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'demo\Get-DemoScript.ps1')

# ── Checks ───────────────────────────────────────────────────────────────

if (-not $Auth) {
    if ($Demo) { $Auth = 'None' }
    else { throw "Choose a sign-in option with -Auth None, ApiKey, or EntraId. (-Demo uses None.)" }
}
if (-not $DryRun) {
    if (-not $Environment) { throw 'Pass -Environment with your Power Platform environment ID, or use -DryRun.' }
    if (-not (Get-Command pac -ErrorAction SilentlyContinue)) {
        throw 'PAC CLI (pac) isn''t on your PATH. Install it from https://aka.ms/PowerAppsCLI and run pac auth create.'
    }
}
if ($Auth -eq 'EntraId' -and -not $DryRun) {
    foreach ($name in 'ClientId', 'ResourceUri', 'Scope') {
        if (-not (Get-Variable $name -ValueOnly)) { throw "-Auth EntraId needs -$name." }
    }
}
if ($IntentResolver -ne 'off' -and (-not $LlmEndpoint -or -not $LlmApiKey)) {
    throw "-IntentResolver $IntentResolver needs -LlmEndpoint and -LlmApiKey."
}
if ($ClientSecret -and (-not $ConnectorId -or $Auth -ne 'EntraId')) {
    throw '-ClientSecret works with -Auth EntraId and -ConnectorId. Create the connector, register its redirect URL, then update it with the secret.'
}

# ── Helpers ──────────────────────────────────────────────────────────────

function Get-CSharpLiteral([string]$Value) {
    '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function ConvertFrom-CSharpLiteral([string]$Literal) {
    $Literal.Replace('\"', '"').Replace('\\', '\')
}

function Get-Constant([string]$Text, [string]$Name) {
    $found = [regex]::Match($Text, '\b' + $Name + ' = "((?:[^"\\]|\\.)*)"')
    if (-not $found.Success) { throw "script.csx has no string setting named $Name." }
    ConvertFrom-CSharpLiteral $found.Groups[1].Value
}

# Sets one setting. A setting that isn't there is an error, so a renamed constant can't be skipped silently.
function Set-Constant([string]$Text, [string]$Name, [string]$Value) {
    $found = [regex]::Match($Text, '\b' + $Name + ' = "(?:[^"\\]|\\.)*"')
    if (-not $found.Success) { throw "script.csx has no string setting named $Name, so it can't be set." }
    $Text.Replace($found.Value, $Name + ' = ' + (Get-CSharpLiteral $Value))
}

function Get-JsonEscaped([string]$Value) {
    ($Value | ConvertTo-Json -Compress).Trim('"')
}

function Read-Json([string]$Path) {
    [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
}

function Write-Json($Object, [string]$Path) {
    [System.IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 50), (New-Object System.Text.UTF8Encoding($false)))
}

# ── Build ────────────────────────────────────────────────────────────────

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('a2a-deploy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$keepWork = $false

try {
    $scriptText = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'script.csx'))

    if ($Demo) {
        $demoPath = Join-Path $PSScriptRoot ('demo\' + $Demo + '.skills.txt')
        if (-not (Test-Path $demoPath)) {
            $known = (Get-ChildItem (Join-Path $PSScriptRoot 'demo') -Filter '*.skills.txt' | ForEach-Object { $_.Name -replace '\.skills\.txt$', '' }) -join ', '
            throw "There's no demo named '$Demo'. Available: $known"
        }
        $scriptText = Get-DemoScriptText -ScriptText $scriptText -DemoPath $demoPath
    }

    $apiBase = Get-Constant $scriptText 'HOST_API_BASE_URL'
    $agentName = Get-Constant $scriptText 'HOST_AGENT_NAME'
    $agentDescription = Get-Constant $scriptText 'HOST_AGENT_DESCRIPTION'

    if (-not $ApiHost) { $ApiHost = ([uri]$apiBase).Host }

    $generatedKey = $false
    if (-not $SigningKey) {
        $bytes = New-Object byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $SigningKey = [Convert]::ToBase64String($bytes)
        $generatedKey = $true
    }
    $scriptText = Set-Constant $scriptText 'HOST_SIGNING_KEY' $SigningKey

    if ($Auth -eq 'ApiKey') {
        $scriptText = Set-Constant $scriptText 'HOST_FORWARD_HEADERS' $ApiKeyHeader
    }

    if ($IntentResolver -ne 'off') {
        $scriptText = Set-Constant $scriptText 'HOST_INTENT_RESOLVER' $IntentResolver
        $scriptText = Set-Constant $scriptText 'HOST_LLM_ENDPOINT' $LlmEndpoint
        $scriptText = Set-Constant $scriptText 'HOST_LLM_API_KEY' $LlmApiKey
        $scriptText = Set-Constant $scriptText 'HOST_LLM_KEY_HEADER' $LlmKeyHeader
        if ($LlmModel) { $scriptText = Set-Constant $scriptText 'HOST_LLM_MODEL' $LlmModel }
    }

    $scriptOut = Join-Path $work 'script.csx'
    [System.IO.File]::WriteAllText($scriptOut, $scriptText, (New-Object System.Text.UTF8Encoding($false)))

    # The sign-in option, with its placeholders filled in
    $authText = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ('auth\' + @{ None = 'none'; ApiKey = 'api-key'; EntraId = 'entra-id' }[$Auth] + '.json')))
    if ($Auth -eq 'ApiKey') { $authText = $authText.Replace('[YOUR_API_KEY_HEADER]', (Get-JsonEscaped $ApiKeyHeader)) }
    if ($Auth -eq 'EntraId') {
        if ($ClientId) { $authText = $authText.Replace('[YOUR_CLIENT_ID]', (Get-JsonEscaped $ClientId)) }
        if ($ResourceUri) { $authText = $authText.Replace('[YOUR_RESOURCE_URI]', (Get-JsonEscaped $ResourceUri.TrimEnd('/'))) }
        if ($Scope) { $authText = $authText.Replace('[YOUR_SCOPE]', (Get-JsonEscaped $Scope)) }
    }
    $authSettings = $authText | ConvertFrom-Json

    # apiProperties.json
    $props = Read-Json (Join-Path $PSScriptRoot 'apiProperties.json')
    $props.properties.connectionParameters = $authSettings.connectionParameters
    if ($ClientSecret) { $props.properties.connectionParameters.token.oAuthSettings | Add-Member -NotePropertyName clientSecret -NotePropertyValue $ClientSecret -Force }
    $propsOut = Join-Path $work 'apiProperties.json'
    Write-Json $props $propsOut

    # apiDefinition.swagger.json
    $swagger = Read-Json (Join-Path $PSScriptRoot 'apiDefinition.swagger.json')
    $swagger.host = $ApiHost
    $swagger.info.title = $agentName
    $swagger.securityDefinitions = $authSettings.securityDefinitions
    $swagger.security = @($authSettings.security)
    $invoke = $swagger.paths.'/a2a'.post
    $invoke.summary = $agentName
    $invoke.description = $agentDescription
    $swaggerOut = Join-Path $work 'apiDefinition.swagger.json'
    Write-Json $swagger $swaggerOut

    # ── Report ───────────────────────────────────────────────────────────

    Write-Host ''
    Write-Host 'Connector' -ForegroundColor Cyan
    Write-Host "  Name         $agentName"
    Write-Host "  Host         $ApiHost  (API base URL: $apiBase)"
    Write-Host "  Sign-in      $Auth$(if ($Auth -eq 'ApiKey') { ' (header ' + $ApiKeyHeader + ')' })"
    Write-Host "  Skills       $(if ($Demo) { 'demo: ' + $Demo } else { 'the skills in script.csx' })"
    Write-Host "  Signing key  $(if ($generatedKey) { 'generated (a new one each deploy, so pending confirmations end when you redeploy)' } else { 'the one you passed' })"
    Write-Host "  Resolver     $IntentResolver"
    Write-Host "  Action       $(if ($ConnectorId) { 'update ' + $ConnectorId } else { 'create' })"
    Write-Host ''

    if ($Auth -eq 'EntraId' -and -not $DryRun) {
        foreach ($placeholder in '[YOUR_CLIENT_ID]', '[YOUR_RESOURCE_URI]', '[YOUR_SCOPE]') {
            if ((Get-Content $propsOut -Raw).Contains($placeholder)) { throw "$placeholder is still in the sign-in settings. Pass the matching parameter." }
        }
    }

    if ($DryRun) {
        $keepWork = $true
        Write-Host 'Dry run: nothing was deployed. These files hold the keys, so delete the folder when you are done:' -ForegroundColor Yellow
        Write-Host "  $work"
        return
    }

    # ── Deploy ───────────────────────────────────────────────────────────

    $pacArgs = @('connector', $(if ($ConnectorId) { 'update' } else { 'create' }),
        '--environment', $Environment,
        '--api-definition-file', $swaggerOut,
        '--api-properties-file', $propsOut,
        '--script-file', $scriptOut)
    if ($ConnectorId) { $pacArgs += @('--connector-id', $ConnectorId) }

    $output = & pac @pacArgs 2>&1 | ForEach-Object { "$_" }
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "pac connector $($pacArgs[1]) failed. The temporary files were removed." }

    if (-not $ConnectorId) {
        $id = [regex]::Match(($output -join "`n"), '[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')
        if ($id.Success) { $ConnectorId = $id.Value; Write-Host "`nConnector ID: $ConnectorId" -ForegroundColor Green }
        else { Write-Host "`nCouldn't find the connector ID in the output above. Copy it from there." -ForegroundColor Yellow }
    }

    if ($Auth -eq 'EntraId' -and $ConnectorId -and -not $ClientSecret) {
        $download = Join-Path $work 'downloaded'
        try {
            & pac connector download --environment $Environment --connector-id $ConnectorId --outputDirectory $download 2>&1 | Out-Null
            $redirect = (Read-Json (Join-Path $download 'apiProperties.json')).properties.connectionParameters.token.oAuthSettings.redirectUrl
            Write-Host ''
            Write-Host 'Next, register this redirect URL on your app registration, then run this script again with -ConnectorId and -ClientSecret:' -ForegroundColor Cyan
            Write-Host "  $redirect"
        }
        catch { Write-Host "Couldn't read the redirect URL. Run pac connector download, and look under properties.connectionParameters.token.oAuthSettings.redirectUrl." -ForegroundColor Yellow }
    }
}
finally {
    if (-not $keepWork) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
