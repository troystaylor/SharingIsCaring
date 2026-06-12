/**
 * Sales Dashboard widget — KPI cards, revenue line chart, pipeline bars, deal table.
 * Uses inline Chart.js for charts. Self-contained HTML.
 */

import { injectDisclaimer } from "../../shared/disclaimer.js";

export function salesDashboardWidgetHtml(): string {
  const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Sales Dashboard</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,-apple-system,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:4px; }
  .subtitle { font-size:12px; color:#888; margin-bottom:16px; }
  .kpis { display:grid; grid-template-columns:repeat(auto-fit,minmax(140px,1fr)); gap:10px; margin-bottom:16px; }
  .kpi { background:#fff; border-radius:8px; padding:14px; border-left:4px solid #4361ee; }
  .kpi-value { font-size:22px; font-weight:700; color:#1a1a2e; }
  .kpi-label { font-size:11px; color:#888; text-transform:uppercase; letter-spacing:0.5px; margin-top:2px; }
  .charts { display:grid; grid-template-columns:1fr 1fr; gap:12px; margin-bottom:16px; }
  .chart-box { background:#fff; border-radius:8px; padding:14px; }
  .chart-box h2 { font-size:13px; font-weight:600; color:#555; margin-bottom:8px; }
  canvas { width:100% !important; height:200px !important; }
  table { width:100%; border-collapse:collapse; background:#fff; border-radius:8px; overflow:hidden; }
  th { background:#f0f2f5; text-align:left; padding:8px 10px; font-size:11px; color:#666;
    text-transform:uppercase; letter-spacing:0.5px; }
  td { padding:8px 10px; font-size:12px; color:#333; border-top:1px solid #eee; }
  .stage-badge { display:inline-block; padding:2px 8px; border-radius:4px; font-size:10px; font-weight:600; }
  .stage-Prospecting { background:#e3f2fd; color:#1565c0; }
  .stage-Qualification { background:#f3e5f5; color:#7b1fa2; }
  .stage-Proposal { background:#fff3e0; color:#ef6c00; }
  .stage-Negotiation { background:#fce4ec; color:#c62828; }
  .stage-Closed\\ Won { background:#e8f5e9; color:#2e7d32; }
  @media(max-width:600px) { .charts { grid-template-columns:1fr; } }
</style>
</head>
<body>
<h1 id="title">Sales Dashboard</h1>
<div class="subtitle" id="subtitle"></div>
<div class="kpis" id="kpis"></div>
<div class="charts">
  <div class="chart-box"><h2>Monthly Revenue</h2><canvas id="revenueChart"></canvas></div>
  <div class="chart-box"><h2>Pipeline by Stage</h2><canvas id="pipelineChart"></canvas></div>
</div>
<h2 style="font-size:14px;font-weight:600;color:#555;margin:12px 0 8px;">Top Deals</h2>
<table id="dealsTable"><thead><tr><th>Deal</th><th>Value</th><th>Stage</th><th>Rep</th></tr></thead><tbody></tbody></table>

<script>
(function() {
  // Minimal inline Chart.js substitute — draws simple bar/line charts on canvas
  function drawBarChart(canvas, labels, values, color) {
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth * 2;
    const h = canvas.height = canvas.offsetHeight * 2;
    ctx.scale(2, 2);
    const cw = canvas.offsetWidth, ch = canvas.offsetHeight;
    const max = Math.max(...values, 1);
    const barW = (cw - 40) / labels.length - 4;
    const chartH = ch - 30;
    ctx.clearRect(0, 0, cw, ch);
    ctx.fillStyle = "#f0f2f5";
    for (let i = 0; i < 4; i++) {
      const y = 10 + (chartH / 4) * i;
      ctx.fillRect(30, y, cw - 40, 1);
    }
    values.forEach((v, i) => {
      const x = 34 + i * (barW + 4);
      const barH = (v / max) * (chartH - 10);
      ctx.fillStyle = color || "#4361ee";
      ctx.fillRect(x, 10 + chartH - barH, barW, barH);
      ctx.fillStyle = "#888";
      ctx.font = "10px system-ui";
      ctx.textAlign = "center";
      ctx.fillText(labels[i], x + barW / 2, ch - 4);
    });
  }

  function drawLineChart(canvas, labels, values, color) {
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth * 2;
    const h = canvas.height = canvas.offsetHeight * 2;
    ctx.scale(2, 2);
    const cw = canvas.offsetWidth, ch = canvas.offsetHeight;
    const max = Math.max(...values, 1);
    const chartH = ch - 30;
    const stepX = (cw - 50) / (labels.length - 1);
    ctx.clearRect(0, 0, cw, ch);
    ctx.strokeStyle = color || "#4361ee";
    ctx.lineWidth = 2;
    ctx.beginPath();
    values.forEach((v, i) => {
      const x = 35 + i * stepX;
      const y = 10 + chartH - (v / max) * (chartH - 10);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.stroke();
    // Dots
    values.forEach((v, i) => {
      const x = 35 + i * stepX;
      const y = 10 + chartH - (v / max) * (chartH - 10);
      ctx.fillStyle = color || "#4361ee";
      ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = "#888"; ctx.font = "10px system-ui"; ctx.textAlign = "center";
      ctx.fillText(labels[i], x, ch - 4);
    });
  }

  function fmt(n) { return n >= 1e6 ? "$" + (n/1e6).toFixed(1) + "M" : "$" + (n/1e3).toFixed(0) + "K"; }

  function render(data) {
    document.getElementById("title").textContent = "Sales Dashboard — " + (data.region || "All Regions");
    document.getElementById("subtitle").textContent = data.dateRange || "";

    const kpis = document.getElementById("kpis");
    const k = data.kpis;
    kpis.innerHTML =
      '<div class="kpi"><div class="kpi-value">' + fmt(k.totalRevenue) + '</div><div class="kpi-label">Total Revenue</div></div>' +
      '<div class="kpi"><div class="kpi-value">' + (k.winRate * 100).toFixed(0) + '%</div><div class="kpi-label">Win Rate</div></div>' +
      '<div class="kpi"><div class="kpi-value">' + fmt(k.avgDealSize) + '</div><div class="kpi-label">Avg Deal Size</div></div>' +
      '<div class="kpi"><div class="kpi-value">' + k.openDeals + '</div><div class="kpi-label">Open Deals</div></div>';

    // Revenue line chart
    drawLineChart(
      document.getElementById("revenueChart"),
      data.revenue.map(r => r.month),
      data.revenue.map(r => r.value),
      "#4361ee"
    );

    // Pipeline bar chart
    drawBarChart(
      document.getElementById("pipelineChart"),
      data.pipeline.map(p => p.stage.split(" ")[0]),
      data.pipeline.map(p => p.value),
      "#7c3aed"
    );

    // Deals table
    const tbody = document.querySelector("#dealsTable tbody");
    tbody.innerHTML = data.topDeals.map(d =>
      '<tr><td>' + escHtml(d.name) + '</td><td>' + fmt(d.value) + '</td>' +
      '<td><span class="stage-badge stage-' + d.stage.replace(/ /g,"\\\\ ") + '">' + d.stage + '</span></td>' +
      '<td>' + escHtml(d.rep) + '</td></tr>'
    ).join("");
  }

  function escHtml(s) { const d = document.createElement("div"); d.textContent = s || ""; return d.innerHTML; }

  // Listen for tool result data
  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;

  return injectDisclaimer(html);
}
