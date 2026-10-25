<#
.SYNOPSIS
    Helpers that swap a demo's skills and settings into a copy of script.csx.

.DESCRIPTION
    Dot-source this file, then call Get-DemoScriptText. It reads script.csx and returns its text with:

      * everything between the "// BEGIN SKILLS" and "// END SKILLS" lines replaced by the demo's skills, and
      * the settings listed under "// DEMO SETTINGS" in the demo file applied to the matching constants.

    It never changes script.csx. A demo file is plain text, and the format is described at the top of
    DummyJson.skills.txt.
#>

function Get-DemoScriptText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptText,
        [Parameter(Mandatory)][string]$DemoPath
    )

    if (-not (Test-Path $DemoPath)) { throw "Demo file not found: $DemoPath" }
    $demoLines = [System.IO.File]::ReadAllLines((Resolve-Path $DemoPath))

    $settingsAt = [array]::IndexOf($demoLines, '// DEMO SETTINGS')
    $skillsAt = [array]::IndexOf($demoLines, '// DEMO SKILLS')
    if ($skillsAt -lt 0) { throw "$DemoPath has no '// DEMO SKILLS' line." }

    $settings = [ordered]@{}
    if ($settingsAt -ge 0) {
        for ($i = $settingsAt + 1; $i -lt $skillsAt; $i++) {
            if ($demoLines[$i] -match '^//\s*(HOST_[A-Z_]+)\s*=\s*(.*)$') { $settings[$Matches[1]] = $Matches[2].Trim() }
        }
    }

    $skills = ($demoLines[($skillsAt + 1)..($demoLines.Length - 1)]) -join "`r`n"

    $begin = [regex]::Match($ScriptText, '(?m)^[ \t]*// BEGIN SKILLS[^\r\n]*\r?\n')
    $end = [regex]::Match($ScriptText, '(?m)^[ \t]*// END SKILLS[^\r\n]*')
    if (-not $begin.Success -or -not $end.Success -or $end.Index -lt $begin.Index) {
        throw "script.csx has no '// BEGIN SKILLS' and '// END SKILLS' lines, so the demo can't be swapped in."
    }

    $start = $begin.Index + $begin.Length
    $text = $ScriptText.Substring(0, $start) + "`r`n" + $skills.TrimEnd() + "`r`n`r`n    " + $ScriptText.Substring($end.Index).TrimStart()

    foreach ($name in $settings.Keys) {
        $pattern = 'private const string ' + $name + ' = "(?:[^"\\]|\\.)*";'
        $found = [regex]::Match($text, $pattern)
        if (-not $found.Success) { throw "script.csx has no string constant named $name, so the demo setting can't be applied." }

        $literal = $settings[$name].Replace('\', '\\').Replace('"', '\"')
        $text = $text.Replace($found.Value, 'private const string ' + $name + ' = "' + $literal + '";')
    }

    return $text
}
