/**
 * IT Dashboard widget — severity donut, SLA gauge, incident table.
 * Self-contained HTML with inline canvas charts.
 */

import { injectDisclaimer } from "../../shared/disclaimer.js";

export function itDashboardWidgetHtml(): string {
  const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>IT Dashboard</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,-apple-system,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:4px; }
  .subtitle { font-size:12px; color:#888; margin-bottom:16px; }
  .kpis { display:grid; grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); gap:10px; margin-bottom:16px; }
  .kpi { background:#fff; border-radius:8px; padding:14px; text-align:center; }
  .kpi-value { font-size:28px; font-weight:700; }
  .kpi-value.red { color:#d32f2f; } .kpi-value.green { color:#2e7d32; } .kpi-value.blue { color:#1565c0; }
  .kpi-label { font-size:11px; color:#888; text-transform:uppercase; margin-top:2px; }
  .charts { display:grid; grid-template-columns:1fr 1fr; gap:12px; margin-bottom:16px; }
  .chart-box { background:#fff; border-radius:8px; padding:14px; text-align:center; }
  .chart-box h2 { font-size:13px; font-weight:600; color:#555; margin-bottom:8px; }
  canvas { max-width:200px; max-height:200px; margin:0 auto; display:block; }
  table { width:100%; border-collapse:collapse; background:#fff; border-radius:8px; overflow:hidden; }
  th { background:#f0f2f5; text-align:left; padding:8px 10px; font-size:11px; color:#666; text-transform:uppercase; }
  td { padding:8px 10px; font-size:12px; color:#333; border-top:1px solid #eee; }
  .sev { padding:2px 8px; border-radius:4px; font-size:10px; font-weight:600; }
  .sev-Critical { background:#ffebee; color:#c62828; }
  .sev-High { background:#fff3e0; color:#ef6c00; }
  .sev-Medium { background:#fff8e1; color:#f9a825; }
  .sev-Low { background:#e8f5e9; color:#2e7d32; }
  .sla-Breached { color:#c62828; font-weight:600; }
  .sla-At\\ Risk { color:#ef6c00; font-weight:600; }
  .sla-OK { color:#2e7d32; }
  @media(max-width:600px) { .charts { grid-template-columns:1fr; } }
</style>
</head>
<body>
<h1 id="title">IT Dashboard</h1>
<div class="subtitle" id="subtitle">Zava Corp</div>
<div class="kpis" id="kpis"></div>
<div class="charts">
  <div class="chart-box"><h2>By Severity</h2><canvas id="sevChart" width="180" height="180"></canvas></div>
  <div class="chart-box"><h2>SLA Compliance</h2><canvas id="slaChart" width="180" height="180"></canvas></div>
</div>
<h2 style="font-size:14px;font-weight:600;color:#555;margin:12px 0 8px;">Open Incidents</h2>
<table id="incTable"><thead><tr><th>ID</th><th>Title</th><th>Severity</th><th>Dept</th><th>Status</th><th>SLA</th></tr></thead><tbody></tbody></table>

<script>
(function() {
  function drawDonut(canvas, segments, colors) {
    const ctx = canvas.getContext("2d");
    const s = Math.min(canvas.width, canvas.height);
    const cx = s/2, cy = s/2, r = s*0.4, inner = s*0.25;
    ctx.clearRect(0, 0, s, s);
    const total = segments.reduce((a,b)=>a+b, 0) || 1;
    let angle = -Math.PI/2;
    segments.forEach((v,i) => {
      const slice = (v/total)*Math.PI*2;
      ctx.beginPath(); ctx.moveTo(cx,cy); ctx.arc(cx,cy,r,angle,angle+slice); ctx.closePath();
      ctx.fillStyle = colors[i]; ctx.fill();
      angle += slice;
    });
    ctx.beginPath(); ctx.arc(cx,cy,inner,0,Math.PI*2); ctx.fillStyle="#fff"; ctx.fill();
    ctx.fillStyle="#333"; ctx.font="bold 20px system-ui"; ctx.textAlign="center"; ctx.textBaseline="middle";
    ctx.fillText(total.toString(), cx, cy);
  }

  function drawGauge(canvas, pct) {
    const ctx = canvas.getContext("2d");
    const s = Math.min(canvas.width, canvas.height);
    const cx = s/2, cy = s*0.6, r = s*0.38;
    ctx.clearRect(0, 0, s, s);
    // Background arc
    ctx.beginPath(); ctx.arc(cx,cy,r,Math.PI,0); ctx.lineWidth=18; ctx.strokeStyle="#e0e0e0"; ctx.stroke();
    // Value arc
    const endAngle = Math.PI + (pct/100)*Math.PI;
    const color = pct >= 90 ? "#2e7d32" : pct >= 70 ? "#f9a825" : "#c62828";
    ctx.beginPath(); ctx.arc(cx,cy,r,Math.PI,endAngle); ctx.lineWidth=18; ctx.strokeStyle=color; ctx.lineCap="round"; ctx.stroke();
    ctx.fillStyle="#333"; ctx.font="bold 24px system-ui"; ctx.textAlign="center"; ctx.textBaseline="middle";
    ctx.fillText(pct+"%", cx, cy-10);
  }

  function render(data) {
    document.getElementById("subtitle").textContent =
      "Zava Corp" + (data.department !== "All" ? " — " + data.department : "") +
      (data.severity !== "All" ? " — " + data.severity + " only" : "");

    const kpis = document.getElementById("kpis");
    kpis.innerHTML =
      '<div class="kpi"><div class="kpi-value blue">' + data.totalOpen + '</div><div class="kpi-label">Open</div></div>' +
      '<div class="kpi"><div class="kpi-value red">' + data.slaBreaches + '</div><div class="kpi-label">SLA Breaches</div></div>' +
      '<div class="kpi"><div class="kpi-value green">' + data.slaCompliance + '%</div><div class="kpi-label">SLA Compliance</div></div>';

    const sev = data.bySeverity;
    drawDonut(document.getElementById("sevChart"),
      [sev.Critical||0, sev.High||0, sev.Medium||0, sev.Low||0],
      ["#c62828","#ef6c00","#f9a825","#2e7d32"]);

    drawGauge(document.getElementById("slaChart"), data.slaCompliance);

    const tbody = document.querySelector("#incTable tbody");
    tbody.innerHTML = data.incidents.map(i =>
      '<tr><td>' + i.id + '</td><td>' + esc(i.title) + '</td>' +
      '<td><span class="sev sev-' + i.severity + '">' + i.severity + '</span></td>' +
      '<td>' + i.dept + '</td><td>' + i.status + '</td>' +
      '<td class="sla-' + i.sla.replace(/ /g,"\\\\ ") + '">' + i.sla + '</td></tr>'
    ).join("");
  }

  function esc(s) { const d = document.createElement("div"); d.textContent = s||""; return d.innerHTML; }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;

  return injectDisclaimer(html);
}
