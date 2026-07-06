# ACA Code Interpreter — Deployment Script
# Run from the infra/ directory
# Prerequisites: az CLI logged in, .NET 9 SDK, Docker

param(
    [Parameter(Mandatory=$true)]
    [string]$PrincipalId,

    [string]$Location = "eastus2",
    [string]$ResourceGroupName = "rg-aca-code-interpreter",
    [string]$SandboxGroupName = "code-interpreter",
    [string]$PrincipalType = "User",
    [switch]$SkipInfra,
    [switch]$SkipImage,
    [switch]$SkipConnector
)

$ErrorActionPreference = "Stop"
$InfraDir = $PSScriptRoot
$RootDir = Split-Path $InfraDir -Parent

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " ACA Code Interpreter — Deployment" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ─── Step 1: Build .NET tools ──────────────────────────────────────────────────
Write-Host "[1/4] Building .NET tools..." -ForegroundColor Yellow

Write-Host "  Building pfx (Power Fx CLI)..."
Push-Location "$InfraDir\tools\pfx"
dotnet publish -c Release -r linux-x64 --self-contained -o "$InfraDir\tools\pfx\publish" -v quiet
Pop-Location

Write-Host "  Building expr-eval (Expression Evaluator)..."
Push-Location "$InfraDir\tools\expr-eval"
dotnet publish -c Release -r linux-x64 --self-contained -o "$InfraDir\tools\expr-eval\publish" -v quiet
Pop-Location

Write-Host "  .NET tools built." -ForegroundColor Green
Write-Host ""

# ─── Step 2: Deploy Azure infrastructure ──────────────────────────────────────
if (-not $SkipInfra) {
    Write-Host "[2/4] Deploying Azure infrastructure..." -ForegroundColor Yellow

    $deployResult = az deployment sub create `
        --location $Location `
        --template-file "$InfraDir\main.bicep" `
        --parameters principalId=$PrincipalId `
            location=$Location `
            resourceGroupName=$ResourceGroupName `
            sandboxGroupName=$SandboxGroupName `
            principalType=$PrincipalType `
        --query "properties.outputs" `
        --output json | ConvertFrom-Json

    $subscriptionId = (az account show --query id -o tsv)

    Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Green
    Write-Host "  Sandbox Group:  $SandboxGroupName" -ForegroundColor Green
    Write-Host "  Region:         $Location" -ForegroundColor Green
    Write-Host ""

    # Update script.csx constants
    Write-Host "  Updating script.csx with deployment values..."
    $scriptPath = "$RootDir\script.csx"
    $scriptContent = Get-Content $scriptPath -Raw
    $scriptContent = $scriptContent -replace '\[\[REPLACE_WITH_SUBSCRIPTION_ID\]\]', $subscriptionId
    $scriptContent = $scriptContent -replace '\[\[REPLACE_WITH_RESOURCE_GROUP\]\]', $ResourceGroupName
    $scriptContent = $scriptContent -replace '\[\[REPLACE_WITH_SANDBOX_GROUP\]\]', $SandboxGroupName
    Set-Content $scriptPath -Value $scriptContent -NoNewline

    Write-Host "  script.csx updated." -ForegroundColor Green
} else {
    Write-Host "[2/4] Skipping infrastructure deployment." -ForegroundColor DarkGray
}
Write-Host ""

# ─── Step 3: Build and import disk image ──────────────────────────────────────
if (-not $SkipImage) {
    Write-Host "[3/4] Building container image..." -ForegroundColor Yellow

    # Update Dockerfile to use published .NET binaries
    # The Dockerfile COPYs from tools/ — ensure publish outputs are in place
    Copy-Item "$InfraDir\tools\pfx\publish\*" "$InfraDir\tools\pfx\" -Force -Recurse
    Copy-Item "$InfraDir\tools\expr-eval\publish\*" "$InfraDir\tools\expr-eval\" -Force -Recurse

    docker build -t aca-code-interpreter:latest "$InfraDir"

    Write-Host "  Image built: aca-code-interpreter:latest" -ForegroundColor Green
    Write-Host ""
    Write-Host "  To import into ACA Sandboxes:" -ForegroundColor Yellow
    Write-Host "    aca disk import --group $SandboxGroupName --name aca-code-interpreter --image aca-code-interpreter:latest"
    Write-Host ""
} else {
    Write-Host "[3/4] Skipping image build." -ForegroundColor DarkGray
}
Write-Host ""

# ─── Step 4: Deploy connector ─────────────────────────────────────────────────
if (-not $SkipConnector) {
    Write-Host "[4/4] Deploying connector to Power Platform..." -ForegroundColor Yellow

    $envId = "c4f149b0-9f42-e8c4-97d8-bc69b59f971c"

    # Check if connector exists
    $existing = pac connector list --environment $envId 2>$null | Select-String "ACA Code Interpreter"

    if ($existing) {
        Write-Host "  Updating existing connector..."
        pac connector update `
            --api-def "$RootDir\apiDefinition.swagger.json" `
            --api-prop "$RootDir\apiProperties.json" `
            --script "$RootDir\script.csx" `
            --environment $envId
    } else {
        Write-Host "  Creating new connector..."
        pac connector create `
            --api-def "$RootDir\apiDefinition.swagger.json" `
            --api-prop "$RootDir\apiProperties.json" `
            --script "$RootDir\script.csx" `
            --environment $envId
    }

    Write-Host "  Connector deployed." -ForegroundColor Green
} else {
    Write-Host "[4/4] Skipping connector deployment." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Deployment complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Register Entra ID app and update apiProperties.json clientId"
Write-Host "  2. Import disk image: aca disk import --group $SandboxGroupName --name aca-code-interpreter --image aca-code-interpreter:latest"
Write-Host "  3. Create a connection in Power Platform and test"
Write-Host ""
