/**
 * Budget Allocator — port of ext-apps budget-allocator-server.
 * Interactive budget allocation with donut chart and category sliders.
 * Synthetic demo data — includes disclaimer.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { injectDisclaimer } from "../shared/disclaimer.js";

const CATEGORIES = ["Engineering", "Marketing", "Sales", "Operations", "R&D"];
const DEFAULT_BUDGET = 1000000;

export function registerBudgetAllocator(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/budget.html";

  server.resource("Budget Allocator UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectDisclaimer(budgetWidgetHtml()),
    }],
  }));

  server.registerTool(
    "allocate_budget",
    {
      description: "Display an interactive budget allocator with donut chart visualization across 5 categories.",
      inputSchema: {
        totalBudget: z.number().optional().describe("Total budget in dollars (default 1,000,000)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const total = args.totalBudget || DEFAULT_BUDGET;
      const allocations = CATEGORIES.map((cat, i) => ({
        category: cat,
        amount: Math.round(total * [0.30, 0.20, 0.20, 0.15, 0.15][i]),
        percentage: [30, 20, 20, 15, 15][i],
      }));

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Budget Allocator: $${(total / 1e6).toFixed(1)}M across ${CATEGORIES.length} categories.`
        )}],
        structuredContent: { totalBudget: total, allocations, categories: CATEGORIES },
      };
    }
  );
}

function budgetWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Budget Allocator</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:16px; }
  .layout { display:grid; grid-template-columns:200px 1fr; gap:20px; align-items:start; }
  canvas { width:200px !important; height:200px !important; }
  .sliders { display:flex; flex-direction:column; gap:10px; }
  .slider-row { display:flex; align-items:center; gap:8px; }
  .slider-label { width:90px; font-size:12px; font-weight:600; color:#555; }
  .slider-value { width:70px; font-size:12px; color:#333; text-align:right; }
  input[type=range] { flex:1; }
  .total { font-size:14px; font-weight:600; color:#1a1a2e; margin-top:12px; padding-top:8px; border-top:1px solid #ddd; }
  @media(max-width:500px) { .layout { grid-template-columns:1fr; } }
</style>
</head>
<body>
<h1>Budget Allocator</h1>
<div class="layout">
  <canvas id="donut" width="200" height="200"></canvas>
  <div class="sliders" id="sliders"></div>
</div>
<div class="total" id="total"></div>

<script>
(function() {
  const COLORS = ["#4361ee","#7c3aed","#f72585","#ef6c00","#2e7d32"];
  let data = null;

  function drawDonut(allocations) {
    const canvas = document.getElementById("donut");
    const ctx = canvas.getContext("2d");
    const s = 200, cx = s/2, cy = s/2, r = 80, inner = 50;
    ctx.clearRect(0, 0, s, s);
    const total = allocations.reduce((a, b) => a + b.amount, 0) || 1;
    let angle = -Math.PI / 2;
    allocations.forEach((a, i) => {
      const slice = (a.amount / total) * Math.PI * 2;
      ctx.beginPath(); ctx.moveTo(cx, cy); ctx.arc(cx, cy, r, angle, angle + slice); ctx.closePath();
      ctx.fillStyle = COLORS[i % COLORS.length]; ctx.fill();
      angle += slice;
    });
    ctx.beginPath(); ctx.arc(cx, cy, inner, 0, Math.PI * 2);
    ctx.fillStyle = "#f8f9fa"; ctx.fill();
    ctx.fillStyle = "#333"; ctx.font = "bold 14px system-ui"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
    ctx.fillText(fmt(total), cx, cy);
  }

  function fmt(n) { return n >= 1e6 ? "$" + (n/1e6).toFixed(1) + "M" : "$" + (n/1e3).toFixed(0) + "K"; }

  function render(d) {
    data = d;
    drawDonut(d.allocations);
    const el = document.getElementById("sliders");
    el.innerHTML = d.allocations.map((a, i) =>
      '<div class="slider-row">' +
      '<span class="slider-label" style="color:' + COLORS[i] + '">' + a.category + '</span>' +
      '<input type="range" min="0" max="100" value="' + a.percentage + '" data-idx="' + i + '">' +
      '<span class="slider-value">' + fmt(a.amount) + '</span>' +
      '</div>'
    ).join("");
    document.getElementById("total").textContent = "Total: " + fmt(d.totalBudget);

    el.querySelectorAll("input[type=range]").forEach(input => {
      input.addEventListener("input", () => {
        const idx = parseInt(input.dataset.idx);
        data.allocations[idx].percentage = parseInt(input.value);
        data.allocations[idx].amount = Math.round(data.totalBudget * parseInt(input.value) / 100);
        drawDonut(data.allocations);
        const vals = el.querySelectorAll(".slider-value");
        vals[idx].textContent = fmt(data.allocations[idx].amount);
      });
    });
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;
}
