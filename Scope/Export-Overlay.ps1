<#
.SYNOPSIS
    Regenerates this folder's overlay and patch from a working Scope clone.

.DESCRIPTION
    The reverse of Apply-Overlay.ps1. Compares a clone of microsoft/scope with its HEAD and
    writes the differences here:

      overlay\   Whole files for README.md, docs\**, and scripts\local\** (modified or new).
      patches\local-changes.patch   A patch for every other modified tracked file, such as
                                    pnpm-workspace.yaml.

    Untracked files outside the overlay paths are listed and skipped. The existing overlay and
    patch are replaced, so deletions in the clone carry over.

    The clone's HEAD must match upstream.json. When you move to a newer upstream commit, check
    it out, re-apply the overlay, resolve conflicts, then run this with -UpdateCommit.

.PARAMETER ScopePath
    Path to your working clone of microsoft/scope.

.PARAMETER UpdateCommit
    Write the clone's HEAD to upstream.json instead of requiring it to match.

.PARAMETER WhatIf
    List what would be exported without changing anything.

.EXAMPLE
    .\Export-Overlay.ps1 -ScopePath C:\src\scope
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ScopePath,
    [switch]$UpdateCommit
)

$ErrorActionPreference = 'Stop'
$UpstreamFile = Join-Path $PSScriptRoot 'upstream.json'
$Upstream = Get-Content $UpstreamFile -Raw | ConvertFrom-Json
$Overlay = Join-Path $PSScriptRoot 'overlay'
$Patches = Join-Path $PSScriptRoot 'patches'
$OverlayPaths = @('README.md', 'docs/', 'scripts/local/')

$ScopePath = (Resolve-Path $ScopePath).Path
$head = (git -C $ScopePath rev-parse HEAD).Trim()
if ($head -ne $Upstream.commit -and -not $UpdateCommit) {
    throw "The clone is at $head but upstream.json pins $($Upstream.commit). Check out the pinned commit, or pass -UpdateCommit after rebasing your changes."
}

function Test-InOverlay([string]$RelativePath) {
    $p = $RelativePath -replace '\\', '/'
    foreach ($o in $OverlayPaths) {
        if ($o.EndsWith('/')) { if ($p.StartsWith($o)) { return $true } } elseif ($p -eq $o) { return $true }
    }
    return $false
}

$modified = @(git -C $ScopePath ls-files -m) | Where-Object { $_ }
$untracked = @(git -C $ScopePath ls-files -o --exclude-standard) | Where-Object { $_ }
$deleted = @(git -C $ScopePath ls-files -d) | Where-Object { $_ }

$overlayFiles = @(($modified + $untracked) | Where-Object { Test-InOverlay $_ } | Where-Object { $deleted -notcontains $_ } | Sort-Object -Unique)
$patchFiles = @($modified | Where-Object { -not (Test-InOverlay $_) -and $deleted -notcontains $_ } | Sort-Object -Unique)
$skipped = @($untracked | Where-Object { -not (Test-InOverlay $_) })
$deletedOverlay = @($deleted | Where-Object { Test-InOverlay $_ })

Write-Host "Overlay files ($($overlayFiles.Count)):"
$overlayFiles | ForEach-Object { Write-Host "  $_" }
Write-Host "Patched files ($($patchFiles.Count)):"
$patchFiles | ForEach-Object { Write-Host "  $_" }
if ($skipped.Count) {
    Write-Warning "Skipping $($skipped.Count) new file(s) outside the overlay paths ($($OverlayPaths -join ', ')):"
    $skipped | ForEach-Object { Write-Warning "  $_" }
}
if ($deletedOverlay.Count) {
    Write-Warning "These overlay-path upstream files are deleted in the clone; the overlay can't delete files: $($deletedOverlay -join ', ')"
}

if (-not $PSCmdlet.ShouldProcess($PSScriptRoot, 'Replace overlay and patches')) { return }

if (Test-Path $Overlay) { Remove-Item $Overlay -Recurse -Force }
foreach ($rel in $overlayFiles) {
    $target = Join-Path $Overlay $rel
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item (Join-Path $ScopePath $rel) $target -Force
}

if (Test-Path $Patches) { Get-ChildItem $Patches -Filter *.patch | Remove-Item -Force }
if ($patchFiles.Count) {
    New-Item -ItemType Directory -Force $Patches | Out-Null
    git -C $ScopePath diff --no-color --output="$(Join-Path $Patches 'local-changes.patch')" -- @patchFiles
}

if ($UpdateCommit -and $head -ne $Upstream.commit) {
    $Upstream.commit = $head
    $Upstream | ConvertTo-Json | Set-Content $UpstreamFile -Encoding UTF8
    Write-Host "Updated upstream.json to $head"
}
Write-Host 'Done. Review the changes with git before committing.'
