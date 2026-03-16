# Install a skills repo and auto-configure VS Code settings
param(
    [string]$RepoUrl = "https://github.com/microsoft/skills-for-copilot-studio",
    [string]$SkillsSubPath = "skills"  # Path inside repo where skills live
)

$SkillsRoot = "$env:USERPROFILE\.copilot\skills"
$RepoName = ($RepoUrl -split '/')[-1] -replace '\.git$', ''
$ClonePath = Join-Path $SkillsRoot $RepoName
$NestedSkillsPath = Join-Path $ClonePath $SkillsSubPath

# Create skills root if needed
if (-not (Test-Path $SkillsRoot)) {
    New-Item -ItemType Directory -Path $SkillsRoot -Force | Out-Null
}

# Clone the repo
if (Test-Path $ClonePath) {
    Write-Host "Repo already exists at $ClonePath - pulling latest..." -ForegroundColor Yellow
    git -C $ClonePath pull
} else {
    Write-Host "Cloning $RepoUrl..." -ForegroundColor Cyan
    git clone $RepoUrl $ClonePath
}

# Find VS Code settings.json (try Insiders first, then stable)
$SettingsPaths = @(
    "$env:APPDATA\Code - Insiders\User\settings.json",
    "$env:APPDATA\Code\User\settings.json"
)

$SettingsFile = $SettingsPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $SettingsFile) {
    $SettingsFile = $SettingsPaths[0]
    $SettingsDir = Split-Path $SettingsFile
    if (-not (Test-Path $SettingsDir)) {
        New-Item -ItemType Directory -Path $SettingsDir -Force | Out-Null
    }
    '{}' | Set-Content $SettingsFile
    Write-Host "Created new settings.json at $SettingsFile" -ForegroundColor Green
}

Write-Host "Using settings: $SettingsFile" -ForegroundColor Cyan

# Read and update settings
$Settings = Get-Content $SettingsFile -Raw | ConvertFrom-Json

# Get or create the agentSkillsLocations array
if (-not $Settings.'chat.agentSkillsLocations') {
    $Settings | Add-Member -NotePropertyName 'chat.agentSkillsLocations' -NotePropertyValue @()
}

# Add the nested path if not already present
$Locations = @($Settings.'chat.agentSkillsLocations')
if ($NestedSkillsPath -notin $Locations) {
    $Locations += $NestedSkillsPath
    $Settings.'chat.agentSkillsLocations' = $Locations
    $Settings | ConvertTo-Json -Depth 10 | Set-Content $SettingsFile
    Write-Host "Added $NestedSkillsPath to chat.agentSkillsLocations" -ForegroundColor Green
} else {
    Write-Host "Path already in settings" -ForegroundColor Yellow
}

Write-Host "
Done! Restart VS Code to load the new skills." -ForegroundColor Green
