<#
.SYNOPSIS
    Targets the plugin at one Salesforce hosted MCP server.

.DESCRIPTION
    Rewrites manifest.json to point at the chosen Salesforce-hosted MCP server
    and declares only the skills that server's tools can actually support.

    The Salesforce sObject servers expose different tool subsets, so a skill
    that calls createSobjectRecord is dead weight on a read-only server, and a
    skill that calls getUserInfo cannot scope "my deals" on a server that does
    not expose it. This script computes the supported set from a tool matrix
    rather than leaving it to manual manifest edits.

    Skill folders are never deleted, so re-running with a different server
    restores the fuller set.

.PARAMETER Server
    Which Salesforce standard hosted server to target:
      sobject-all       Full CRUD (default)
      sobject-reads     Read-only
      sobject-mutations Create and update, no delete, no getUserInfo
      sobject-deletes   Delete only, no getUserInfo

.PARAMETER CustomServer
    Target a custom Salesforce MCP server by its org-specific path, e.g.
    "myorg/crm-plus". Custom servers are configured in Salesforce Setup and can
    combine sObject tools with Flows, Apex invocable actions, @AuraEnabled
    methods, Apex REST, and API Catalog endpoints.

.PARAMETER BaseOn
    Which standard server's tool set the custom server is assumed to include,
    used to pick the skill set. Defaults to sobject-all. Verify against the
    server's actual tools/list output.

.PARAMETER Sandbox
    Target a sandbox or scratch org (inserts 'sandbox/' into the server URL).

.PARAMETER ReferenceId
    Set the OAuth auth config ID (the Teams developer portal's OAuth client
    registration ID) at the same time.

.EXAMPLE
    .\configure.ps1 -Server sobject-reads
    .\configure.ps1 -Server sobject-all -Sandbox
    .\configure.ps1 -Server sobject-all -ReferenceId "a1b2c3d4-..."
    .\configure.ps1 -CustomServer "myorg/crm-plus"
    .\configure.ps1 -CustomServer "myorg/crm-readonly" -BaseOn sobject-reads
    .\configure.ps1 -ListServers
#>

[CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = "Standard")]
param(
    [Parameter(ParameterSetName = "Standard")]
    [ValidateSet("sobject-all", "sobject-reads", "sobject-mutations", "sobject-deletes")]
    [string]$Server = "sobject-all",

    [Parameter(ParameterSetName = "Custom", Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_\-]*(/[A-Za-z0-9_\-]+)+$')]
    [string]$CustomServer,

    [Parameter(ParameterSetName = "Custom")]
    [ValidateSet("sobject-all", "sobject-reads", "sobject-mutations", "sobject-deletes")]
    [string]$BaseOn = "sobject-all",

    [switch]$Sandbox,

    [string]$ReferenceId,

    [Parameter(ParameterSetName = "List")]
    [switch]$ListServers
)

$ErrorActionPreference = "Stop"

# Tools each Salesforce hosted sObject server exposes.
$serverTools = [ordered]@{
    "sobject-reads"     = @("getObjectSchema", "soqlQuery", "find", "getUserInfo",
                            "listRecentSobjectRecords", "getRelatedRecords")
    "sobject-mutations" = @("getObjectSchema", "soqlQuery", "find",
                            "createSobjectRecord", "updateSobjectRecord",
                            "updateRelatedSobjectRecord")
    "sobject-deletes"   = @("getObjectSchema", "soqlQuery", "find",
                            "deleteSobjectRecord", "deleteRelatedSobjectRecord")
    "sobject-all"       = @("getObjectSchema", "soqlQuery", "find", "getUserInfo",
                            "listRecentSobjectRecords", "getRelatedRecords",
                            "createSobjectRecord", "updateSobjectRecord",
                            "updateRelatedSobjectRecord", "deleteSobjectRecord",
                            "deleteRelatedSobjectRecord")
}

# Tools each skill cannot function without. Order here is the manifest order.
$skillCatalog = @(
    @{ name = "account-briefing";           requires = @("find", "soqlQuery") }
    @{ name = "opportunity-health-summary"; requires = @("soqlQuery", "getUserInfo") }
    @{ name = "pipeline-review";            requires = @("soqlQuery", "getUserInfo") }
    @{ name = "open-risks-and-blockers";    requires = @("soqlQuery", "getUserInfo") }
    @{ name = "next-best-action";           requires = @("soqlQuery", "find") }
    @{ name = "find-contacts";              requires = @("find", "soqlQuery") }
    @{ name = "review-tasks";               requires = @("soqlQuery", "getUserInfo") }
    @{ name = "lead-followup";              requires = @("soqlQuery", "getUserInfo") }
    @{ name = "case-triage";                requires = @("soqlQuery", "getUserInfo") }
    @{ name = "explore-salesforce-data";    requires = @("getObjectSchema", "soqlQuery") }
    @{ name = "create-account";             requires = @("createSobjectRecord", "find") }
    @{ name = "update-account";             requires = @("updateSobjectRecord", "find", "soqlQuery") }
    @{ name = "create-opportunity";         requires = @("createSobjectRecord", "find") }
    @{ name = "update-opportunity";         requires = @("updateSobjectRecord", "find", "soqlQuery") }
    @{ name = "add-contact";                requires = @("createSobjectRecord", "find") }
    @{ name = "update-contact";             requires = @("updateSobjectRecord", "find", "soqlQuery") }
    @{ name = "log-call-notes";             requires = @("createSobjectRecord", "find") }
    @{ name = "delete-salesforce-record";   requires = @("deleteSobjectRecord", "find", "soqlQuery") }
    # Meta skill: needs no connector tools. Persists to a Salesforce custom
    # object when one exists, otherwise session-only, so it ships on every server.
    @{ name = "improve-skills";             requires = @() }
)

if ($ListServers) {
    Write-Host "`nSalesforce hosted sObject servers" -ForegroundColor Cyan
    Write-Host ("=" * 50)
    foreach ($name in $serverTools.Keys) {
        $tools = $serverTools[$name]
        $supported = @($skillCatalog | Where-Object {
            $reqs = $_.requires
            @($reqs | Where-Object { $tools -notcontains $_ }).Count -eq 0
        })
        Write-Host ("{0,-18} {1,2} tools -> {2,2} of {3} skills" -f `
            $name, $tools.Count, $supported.Count, $skillCatalog.Count)
    }
    Write-Host "`nData 360 and Tableau Next servers expose unrelated tools and are"
    Write-Host "not supported by these skills. See readme.md."
    Write-Host "`nFor a custom server built in Salesforce Setup, use:"
    Write-Host "  .\configure.ps1 -CustomServer 'myorg/crm-plus' [-BaseOn sobject-all]"
    exit 0
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root "manifest.json"

if (-not (Test-Path $manifestPath)) {
    Write-Error "manifest.json not found at plugin root."
    exit 1
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

$isCustom = $PSCmdlet.ParameterSetName -eq "Custom"
$toolSet = if ($isCustom) { $BaseOn } else { $Server }
$label = if ($isCustom) { $CustomServer } else { "platform/$Server" }

$tools = $serverTools[$toolSet]
$selected = @($skillCatalog | Where-Object {
    $reqs = $_.requires
    @($reqs | Where-Object { $tools -notcontains $_ }).Count -eq 0
})
$excluded = @($skillCatalog | Where-Object { $selected.name -notcontains $_.name })

# Every selected skill must exist on disk.
foreach ($skill in $selected) {
    $skillFile = Join-Path $root "skills\$($skill.name)\SKILL.md"
    if (-not (Test-Path $skillFile)) {
        Write-Error "Skill folder 'skills\$($skill.name)' is missing; cannot configure."
        exit 1
    }
}

$segment = if ($Sandbox) { "sandbox/$label" } else { $label }
$url = "https://api.salesforce.com/platform/mcp/v1/$segment"

$connector = $manifest.agentConnectors[0]
$previousUrl = [string]$connector.toolSource.remoteMcpServer.mcpServerUrl

if (-not $PSCmdlet.ShouldProcess($manifestPath, "Target $label")) {
    exit 0
}

$connector.toolSource.remoteMcpServer.mcpServerUrl = $url

$mutateTools = @("createSobjectRecord", "updateSobjectRecord")
$canWrite = @($mutateTools | Where-Object { $tools -contains $_ }).Count -gt 0
$canDelete = $tools -contains "deleteSobjectRecord"

$capability = if ($canWrite -and $canDelete) { "Query, create, update, and delete" }
              elseif ($canWrite) { "Query, create, and update" }
              elseif ($canDelete) { "Query and delete" }
              else { "Query" }

$connector.description = "$capability Salesforce records through the Salesforce-hosted $label MCP server. Runs as the signed-in Salesforce user."

if ($ReferenceId) {
    $connector.toolSource.remoteMcpServer.authorization.referenceId = $ReferenceId
}

$manifest.agentSkills = @($selected | ForEach-Object { [PSCustomObject]@{ folder = "./skills/$($_.name)" } })

# ConvertTo-Json expands every object across multiple lines. Collapse the
# single-property skill entries back to one line each so the manifest stays
# readable and diffs stay small.
$json = $manifest | ConvertTo-Json -Depth 10
$json = [regex]::Replace(
    $json,
    '\{\s*\r?\n\s*"folder":\s*("[^"]*")\s*\r?\n\s*\}',
    '{ "folder": $1 }'
)
Set-Content -Path $manifestPath -Value $json -Encoding UTF8

Write-Host "`nConfigured plugin for '$label'" -ForegroundColor Cyan
Write-Host ("=" * 50)
Write-Host "  Server URL : $url"
if ($previousUrl -ne $url) {
    Write-Host "  Was        : $previousUrl" -ForegroundColor DarkGray
}
if ($isCustom) {
    Write-Host "  Tool set   : assumed to match $BaseOn ($($tools.Count) tools)"
}
else {
    Write-Host "  Tools      : $($tools.Count)"
}
Write-Host "  Skills     : $($selected.Count) of $($skillCatalog.Count)"
if ($ReferenceId) {
    Write-Host "  Reference  : $ReferenceId"
}

if ($excluded.Count -gt 0) {
    Write-Host "`nExcluded ($($excluded.Count)) - the server lacks the tools these need:" -ForegroundColor Yellow
    foreach ($skill in $excluded) {
        $missing = @($skill.requires | Where-Object { $tools -notcontains $_ }) -join ", "
        Write-Host ("  - {0,-28} needs {1}" -f $skill.name, $missing) -ForegroundColor Yellow
    }
    Write-Host "`nTheir folders were left in place, so re-running with a broader" -ForegroundColor DarkGray
    Write-Host "server restores them." -ForegroundColor DarkGray
}

if ($tools -notcontains "getUserInfo") {
    Write-Host "`nNote: this server does not expose getUserInfo, so skills cannot" -ForegroundColor Yellow
    Write-Host "resolve 'my deals' or 'my tasks' on their own." -ForegroundColor Yellow
}

if ($isCustom) {
    Write-Host "`nCustom server: verify its tools/list output matches the $BaseOn" -ForegroundColor Yellow
    Write-Host "tool set before publishing. If it exposes extra tools (Flows, Apex" -ForegroundColor Yellow
    Write-Host "actions, Connect APIs), add skills that name them." -ForegroundColor Yellow
    Write-Host "preflight.ps1 cannot validate custom server tool sets." -ForegroundColor Yellow
}

if ($Sandbox) {
    Write-Host "`nSandbox URL set. Use test.salesforce.com OAuth endpoints in the" -ForegroundColor Yellow
    Write-Host "Teams developer portal registration, and set Base URL to match." -ForegroundColor Yellow
}

Write-Host "`nNext: ./preflight.ps1 then ./package.ps1"
exit 0
