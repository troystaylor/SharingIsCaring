<#
.SYNOPSIS
    Validates and packages a Cowork plugin into a deployable .zip file.

.DESCRIPTION
    Checks the plugin structure against Cowork validation rules (ASKILL-M*,
    ASKILL-P*), verifies SKILL.md frontmatter, and creates a .zip package
    ready for sideloading or App Store submission.

.PARAMETER Path
    Path to the plugin folder. Defaults to the current directory.

.PARAMETER OutputPath
    Path for the output .zip file. Defaults to {folder-name}.zip in the
    current directory.

.PARAMETER SkipIcons
    Skip icon validation (useful during development when placeholder icons
    are not yet created).

.EXAMPLE
    .\package.ps1
    .\package.ps1 -Path .\my-plugin -OutputPath .\dist\my-plugin.zip
    .\package.ps1 -SkipIcons
    .\package.ps1 -Json | ConvertFrom-Json
#>

param(
    [string]$Path = ".",
    [string]$OutputPath,
    [switch]$SkipIcons,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

$pluginPath = Resolve-Path $Path
$pluginName = Split-Path $pluginPath -Leaf

if (-not $OutputPath) {
    $OutputPath = Join-Path (Get-Location) "$pluginName.zip"
}

$errors = @()
$warnings = @()

Write-Host "`nValidating Cowork plugin: $pluginName" -ForegroundColor Cyan
Write-Host ("=" * 50)

# --- Manifest validation ---

$manifestPath = Join-Path $pluginPath "manifest.json"
if (-not (Test-Path $manifestPath)) {
    $errors += "manifest.json not found in plugin root"
}
else {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    if (-not $manifest.id -or $manifest.id -eq "{{GUID}}") {
        $errors += "manifest.json: 'id' must be a valid GUID (not a placeholder)"
    }

    if (-not $manifest.manifestVersion) {
        $errors += "manifest.json: 'manifestVersion' is required"
    }

    # ASKILL-M001: folder is required on each agentSkills entry
    if ($manifest.agentSkills) {
        # ASKILL-M002: Max 20 items
        if ($manifest.agentSkills.Count -gt 20) {
            $errors += "ASKILL-M002: agentSkills has $($manifest.agentSkills.Count) entries (max 20)"
        }

        foreach ($skill in $manifest.agentSkills) {
            if (-not $skill.folder) {
                $errors += "ASKILL-M001: 'folder' is required on each agentSkills entry"
            }
            else {
                # ASKILL-M003: folder path max 256 characters
                if ($skill.folder.Length -gt 256) {
                    $errors += "ASKILL-M003: folder path exceeds 256 characters: $($skill.folder)"
                }
            }
        }
    }

    # Connector validation
    if ($manifest.agentConnectors) {
        if ($manifest.agentConnectors.Count -gt 10) {
            $errors += "agentConnectors has $($manifest.agentConnectors.Count) entries (max 10)"
        }

        $connectorIds = @()
        foreach ($conn in $manifest.agentConnectors) {
            if (-not $conn.id) { $errors += "Connector missing 'id'" }
            if (-not $conn.displayName) { $errors += "Connector missing 'displayName'" }
            if (-not $conn.toolSource) { $errors += "Connector missing 'toolSource'" }

            if ($conn.id -and $connectorIds -contains $conn.id) {
                $errors += "Duplicate connector id: $($conn.id)"
            }
            $connectorIds += $conn.id

            if ($conn.toolSource.remoteMcpServer) {
                $url = $conn.toolSource.remoteMcpServer.mcpServerUrl
                if ($url -and $url -notmatch '^https://') {
                    $errors += "Connector '$($conn.id)': mcpServerUrl must be HTTPS"
                }
                if ($url -and $url -match '\{\{') {
                    $warnings += "Connector '$($conn.id)': mcpServerUrl contains template placeholder"
                }

                # Authorization validation per connector rules
                $auth = $conn.toolSource.remoteMcpServer.authorization
                if ($auth) {
                    if ($auth.type -eq "None" -and $auth.referenceId) {
                        $errors += "Connector '$($conn.id)': referenceId must not be present when authorization type is 'None'"
                    }
                    if ($auth.type -and $auth.type -ne "None" -and -not $auth.referenceId) {
                        $errors += "Connector '$($conn.id)': referenceId is required when authorization type is '$($auth.type)'"
                    }
                }
            }
        }
    }
}

# --- Icon validation ---

if (-not $SkipIcons) {
    $colorIcon = Join-Path $pluginPath "color.png"
    $outlineIcon = Join-Path $pluginPath "outline.png"

    if (-not (Test-Path $colorIcon)) {
        $errors += "color.png not found (192x192 full-color icon required)"
    }
    if (-not (Test-Path $outlineIcon)) {
        $errors += "outline.png not found (32x32 outline icon required)"
    }
}

# --- Skills validation ---

$skillsPath = Join-Path $pluginPath "skills"

if ($manifest.agentSkills) {
    foreach ($skillEntry in $manifest.agentSkills) {
        $folder = $skillEntry.folder -replace '^\.\/', ''
        $skillDir = Join-Path $pluginPath $folder
        $folderName = Split-Path $skillDir -Leaf

        # ASKILL-P001: Folder exists
        if (-not (Test-Path $skillDir)) {
            $errors += "ASKILL-P001: Folder '$folder' referenced in manifest does not exist"
            continue
        }

        $skillMd = Join-Path $skillDir "SKILL.md"

        # ASKILL-P002: SKILL.md exists
        if (-not (Test-Path $skillMd)) {
            $errors += "ASKILL-P002: '$folder' does not contain a SKILL.md file"
            continue
        }

        # Parse YAML frontmatter
        $content = Get-Content $skillMd -Raw
        if ($content -notmatch '(?s)^---\r?\n(.+?)\r?\n---') {
            $errors += "ASKILL-P003: '$folder/SKILL.md' has no valid YAML frontmatter"
            continue
        }

        $frontmatter = $Matches[1]

        # ASKILL-P004: name field
        if ($frontmatter -notmatch 'name:\s*(\S+)') {
            $errors += "ASKILL-P004: '$folder/SKILL.md' missing 'name' in frontmatter"
            continue
        }
        $skillName = $Matches[1]

        # ASKILL-P005: description field
        if ($frontmatter -notmatch 'description:') {
            $errors += "ASKILL-P005: '$folder/SKILL.md' missing 'description' in frontmatter"
        }

        # ASKILL-P006: name matches folder name
        if ($skillName -ne $folderName) {
            $errors += "ASKILL-P006: Skill name '$skillName' does not match folder name '$folderName'"
        }

        # ASKILL-P007: name is kebab-case
        if ($skillName -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
            $errors += "ASKILL-P007: Skill name '$skillName' is not kebab-case"
        }

        # Built-in skill name conflict check
        $builtinSkills = @(
            "word", "excel", "powerpoint", "pdf", "email", "scheduling",
            "calendar-management", "meetings", "daily-briefing",
            "enterprise-search", "deep-research", "communications",
            "adaptive-cards"
        )
        if ($builtinSkills -contains $skillName) {
            $errors += "Skill name '$skillName' conflicts with a Cowork built-in skill. Built-in skills take priority and your skill will be silently skipped."
        }

        # Companion file validation
        $companionFiles = Get-ChildItem $skillDir -Recurse -File |
            Where-Object { $_.Name -ne "SKILL.md" }

        if ($companionFiles.Count -gt 20) {
            $errors += "'$folder': $($companionFiles.Count) companion files (max 20)"
        }

        $totalSize = 0
        foreach ($file in $companionFiles) {
            $sizeBytes = $file.Length
            $totalSize += $sizeBytes

            if ($sizeBytes -gt 5MB) {
                $errors += "'$folder/$($file.Name)': $([math]::Round($sizeBytes/1MB, 1))MB (max 5MB per file)"
            }

            if ($file.Name -match '\\|\x00') {
                $errors += "'$folder/$($file.Name)': contains backslash or null byte"
            }

            if ($file.Name.StartsWith('.')) {
                $errors += "'$folder/$($file.Name)': hidden files not allowed"
            }

            if ($file.BaseName -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
                $errors += "'$folder/$($file.Name)': Windows reserved name"
            }
        }

        if ($totalSize -gt 10MB) {
            $errors += "'$folder': total companion size $([math]::Round($totalSize/1MB, 1))MB (max 10MB)"
        }

        # Content check
        if ($content -match '\{\{') {
            $warnings += "'$folder/SKILL.md' contains template placeholders ({{ }})"
        }
    }

    # ASKILL-P008: No duplicate folders
    $folders = $manifest.agentSkills | ForEach-Object { $_.folder }
    $dupes = $folders | Group-Object | Where-Object { $_.Count -gt 1 }
    foreach ($dupe in $dupes) {
        $errors += "ASKILL-P008: Duplicate folder '$($dupe.Name)' in agentSkills"
    }
}

# --- Report ---

if ($Json) {
    $skillCount = if ($manifest.agentSkills) { $manifest.agentSkills.Count } else { 0 }
    $connCount = if ($manifest.agentConnectors) { $manifest.agentConnectors.Count } else { 0 }
    $result = @{
        valid = ($errors.Count -eq 0)
        errors = $errors
        warnings = $warnings
        skillCount = $skillCount
        connectorCount = $connCount
        version = if ($manifest.version) { $manifest.version } else { "unknown" }
    }

    if ($errors.Count -eq 0) {
        if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
        $items = @("manifest.json")
        if (Test-Path (Join-Path $pluginPath "color.png")) { $items += "color.png" }
        if (Test-Path (Join-Path $pluginPath "outline.png")) { $items += "outline.png" }
        if (Test-Path (Join-Path $pluginPath "skills")) { $items += "skills" }
        $itemPaths = $items | ForEach-Object { Join-Path $pluginPath $_ }
        Compress-Archive -Path $itemPaths -DestinationPath $OutputPath -Force
        $result.outputPath = $OutputPath
        $result.sizeKB = [math]::Round((Get-Item $OutputPath).Length / 1KB, 1)
    }

    $result | ConvertTo-Json -Depth 5
    if ($errors.Count -gt 0) { exit 1 }
    exit 0
}

Write-Host ""

if ($warnings.Count -gt 0) {
    Write-Host "Warnings ($($warnings.Count)):" -ForegroundColor Yellow
    foreach ($w in $warnings) {
        Write-Host "  ! $w" -ForegroundColor Yellow
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Errors ($($errors.Count)):" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  x $e" -ForegroundColor Red
    }
    Write-Host "`nFix errors before packaging." -ForegroundColor Red
    exit 1
}

Write-Host "All checks passed." -ForegroundColor Green

# --- Package ---

Write-Host "`nPackaging..." -ForegroundColor Cyan

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

$items = @("manifest.json")

if (Test-Path (Join-Path $pluginPath "color.png")) { $items += "color.png" }
if (Test-Path (Join-Path $pluginPath "outline.png")) { $items += "outline.png" }
if (Test-Path (Join-Path $pluginPath "skills")) { $items += "skills" }

$itemPaths = $items | ForEach-Object { Join-Path $pluginPath $_ }

Compress-Archive -Path $itemPaths -DestinationPath $OutputPath -Force

$zipSize = [math]::Round((Get-Item $OutputPath).Length / 1KB, 1)
Write-Host "Created: $OutputPath ($($zipSize) KB)" -ForegroundColor Green

$skillCount = if ($manifest.agentSkills) { $manifest.agentSkills.Count } else { 0 }
$connCount = if ($manifest.agentConnectors) { $manifest.agentConnectors.Count } else { 0 }
Write-Host "Contents: $skillCount skill(s), $connCount connector(s)" -ForegroundColor Cyan
