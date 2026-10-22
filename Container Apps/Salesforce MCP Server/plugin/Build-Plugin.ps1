<#
.SYNOPSIS
    Regenerates the tool description, then packages (and optionally provisions) the plugin.

.DESCRIPTION
    1. Starts the server locally in Development mode, where sign-in is off, and captures its
       tools/list into appPackage/mcp-tools.json. The deployed server needs a token to list
       tools, and a locally started copy exposes the same set.
    2. Warns about any tool that no skill mentions.
    3. Packages the plugin with `wiqd plugin package` and runs deep validation.
    4. With -Provision, registers the plugin with Microsoft 365.

    Regeneration is the point. A plugin that describes tools the server no longer has is worse
    than one missing a tool, because an agent follows a stale description confidently. Run this
    after any change to the server's tools.

    The server URL and OAuth reference ID are environment-specific, so they are passed in and
    exported as environment variables for the manifest rather than stored in a file.

.EXAMPLE
    .\Build-Plugin.ps1 -McpUrl https://<fqdn>/mcp -TenantId <tenant-id> -AuthConfigId <oauth-config-id>

.EXAMPLE
    .\Build-Plugin.ps1 -McpUrl https://<fqdn>/mcp -TenantId <tenant-id> -AuthConfigId <id> -WriteTools -Provision
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^https://.+/mcp$')] [string] $McpUrl,

    # Your Microsoft 365 tenant ID: the tenant whose users will use the plugin.
    [Parameter(Mandatory)] [guid] $TenantId,

    # The ID of the OAuth client registration made in the Teams developer portal.
    [Parameter(Mandatory)] [guid] $AuthConfigId,

    [string] $Environment = 'dev',
    [string] $OutputPath = (Join-Path $PSScriptRoot "salesforce-mcp.$Environment.zip"),

    # Include the create, update, and delete tools in the description. Match your deployment.
    [switch] $WriteTools,

    # Keep the existing mcp-tools.json instead of regenerating it.
    [switch] $SkipRegenerate,

    [switch] $Provision,
    [ValidateRange(1024, 65535)] [int] $LocalPort = 5197
)

$ErrorActionPreference = 'Stop'
$serverProject = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\SalesforceMcp'
$package = Join-Path $PSScriptRoot 'appPackage'
$toolsFile = Join-Path $package 'mcp-tools.json'

if (-not (Get-Command wiqd -ErrorAction SilentlyContinue)) {
    throw 'wiqd was not found on PATH. Install the Work IQ Dev Tools CLI first.'
}

if (-not $SkipRegenerate) {
    Write-Host 'Reading tools from a local copy of the server' -ForegroundColor Cyan

    $saved = @{}
    $settings = @{
        ASPNETCORE_ENVIRONMENT         = 'Development'
        ASPNETCORE_URLS                = "http://127.0.0.1:$LocalPort"
        Salesforce__InstanceUrl        = 'https://example.my.salesforce.com'
        Salesforce__EnableWriteTools   = [string] [bool] $WriteTools
        Salesforce__EnableToolingTools = 'true'
    }
    foreach ($name in $settings.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $settings[$name])
    }

    # Start-Process does not quote array arguments, and project paths often contain spaces.
    $server = Start-Process dotnet -ArgumentList 'run', '--project', "`"$serverProject`"" -PassThru -WindowStyle Hidden
    try {
        $base = "http://127.0.0.1:$LocalPort"
        $deadline = (Get-Date).AddSeconds(120)
        do {
            Start-Sleep -Seconds 2
            try { $ready = (Invoke-WebRequest "$base/healthz" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 }
            catch { $ready = $false }
        } until ($ready -or (Get-Date) -gt $deadline -or $server.HasExited)
        if (-not $ready) { throw 'The local server did not start. Run it with `dotnet run` to see why.' }

        $response = Invoke-WebRequest "$base/mcp" -Method Post -ContentType 'application/json' -UseBasicParsing `
            -Headers @{ Accept = 'application/json, text/event-stream' } `
            -Body '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
        $data = ($response.Content -split "`n" | Where-Object { $_ -like 'data:*' } | Select-Object -First 1)
        $listing = if ($data) { $data.Substring(5).Trim() | ConvertFrom-Json } else { $response.Content | ConvertFrom-Json }
        $tools = $listing.result.tools
        if (-not $tools) { throw 'The server returned no tools.' }

        # The Microsoft 365 service requires `tools` at the top level, not wrapped as a JSON-RPC result.
        $json = [ordered]@{ tools = $tools } | ConvertTo-Json -Depth 30
        [IO.File]::WriteAllText($toolsFile, $json, [Text.UTF8Encoding]::new($false))
        Write-Host "Wrote $($tools.Count) tools to appPackage\mcp-tools.json"
    }
    finally {
        if (-not $server.HasExited) { $server.Kill($true) }
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}

# A tool no skill mentions is still usable, but Copilot gets no guidance on when to reach for it.
$toolNames = (Get-Content $toolsFile -Raw | ConvertFrom-Json).tools.name
$skillText = (Get-ChildItem (Join-Path $package 'skills') -Recurse -Filter SKILL.md | Get-Content -Raw) -join "`n"
$unmentioned = $toolNames | Where-Object { $skillText -notmatch "``$_``" }
if ($unmentioned) {
    Write-Warning "No skill mentions: $($unmentioned -join ', ')"
}

$env:SALESFORCE_MCP_URL = $McpUrl
$env:SALESFORCE_MCP_AUTH_REFERENCE_ID =
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$TenantId##$AuthConfigId"))

Push-Location $PSScriptRoot
try {
    # Provisioning creates TEAMS_APP_ID, which packaging needs, so on a first run it has to come first.
    if ($Provision) {
        Write-Host 'Provisioning' -ForegroundColor Cyan
        wiqd --no-banner plugin provision --env $Environment
        if ($LASTEXITCODE -ne 0) { throw 'Provisioning failed. Sign in first with `wiqd auth login --interactive`.' }
    }

    Write-Host 'Packaging' -ForegroundColor Cyan
    wiqd --no-banner plugin package --env $Environment --output $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed. On a first run, add -Provision so the app ID is created.' }

    Write-Host 'Validating the package' -ForegroundColor Cyan
    wiqd --no-banner plugin validate --env $Environment --mode deep --package-file $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'Validation failed.' }
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host "Package: $OutputPath" -ForegroundColor Green
