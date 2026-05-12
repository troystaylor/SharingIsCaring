<#
.SYNOPSIS
    Deploy the Durable Agent Workflows Azure Functions app.

.DESCRIPTION
    Creates Azure resources via Bicep and deploys the Functions app code.
    Requires Azure CLI and Azure Functions Core Tools.

.PARAMETER ResourceGroupName
    Name of the resource group to deploy into (created if it doesn't exist).

.PARAMETER Location
    Azure region. Defaults to westus2.

.PARAMETER AzureOpenAIEndpoint
    Azure OpenAI endpoint URL.

.PARAMETER AzureOpenAIDeployment
    Azure OpenAI model deployment name. Defaults to gpt-4o.

.PARAMETER DtsConnectionString
    Durable Task Scheduler connection string.
#>

param(
    [Parameter(Mandatory)]
    [string]$ResourceGroupName,

    [string]$Location = 'westus2',

    [Parameter(Mandatory)]
    [string]$AzureOpenAIEndpoint,

    [string]$AzureOpenAIDeployment = 'gpt-4o',

    [Parameter(Mandatory)]
    [string]$DtsConnectionString
)

$ErrorActionPreference = 'Stop'

# ── Git identity ──────────────────────────────────────────────────────────────
git config user.name "Troy Simeon Taylor"
git config user.email "44444967+troystaylor@users.noreply.github.com"

# ── Step 1: Resource group ────────────────────────────────────────────────────

Write-Host "`n[1/4] Creating resource group '$ResourceGroupName' in '$Location'..." -ForegroundColor Cyan
az group create --name $ResourceGroupName --location $Location --output none

# ── Step 2: Bicep deployment ─────────────────────────────────────────────────

Write-Host "[2/4] Deploying infrastructure via Bicep..." -ForegroundColor Cyan
$deployment = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file "$PSScriptRoot/main.bicep" `
    --parameters `
        azureOpenAIEndpoint=$AzureOpenAIEndpoint `
        azureOpenAIDeployment=$AzureOpenAIDeployment `
        dtsConnectionString=$DtsConnectionString `
    --output json | ConvertFrom-Json

$functionAppName = $deployment.properties.outputs.functionAppName.value
$functionAppUrl = $deployment.properties.outputs.functionAppUrl.value
$mcpEndpoint = $deployment.properties.outputs.mcpEndpoint.value

Write-Host "  Function App: $functionAppName" -ForegroundColor Gray
Write-Host "  URL: $functionAppUrl" -ForegroundColor Gray

# ── Step 3: Publish function app ─────────────────────────────────────────────

Write-Host "[3/4] Publishing function app code..." -ForegroundColor Cyan
Push-Location "$PSScriptRoot/../functions-app"
try {
    func azure functionapp publish $functionAppName --dotnet-isolated
}
finally {
    Pop-Location
}

# ── Step 4: Summary ──────────────────────────────────────────────────────────

Write-Host "`n[4/4] Deployment complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  Function App URL : $functionAppUrl"
Write-Host "  MCP Endpoint     : $mcpEndpoint"
Write-Host ""
Write-Host "  Workflow HTTP Triggers:" -ForegroundColor Yellow
Write-Host "    POST $functionAppUrl/api/workflows/TriageTicket/run"
Write-Host "    POST $functionAppUrl/api/workflows/EvaluateResponse/run"
Write-Host "    POST $functionAppUrl/api/workflows/ReviewDocument/run"
Write-Host ""
Write-Host "  HITL Respond Endpoints:" -ForegroundColor Yellow
Write-Host "    POST $functionAppUrl/api/workflows/TriageTicket/respond/{runId}"
Write-Host "    POST $functionAppUrl/api/workflows/EvaluateResponse/respond/{runId}"
Write-Host "    POST $functionAppUrl/api/workflows/ReviewDocument/respond/{runId}"
Write-Host ""
Write-Host "  Update agent-package/plugin.json 'spec.url' with:" -ForegroundColor Yellow
Write-Host "    $mcpEndpoint"
