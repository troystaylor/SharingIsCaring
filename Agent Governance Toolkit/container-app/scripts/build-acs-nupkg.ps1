#requires -Version 7.0
<#
.SYNOPSIS
    Builds the AgentControlSpecification nupkg for linux-x64 inside a Docker container.

.DESCRIPTION
    Clones the microsoft/agent-governance-toolkit repo at a pinned commit, compiles the
    Rust native library, packs the .NET SDK as a nupkg with
    AgentControlSpecificationAllowIncompleteNativePack=true (host-only), and copies the
    resulting nupkg into container-app/local-packages/.

    Requires Docker Desktop running in Linux container mode.

.PARAMETER Commit
    Upstream commit to pin to. Defaults to a known-good commit.

.PARAMETER Force
    Overwrite any existing nupkg in local-packages/.
#>
param(
    [string]$Commit = "45b59b3c0f507613648e31ca79ab13b7828d616e",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$containerAppDir = Split-Path -Parent $scriptDir
$localPackagesDir = Join-Path $containerAppDir "local-packages"

if (-not (Test-Path $localPackagesDir)) {
    New-Item -ItemType Directory -Path $localPackagesDir | Out-Null
}

$existing = Get-ChildItem -Path $localPackagesDir -Filter "AgentControlSpecification.*.nupkg" -ErrorAction SilentlyContinue
if ($existing -and -not $Force) {
    Write-Host "Existing nupkg found in local-packages/:" -ForegroundColor Yellow
    $existing | ForEach-Object { Write-Host "  $($_.Name)" }
    Write-Host "Pass -Force to rebuild." -ForegroundColor Yellow
    return
}

# Verify Docker is running and in Linux mode
try {
    $osType = docker info --format '{{.OSType}}' 2>&1
    if ($LASTEXITCODE -ne 0 -or $osType -notmatch 'linux') {
        throw "Docker is not running in Linux container mode (OSType=$osType)."
    }
}
catch {
    Write-Error "Docker check failed: $_"
    Write-Host "Start Docker Desktop and switch to Linux containers, then re-run this script." -ForegroundColor Red
    exit 1
}

Write-Host "Building AgentControlSpecification nupkg in Linux container..." -ForegroundColor Cyan
Write-Host "  Commit: $Commit"
Write-Host "  Output: $localPackagesDir"

$buildScript = @"
set -euo pipefail

apt-get update -qq
apt-get install -y -qq git curl build-essential pkg-config libssl-dev > /dev/null

curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --default-toolchain stable --profile minimal > /dev/null
. /root/.cargo/env

cd /tmp
git clone --filter=blob:none https://github.com/microsoft/agent-governance-toolkit.git
cd agent-governance-toolkit
git checkout $Commit

cd policy-engine

# Build Rust native lib for linux-x64
cargo build --release -p agent_control_specification_core --features default-dispatchers > /tmp/cargo.log 2>&1 || (tail -100 /tmp/cargo.log; exit 1)

# Stage native lib in the SDK's expected location
SDK_DIR=sdk/dotnet/src/AgentControlSpecification
mkdir -p `${SDK_DIR}/runtimes/linux-x64/native
cp target/release/libagent_control_specification_core.so `${SDK_DIR}/runtimes/linux-x64/native/

# Pack the nupkg (host-only — only linux-x64 native)
cd `${SDK_DIR}
dotnet pack -c Release -o /out \
  -p:AgentControlSpecificationSkipNativeBuild=true \
  -p:AgentControlSpecificationAllowIncompleteNativePack=true

ls -la /out
"@

$buildScript = $buildScript -replace "`r`n", "`n"

$mountTarget = ($localPackagesDir -replace '\\', '/')

docker run --rm `
    --platform linux/amd64 `
    -v "${mountTarget}:/out" `
    mcr.microsoft.com/dotnet/sdk:8.0 `
    bash -c $buildScript

if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker build failed."
    exit 1
}

$produced = Get-ChildItem -Path $localPackagesDir -Filter "AgentControlSpecification.*.nupkg"
Write-Host "`nBuilt nupkg(s):" -ForegroundColor Green
$produced | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" }

# Drop the marker that flips AgentGovernance.Api.csproj into ACS-enabled mode.
$marker = Join-Path $localPackagesDir ".acs-enabled"
Set-Content -Path $marker -Encoding ASCII -Value @"
Presence of this file signals AgentGovernance.Api.csproj to enable ACS_ENABLED
and add a PackageReference to AgentControlSpecification (restored from the
local-packages/ folder via NuGet.config). Delete this file to disable ACS
without removing the nupkg.
"@

Write-Host "`nNext: rebuild the container image to pick up live ACS evaluation:" -ForegroundColor Cyan
Write-Host "  cd '$containerAppDir'"
Write-Host "  docker build -t agent-governance ."
