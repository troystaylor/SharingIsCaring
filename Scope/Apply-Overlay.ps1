<#
.SYNOPSIS
    Applies this folder's overlay to a clone of microsoft/scope.

.DESCRIPTION
    Copies the files in .\overlay over the matching paths in a clone of microsoft/scope, then
    applies each patch in .\patches. The overlay was made against the upstream commit in
    upstream.json; the script warns if your clone is on a different commit, because the overlay
    replaces whole files.

.PARAMETER ScopePath
    Path to your clone of https://github.com/microsoft/scope.

.PARAMETER SkipPatch
    Don't apply the patches in .\patches. Use this if your network reaches the public npm
    registry without problems.

.EXAMPLE
    git -c core.longpaths=true clone https://github.com/microsoft/scope.git C:\src\scope
    git -C C:\src\scope -c core.longpaths=true checkout <commit from upstream.json>
    .\Apply-Overlay.ps1 -ScopePath C:\src\scope
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScopePath,
    [switch]$SkipPatch
)

# git writes to stderr for expected conditions, such as a patch that is already applied. With
# 'Stop', Windows PowerShell turns that into a terminating error, so errors are handled
# explicitly with `throw` instead.
$ErrorActionPreference = 'Continue'
$Upstream = Get-Content (Join-Path $PSScriptRoot 'upstream.json') -Raw | ConvertFrom-Json
$Overlay = Join-Path $PSScriptRoot 'overlay'
$Patches = Join-Path $PSScriptRoot 'patches'

$ScopePath = (Resolve-Path $ScopePath).Path
if (-not (Test-Path (Join-Path $ScopePath 'pnpm-workspace.yaml'))) {
    throw "$ScopePath doesn't look like a Scope clone (no pnpm-workspace.yaml)."
}

$head = (git -C $ScopePath rev-parse HEAD 2>$null)
if ($head -ne $Upstream.commit) {
    Write-Warning "Your clone is at '$head', not the pinned commit $($Upstream.commit). The overlay replaces whole files and can discard newer upstream changes to them."
}

foreach ($f in Get-ChildItem $Overlay -Recurse -File) {
    $rel = $f.FullName.Substring($Overlay.Length + 1)
    $target = Join-Path $ScopePath $rel
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $f.FullName $target -Force
    Write-Host "copied  $rel"
}

if ($SkipPatch -or -not (Test-Path $Patches)) { return }

foreach ($patch in Get-ChildItem $Patches -Filter *.patch) {
    git -C $ScopePath apply --check --ignore-whitespace $patch.FullName 2>$null
    if ($LASTEXITCODE -eq 0) {
        git -C $ScopePath apply --ignore-whitespace $patch.FullName
        Write-Host "applied patches\$($patch.Name)"
        continue
    }
    git -C $ScopePath apply --reverse --check --ignore-whitespace $patch.FullName 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "patches\$($patch.Name) is already applied"
    } else {
        Write-Warning "patches\$($patch.Name) does not apply cleanly. Apply its changes by hand (see docs\maintenance.md)."
    }
}
