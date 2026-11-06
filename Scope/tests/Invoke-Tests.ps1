<#
.SYNOPSIS
    Runs the tests in this folder.

.DESCRIPTION
    Uses Pester 3.4 (installed with Windows PowerShell) or Pester 5, whichever is available.
    The tests build throwaway git repositories in the temp folder and never touch a real Scope
    clone or this folder's overlay.

.EXAMPLE
    .\tests\Invoke-Tests.ps1
#>
[CmdletBinding()]
param()

$pester = Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1
if (-not $pester) { throw 'Pester is not installed.' }
Import-Module $pester.Path -Force
Write-Host "Using Pester $($pester.Version)"

$result = Invoke-Pester -Path (Join-Path $PSScriptRoot 'Scope.Tests.ps1') -PassThru
if ($result.FailedCount -gt 0) { exit 1 }
