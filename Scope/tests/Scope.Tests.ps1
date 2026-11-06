# Tests for the scripts in this folder. They use plain `throw` assertions so they run under
# Pester 3.4 (installed with Windows PowerShell) and Pester 5.
#
# Run them with: .\tests\Invoke-Tests.ps1

$KitRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EvalScript = Join-Path $KitRoot 'evaluations\Invoke-ScopeEval.ps1'

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ("$Actual" -ne "$Expected") { throw "$Message`n  expected: $Expected`n  actual:   $Actual" }
}

function Invoke-Git([string]$Repo, [string[]]$GitArgs) {
    $out = & git -C $Repo -c core.autocrlf=false -c user.name=test -c user.email=test@example.com @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed: $out" }
    return $out
}

# Builds a throwaway upstream clone and a copy of the kit's scripts pointing at it.
function New-Fixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("scope-kit-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $clone = Join-Path $root 'clone'
    $kit = Join-Path $root 'kit'
    New-Item -ItemType Directory -Force (Join-Path $clone 'docs\architecture'), (Join-Path $clone 'scripts\local'), $kit | Out-Null

    Set-Content (Join-Path $clone 'pnpm-workspace.yaml') "packages:`n  - apps/*`n"
    Set-Content (Join-Path $clone 'README.md') 'upstream readme'
    Set-Content (Join-Path $clone 'docs\a.md') 'upstream a'
    Set-Content (Join-Path $clone 'docs\architecture\b.md') 'upstream b'
    Set-Content (Join-Path $clone 'other.txt') 'upstream other'
    git -C $clone init -q 2>&1 | Out-Null
    Invoke-Git $clone @('add', '-A') | Out-Null
    Invoke-Git $clone @('commit', '-q', '-m', 'base') | Out-Null
    $head = (Invoke-Git $clone @('rev-parse', 'HEAD')).Trim()

    foreach ($f in 'Apply-Overlay.ps1', 'Export-Overlay.ps1') { Copy-Item (Join-Path $KitRoot $f) $kit }
    Set-Content (Join-Path $kit 'upstream.json') ('{"repository":"test","commit":"' + $head + '"}')
    return [pscustomobject]@{ Root = $root; Clone = $clone; Kit = $kit; Head = $head }
}

function Remove-Fixture($Fixture) {
    if ($Fixture -and (Test-Path $Fixture.Root)) { Remove-Item $Fixture.Root -Recurse -Force -ErrorAction SilentlyContinue }
}

function Edit-Clone($Fixture) {
    Set-Content (Join-Path $Fixture.Clone 'README.md') 'our readme'
    Set-Content (Join-Path $Fixture.Clone 'docs\a.md') 'our a'
    Set-Content (Join-Path $Fixture.Clone 'docs\new.md') 'our new doc'
    Set-Content (Join-Path $Fixture.Clone 'scripts\local\tool.ps1') 'our tool'
    Set-Content (Join-Path $Fixture.Clone 'pnpm-workspace.yaml') "packages:`n  - apps/*`nfetchRetries: 8`n"
}

function Reset-Clone($Fixture) {
    Invoke-Git $Fixture.Clone @('checkout', '-q', '--', '.') | Out-Null
    Invoke-Git $Fixture.Clone @('clean', '-fdq') | Out-Null
}

Describe 'Export-Overlay.ps1 and Apply-Overlay.ps1' {
    It 'exports changes and applies them to a fresh clone (round trip)' {
        $fx = New-Fixture
        try {
            Edit-Clone $fx
            & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone | Out-Null

            Assert-True (Test-Path (Join-Path $fx.Kit 'overlay\README.md')) 'README.md was not exported'
            Assert-True (Test-Path (Join-Path $fx.Kit 'overlay\docs\new.md')) 'a new doc was not exported'
            Assert-True (Test-Path (Join-Path $fx.Kit 'overlay\scripts\local\tool.ps1')) 'a new script was not exported'
            Assert-True (-not (Test-Path (Join-Path $fx.Kit 'overlay\docs\architecture\b.md'))) 'an unchanged file was exported'
            Assert-True (Select-String -Path (Join-Path $fx.Kit 'patches\local-changes.patch') -Pattern 'fetchRetries' -Quiet) 'the config change is missing from the patch'

            Reset-Clone $fx
            & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $fx.Clone | Out-Null

            Assert-Equal ((Get-Content (Join-Path $fx.Clone 'README.md') -Raw).Trim()) 'our readme' 'README.md differs after apply'
            Assert-Equal ((Get-Content (Join-Path $fx.Clone 'docs\new.md') -Raw).Trim()) 'our new doc' 'new doc differs after apply'
            Assert-True (Select-String -Path (Join-Path $fx.Clone 'pnpm-workspace.yaml') -Pattern 'fetchRetries: 8' -Quiet) 'the patch was not applied'
        } finally { Remove-Fixture $fx }
    }

    It 'applies twice without error and reports the patch as already applied' {
        $fx = New-Fixture
        try {
            Edit-Clone $fx
            & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone | Out-Null
            Reset-Clone $fx
            & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $fx.Clone | Out-Null
            $second = & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $fx.Clone 3>&1 4>&1 6>&1 | Out-String
            Assert-True ($second -match 'already applied') "second apply output did not say 'already applied': $second"
            $content = Get-Content (Join-Path $fx.Clone 'pnpm-workspace.yaml') -Raw
            Assert-Equal ([regex]::Matches($content, 'fetchRetries').Count) 1 'the patch was applied twice'
        } finally { Remove-Fixture $fx }
    }

    It 'skips the patch with -SkipPatch' {
        $fx = New-Fixture
        try {
            Edit-Clone $fx
            & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone | Out-Null
            Reset-Clone $fx
            & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $fx.Clone -SkipPatch | Out-Null
            Assert-True (-not (Select-String -Path (Join-Path $fx.Clone 'pnpm-workspace.yaml') -Pattern 'fetchRetries' -Quiet)) 'the patch was applied despite -SkipPatch'
            Assert-Equal ((Get-Content (Join-Path $fx.Clone 'README.md') -Raw).Trim()) 'our readme' 'the overlay was not applied'
        } finally { Remove-Fixture $fx }
    }

    It 'warns when the clone is not at the pinned commit' {
        $fx = New-Fixture
        try {
            Set-Content (Join-Path $fx.Clone 'other.txt') 'changed upstream'
            Invoke-Git $fx.Clone @('commit', '-q', '-am', 'second') | Out-Null
            $out = & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $fx.Clone 3>&1 | Out-String
            Assert-True ($out -match 'not the pinned commit') "no pinned-commit warning: $out"
        } finally { Remove-Fixture $fx }
    }

    It 'refuses to export from a different commit unless -UpdateCommit is given' {
        $fx = New-Fixture
        try {
            Set-Content (Join-Path $fx.Clone 'other.txt') 'changed upstream'
            Invoke-Git $fx.Clone @('commit', '-q', '-am', 'second') | Out-Null
            $threw = $false
            try { & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone | Out-Null } catch { $threw = $true }
            Assert-True $threw 'export should fail on a different commit'

            & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone -UpdateCommit | Out-Null
            $newHead = (Invoke-Git $fx.Clone @('rev-parse', 'HEAD')).Trim()
            $pinned = (Get-Content (Join-Path $fx.Kit 'upstream.json') -Raw | ConvertFrom-Json).commit
            Assert-Equal $pinned $newHead 'upstream.json was not updated'
        } finally { Remove-Fixture $fx }
    }

    It 'skips new files outside the overlay paths and warns' {
        $fx = New-Fixture
        try {
            Set-Content (Join-Path $fx.Clone 'stray.txt') 'not ours to export'
            $out = & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone 3>&1 | Out-String
            Assert-True ($out -match 'stray.txt') "no warning about the stray file: $out"
            Assert-True (-not (Test-Path (Join-Path $fx.Kit 'overlay\stray.txt'))) 'a file outside the overlay paths was exported'
        } finally { Remove-Fixture $fx }
    }

    It 'changes nothing with -WhatIf' {
        $fx = New-Fixture
        try {
            Edit-Clone $fx
            & (Join-Path $fx.Kit 'Export-Overlay.ps1') -ScopePath $fx.Clone -WhatIf | Out-Null
            Assert-True (-not (Test-Path (Join-Path $fx.Kit 'overlay'))) '-WhatIf created the overlay'
            Assert-True (-not (Test-Path (Join-Path $fx.Kit 'patches'))) '-WhatIf created the patches'
        } finally { Remove-Fixture $fx }
    }

    It 'rejects a path that is not a Scope clone' {
        $fx = New-Fixture
        try {
            $empty = Join-Path $fx.Root 'empty'
            New-Item -ItemType Directory $empty | Out-Null
            $threw = $false
            try { & (Join-Path $fx.Kit 'Apply-Overlay.ps1') -ScopePath $empty | Out-Null } catch { $threw = $true }
            Assert-True $threw 'apply should reject a folder without pnpm-workspace.yaml'
        } finally { Remove-Fixture $fx }
    }
}

# Loads functions from Invoke-ScopeEval.ps1 without running the script.
function Import-EvalFunction([string]$Name) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($EvalScript, [ref]$tokens, [ref]$errors)
    $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true)
    if (-not $fn) { throw "Function $Name not found in Invoke-ScopeEval.ps1" }
    # Defined at global scope so the function outlives this helper.
    $text = $fn.Extent.Text -replace "^function\s+$([regex]::Escape($Name))", "function global:$Name"
    . ([scriptblock]::Create($text))
}

Describe 'Invoke-ScopeEval.ps1 helpers' {
    It 'reads a criterion with a block prompt, dependencies, and gates' {
        Import-EvalFunction 'Read-CriterionYaml'
        $file = Join-Path ([IO.Path]::GetTempPath()) ("crit-" + [guid]::NewGuid().ToString('N') + '.yaml')
        Set-Content $file "id: my_check`nprompt: |`n  First line.`n  Second line.`ndepends_on:`n  - parent_one`n  - parent_two`ngates:`n  - select`n"
        try {
            $c = Read-CriterionYaml $file
            Assert-Equal $c.id 'my_check' 'id'
            Assert-Equal $c.prompt 'First line. Second line.' 'prompt'
            Assert-Equal ($c.depends_on -join ',') 'parent_one,parent_two' 'depends_on'
            Assert-Equal ($c.gates -join ',') 'select' 'gates'
        } finally { Remove-Item $file -Force }
    }

    It 'reads a criterion with an empty dependency list' {
        Import-EvalFunction 'Read-CriterionYaml'
        $file = Join-Path ([IO.Path]::GetTempPath()) ("crit-" + [guid]::NewGuid().ToString('N') + '.yaml')
        Set-Content $file "id: root_check`nprompt: A single line prompt.`ndepends_on: []`ngates:`n  - select`n"
        try {
            $c = Read-CriterionYaml $file
            Assert-Equal @($c.depends_on).Count 0 'depends_on should be empty'
            Assert-Equal $c.prompt 'A single line prompt.' 'prompt'
        } finally { Remove-Item $file -Force }
    }

    It 'rejects a criterion without an id' {
        Import-EvalFunction 'Read-CriterionYaml'
        $file = Join-Path ([IO.Path]::GetTempPath()) ("crit-" + [guid]::NewGuid().ToString('N') + '.yaml')
        Set-Content $file "prompt: no id here`n"
        try {
            $threw = $false
            try { Read-CriterionYaml $file | Out-Null } catch { $threw = $true }
            Assert-True $threw 'a criterion without an id should be rejected'
        } finally { Remove-Item $file -Force }
    }

    It 'reads every shipped criterion file' {
        Import-EvalFunction 'Read-CriterionYaml'
        $files = Get-ChildItem (Join-Path $KitRoot 'evaluations') -Recurse -Include *.yaml |
            Where-Object { $_.DirectoryName -match '\\criteria$' -and $_.FullName -notmatch '\\_template\\' -and $_.FullName -notmatch '\\local\\' }
        Assert-True (@($files).Count -gt 0) 'no shipped criteria found'
        foreach ($f in $files) { Read-CriterionYaml $f.FullName | Out-Null }
    }

    It 'expands environment references and fails on missing ones' {
        Import-EvalFunction 'Expand-EnvReferences'
        $env:SCOPE_KIT_TEST_VALUE = 'abc123'
        try {
            Assert-Equal (Expand-EnvReferences 'Bearer ${env:SCOPE_KIT_TEST_VALUE}') 'Bearer abc123' 'expansion'
        } finally { Remove-Item Env:\SCOPE_KIT_TEST_VALUE }
        $threw = $false
        try { Expand-EnvReferences '${env:SCOPE_KIT_TEST_MISSING_VALUE}' | Out-Null } catch { $threw = $true }
        Assert-True $threw 'a missing environment variable should throw'
    }

    It 'unrolls JSON arrays into separate items' {
        Import-EvalFunction 'ConvertFrom-JsonSafe'
        Assert-Equal @(ConvertFrom-JsonSafe '[{"a":1},{"a":2}]').Count 2 'two items'
        Assert-Equal @(ConvertFrom-JsonSafe '[{"a":1}]').Count 1 'one item'
        Assert-Equal @(ConvertFrom-JsonSafe 'not json').Count 0 'invalid JSON returns nothing'
    }

    It 'creates parent criteria before their children' {
        Import-EvalFunction 'Import-Criteria'
        Import-EvalFunction 'Read-CriterionYaml'
        Import-EvalFunction 'Get-ExistingCriterionIds'
        Import-EvalFunction 'ConvertFrom-JsonSafe'
        $global:EvalRoot = Join-Path ([IO.Path]::GetTempPath()) ("eval-" + [guid]::NewGuid().ToString('N'))
        $dir = Join-Path $global:EvalRoot 'criteria'
        New-Item -ItemType Directory -Force $dir | Out-Null
        # The child sorts first alphabetically, which is what broke the first version.
        Set-Content (Join-Path $dir 'a_child.yaml') "id: child_check`nprompt: Child.`ndepends_on:`n  - parent_check`n"
        Set-Content (Join-Path $dir 'z_parent.yaml') "id: parent_check`nprompt: Parent.`ndepends_on: []`n"
        $global:KitCalls = New-Object System.Collections.Generic.List[string]
        function global:Invoke-Cli {
            if ($args[0] -eq 'criteria' -and $args[1] -eq 'list') { return '[]' }
            if ($args[0] -eq 'criteria' -and $args[1] -eq 'create') { $global:KitCalls.Add($args[3]); return 'Created criterion' }
            return ''
        }
        try {
            Import-Criteria | Out-Null
            Assert-Equal ($global:KitCalls -join ',') 'parent_check,child_check' 'creation order'
        } finally {
            Remove-Item Function:\global:Invoke-Cli -ErrorAction SilentlyContinue
            Remove-Item $global:EvalRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'fails when a criterion depends on one that does not exist' {
        Import-EvalFunction 'Import-Criteria'
        Import-EvalFunction 'Read-CriterionYaml'
        Import-EvalFunction 'Get-ExistingCriterionIds'
        Import-EvalFunction 'ConvertFrom-JsonSafe'
        $global:EvalRoot = Join-Path ([IO.Path]::GetTempPath()) ("eval-" + [guid]::NewGuid().ToString('N'))
        $dir = Join-Path $global:EvalRoot 'criteria'
        New-Item -ItemType Directory -Force $dir | Out-Null
        Set-Content (Join-Path $dir 'orphan.yaml') "id: orphan_check`nprompt: Orphan.`ndepends_on:`n  - missing_parent`n"
        function global:Invoke-Cli { return '[]' }
        try {
            $threw = $false
            try { Import-Criteria | Out-Null } catch { $threw = $true }
            Assert-True $threw 'a missing parent should fail the import'
        } finally {
            Remove-Item Function:\global:Invoke-Cli -ErrorAction SilentlyContinue
            Remove-Item $global:EvalRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'produces a stable short hash that changes with the input' {
        Import-EvalFunction 'Get-ShortHash'
        Assert-Equal (Get-ShortHash 'a|b') (Get-ShortHash 'a|b') 'hash is not stable'
        Assert-True ((Get-ShortHash 'a|b') -ne (Get-ShortHash 'a|c')) 'hash does not change with input'
        Assert-Equal (Get-ShortHash 'x').Length 6 'hash length'
    }

    It 'measures active time from the worker log lines, across midnight' {
        Import-EvalFunction 'Read-RunLog'
        $log = "[11:59:50 PM] [INFO ] [coder-acp-copilot] [iter 1] start`n[11:59:55 PM] [INFO ] [post-processor] ignored`n[12:00:20 AM] [INFO ] [coder-acp-copilot] [iter 1] end"
        Assert-Equal (Read-RunLog $log).ActiveSeconds 30 'active seconds'
    }

    It 'returns no duration when a run has no worker log lines' {
        Import-EvalFunction 'Read-RunLog'
        Assert-True ($null -eq (Read-RunLog 'nothing useful').ActiveSeconds) 'expected no duration'
    }

    It 'counts MCP tool calls by tool and ignores tool updates' {
        Import-EvalFunction 'Read-RunLog'
        $log = @(
            '[3:33:41 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [search] mcp-gateway-my-server__search_items query=blog (pending)',
            '[3:33:41 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool update: [search] mcp-gateway-my-server__search_items - completed',
            '[3:33:43 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [read] mcp-gateway-my-server__get_item name=x (pending)',
            '[3:33:44 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [read] mcp-gateway-my-server__get_item name=y (pending)',
            '[3:33:46 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [edit] Creating /tmp/workspaces/p/out.md path=/tmp/workspaces/p/out.md (pending)'
        ) -join "`n"
        $t = Read-RunLog $log
        Assert-Equal $t.McpCalls 3 'MCP call count'
        Assert-Equal ($t.McpTools -join ',') 'my-server__get_item x2,my-server__search_items x1' 'MCP tools'
        Assert-Equal $t.WebCalls 0 'web call count'
    }

    It 'counts fetches and curl or wget commands as web access' {
        Import-EvalFunction 'Read-RunLog'
        $log = @(
            '[2:57:23 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [execute] List skills command=curl -s "https://api.github.com/repos/x/y", description=List (pending)',
            '[2:57:40 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [fetch] Fetching https://example.com/docs url=https://example.com/docs (pending)',
            '[2:57:50 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [execute] List files command=ls -la /tmp, description=List (pending)',
            '[2:57:55 PM] [DEBUG] [coder-acp-copilot] [iter 1] Tool call: [read] Viewing /tmp/workspaces/p path=/tmp/workspaces/p (pending)'
        ) -join "`n"
        $t = Read-RunLog $log
        Assert-Equal $t.WebCalls 2 'web call count'
        Assert-Equal $t.McpCalls 0 'MCP call count'
    }

    It 'reports the last criteria summary in a run log' {
        Import-EvalFunction 'Read-RunLog'
        $log = "[3:33:49 PM] [INFO ] [coder-acp-copilot] [iter 1] Criteria DAG: 0 passed, 2 failed, 0 skipped`n[3:34:07 PM] [INFO ] [coder-acp-copilot] [iter 2] Criteria DAG: 1 passed, 1 failed, 0 skipped"
        Assert-Equal (Read-RunLog $log).Criteria '1 passed, 1 failed, 0 skipped' 'criteria summary'
    }

    It 'writes a report that excludes invalid runs from pass rates' {
        Import-EvalFunction 'Write-Report'
        $OutDir = Join-Path ([IO.Path]::GetTempPath()) ("report-" + [guid]::NewGuid().ToString('N'))
        $EvalName = 'sample'; $Model = 'test-model'; $Worker = 'test-worker'; $Count = 4
        $profiles = @(
            [pscustomobject]@{ Label = 'baseline'; Mcp = @(); Skills = @() },
            [pscustomobject]@{ Label = 'variant'; Mcp = @('some-server'); Skills = @() }
        )
        $results = @(
            [pscustomobject]@{ RunId = 'r1'; Profile = 'baseline'; Outcome = 'failed'; Gates = 'select:failed'; Turns = '1'; Seconds = 60; ActiveSeconds = 50; McpCalls = 0; WebCalls = 2; Criteria = '1 passed, 1 failed, 0 skipped'; Error = 'criterion failed' },
            [pscustomobject]@{ RunId = 'r2'; Profile = 'baseline'; Outcome = 'passed'; Gates = 'select:passed'; Turns = '1'; Seconds = 70; ActiveSeconds = 60; McpCalls = 0; WebCalls = 1; Criteria = '2 passed, 0 failed, 0 skipped'; Error = '' },
            [pscustomobject]@{ RunId = 'r3'; Profile = 'variant'; Outcome = 'passed'; Gates = 'select:passed'; Turns = '2'; Seconds = 80; ActiveSeconds = 70; McpCalls = 3; WebCalls = 0; Criteria = '2 passed, 0 failed, 0 skipped'; Error = '' },
            [pscustomobject]@{ RunId = 'r4'; Profile = 'variant'; Outcome = 'invalid'; Gates = 'select:failed'; Turns = '1'; Seconds = 5; ActiveSeconds = 4; McpCalls = 0; WebCalls = 0; Criteria = ''; Error = 'Judge infrastructure error' }
        )
        try {
            Write-Report $profiles $results
            $report = Get-Content (Join-Path $OutDir 'report.md') -Raw
            Assert-True ($report -match 'baseline \|[^\n]*\| 50% \(1/2\)') "baseline pass rate missing:`n$report"
            Assert-True ($report -match 'variant \|[^\n]*\| 100% \(1/1\)') "variant pass rate should ignore the invalid run:`n$report"
            Assert-True ($report -match '\+50 percentage points') "comparison missing:`n$report"
            Assert-True ($report -match 'Median turns') 'turns column missing'
            Assert-True ($report -match 'baseline \|[^\n]*\| 0 of 2 \| 2 of 2 \|') "baseline MCP and web counts missing:`n$report"
            Assert-True ($report -match 'variant \|[^\n]*\| 1 of 2 \| 0 of 2 \|') "variant MCP and web counts missing:`n$report"
            Assert-True ($report -match 'Check your baseline') "baseline web warning missing:`n$report"
            Assert-True ($report -match '1 passed, 1 failed, 0 skipped') 'the per-criterion summary is not listed for a failed run'
            Assert-True ($report -match 'r4') 'the invalid run is not listed for review'
            Assert-True (Test-Path (Join-Path $OutDir 'results.json')) 'results.json missing'
        } finally { if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force } }
    }

    It 'does not warn about the baseline when it never used the web' {
        Import-EvalFunction 'Write-Report'
        $OutDir = Join-Path ([IO.Path]::GetTempPath()) ("report-" + [guid]::NewGuid().ToString('N'))
        $EvalName = 'sample'; $Model = 'test-model'; $Worker = 'test-worker'; $Count = 2
        $profiles = @(
            [pscustomobject]@{ Label = 'baseline'; Mcp = @(); Skills = @() },
            [pscustomobject]@{ Label = 'variant'; Mcp = @('some-server'); Skills = @() }
        )
        $results = @(
            [pscustomobject]@{ RunId = 'r1'; Profile = 'baseline'; Outcome = 'passed'; Gates = 'select:passed'; Turns = '1'; Seconds = 60; ActiveSeconds = 50; McpCalls = 0; WebCalls = 0; Criteria = ''; Error = '' },
            [pscustomobject]@{ RunId = 'r2'; Profile = 'variant'; Outcome = 'passed'; Gates = 'select:passed'; Turns = '1'; Seconds = 60; ActiveSeconds = 50; McpCalls = 2; WebCalls = 0; Criteria = ''; Error = '' }
        )
        try {
            Write-Report $profiles $results
            $report = Get-Content (Join-Path $OutDir 'report.md') -Raw
            Assert-True ($report -notmatch 'Check your baseline') 'unexpected baseline warning'
        } finally { if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force } }
    }
}

Describe 'Scripts' {
    It 'parse without errors' {
        foreach ($f in Get-ChildItem $KitRoot -Recurse -Filter *.ps1 | Where-Object { $_.FullName -notmatch '\\local\\' -and $_.FullName -notmatch '\\.results\\' }) {
            $tokens = $null; $errors = $null
            [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
            Assert-Equal @($errors).Count 0 "$($f.Name) has parse errors: $($errors -join '; ')"
        }
    }

    It 'have no PSScriptAnalyzer errors' {
        if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) { return }
        $findings = Get-ChildItem $KitRoot -Recurse -Filter *.ps1 |
            Where-Object { $_.FullName -notmatch '\\local\\' -and $_.FullName -notmatch '\\.results\\' } |
            ForEach-Object { Invoke-ScriptAnalyzer -Path $_.FullName -Severity Error }
        Assert-Equal @($findings).Count 0 ("PSScriptAnalyzer errors: " + (($findings | ForEach-Object { "$($_.ScriptName):$($_.Line) $($_.RuleName)" }) -join '; '))
    }

    It 'ship no personal paths or secrets' {
        $pattern = 'gh[opsu]_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,}|BEGIN (RSA |EC )?PRIVATE KEY|troytaylor'
        $hits = Get-ChildItem $KitRoot -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\evaluations\\local\\' -and $_.FullName -notmatch '\\evaluations\\.results\\' -and $_.FullName -notmatch '\\tests\\' } |
            Select-String -Pattern $pattern
        Assert-Equal @($hits).Count 0 ("Found sensitive-looking content: " + (($hits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join '; '))
    }
}
