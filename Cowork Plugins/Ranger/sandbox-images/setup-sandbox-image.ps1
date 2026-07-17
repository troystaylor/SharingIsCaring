<#
.SYNOPSIS
    Build and register a PowerHoof sandbox disk image in ACA Sandbox Groups.

.DESCRIPTION
    Builds a container image in Azure Container Registry, then registers it as a
    disk image in the specified ACA Sandbox Group. Outputs the disk image ID for
    use as an environment variable on the PowerHoof MCP server.

.EXAMPLE
    ./setup-sandbox-image.ps1 -Registry acacodeinterpreter -ResourceGroup rg-aca-code-interpreter -SandboxGroup code-interpreter -ImageType code

.EXAMPLE
    ./setup-sandbox-image.ps1 -Registry myacr -ResourceGroup my-rg -SandboxGroup my-sandboxes -ImageType code -Region westus2
#>
param(
    [Parameter(Mandatory)][string]$Registry,         # ACR name (without .azurecr.io)
    [Parameter(Mandatory)][string]$ResourceGroup,    # Resource group containing sandbox group
    [Parameter(Mandatory)][string]$SandboxGroup,     # Sandbox group name
    [Parameter(Mandatory)][ValidateSet("code","browser")][string]$ImageType,
    [string]$Region = "westus2",                     # Region of sandbox group
    [string]$Tag = "latest",                         # Image tag
    [string]$Label                                   # Disk image label (default: powerhoof-{ImageType})
)

$ErrorActionPreference = "Stop"
$label = if ($Label) { $Label } else { "powerhoof-$ImageType" }
$imageName = "powerhoof-sandbox-$ImageType"
$fullImage = "$Registry.azurecr.io/${imageName}:$Tag"
$dockerfilePath = Join-Path $PSScriptRoot $ImageType "Dockerfile"

if (-not (Test-Path $dockerfilePath)) {
    Write-Error "Dockerfile not found at $dockerfilePath"
    return
}

Write-Host "=== PowerHoof Sandbox Image Setup ===" -ForegroundColor Cyan
Write-Host "Image type:    $ImageType"
Write-Host "Registry:      $Registry.azurecr.io"
Write-Host "Image:         $fullImage"
Write-Host "Sandbox group: $SandboxGroup (in $ResourceGroup)"
Write-Host ""

# Step 1: Build image in ACR
Write-Host "[1/3] Building container image in ACR..." -ForegroundColor Yellow
$buildContext = Join-Path $PSScriptRoot $ImageType
az acr build --registry $Registry --image "${imageName}:$Tag" --file $dockerfilePath $buildContext
if ($LASTEXITCODE -ne 0) { Write-Error "ACR build failed"; return }
Write-Host "[1/3] Image built: $fullImage" -ForegroundColor Green

# Step 2: Get data plane token
Write-Host "[2/3] Registering disk image with sandbox group..." -ForegroundColor Yellow
$subscriptionId = az account show --query id -o tsv
$token = az account get-access-token --resource "https://dynamicsessions.io" --query accessToken -o tsv
if (-not $token) { Write-Error "Failed to get dynamicsessions.io token"; return }

# Step 3: Create/update disk image in sandbox group
$endpoint = "https://management.$Region.azuredevcompute.io"
$path = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/sandboxGroups/$SandboxGroup/diskImages"
$body = @{
    image = @{ base = $fullImage }
    labels = @{ name = $label }
} | ConvertTo-Json -Depth 3

$response = Invoke-WebRequest -Uri "$endpoint$path" -Method POST -Headers @{
    Authorization = "Bearer $token"
    "Content-Type" = "application/json"
} -Body $body -SkipHttpErrorCheck

if ($response.StatusCode -ge 400) {
    Write-Error "Disk image registration failed ($($response.StatusCode)): $($response.Content)"
    return
}

$result = $response.Content | ConvertFrom-Json
$diskImageId = $result.id
Write-Host "[2/3] Disk image registered: $diskImageId" -ForegroundColor Green

# Step 4: Wait for Ready state
Write-Host "[3/3] Waiting for disk image to be ready..." -ForegroundColor Yellow
$maxWait = 600  # 10 minutes
$elapsed = 0
do {
    Start-Sleep -Seconds 15
    $elapsed += 15
    $status = Invoke-RestMethod -Uri "$endpoint$path/$diskImageId" -Headers @{ Authorization = "Bearer $token" } -ErrorAction SilentlyContinue
    $state = $status.status.state
    Write-Host "       State: $state ($elapsed`s elapsed)"
} while ($state -ne "Ready" -and $state -ne "Failed" -and $elapsed -lt $maxWait)

if ($state -eq "Ready") {
    Write-Host ""
    Write-Host "=== SUCCESS ===" -ForegroundColor Green
    Write-Host "Disk image ID: $diskImageId"
    Write-Host "Size:          $($status.sizeInMB) MB"
    Write-Host ""
    Write-Host "Set this environment variable on your PowerHoof MCP container app:" -ForegroundColor Cyan
    $envVar = if ($ImageType -eq "code") { "CODE_DISK_IMAGE" } else { "BROWSER_DISK_IMAGE" }
    Write-Host "  az containerapp update --name <app> --resource-group <rg> --set-env-vars `"$envVar=$diskImageId`""
    Write-Host ""
} elseif ($state -eq "Failed") {
    Write-Error "Disk image build failed: $($status.status.errorMessage)"
} else {
    Write-Warning "Timed out waiting for disk image. Check status manually with ID: $diskImageId"
}
