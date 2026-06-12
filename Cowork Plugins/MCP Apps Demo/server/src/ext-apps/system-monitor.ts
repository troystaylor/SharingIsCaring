/**
 * System Monitor — port of ext-apps system-monitor-server.
 * Shows real-time CPU and memory usage. Widget auto-refreshes via app-only tool.
 * Uses real local system data (no disclaimer).
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import * as os from "node:os";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

function getSystemMetrics() {
  const cpus = os.cpus();
  const cpuUsage = cpus.map((cpu, i) => {
    const total = Object.values(cpu.times).reduce((a, b) => a + b, 0);
    const idle = cpu.times.idle;
    return { core: i, usage: Math.round(((total - idle) / total) * 100) };
  });
  const avgCpu = Math.round(cpuUsage.reduce((s, c) => s + c.usage, 0) / cpuUsage.length);

  const totalMem = os.totalmem();
  const freeMem = os.freemem();
  const usedMem = totalMem - freeMem;
  const memPercent = Math.round((usedMem / totalMem) * 100);

  return {
    hostname: os.hostname(),
    platform: os.platform(),
    arch: os.arch(),
    uptime: Math.round(os.uptime()),
    cpuModel: cpus[0]?.model || "Unknown",
    cpuCores: cpus.length,
    cpuUsage,
    avgCpu,
    totalMem: Math.round(totalMem / 1024 / 1024 / 1024 * 10) / 10,
    usedMem: Math.round(usedMem / 1024 / 1024 / 1024 * 10) / 10,
    memPercent,
    timestamp: new Date().toISOString(),
  };
}

export function registerSystemMonitor(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/sysmon.html";

  server.resource("System Monitor UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(sysmonWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_system_monitor",
    {
      description: "Display real-time system metrics (CPU usage per core, memory utilization) for the server host.",
      inputSchema: {},
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async () => {
      const metrics = getSystemMetrics();
      return {
        content: [{ type: "text" as const, text:
          `System: ${metrics.hostname} (${metrics.platform}/${metrics.arch}), CPU: ${metrics.avgCpu}% avg across ${metrics.cpuCores} cores, Memory: ${metrics.usedMem}/${metrics.totalMem} GB (${metrics.memPercent}%)`
        }],
        structuredContent: metrics,
      };
    }
  );

  server.registerTool(
    "refresh_system_monitor",
    {
      description: "Refresh system monitor metrics.",
      inputSchema: {},
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI, visibility: ["app"] } },
    },
    async () => {
      const metrics = getSystemMetrics();
      return {
        content: [{ type: "text" as const, text: `CPU: ${metrics.avgCpu}%, Mem: ${metrics.memPercent}%` }],
        structuredContent: metrics,
      };
    }
  );
}

function sysmonWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>System Monitor</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#0d1117; color:#c9d1d9; padding:16px; }
  h1 { font-size:16px; font-weight:600; color:#58a6ff; margin-bottom:4px; }
  .meta { font-size:11px; color:#8b949e; margin-bottom:16px; }
  .section { margin-bottom:16px; }
  .section h2 { font-size:13px; color:#8b949e; margin-bottom:8px; text-transform:uppercase; letter-spacing:0.5px; }
  .cpu-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(60px,1fr)); gap:6px; }
  .core { background:#161b22; border-radius:6px; padding:8px; text-align:center; }
  .core-label { font-size:10px; color:#8b949e; }
  .core-value { font-size:18px; font-weight:700; margin-top:2px; }
  .core-bar { height:4px; background:#21262d; border-radius:2px; margin-top:4px; overflow:hidden; }
  .core-bar-fill { height:100%; border-radius:2px; transition:width 0.3s; }
  .mem-bar { background:#21262d; border-radius:8px; height:32px; overflow:hidden; position:relative; }
  .mem-bar-fill { height:100%; border-radius:8px; transition:width 0.3s; }
  .mem-bar-text { position:absolute; top:50%; left:50%; transform:translate(-50%,-50%);
    font-size:12px; font-weight:600; color:#fff; }
  .stats { display:flex; gap:16px; margin-top:8px; }
  .stat { font-size:11px; color:#8b949e; }
  .stat strong { color:#c9d1d9; }
</style>
</head>
<body>
<h1 id="hostname">System Monitor</h1>
<div class="meta" id="meta"></div>
<div class="section"><h2>CPU Usage</h2><div class="cpu-grid" id="cpuGrid"></div></div>
<div class="section"><h2>Memory</h2><div class="mem-bar" id="memBar"><div class="mem-bar-fill" id="memFill"></div><div class="mem-bar-text" id="memText"></div></div>
<div class="stats" id="memStats"></div></div>

<script>
(function() {
  function getColor(pct) {
    if (pct > 80) return "#f85149";
    if (pct > 60) return "#d29922";
    if (pct > 30) return "#3fb950";
    return "#238636";
  }

  function render(d) {
    document.getElementById("hostname").textContent = d.hostname || "System Monitor";
    document.getElementById("meta").textContent =
      (d.platform || "") + "/" + (d.arch || "") + " | " + (d.cpuModel || "") +
      " | Uptime: " + formatUptime(d.uptime);

    const grid = document.getElementById("cpuGrid");
    grid.innerHTML = d.cpuUsage.map(c =>
      '<div class="core"><div class="core-label">Core ' + c.core + '</div>' +
      '<div class="core-value" style="color:' + getColor(c.usage) + '">' + c.usage + '%</div>' +
      '<div class="core-bar"><div class="core-bar-fill" style="width:' + c.usage + '%;background:' + getColor(c.usage) + '"></div></div></div>'
    ).join("");

    document.getElementById("memFill").style.width = d.memPercent + "%";
    document.getElementById("memFill").style.background = getColor(d.memPercent);
    document.getElementById("memText").textContent = d.memPercent + "%";
    document.getElementById("memStats").innerHTML =
      '<div class="stat">Used: <strong>' + d.usedMem + ' GB</strong></div>' +
      '<div class="stat">Total: <strong>' + d.totalMem + ' GB</strong></div>';
  }

  function formatUptime(s) {
    if (!s) return "N/A";
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    return h + "h " + m + "m";
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
