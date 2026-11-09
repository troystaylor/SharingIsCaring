#requires -Version 7
<#
.SYNOPSIS
    Downloads the Databricks API reference pages that generate-swagger.py reads.

.EXAMPLE
    ./download-api-reference.ps1 -Output ./api-reference
    python generate-swagger.py ./api-reference ../apiDefinition.swagger.json ../apiDefinition.swagger.json
#>
param(
    [Parameter(Mandatory)][string]$Output
)

$services = 'statement-execution/v1', 'warehouses/v1', 'jobs/v2', 'genie/v1', 'uc-catalogs/v1', 'uc-schemas/v1',
            'uc-tables/v1', 'uc-volumes/v1', 'uc-functions/v1', 'queries/v1', 'query-history/v1', 'alerts/v2', 'lakeview/v1'

New-Item -ItemType Directory $Output -Force | Out-Null
$index = (Invoke-WebRequest -UseBasicParsing -Uri 'https://docs.databricks.com/api/llms.txt').Content -split "`n"

$pages = @()
$current = $null
foreach ($line in $index) {
    if ($line -match '^## (.+)$') { $current = $Matches[1].Trim(); continue }
    if ($current -in $services -and $line -match '^\- \[.+?\]\((.+?\.md)\)') { $pages += $Matches[1] }
}

$pages | ForEach-Object -Parallel {
    $target = Join-Path $using:Output ($_ -replace '[/\\]', '__')
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 60 -Uri "https://docs.databricks.com/api/$_" -OutFile $target
} -ThrottleLimit 12

"Downloaded $($pages.Count) pages to $Output"
