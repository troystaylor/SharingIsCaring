<#
.SYNOPSIS
    Windows helper for running the Scope stack locally (see docs/local-setup-windows.md).

.DESCRIPTION
    Wraps the upstream `pnpm docker:dev:copilot` flow with the extra handling a Windows
    machine behind a package proxy needs: preflight checks, Git Bash instead of WSL bash,
    sequential image builds, recovery when Compose aborts on the gateway healthcheck,
    credential registration, Judge model overrides, and a smoke test.

    It adds files only. It does not modify upstream Scope scripts or configuration.
    Compose logs are written to %TEMP%\scope-local.

.PARAMETER Action
    check        Verify prerequisites.
    up           Start the stack and wait until it is ready.
    status       Show container state and readiness checks.
    credential   Register the GitHub CLI token as a Scope secret if none has copilot-sdk.
    judge-model  Set JUDGE_MODEL, FEEDBACK_MODEL, REPORT_MODEL in .env.local and recreate
                 the Judge and report generator. Requires -JudgeModel.
    smoke        Submit a trivial evaluation and report the result. Requires -Model.
    down         Stop the stack.

.EXAMPLE
    .\scripts\local\scope-local.ps1 check
    .\scripts\local\scope-local.ps1 up
    .\scripts\local\scope-local.ps1 judge-model -JudgeModel <model>
    .\scripts\local\scope-local.ps1 credential
    .\scripts\local\scope-local.ps1 smoke -Model <model>
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('check', 'up', 'status', 'credential', 'judge-model', 'smoke', 'down')]
    [string]$Action = 'status',

    [string]$JudgeModel,
    [string]$Model,
    [string]$Worker = 'coder-acp-copilot',
    [string]$ProjectName = 'scope-mt-app',
    [int]$ApiPort = 3100,
    [int]$MaxAttempts = 3,
    [int]$TimeoutMinutes = 30
)

# Native commands such as `docker inspect` write to stderr for expected conditions (for
# example a container that doesn't exist yet). With 'Stop', Windows PowerShell turns that
# into a terminating error, so errors are handled explicitly with `throw` instead.
$ErrorActionPreference = 'Continue'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$LogDir = Join-Path $env:TEMP 'scope-local'
$PidFile = Join-Path $LogDir 'compose.pid'
$LongRunning = 'api', 'portal', 'judge', 'token-manager', 'scheduler', 'report-generator',
    'post-processor', 'gateway', 'coder-acp-copilot'

function Initialize-Environment {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { throw 'git is required.' }
    $gitRoot = Split-Path (Split-Path $git.Source)
    $gitBash = Join-Path $gitRoot 'bin\bash.exe'
    if (-not (Test-Path $gitBash)) { throw "Git Bash not found at $gitBash." }

    # Git Bash must come before WSL's bash.exe in System32.
    $extra = @("$gitRoot\bin", "$gitRoot\usr\bin", "$env:LOCALAPPDATA\Microsoft\WinGet\Links")
    $env:Path = (($extra | Where-Object { Test-Path $_ }) -join ';') + ';' + $env:Path
    $env:npm_config_script_shell = $gitBash
    $env:COMPOSE_PARALLEL_LIMIT = '1'
    $env:SCOPE_API_URL = "http://localhost:$ApiPort"
    Set-Location $RepoRoot
}

function Get-Pnpm {
    $cmd = Get-Command pnpm -ErrorAction SilentlyContinue
    if (-not $cmd) { throw 'pnpm is not installed. Run: npm install -g pnpm@10.29.1' }
    $cmdShim = Join-Path (Split-Path $cmd.Source) 'pnpm.cmd'
    if (Test-Path $cmdShim) { return $cmdShim }
    return $cmd.Source
}

function Invoke-ScopeCli {
    $out = & pnpm --silent cli @args 2>&1
    return ($out | ForEach-Object { "$_" }) -join "`n"
}

function Get-ContainerName([string]$Service) { "$ProjectName-$Service-1" }

function Get-ContainerState([string]$Service) {
    $s = docker inspect -f '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' (Get-ContainerName $Service) 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $s) { return @{ Status = 'missing'; Health = '' } }
    $parts = $s -split '\|'
    return @{ Status = $parts[0]; Health = $parts[1] }
}

function Test-TokenManagerReachable {
    $js = "fetch('http://token-manager:80/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
    docker exec (Get-ContainerName 'judge') node -e $js 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Get-Readiness {
    $checks = [ordered]@{}
    foreach ($svc in 'api', 'gateway') {
        $checks["$svc healthy"] = ((Get-ContainerState $svc).Health -eq 'healthy')
    }
    $checks['worker running'] = ((Get-ContainerState 'coder-acp-copilot').Status -eq 'running')
    $checks['judge running'] = ((Get-ContainerState 'judge').Status -eq 'running')
    $checks['judge -> token-manager'] = $checks['judge running'] -and (Test-TokenManagerReachable)
    try {
        $r = Invoke-WebRequest "http://localhost:$ApiPort/api-docs" -UseBasicParsing -TimeoutSec 5
        $checks['api reachable'] = ($r.StatusCode -eq 200)
    } catch { $checks['api reachable'] = $false }
    return $checks
}

function Start-StoppedServices {
    foreach ($svc in $LongRunning) {
        if ((Get-ContainerState $svc).Status -eq 'exited') {
            Write-Host "  restarting stopped service: $svc"
            docker start (Get-ContainerName $svc) | Out-Null
        }
    }
}

function Invoke-Check {
    $results = [ordered]@{}
    $node = (& node --version 2>$null)
    $results['Node.js 22'] = [bool]($node -match '^v22\.')
    $results['pnpm'] = [bool](Get-Command pnpm -ErrorAction SilentlyContinue)
    docker info 2>$null | Out-Null
    $results['Docker daemon running'] = ($LASTEXITCODE -eq 0)
    $results['mkcert'] = [bool](Get-Command mkcert -ErrorAction SilentlyContinue)
    gh auth status 2>$null | Out-Null
    $results['GitHub CLI signed in'] = ($LASTEXITCODE -eq 0)
    $bash = (Get-Command bash -ErrorAction SilentlyContinue).Source
    $results['bash is Git Bash (not WSL)'] = ($bash -and $bash -notmatch 'System32')
    $results['dependencies installed'] = (Test-Path (Join-Path $RepoRoot 'node_modules'))

    $failed = $false
    foreach ($k in $results.Keys) {
        $mark = if ($results[$k]) { 'PASS' } else { $failed = $true; 'FAIL' }
        Write-Host ("{0,-6}{1}" -f $mark, $k)
    }
    if (-not $results['Node.js 22']) { Write-Host "       Found '$node'. Run: nvm use 22" }
    if (-not $results['Docker daemon running']) { Write-Host '       Start Docker Desktop and retry.' }
    if (-not $results['mkcert']) { Write-Host '       Install with: winget install FiloSottile.mkcert' }
    if (-not $results['dependencies installed']) { Write-Host '       Run: pnpm install --frozen-lockfile' }
    return (-not $failed)
}

function Start-Compose {
    New-Item -ItemType Directory -Force $LogDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $log = Join-Path $LogDir "compose-$stamp.log"
    $env:GITHUB_TOKEN = (gh auth token)
    $p = Start-Process -FilePath (Get-Pnpm) -ArgumentList 'docker:dev:copilot' `
        -WorkingDirectory $RepoRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $env:GITHUB_TOKEN = $null
    Set-Content -Path $PidFile -Value $p.Id
    Write-Host "  compose started (pid $($p.Id)), log: $log"
    return $p
}

function Wait-GatewayHealthy([datetime]$Deadline) {
    Write-Host '  waiting for the gateway to finish compiling and become healthy...'
    while ((Get-Date) -lt $Deadline) {
        if ((Get-ContainerState 'gateway').Health -eq 'healthy') { return $true }
        Start-Sleep -Seconds 15
    }
    return $false
}

function Invoke-Up {
    if (-not (Invoke-Check)) { throw 'Preflight checks failed.' }
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $attempt = 1
    Write-Host "Starting Scope (attempt $attempt of $MaxAttempts)..."
    $proc = Start-Compose

    while ((Get-Date) -lt $deadline) {
        $ready = Get-Readiness
        $pending = @($ready.Keys | Where-Object { -not $ready[$_] })
        if ($pending.Count -eq 0) {
            Write-Host 'Scope is ready.'
            Write-Host "  Portal: run 'pnpm open:portal'   API: http://localhost:$ApiPort"
            return
        }
        Write-Host ("  {0:HH:mm:ss} waiting for: {1}" -f (Get-Date), ($pending -join ', '))
        if ($proc.HasExited) {
            # Compose aborts when the gateway's first compile outlasts its healthcheck.
            if ($attempt -ge $MaxAttempts) { throw "Compose exited $MaxAttempts times. See logs in $LogDir." }
            Write-Host "Compose exited (code $($proc.ExitCode))."
            if (-not (Wait-GatewayHealthy $deadline)) { throw 'Gateway did not become healthy before the timeout.' }
            $attempt++
            Write-Host "Starting Scope (attempt $attempt of $MaxAttempts)..."
            $proc = Start-Compose
        } else {
            Start-StoppedServices
        }
        Start-Sleep -Seconds 15
    }
    throw "Scope was not ready within $TimeoutMinutes minutes. Run 'status' for details."
}

function Invoke-Status {
    docker ps -a --filter "name=$ProjectName" --format 'table {{.Names}}\t{{.Status}}'
    Write-Host ''
    $ready = Get-Readiness
    foreach ($k in $ready.Keys) {
        Write-Host ("{0,-6}{1}" -f $(if ($ready[$k]) { 'PASS' } else { 'FAIL' }), $k)
    }
}

function Invoke-Credential {
    $json = Invoke-ScopeCli secret list -o json
    if ($json -match 'copilot-sdk') {
        Write-Host 'A credential with the copilot-sdk capability already exists.'
        return
    }
    Write-Host 'Registering the GitHub CLI token as a Scope secret...'
    $out = (gh auth token) | & pnpm --silent cli secret create --type github-oauth --value-stdin `
        --comment 'local dev (scope-local.ps1)' 2>&1
    $out | ForEach-Object { "$_" } | Select-Object -Last 3 | ForEach-Object { Write-Host $_ }
    # Validation is asynchronous: capabilities appear a few seconds after creation.
    for ($i = 0; $i -lt 12; $i++) {
        if ((Invoke-ScopeCli secret list -o json) -match 'copilot-sdk') { return }
        Start-Sleep -Seconds 5
    }
    throw 'The secret was stored but has no copilot-sdk capability. Check it in the Portal Secrets tab.'
}

function Set-EnvLocalValue([string]$Path, [string]$Key, [string]$Value) {
    $lines = if (Test-Path $Path) { @(Get-Content $Path) } else { @() }
    $found = $false
    $lines = @($lines | ForEach-Object {
        if ($_ -match "^\s*$Key=") { $found = $true; "$Key=$Value" } else { $_ }
    })
    if (-not $found) { $lines += "$Key=$Value" }
    Set-Content -Path $Path -Value $lines
}

function Invoke-JudgeModel {
    if (-not $JudgeModel) { throw 'Pass -JudgeModel <model>.' }
    $envLocal = Join-Path $RepoRoot '.env.local'
    foreach ($k in 'JUDGE_MODEL', 'FEEDBACK_MODEL', 'REPORT_MODEL') {
        Set-EnvLocalValue $envLocal $k $JudgeModel
    }
    Write-Host "Set JUDGE_MODEL, FEEDBACK_MODEL, REPORT_MODEL to '$JudgeModel' in .env.local."
    if ((Get-ContainerState 'judge').Status -eq 'missing') {
        Write-Host 'The stack is not running; the values apply on the next start.'
        return
    }
    $env:GIT_BRANCH = (git rev-parse --abbrev-ref HEAD)
    & bash scripts/dev-compose.sh --env-file .env --env-file apps/workers/coder-acp-copilot/versions.env `
        -f docker-compose.yml -f docker-compose.dev.yml --profile copilot --profile report `
        --profile post-processor --profile auth --profile portal up -d --no-deps judge report-generator
    Write-Host 'Waiting for the Judge to compile and reach the token manager...'
    $deadline = (Get-Date).AddMinutes(10)
    while ((Get-Date) -lt $deadline) {
        if (Test-TokenManagerReachable) { Write-Host 'Judge is ready.'; return }
        Start-Sleep -Seconds 10
    }
    throw 'The Judge did not become ready within 10 minutes.'
}

function Invoke-Smoke {
    if (-not $Model) { throw 'Pass -Model <model>. List models with: pnpm cli agent list' }
    if (-not $env:SCOPE_PROJECT) {
        $projects = Invoke-ScopeCli project list -o json | ConvertFrom-Json
        if (-not $projects) { throw 'No project found. Create one in the Portal.' }
        $env:SCOPE_PROJECT = @($projects)[0]._id
    }
    $criterion = 'smoke_health_route'
    if ((Invoke-ScopeCli criteria list) -notmatch $criterion) {
        Invoke-ScopeCli criteria create --id $criterion --prompt `
            'The workspace contains a Node.js HTTP server whose source implements a GET /health route that returns a JSON body.' | Out-Null
    }
    $task = "Create a minimal Node.js HTTP server (no dependencies) in server.js with a GET /health route that returns JSON {status:'ok'}."
    $out = Invoke-ScopeCli run submit -w $Worker --model $Model -c $criterion -m $task --no-stream
    $id = [regex]::Match($out, 'Run ID: (\S+)').Groups[1].Value
    if (-not $id) { throw "Could not read the run ID from: $out" }
    Write-Host "Submitted run $id. Polling..."

    $deadline = (Get-Date).AddMinutes(15)
    do {
        Start-Sleep -Seconds 15
        $status = Invoke-ScopeCli run status -i $id
    } while ((Get-Date) -lt $deadline -and $status -notmatch 'Status: (done|failed|cancelled)')

    $run = @(Invoke-ScopeCli run get -i $id -o json | ConvertFrom-Json)[0]
    Write-Host "status: $($run.status)  passed: $($run.passed)  gates: $($run.gateSummaries)"
    if ($run.error -and $run.error -ne 'N/A') { Write-Host "error: $($run.error)" }
    if ($run.passed -ne 'yes') { exit 1 }
}

function Invoke-Down {
    & pnpm docker:down
    if (Test-Path $PidFile) {
        $procId = [int](Get-Content $PidFile)
        if (Get-Process -Id $procId -ErrorAction SilentlyContinue) { Stop-Process -Id $procId }
        Remove-Item $PidFile
    }
}

Initialize-Environment
switch ($Action) {
    'check'       { if (-not (Invoke-Check)) { exit 1 } }
    'up'          { Invoke-Up }
    'status'      { Invoke-Status }
    'credential'  { Invoke-Credential }
    'judge-model' { Invoke-JudgeModel }
    'smoke'       { Invoke-Smoke }
    'down'        { Invoke-Down }
}
