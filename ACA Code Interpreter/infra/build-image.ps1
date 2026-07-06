# build-image.ps1 — Build and import ACA Code Interpreter disk image
# Usage:
#   .\build-image.ps1                     # Full image (all 16 runtimes)
#   .\build-image.ps1 -Lite               # Lite image (Python + Bash only)
#   .\build-image.ps1 -SkipImport         # Build only, don't import to ACA
#   .\build-image.ps1 -ImageName myimage  # Custom disk image name

param(
    [switch]$Lite,
    [switch]$SkipImport,
    [string]$ImageName = "aca-code-interpreter",
    [string]$SandboxGroup = "code-interpreter",
    [string]$ResourceGroup = "rg-aca-code-interpreter"
)

$ErrorActionPreference = "Stop"
$InfraDir = $PSScriptRoot

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " ACA Code Interpreter — Disk Image Builder" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ─── Determine which Dockerfile to use ─────────────────────────────────────────
$dockerfile = if ($Lite) { "Dockerfile.lite" } else { "Dockerfile" }
$tag = if ($Lite) { "${ImageName}:lite" } else { "${ImageName}:latest" }

Write-Host "  Image:      $tag" -ForegroundColor White
Write-Host "  Dockerfile: $dockerfile" -ForegroundColor White
Write-Host "  Tier:       $(if ($Lite) { 'Lite (Python + Bash)' } else { 'Full (all 16 runtimes)' })" -ForegroundColor White
Write-Host ""

# ─── Build .NET tools (full image only) ────────────────────────────────────────
if (-not $Lite) {
    Write-Host "[1/3] Building .NET tools..." -ForegroundColor Yellow

    if (Test-Path "$InfraDir\tools\pfx\pfx.csproj") {
        Write-Host "  Building pfx (Power Fx CLI)..."
        Push-Location "$InfraDir\tools\pfx"
        dotnet publish -c Release -r linux-x64 --self-contained -o "$InfraDir\tools\pfx\publish" -v quiet 2>$null
        Pop-Location
    }

    if (Test-Path "$InfraDir\tools\expr-eval\expr-eval.csproj") {
        Write-Host "  Building expr-eval (Expression Evaluator)..."
        Push-Location "$InfraDir\tools\expr-eval"
        dotnet publish -c Release -r linux-x64 --self-contained -o "$InfraDir\tools\expr-eval\publish" -v quiet 2>$null
        Pop-Location
    }

    Write-Host "  Done." -ForegroundColor Green
} else {
    Write-Host "[1/3] Skipping .NET tools (lite image)." -ForegroundColor DarkGray
}
Write-Host ""

# ─── Docker build ──────────────────────────────────────────────────────────────
Write-Host "[2/3] Building Docker image..." -ForegroundColor Yellow
docker build -f "$InfraDir\$dockerfile" -t $tag "$InfraDir"

if ($LASTEXITCODE -ne 0) {
    Write-Host "  Docker build failed!" -ForegroundColor Red
    exit 1
}

$size = docker image inspect $tag --format '{{.Size}}' | ForEach-Object { [math]::Round($_ / 1MB) }
Write-Host "  Built: $tag ($size MB)" -ForegroundColor Green
Write-Host ""

# ─── Import to ACA Sandboxes ──────────────────────────────────────────────────
if (-not $SkipImport) {
    Write-Host "[3/3] Importing to ACA Sandboxes..." -ForegroundColor Yellow

    # Check if aca CLI is available
    $acaCmd = Get-Command aca -ErrorAction SilentlyContinue
    if (-not $acaCmd) {
        Write-Host "  'aca' CLI not found. Install from: https://aka.ms/aca-cli-install" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Manual import command:" -ForegroundColor Yellow
        Write-Host "    aca disk import --group $SandboxGroup --name $ImageName --image $tag" -ForegroundColor White
        exit 1
    }

    aca disk import --group $SandboxGroup --name $ImageName --image $tag

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Imported as disk image: $ImageName" -ForegroundColor Green
    } else {
        Write-Host "  Import failed. You may need to run:" -ForegroundColor Red
        Write-Host "    aca disk import --group $SandboxGroup --name $ImageName --image $tag" -ForegroundColor White
    }
} else {
    Write-Host "[3/3] Skipping import (--SkipImport)." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Done!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Update connector script.csx: ACA_DISK_IMAGE = `"$ImageName`"" -ForegroundColor White
Write-Host "  2. Redeploy connector: pac connector update ..." -ForegroundColor White
Write-Host "  3. Create a new session to use the custom image" -ForegroundColor White
Write-Host ""
Write-Host "Pro tip: Snapshot the first-booted sandbox for <1s starts:" -ForegroundColor Yellow
Write-Host "  aca sandbox create --disk $ImageName --label name=warmup" -ForegroundColor White
Write-Host "  aca snapshot create --sandbox-label name=warmup --name ${ImageName}-warm" -ForegroundColor White
Write-Host ""
