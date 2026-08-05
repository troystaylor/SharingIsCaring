#Requires -Version 7.0
<#
.SYNOPSIS
    Reports the harness, projected components, and node-kind vocabulary of a cloned Copilot Studio agent.

.DESCRIPTION
    Reads the extension state in .mcs/ to identify which authoring shape an agent uses, lists what
    was projected to disk, and extracts every node kind actually present in the definition.

    Use the extracted vocabulary instead of guessing kinds. Microsoft's documentation shows five;
    a real agent uses closer to thirty.

.PARAMETER Path
    The cloned agent folder. Accepts either the folder containing .mcs, or its parent — clone
    nests a folder named after the agent inside the one you select.

.PARAMETER Kinds
    Emit only the node-kind vocabulary, one per line, for piping.

.EXAMPLE
    ./Get-AgentSchema.ps1 "C:\agents\Procurement Strategy Harness"

.EXAMPLE
    ./Get-AgentSchema.ps1 . -Kinds
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = '.',

    [switch]$Kinds
)

$ErrorActionPreference = 'Stop'

$root = Get-Item -LiteralPath $Path
if (-not (Test-Path -LiteralPath (Join-Path $root.FullName '.mcs'))) {
    $nested = Get-ChildItem -LiteralPath $root.FullName -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '.mcs') } |
        Select-Object -First 1
    if (-not $nested) {
        throw "No cloned agent at '$Path'. Expected a .mcs folder here or one level down."
    }
    $root = $nested
}

$mcs = Join-Path $root.FullName '.mcs'
$defPath = Join-Path $mcs 'botdefinition.json'
if (-not (Test-Path -LiteralPath $defPath)) { throw "Missing $defPath." }

# -AsHashtable is required: the definition contains keys differing only by case (id and Id).
$def = Get-Content -LiteralPath $defPath -Raw | ConvertFrom-Json -AsHashtable

$nodeKinds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($component in $def.components) {
    foreach ($field in 'dialog', 'metadata') {
        $payload = $component[$field]
        if ($payload -isnot [string]) { continue }
        foreach ($match in [regex]::Matches($payload, '(?m)^\s*(?:-\s*)?kind:\s*([A-Za-z0-9_.]+)')) {
            [void]$nodeKinds.Add($match.Groups[1].Value)
        }
    }
}

if ($Kinds) {
    $nodeKinds | Sort-Object
    return
}

$conn = Get-Content -LiteralPath (Join-Path $mcs 'conn.json') -Raw | ConvertFrom-Json
$settingsPath = Join-Path $root.FullName 'settings.mcs.yml'
$settings = if (Test-Path -LiteralPath $settingsPath) { Get-Content -LiteralPath $settingsPath -Raw } else { '' }

function Get-YamlScalar([string]$Text, [string]$Key) {
    $m = [regex]::Match($Text, "(?m)^\s*$Key\s*:\s*(\S+)\s*$")
    if ($m.Success) { $m.Groups[1].Value } else { '(not found)' }
}

$template = Get-YamlScalar $settings 'template'
$harness = switch ($conn.AuthoringShape) {
    1 { 'Standard harness' }
    2 { 'GitHub Copilot harness' }
    default { "Unknown (AuthoringShape=$($conn.AuthoringShape))" }
}

Write-Host "`nAGENT" -ForegroundColor Cyan
[pscustomobject]@{
    Name           = $def.entity['displayName']
    SchemaName     = $def.entity['schemaName']
    Harness        = $harness
    AuthoringShape = $conn.AuthoringShape
    Template       = $template
    Recognizer     = Get-YamlScalar $settings 'kind'
    Environment    = $conn.EnvironmentDisplayName
    Publisher      = ($def.components | ForEach-Object { $_['publisherUniqueName'] } | Sort-Object -Unique) -join ', '
} | Format-List

Write-Host "COMPONENTS ($($def.components.Count))" -ForegroundColor Cyan
$def.components |
    Group-Object { $_['$kind'] } |
    ForEach-Object { "  {0,-18} x{1}" -f $_.Name, $_.Count }

Write-Host "`nPROJECTED TO DISK" -ForegroundColor Cyan
$projected = Get-ChildItem -LiteralPath $root.FullName -Recurse -File |
    Where-Object { $_.FullName -notlike "*\.mcs\*" }
if ($projected) {
    $projected | ForEach-Object { "  " + $_.FullName.Replace($root.FullName + '\', '') }
} else {
    Write-Host "  (nothing — definition exists only in the git-ignored .mcs folder)" -ForegroundColor Yellow
}

Write-Host "`nVERIFIED NODE KINDS ($($nodeKinds.Count))" -ForegroundColor Cyan
Write-Host "  Safe to use. Anything outside this list needs IntelliSense confirmation.`n"
$nodeKinds | Sort-Object | ForEach-Object { "  $_" }
Write-Host ""
