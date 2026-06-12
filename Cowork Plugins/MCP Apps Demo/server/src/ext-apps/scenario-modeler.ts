/**
 * Scenario Modeler — port of ext-apps scenario-modeler-server.
 * SaaS financial projector with sliders and 12-month revenue chart.
 * Synthetic demo data — includes disclaimer.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText, injectDisclaimer } from "../shared/disclaimer.js";

function projectRevenue(mrr: number, growthRate: number, churnRate: number, months: number) {
  const projections: Array<{ month: number; revenue: number; customers: number; }> = [];
  let customers = Math.round(mrr / 50); // assume $50 ARPU
  for (let m = 1; m <= months; m++) {
    customers = Math.round(customers * (1 + growthRate / 100 - churnRate / 100));
    projections.push({ month: m, revenue: customers * 50, customers });
  }
  return projections;
}

export function registerScenarioModeler(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/scenario.html";

  server.resource("Scenario Modeler UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectDisclaimer(scenarioWidgetHtml()),
    }],
  }));

  server.registerTool(
    "model_scenario",
    {
      description: "Display a SaaS business scenario modeler. Project 12-month revenue based on MRR, growth rate, and churn rate.",
      inputSchema: {
        mrr: z.number().optional().describe("Monthly recurring revenue in dollars (default 50000)"),
        growthRate: z.number().optional().describe("Monthly growth rate percentage (default 8)"),
        churnRate: z.number().optional().describe("Monthly churn rate percentage (default 3)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const mrr = args.mrr || 50000;
      const growthRate = args.growthRate || 8;
      const churnRate = args.churnRate || 3;
      const projections = projectRevenue(mrr, growthRate, churnRate, 12);
      const finalRevenue = projections[projections.length - 1].revenue;

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Scenario Model: Starting MRR $${(mrr / 1000).toFixed(0)}K, ${growthRate}% growth, ${churnRate}% churn. ` +
          `Projected Month 12 MRR: $${(finalRevenue / 1000).toFixed(0)}K.`
        )}],
        structuredContent: { mrr, growthRate, churnRate, projections },
      };
    }
  );
}

function scenarioWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Scenario Modeler</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:16px; }
  .controls { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:10px; margin-bottom:16px; }
  .control { background:#fff; border-radius:8px; padding:12px; }
  .control label { font-size:11px; color:#888; text-transform:uppercase; display:block; margin-bottom:4px; }
  .control .value { font-size:18px; font-weight:700; color:#4361ee; }
  input[type=range] { width:100%; margin-top:6px; }
  .chart-box { background:#fff; border-radius:8px; padding:14px; }
  canvas { width:100% !important; height:220px !important; }
  .summary { display:grid; grid-template-columns:1fr 1fr; gap:10px; margin-top:12px; }
  .summary-item { background:#fff; border-radius:8px; padding:12px; text-align:center; }
  .summary-value { font-size:20px; font-weight:700; color:#1a1a2e; }
  .summary-label { font-size:11px; color:#888; }
</style>
</head>
<body>
<h1>SaaS Scenario Modeler</h1>
<div class="controls" id="controls"></div>
<div class="chart-box"><canvas id="chart"></canvas></div>
<div class="summary" id="summary"></div>

<script>
(function() {
  let data = null;

  function fmt(n) { return n >= 1e6 ? "$" + (n/1e6).toFixed(1) + "M" : "$" + (n/1e3).toFixed(0) + "K"; }

  function drawChart(projections) {
    const canvas = document.getElementById("chart");
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth * 2;
    const h = canvas.height = canvas.offsetHeight * 2;
    ctx.scale(2, 2);
    const cw = canvas.offsetWidth, ch = canvas.offsetHeight;
    const values = projections.map(p => p.revenue);
    const max = Math.max(...values) * 1.1 || 1;
    const stepX = (cw - 50) / (values.length - 1 || 1);

    ctx.clearRect(0, 0, cw, ch);

    // Fill
    ctx.fillStyle = "rgba(67,97,238,0.1)";
    ctx.beginPath();
    ctx.moveTo(35, ch - 20);
    values.forEach((v, i) => ctx.lineTo(35 + i * stepX, 10 + (ch - 30) - (v / max) * (ch - 40)));
    ctx.lineTo(35 + (values.length - 1) * stepX, ch - 20);
    ctx.closePath(); ctx.fill();

    // Line
    ctx.strokeStyle = "#4361ee"; ctx.lineWidth = 2; ctx.beginPath();
    values.forEach((v, i) => {
      const x = 35 + i * stepX, y = 10 + (ch - 30) - (v / max) * (ch - 40);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Labels
    ctx.fillStyle = "#888"; ctx.font = "10px system-ui"; ctx.textAlign = "center";
    projections.forEach((p, i) => ctx.fillText("M" + p.month, 35 + i * stepX, ch - 4));
  }

  function render(d) {
    data = d;
    const controls = document.getElementById("controls");
    controls.innerHTML =
      '<div class="control"><label>Starting MRR</label><div class="value">' + fmt(d.mrr) + '</div></div>' +
      '<div class="control"><label>Growth Rate</label><div class="value">' + d.growthRate + '%</div></div>' +
      '<div class="control"><label>Churn Rate</label><div class="value">' + d.churnRate + '%</div></div>';

    drawChart(d.projections);

    const last = d.projections[d.projections.length - 1];
    const growth = ((last.revenue - d.mrr) / d.mrr * 100).toFixed(0);
    document.getElementById("summary").innerHTML =
      '<div class="summary-item"><div class="summary-value">' + fmt(last.revenue) + '</div><div class="summary-label">Month 12 MRR</div></div>' +
      '<div class="summary-item"><div class="summary-value">' + growth + '%</div><div class="summary-label">Total Growth</div></div>';
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;
}
