<#
.SYNOPSIS
    Runs a Scope evaluation across one or more profiles, several times each, and writes a report.

.DESCRIPTION
    An evaluation is a folder (see evaluations\_template) containing:
      scenario.yaml     Scope scenario: the task prompt and criteria IDs.
      criteria\*.yaml   Criteria the Judge evaluates (imported into Scope; existing IDs are kept).
      profiles.json     Optional. Profiles to compare, for example a baseline and a variant.
      mcp-servers.json  Optional. MCP servers to register so profiles can reference them.

    The script sets up what's missing, submits Count runs per profile, waits for them, and writes
    report.md and results.json to the output folder. The default output folder is
    evaluations\.results, which git ignores, so results stay out of commits.

    Runs where the Judge itself failed (infrastructure errors) are reported separately and are
    excluded from pass rates, because they say nothing about the agent.

.PARAMETER Path
    The evaluation folder.

.PARAMETER Model
    The model for the coding agent. List models with: pnpm cli agent list

.PARAMETER ScopePath
    A clone of microsoft/scope. Used to run the CLI through pnpm when the `scope` CLI isn't
    installed. Defaults to $env:SCOPE_REPO, then C:\src\scope.

.PARAMETER MaxIterations
    The most Judge feedback turns a run may take. By default Scope lets the agent retry after the
    Judge's feedback, so a run that passes on turn 3 counts as passed. Use 1 to measure whether the
    agent succeeds on its first attempt.

.EXAMPLE
    .\Invoke-ScopeEval.ps1 -Path .\_template -Model <model> -Count 3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Model,
    [string]$Worker = 'coder-acp-copilot',
    [string[]]$ProfileName,
    [int]$Count = 3,
    [int]$MaxIterations = 0,
    [string]$ApiUrl = 'http://localhost:3100',
    [string]$Project,
    [string]$ScopePath = $(if ($env:SCOPE_REPO) { $env:SCOPE_REPO } else { 'C:\src\scope' }),
    [string]$OutDir,
    [int]$TimeoutMinutes = 30,
    [switch]$SkipSetup
)

$ErrorActionPreference = 'Continue'
$EvalRoot = (Resolve-Path $Path).Path
$EvalName = Split-Path $EvalRoot -Leaf
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '.results' }
$RunStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$OutDir = Join-Path $OutDir "$EvalName\$RunStamp"

function Invoke-Cli {
    if ($script:UseScopeCli) {
        $out = & scope @args 2>&1
    } else {
        Push-Location $ScopePath
        try { $out = & pnpm --silent cli @args 2>&1 } finally { Pop-Location }
    }
    return ($out | ForEach-Object { "$_" }) -join "`n"
}

function ConvertFrom-JsonSafe([string]$Text) {
    # Piping unrolls the array; Windows PowerShell 5.1 otherwise returns it as one object.
    try { return @($Text | ConvertFrom-Json | ForEach-Object { $_ }) } catch { return @() }
}

function Initialize-Cli {
    $script:UseScopeCli = [bool](Get-Command scope -ErrorAction SilentlyContinue)
    if (-not $script:UseScopeCli) {
        if (-not (Test-Path (Join-Path $ScopePath 'apps\cli'))) {
            throw "No 'scope' CLI on PATH and no Scope clone at $ScopePath. Install the CLI or pass -ScopePath."
        }
        $git = (Get-Command git).Source
        $gitRoot = Split-Path (Split-Path $git)
        $env:Path = "$gitRoot\bin;$env:Path"
        $env:npm_config_script_shell = Join-Path $gitRoot 'bin\bash.exe'
    }
    $env:SCOPE_API_URL = $ApiUrl
    if ($Project) { $env:SCOPE_PROJECT = $Project }
    if (-not $env:SCOPE_PROJECT) {
        $projects = @(ConvertFrom-JsonSafe (Invoke-Cli project list -o json))
        if ($projects.Count -eq 0) { throw 'No Scope project found. Is Scope running? Create a project in the Portal.' }
        $env:SCOPE_PROJECT = $projects[0]._id
    }
}

# Replaces ${env:NAME} so secrets never need to be stored in evaluation files.
function Expand-EnvReferences([string]$Value) {
    return [regex]::Replace($Value, '\$\{env:([A-Za-z_][A-Za-z0-9_]*)\}', {
        param($m)
        $v = [Environment]::GetEnvironmentVariable($m.Groups[1].Value)
        if ($null -eq $v) { throw "Environment variable $($m.Groups[1].Value) is not set." }
        $v
    })
}

function Get-ShortHash([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))
    return (($bytes[0..2] | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-CriterionYaml([string]$File) {
    $lines = Get-Content $File
    $c = [ordered]@{ id = $null; prompt = ''; depends_on = @(); gates = @() }
    $section = $null
    $promptLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^\s*#' -or $line -match '^---\s*$') { continue }
        if ($line -match '^id:\s*(.+?)\s*$') { $c.id = $Matches[1]; $section = $null; continue }
        if ($line -match '^prompt:\s*[|>][-+]?\s*$') { $section = 'prompt'; continue }
        if ($line -match '^prompt:\s*(.+?)\s*$') { $c.prompt = $Matches[1].Trim('"', "'"); $section = $null; continue }
        if ($line -match '^(depends_on|dependsOn):\s*\[\s*\]\s*$') { $section = $null; continue }
        if ($line -match '^(depends_on|dependsOn):\s*$') { $section = 'depends_on'; continue }
        if ($line -match '^gates:\s*$') { $section = 'gates'; continue }
        if ($line -match '^\S') { $section = $null; continue }
        switch ($section) {
            'prompt' { $promptLines.Add($line.Trim()) }
            'depends_on' { if ($line -match '^\s*-\s*(.+?)\s*$') { $c.depends_on += $Matches[1] } }
            'gates' { if ($line -match '^\s*-\s*(.+?)\s*$') { $c.gates += $Matches[1] } }
        }
    }
    if ($promptLines.Count) { $c.prompt = (($promptLines -join ' ').Trim()) }
    if (-not $c.id -or -not $c.prompt) { throw "$File needs an id and a prompt." }
    return $c
}

# The upstream `criteria import` command doesn't send a project ID, so criteria are created
# one by one with `criteria create`, parents before children. Existing criteria are left alone.
function Import-Criteria {
    $dir = Join-Path $EvalRoot 'criteria'
    if (-not (Test-Path $dir)) { return }
    Write-Host 'Creating criteria...'
    $pending = New-Object System.Collections.Generic.List[object]
    foreach ($file in Get-ChildItem $dir -Include *.yaml, *.yml -Recurse) { $pending.Add((Read-CriterionYaml $file.FullName)) }

    $known = @(Get-ExistingCriterionIds)
    while ($pending.Count -gt 0) {
        $ready = @($pending | Where-Object { -not ($_.depends_on | Where-Object { $known -notcontains $_ }) })
        if ($ready.Count -eq 0) {
            $names = ($pending | ForEach-Object { $_.id }) -join ', '
            throw "Criteria depend on IDs that don't exist: $names. Check depends_on."
        }
        foreach ($c in $ready) {
            if ($known -contains $c.id) { Write-Host "  $($c.id) already exists." }
            else {
                $cliArgs = @('criteria', 'create', '--id', $c.id, '--prompt', $c.prompt)
                if ($c.depends_on.Count) { $cliArgs += @('--depends-on') + $c.depends_on }
                if ($c.gates.Count) { $cliArgs += @('--gates') + $c.gates }
                $out = Invoke-Cli @cliArgs
                if ($out -match 'Error') { throw "Could not create criterion '$($c.id)': $out" }
                Write-Host "  created $($c.id)"
                $known += $c.id
            }
            [void]$pending.Remove($c)
        }
    }
}

function Get-ExistingCriterionIds {
    $out = Invoke-Cli criteria list -o json
    return @(ConvertFrom-JsonSafe $out | ForEach-Object { if ($_._id) { $_._id } elseif ($_.id) { $_.id } })
}

# Fails early when a criterion named in the scenario isn't in Scope; otherwise the Judge
# reports an infrastructure error on every run.
function Assert-ScenarioCriteria {
    $scenario = Get-Content (Join-Path $EvalRoot 'scenario.yaml')
    $ids = @(); $inList = $false
    foreach ($line in $scenario) {
        if ($line -match '^criteria:\s*$') { $inList = $true; continue }
        if ($inList -and $line -match '^\s*-\s*(\S+)\s*$') { $ids += $Matches[1]; continue }
        if ($inList -and $line -match '^\S') { $inList = $false }
    }
    $known = @(Get-ExistingCriterionIds)
    $missing = @($ids | Where-Object { $known -notcontains $_ })
    if ($missing.Count) { throw "scenario.yaml names criteria that don't exist in Scope: $($missing -join ', ')." }
}

function Get-JsonArray([string]$File) {
    # Piping unrolls the array; Windows PowerShell 5.1 otherwise returns it as one object.
    return @(Get-Content $File -Raw | ConvertFrom-Json | ForEach-Object { $_ })
}

function Register-McpServers {
    $file = Join-Path $EvalRoot 'mcp-servers.json'
    if (-not (Test-Path $file)) { return }
    $wanted = Get-JsonArray $file
    $existing = @(ConvertFrom-JsonSafe (Invoke-Cli mcp server list -o json))
    foreach ($s in $wanted) {
        if ($existing | Where-Object { $_._id -eq $s.id }) { Write-Host "MCP server '$($s.id)' already registered."; continue }
        $cliArgs = @('mcp', 'server', 'create', '--id', $s.id, '--name', $s.name, '--type', $s.type)
        if ($s.url) { $cliArgs += @('--url', (Expand-EnvReferences $s.url)) }
        if ($s.description) { $cliArgs += @('--description', $s.description) }
        foreach ($h in @($s.headers)) { if ($h) { $cliArgs += @('--header', (Expand-EnvReferences $h)) } }
        Write-Host "Registering MCP server '$($s.id)'..."
        Invoke-Cli @cliArgs | Select-Object -Last 2 | ForEach-Object { Write-Host "  $_" }
    }
}

function Get-ProfileDefinitions {
    $file = Join-Path $EvalRoot 'profiles.json'
    if (Test-Path $file) { $defs = Get-JsonArray $file }
    else { $defs = @([pscustomobject]@{ name = 'baseline' }) }
    if ($ProfileName) { $defs = @($defs | Where-Object { $ProfileName -contains $_.name }) }
    if ($defs.Count -eq 0) { throw 'No matching profiles.' }
    return $defs
}

# Profile versions are immutable, so a changed definition becomes a new profile (hash suffix).
function Resolve-Profile($Def) {
    # PowerShell variable names are case-insensitive, so these can't be named $worker or $model.
    $agentWorker = if ($Def.worker) { $Def.worker } else { $Worker }
    $agentModel = if ($Def.model) { $Def.model } else { $Model }
    $mcp = @($Def.mcpServers | Where-Object { $_ })
    $skills = @($Def.skills | Where-Object { $_ })
    $hash = Get-ShortHash (($agentWorker, $agentModel, ($mcp -join ','), ($skills -join ',')) -join '|')
    $name = "$EvalName-$($Def.name)-$hash"

    $existing = @(ConvertFrom-JsonSafe (Invoke-Cli profile list -o json)) | Where-Object { $_.name -eq $name }
    if (-not $existing) {
        Write-Host "Creating profile '$name'..."
        $cliArgs = @('profile', 'create', '-n', $name, '-w', $agentWorker, '-m', $agentModel)
        if ($mcp.Count) { $cliArgs += @('--mcp-servers') + $mcp }
        if ($skills.Count) { $cliArgs += @('--skills') + $skills }
        Invoke-Cli @cliArgs | Select-Object -Last 2 | ForEach-Object { Write-Host "  $_" }
        $existing = @(ConvertFrom-JsonSafe (Invoke-Cli profile list -o json)) | Where-Object { $_.name -eq $name }
    }
    $profileId = @($existing)[0]._id
    if (-not $profileId) { throw "Could not find profile '$name' after creating it." }
    return [pscustomobject]@{ Label = $Def.name; Name = $name; Id = $profileId; Worker = $agentWorker; Model = $agentModel; Mcp = $mcp; Skills = $skills }
}

function Submit-Run($Resolved) {
    $scenario = Join-Path $EvalRoot 'scenario.yaml'
    $submitArgs = @('run', 'submit', '-s', $scenario, '--profile', $Resolved.Id, '--no-stream')
    if ($MaxIterations -gt 0) { $submitArgs += @('--max-iterations', "$MaxIterations") }
    $out = Invoke-Cli @submitArgs
    $id = [regex]::Match($out, 'Run ID: (\S+)').Groups[1].Value
    if (-not $id) { Write-Warning "Could not read a run ID from: $out"; return $null }
    return $id
}

function Wait-Runs($Runs) {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline) {
        $open = 0
        foreach ($r in @($Runs | Where-Object { -not $_.Done })) {
            $status = Invoke-Cli run status -i $r.Id
            if ($status -match 'Status: (done|failed|cancelled)') { $r.Done = $true } else { $open++ }
        }
        Write-Host ("  {0:HH:mm:ss} {1} of {2} runs finished" -f (Get-Date), ($Runs.Count - $open), $Runs.Count)
        if ($open -eq 0) { return }
        Start-Sleep -Seconds 20
    }
    Write-Warning "Timed out after $TimeoutMinutes minutes; unfinished runs are reported as incomplete."
}

function Read-RunLog([string]$Log) {
    # Run timestamps include queue time, so active time comes from the worker's own log lines.
    $times = @([regex]::Matches($Log, '(?m)^\[(\d{1,2}:\d{2}:\d{2} [AP]M)\][^\r\n]*\[coder-acp-[^\]]*\]') | ForEach-Object {
        [datetime]::ParseExact($_.Groups[1].Value, 'h:mm:ss tt', [Globalization.CultureInfo]::InvariantCulture)
    })
    $active = $null
    if ($times.Count -ge 2) {
        $span = ($times[-1] - $times[0]).TotalSeconds
        if ($span -lt 0) { $span += 86400 }
        $active = [int]$span
    }

    # Every tool call is logged once as "Tool call: [kind] ...". MCP tools reach the agent
    # through Scope's MCP gateway and are named mcp-gateway-<server>__<tool>.
    $mcp = @{}
    $web = 0
    $toolLines = [regex]::Matches($Log, '(?m)^\[[^\]]+\] \[[^\]]+\] \[coder-acp-[^\]]*\][^\r\n]*?Tool call: \[(\w+)\] ([^\r\n]*)')
    foreach ($m in $toolLines) {
        $kind = $m.Groups[1].Value
        $text = $m.Groups[2].Value
        if ($text -match '^mcp-gateway-(\S+)') { $mcp[$Matches[1]] = 1 + [int]$mcp[$Matches[1]] }
        elseif ($kind -eq 'fetch') { $web++ }
        elseif ($kind -eq 'execute' -and $text -match '\b(curl|wget|Invoke-WebRequest|gh api)\b') { $web++ }
    }
    $mcpCalls = 0
    foreach ($v in $mcp.Values) { $mcpCalls += $v }

    $criteria = ''
    $dag = @([regex]::Matches($Log, 'Criteria DAG: (\d+) passed, (\d+) failed, (\d+) skipped'))
    if ($dag.Count) {
        $last = $dag[$dag.Count - 1]
        $criteria = "$($last.Groups[1].Value) passed, $($last.Groups[2].Value) failed, $($last.Groups[3].Value) skipped"
    }
    return [pscustomobject]@{
        ActiveSeconds = $active
        McpCalls = $mcpCalls
        McpTools = @($mcp.Keys | Sort-Object | ForEach-Object { "$_ x$($mcp[$_])" })
        WebCalls = $web
        Criteria = $criteria
    }
}

function Get-RunTrace([string]$RunId) {
    return Read-RunLog (Invoke-Cli run logs -i $RunId --from-start)
}

function Get-RunResult($Run) {
    $o = @(ConvertFrom-JsonSafe (Invoke-Cli run get -i $Run.Id -o json))
    $r = if ($o.Count) { $o[0] } else { $null }
    $detail = if ($r -and $r.error -and $r.error -ne 'N/A') { "$($r.error)" } else { '' }
    $outcome = if (-not $Run.Done -or -not $r) { 'incomplete' }
        elseif ($detail -match 'Judge infrastructure error|Judge evaluation failed|Key acquisition failed') { 'invalid' }
        elseif ($r.passed -eq 'yes') { 'passed' }
        else { 'failed' }
    $seconds = $null
    if ($r -and $r.createdAt -and $r.updatedAt) {
        try { $seconds = [int]([datetime]$r.updatedAt - [datetime]$r.createdAt).TotalSeconds } catch { $seconds = $null }
    }
    $trace = if ($Run.Done) { Get-RunTrace $Run.Id } else { $null }
    return [pscustomobject]@{
        RunId = $Run.Id; Profile = $Run.Profile; Outcome = $outcome
        Gates = if ($r) { "$($r.gateSummaries)" } else { '' }
        Turns = if ($r) { "$($r.turnsCount)" } else { '' }
        Seconds = $seconds
        ActiveSeconds = if ($trace) { $trace.ActiveSeconds } else { $null }
        McpCalls = if ($trace) { $trace.McpCalls } else { 0 }
        McpTools = if ($trace) { $trace.McpTools -join ', ' } else { '' }
        WebCalls = if ($trace) { $trace.WebCalls } else { 0 }
        Criteria = if ($trace) { $trace.Criteria } else { '' }
        Error = $detail
    }
}

function Write-Report($Profiles, $Results) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $Results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutDir 'results.json') -Encoding UTF8

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# Evaluation report: $EvalName")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("- Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
    [void]$sb.AppendLine("- Default model: $Model, worker: $Worker")
    [void]$sb.AppendLine("- Runs per profile: $Count")
    [void]$sb.AppendLine("- Max Judge iterations per run: $(if ($MaxIterations -gt 0) { $MaxIterations } else { 'Scope default' })")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Pass rate counts only valid runs. Runs where the Judge itself failed are `invalid` and say nothing about the agent. A run can pass after several turns, because the Judge gives the agent feedback to retry; the turns column shows how many it took.')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Profile | MCP servers | Skills | Runs | Passed | Failed | Invalid | Incomplete | Pass rate | Median turns | Median active seconds | Runs using MCP tools | Runs with web access |')
    [void]$sb.AppendLine('|---------|-------------|--------|------|--------|--------|---------|------------|-----------|--------------|-----------------------|----------------------|----------------------|')
    $rates = @{}
    $webRuns = @{}
    foreach ($p in $Profiles) {
        $mine = @($Results | Where-Object { $_.Profile -eq $p.Label })
        $passed = @($mine | Where-Object { $_.Outcome -eq 'passed' }).Count
        $failed = @($mine | Where-Object { $_.Outcome -eq 'failed' }).Count
        $invalid = @($mine | Where-Object { $_.Outcome -eq 'invalid' }).Count
        $incomplete = @($mine | Where-Object { $_.Outcome -eq 'incomplete' }).Count
        $valid = $passed + $failed
        $rate = if ($valid) { [math]::Round(100 * $passed / $valid) } else { $null }
        $rates[$p.Label] = $rate
        $times = @($mine | Where-Object { $null -ne $_.ActiveSeconds } | ForEach-Object { $_.ActiveSeconds } | Sort-Object)
        $median = if ($times.Count) { $times[[int][math]::Floor(($times.Count - 1) / 2)] } else { '' }
        $turnValues = @($mine | Where-Object { $_.Turns -match '^\d+$' } | ForEach-Object { [int]$_.Turns } | Sort-Object)
        $medianTurns = if ($turnValues.Count) { $turnValues[[int][math]::Floor(($turnValues.Count - 1) / 2)] } else { '' }
        $rateText = if ($null -ne $rate) { "$rate% ($passed/$valid)" } else { 'n/a' }
        $mcpRuns = @($mine | Where-Object { $_.McpCalls -gt 0 }).Count
        $webRunCount = @($mine | Where-Object { $_.WebCalls -gt 0 }).Count
        $webRuns[$p.Label] = $webRunCount
        [void]$sb.AppendLine("| $($p.Label) | $($p.Mcp -join ', ') | $($p.Skills -join ', ') | $($mine.Count) | $passed | $failed | $invalid | $incomplete | $rateText | $medianTurns | $median | $mcpRuns of $($mine.Count) | $webRunCount of $($mine.Count) |")
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Runs using MCP tools and runs with web access come from each run''s trace. A profile that lists an MCP server but shows no runs using its tools means the agent never called it. Web access means the agent fetched a URL or ran curl or wget.')
    [void]$sb.AppendLine('')
    if (@($Profiles).Count -gt 1) {
        $base = @($Profiles)[0].Label
        $baseWeb = $webRuns[$base]
        if ($baseWeb -gt 0 -and $null -ne $rates[$base] -and $rates[$base] -gt 0) {
            [void]$sb.AppendLine("> **Check your baseline.** $baseWeb of the $base runs fetched from the web, and some passed. Their result may reflect public information, not the absence of your tool. See docs/interpreting-results.md.")
            [void]$sb.AppendLine('')
        }
        [void]$sb.AppendLine('Compared with the first profile (' + $base + '):')
        foreach ($p in @($Profiles)[1..(@($Profiles).Count - 1)]) {
            if ($null -ne $rates[$p.Label] -and $null -ne $rates[$base]) {
                $d = $rates[$p.Label] - $rates[$base]
                [void]$sb.AppendLine("- $($p.Label): $('{0:+#;-#;0}' -f $d) percentage points")
            }
        }
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('With few runs per profile, differences of a run or two are noise. Increase `-Count` before drawing conclusions, and read the traces of failures.')
        [void]$sb.AppendLine('')
    }
    $bad = @($Results | Where-Object { $_.Outcome -in 'failed', 'invalid', 'incomplete' })
    if ($bad.Count) {
        [void]$sb.AppendLine('## Runs to review')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('| Profile | Run | Outcome | Gates | Criteria | Detail |')
        [void]$sb.AppendLine('|---------|-----|---------|-------|----------|--------|')
        foreach ($b in $bad) {
            $text = ($b.Error -replace '\|', '\|' -replace "[\r\n]+", ' ')
            if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
            [void]$sb.AppendLine("| $($b.Profile) | $($b.RunId) | $($b.Outcome) | $($b.Gates) | $($b.Criteria) | $text |")
        }
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("Open a run in the Portal to read its trace and the Judge's reasoning for each criterion.")
    }
    Set-Content (Join-Path $OutDir 'report.md') $sb.ToString() -Encoding UTF8
}

Initialize-Cli
if (-not $SkipSetup) {
    Import-Criteria
    Register-McpServers
}
Assert-ScenarioCriteria
$profiles = @(Get-ProfileDefinitions | ForEach-Object { Resolve-Profile $_ })

$runs = New-Object System.Collections.Generic.List[object]
foreach ($p in $profiles) {
    for ($i = 1; $i -le $Count; $i++) {
        $id = Submit-Run $p
        if ($id) {
            Write-Host "Submitted $($p.Label) run $i of ${Count}: $id"
            $runs.Add([pscustomobject]@{ Id = $id; Profile = $p.Label; Done = $false })
        }
    }
}
if ($runs.Count -eq 0) { throw 'No runs were submitted.' }

Write-Host 'Waiting for runs...'
Wait-Runs $runs
$results = @($runs | ForEach-Object { Get-RunResult $_ })
Write-Report $profiles $results
Write-Host "Report: $(Join-Path $OutDir 'report.md')"
