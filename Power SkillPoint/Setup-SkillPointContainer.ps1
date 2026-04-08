# Power SkillPoint — SharePoint Embedded Setup

# This script creates a SharePoint Embedded container type and container
# for Power SkillPoint skill storage.
#
# Prerequisites:
# - Azure CLI installed (az)
# - Microsoft Graph PowerShell module (Install-Module Microsoft.Graph)
# - App registration with FileStorageContainer.Selected permission
# - SharePoint Embedded Administrator or Global Administrator role
#
# Usage:
#   .\Setup-SkillPointContainer.ps1 -AppId "your-app-client-id" -TenantId "your-tenant-id"

param(
    [Parameter(Mandatory = $true)]
    [string]$AppId,

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter()]
    [string]$ContainerTypeName = "PowerSkillPoint",

    [Parameter()]
    [string]$ContainerName = "SkillLibrary"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== Power SkillPoint SPE Setup ===" -ForegroundColor Cyan

# ── Step 1: Connect to Microsoft Graph ────────────────────────

Write-Host "`n[1/5] Connecting to Microsoft Graph..." -ForegroundColor Yellow
Connect-MgGraph -TenantId $TenantId -Scopes @(
    "FileStorageContainer.Selected",
    "Application.ReadWrite.All"
) -NoWelcome

$context = Get-MgContext
Write-Host "  Connected as: $($context.Account)" -ForegroundColor Green

# ── Step 2: Create Container Type ─────────────────────────────

Write-Host "`n[2/5] Creating container type: $ContainerTypeName..." -ForegroundColor Yellow

$containerTypeBody = @{
    displayName    = $ContainerTypeName
    description    = "Power SkillPoint skill storage — behavioral skills for Copilot Studio agents"
    owningAppId    = $AppId
    azureSubscriptionId = $null
} | ConvertTo-Json -Depth 5

try {
    $containerType = Invoke-MgGraphRequest -Method POST `
        -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes" `
        -Body $containerTypeBody `
        -ContentType "application/json"

    $containerTypeId = $containerType.id
    Write-Host "  Container type created: $containerTypeId" -ForegroundColor Green
}
catch {
    if ($_.Exception.Message -match "already exists") {
        Write-Host "  Container type already exists. Retrieving..." -ForegroundColor Yellow
        $existing = Invoke-MgGraphRequest -Method GET `
            -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes?`$filter=displayName eq '$ContainerTypeName'"
        $containerTypeId = $existing.value[0].id
        Write-Host "  Found existing: $containerTypeId" -ForegroundColor Green
    }
    else { throw }
}

# ── Step 3: Register Container Type in Tenant ─────────────────

Write-Host "`n[3/5] Registering container type in tenant..." -ForegroundColor Yellow

try {
    Invoke-MgGraphRequest -Method PUT `
        -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/$containerTypeId" `
        -Body "{}" `
        -ContentType "application/json"

    Write-Host "  Registered in consuming tenant" -ForegroundColor Green
}
catch {
    if ($_.Exception.Message -match "already registered") {
        Write-Host "  Already registered" -ForegroundColor Green
    }
    else { Write-Warning "Registration warning: $_" }
}

# ── Step 4: Create Container ──────────────────────────────────

Write-Host "`n[4/5] Creating container: $ContainerName..." -ForegroundColor Yellow

$containerBody = @{
    displayName    = $ContainerName
    description    = "Skill library for Power SkillPoint agent"
    containerTypeId = $containerTypeId
} | ConvertTo-Json -Depth 5

try {
    $container = Invoke-MgGraphRequest -Method POST `
        -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
        -Body $containerBody `
        -ContentType "application/json"

    $containerId = $container.id
    Write-Host "  Container created: $containerId" -ForegroundColor Green
}
catch {
    Write-Error "Failed to create container: $_"
    throw
}

# ── Step 5: Create Folder Structure ───────────────────────────

Write-Host "`n[5/5] Creating folder structure..." -ForegroundColor Yellow

$folders = @(
    "_index",
    "_evals",
    "_skill-author",
    "org-skills",
    "user-skills"
)

foreach ($folder in $folders) {
    $folderBody = @{
        name                = $folder
        folder              = @{}
        "@microsoft.graph.conflictBehavior" = "fail"
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-MgGraphRequest -Method POST `
            -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers/$containerId/drive/root/children" `
            -Body $folderBody `
            -ContentType "application/json" | Out-Null

        Write-Host "  Created: /$folder" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "nameAlreadyExists") {
            Write-Host "  Exists:  /$folder" -ForegroundColor DarkGray
        }
        else { Write-Warning "  Failed: /$folder — $_" }
    }
}

# ── Summary ───────────────────────────────────────────────────

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Type ID: $containerTypeId"
Write-Host "Container ID:      $containerId"
Write-Host ""
Write-Host "Use this Container ID in your Power SkillPoint connector:" -ForegroundColor Yellow
Write-Host "  Connection parameter: containerId = $containerId" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Upload example skills to the container"
Write-Host "  2. Import the connector in Power Platform"
Write-Host "  3. Create a connection with containerId = $containerId"
Write-Host "  4. Add to your Copilot Studio agent"
Write-Host ""

# Output object for pipeline use
[PSCustomObject]@{
    ContainerTypeId = $containerTypeId
    ContainerId     = $containerId
    AppId           = $AppId
    TenantId        = $TenantId
}
