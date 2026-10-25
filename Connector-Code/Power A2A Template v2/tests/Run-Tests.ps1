<#
.SYNOPSIS
    Runs the offline tests against script.csx.

.DESCRIPTION
    Compiles script.csx together with the test harness in a temporary folder, then runs each variant:

      shipped    script.csx exactly as released, with its generic skills
      host       the framework tests, with the DummyJSON demo skills swapped in
      nohelp     host, with the help skill off
      noclaims   host, with claims-based signing off
      headers    host, with HOST_FORWARD_HEADERS = "X-API-Key, X-Tenant"
      fallback   host, with the intent resolver on in fallback mode
      always     host, with the intent resolver on in always mode

    The demo skills come from ..\demo\DummyJson.skills.txt. They exist so the framework tests have real skills
    to exercise, and they are never part of script.csx. Nothing is written to this folder.

    The build restores only from your local NuGet cache. It never contacts nuget.org, so it works on a
    machine where that feed is blocked. If Newtonsoft.Json 13.0.3 isn't cached, the script stops and says so.

    Run it after you change skills or settings. The framework tests don't depend on your skills. To check your
    own skills, add checks to ShippedTests in Program.cs, which tests the generic skills as released.

.PARAMETER ScriptPath
    The connector script to test. Defaults to ..\script.csx.

.PARAMETER DemoPath
    The demo skills file. Defaults to ..\demo\DummyJson.skills.txt.

.PARAMETER ShowAll
    Print every test result, not just failures and a summary.

.EXAMPLE
    .\Run-Tests.ps1
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot '..\script.csx'),
    [string]$DemoPath = (Join-Path $PSScriptRoot '..\demo\DummyJson.skills.txt'),
    [switch]$ShowAll
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\demo\Get-DemoScript.ps1')

$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$newtonsoft = Join-Path $packages 'newtonsoft.json\13.0.3'
if (-not (Test-Path $newtonsoft)) {
    throw "Newtonsoft.Json 13.0.3 isn't in the local NuGet cache at $packages. These tests never contact nuget.org. " +
          "Put the package in the cache, or change the version in Harness.csproj.template to one that is cached."
}

$script = Resolve-Path $ScriptPath
$source = [System.IO.File]::ReadAllText($script)
$demoSource = Get-DemoScriptText -ScriptText $source -DemoPath $DemoPath

# Each variant changes a setting in a copy of the script. A change that doesn't match means the setting was
# renamed or edited, and the variant would silently test nothing, so that's an error.
$modelEndpoint = 'HOST_LLM_ENDPOINT = "https://llm.example.com/chat"'
$variants = @(
    @{ Name = 'shipped';  Demo = $false; Changes = @() },
    @{ Name = 'host';     Demo = $true;  Changes = @() },
    @{ Name = 'nohelp';   Demo = $true;  Changes = @(, @('HOST_INCLUDE_HELP = true', 'HOST_INCLUDE_HELP = false')) },
    @{ Name = 'noclaims'; Demo = $true;  Changes = @(, @('HOST_SIGN_WITH_CLAIMS = true', 'HOST_SIGN_WITH_CLAIMS = false')) },
    @{ Name = 'headers';  Demo = $true;  Changes = @(, @('HOST_FORWARD_HEADERS = ""', 'HOST_FORWARD_HEADERS = "X-API-Key, X-Tenant"')) },
    @{ Name = 'fallback'; Demo = $true;  Changes = @(
            @('HOST_INTENT_RESOLVER = "off"', 'HOST_INTENT_RESOLVER = "fallback"'),
            @('HOST_LLM_ENDPOINT = ""', $modelEndpoint),
            @('HOST_LLM_API_KEY = "[INSERT_YOUR_LLM_API_KEY]"', 'HOST_LLM_API_KEY = "k123"')) },
    @{ Name = 'always';   Demo = $true;  Changes = @(
            @('HOST_INTENT_RESOLVER = "off"', 'HOST_INTENT_RESOLVER = "always"'),
            @('HOST_LLM_ENDPOINT = ""', $modelEndpoint),
            @('HOST_LLM_API_KEY = "[INSERT_YOUR_LLM_API_KEY]"', 'HOST_LLM_API_KEY = "k123"')) }
)

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('a2a-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null

try {
    # The project file is kept as a .template so that editors don't restore it inside the repository.
    Copy-Item (Join-Path $PSScriptRoot 'Harness.csproj.template') (Join-Path $work 'Harness.csproj')
    foreach ($file in 'Program.cs', 'ScriptBase.cs') {
        Copy-Item (Join-Path $PSScriptRoot $file) $work
    }

    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cache" value="$([System.Security.SecurityElement]::Escape($packages))" />
  </packageSources>
</configuration>
"@
    [System.IO.File]::WriteAllText((Join-Path $work 'NuGet.config'), $nugetConfig)

    $totalPass = 0
    $totalFail = 0
    $failedVariants = @()

    foreach ($variant in $variants) {
        $text = if ($variant.Demo) { $demoSource } else { $source }
        foreach ($change in $variant.Changes) {
            if (-not $text.Contains($change[0])) {
                throw "Variant '$($variant.Name)' expects to find '$($change[0])' in script.csx, and it isn't there. Update Run-Tests.ps1 to match."
            }
            $text = $text.Replace($change[0], $change[1])
        }
        [System.IO.File]::WriteAllText((Join-Path $work 'Script.cs'), $text)

        $build = & dotnet build $work --nologo -v q 2>&1
        if ($LASTEXITCODE -ne 0) {
            $build | ForEach-Object { Write-Host $_ }
            throw "The build failed for variant '$($variant.Name)'."
        }

        $output = & dotnet run --project $work --no-build -- $variant.Name 2>&1
        $exit = $LASTEXITCODE

        $pass = @($output | Where-Object { $_ -cmatch '^PASS ' })
        $fail = @($output | Where-Object { $_ -cmatch '^FAIL ' })
        $other = @($output | Where-Object { $_ -cnotmatch '^(PASS|FAIL) ' -and $_ -cne 'ALL PASS' -and $_ -notmatch '^\d+ FAILED$' -and "$_".Trim() })

        $totalPass += $pass.Count
        $totalFail += $fail.Count

        $status = if ($exit -eq 0 -and $fail.Count -eq 0) { 'ok' } else { 'FAILED' }
        Write-Host ("{0,-9} {1,3} passed, {2} failed  [{3}]" -f $variant.Name, $pass.Count, $fail.Count, $status)

        if ($ShowAll) { $pass | ForEach-Object { Write-Host "    $_" } }
        $fail | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        $other | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }

        if ($status -ne 'ok') { $failedVariants += $variant.Name }
    }

    Write-Host ''
    if ($failedVariants.Count -eq 0) {
        Write-Host "All $totalPass checks passed." -ForegroundColor Green
        exit 0
    }

    Write-Host "$totalFail checks failed in: $($failedVariants -join ', ')" -ForegroundColor Red
    exit 1
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
