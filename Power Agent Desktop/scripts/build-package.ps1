# Build and Package Script for Copilot Studio Agent MCP Server
# Uses WinApp CLI to create MSIX package for Windows distribution
#
# Prerequisites:
# 1. WinApp CLI installed: winget install Microsoft.WinAppCli
# 2. Node.js 20+ installed
# 3. Development certificate generated (optional for testing)

param(
    [switch]$Debug,
    [switch]$SkipBuild,
    [string]$CertPath = ".\certs\dev.pfx",
    [string]$CertPassword = $env:WINAPP_CERT_PASSWORD
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Copilot Studio Agent - Build & Package" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Verify WinApp CLI is installed
Write-Host "[1/6] Checking WinApp CLI installation..." -ForegroundColor Yellow
try {
    $winapVersion = winapp --version
    Write-Host "  WinApp CLI version: $winapVersion" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: WinApp CLI not found. Install it with:" -ForegroundColor Red
    Write-Host "    winget install Microsoft.WinAppCli" -ForegroundColor White
    exit 1
}

# Step 2: Install npm dependencies
Write-Host ""
Write-Host "[2/6] Installing dependencies..." -ForegroundColor Yellow
npm install
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: npm install failed" -ForegroundColor Red
    exit 1
}
Write-Host "  Dependencies installed" -ForegroundColor Green

# Step 3: Build TypeScript
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[3/6] Building TypeScript..." -ForegroundColor Yellow
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: TypeScript build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Build completed" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[3/6] Skipping build (--SkipBuild flag)" -ForegroundColor Gray
}

# Step 4: Initialize WinApp (if not already done)
Write-Host ""
Write-Host "[4/6] Initializing WinApp project..." -ForegroundColor Yellow
if (-not (Test-Path ".\appxmanifest.xml")) {
    winapp init --name "PowerAgentDesktop" --publisher "CN=Developer"
    Write-Host "  WinApp project initialized" -ForegroundColor Green
} else {
    Write-Host "  WinApp project already initialized" -ForegroundColor Gray
}

# Step 5: Generate/verify development certificate
Write-Host ""
Write-Host "[5/6] Checking development certificate..." -ForegroundColor Yellow
if (-not (Test-Path $CertPath)) {
    Write-Host "  Generating new development certificate..." -ForegroundColor Yellow
    if (-not (Test-Path ".\certs")) {
        New-Item -ItemType Directory -Path ".\certs" | Out-Null
    }
    winapp cert generate --output $CertPath --install
    Write-Host "  Certificate generated and installed" -ForegroundColor Green
} else {
    Write-Host "  Certificate exists at $CertPath" -ForegroundColor Green
}

# Step 6: Create MSIX package
Write-Host ""
Write-Host "[6/6] Creating MSIX package..." -ForegroundColor Yellow

$packArgs = @("pack", ".\dist")

if ($Debug) {
    # For debugging, create identity without full packaging
    Write-Host "  Creating debug identity..." -ForegroundColor Yellow
    winapp create-debug-identity .\dist\copilot-agent.exe
    Write-Host "  Debug identity created - you can now test APIs requiring Package Identity" -ForegroundColor Green
} else {
    # Full production package
    if (Test-Path $CertPath) {
        $packArgs += "--cert"
        $packArgs += $CertPath
        
        if ($CertPassword) {
            $packArgs += "--password"
            $packArgs += $CertPassword
        }
    }
    
    & winapp @packArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: MSIX packaging failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  MSIX package created successfully!" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

if ($Debug) {
    Write-Host "Debug mode: Package identity is now available for testing."
    Write-Host "Run your app normally to test APIs requiring identity."
} else {
    Write-Host "Output: .\dist\msix\"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Install the MSIX: double-click the .msix file"
    Write-Host "  2. Or sideload with: Add-AppxPackage .\dist\msix\*.msix"
    Write-Host ""
    Write-Host "To publish to Microsoft Store, submit the signed .msix file."
}
Write-Host "========================================" -ForegroundColor Cyan
