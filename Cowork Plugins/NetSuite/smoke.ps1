param(
    [Parameter(Mandatory = $true)]
    [string]$Fqdn
)

try { $r = Invoke-WebRequest -Uri "https://$Fqdn/health/live" -UseBasicParsing -TimeoutSec 30; "live: $($r.StatusCode) $($r.Content)" } catch { "live ERROR: $($_.Exception.Message)" }
try { $r = Invoke-WebRequest -Uri "https://$Fqdn/health/ready" -UseBasicParsing -TimeoutSec 30; "ready: $($r.StatusCode) $($r.Content)" } catch { "ready ERROR: $($_.Exception.Message)" }
try { $r = Invoke-WebRequest -Uri "https://$Fqdn/status" -UseBasicParsing -TimeoutSec 30; "status: $($r.StatusCode) $($r.Content)" } catch { "status ERROR: $($_.Exception.Message)" }

$body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}'
try {
    $r = Invoke-WebRequest -Uri "https://$Fqdn/mcp/full" -Method POST -Body $body -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream" } -UseBasicParsing -TimeoutSec 30
    "mcp/full initialize: $($r.StatusCode) $($r.Content.Substring(0, [Math]::Min(400, $r.Content.Length)))"
} catch {
    $resp = $_.Exception.Response
    if ($resp) {
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        "mcp/full initialize ERROR: status=$([int]$resp.StatusCode) body=$($sr.ReadToEnd())"
    } else {
        "mcp/full initialize ERROR: $($_.Exception.Message)"
    }
}

try {
    $body2 = '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
    $r = Invoke-WebRequest -Uri "https://$Fqdn/mcp/full" -Method POST -Body $body2 -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream" } -UseBasicParsing -TimeoutSec 30
    "mcp/full tools/list: $($r.StatusCode) $($r.Content.Substring(0, [Math]::Min(400, $r.Content.Length)))"
} catch {
    $resp = $_.Exception.Response
    if ($resp) { "mcp/full tools/list status: $([int]$resp.StatusCode)" } else { "mcp/full tools/list ERROR: $($_.Exception.Message)" }
}
