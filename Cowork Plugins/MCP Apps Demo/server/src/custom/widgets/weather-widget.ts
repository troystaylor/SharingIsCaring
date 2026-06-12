/**
 * Weather Forecast widget — current conditions card + daily forecast line chart.
 * No disclaimer (uses real Open-Meteo data).
 */

export function weatherWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Weather Forecast</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,-apple-system,sans-serif; background:#e8edf3; padding:16px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:4px; }
  .subtitle { font-size:12px; color:#888; margin-bottom:16px; }
  .current { background:linear-gradient(135deg,#4361ee,#7c3aed); color:#fff; border-radius:12px; padding:20px; margin-bottom:16px; display:flex; justify-content:space-between; align-items:center; }
  .current-temp { font-size:48px; font-weight:700; }
  .current-details { text-align:right; }
  .current-condition { font-size:16px; font-weight:500; margin-bottom:4px; }
  .current-meta { font-size:12px; opacity:0.85; }
  .chart-box { background:#fff; border-radius:8px; padding:14px; margin-bottom:12px; }
  .chart-box h2 { font-size:13px; font-weight:600; color:#555; margin-bottom:8px; }
  canvas { width:100% !important; height:180px !important; }
  .forecast-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(90px,1fr)); gap:8px; }
  .day-card { background:#fff; border-radius:8px; padding:10px; text-align:center; }
  .day-date { font-size:11px; color:#888; }
  .day-temps { font-size:14px; font-weight:600; color:#333; margin:4px 0; }
  .day-condition { font-size:10px; color:#666; }
  .day-precip { font-size:10px; color:#1565c0; margin-top:2px; }
</style>
</head>
<body>
<h1 id="title">Weather Forecast</h1>
<div class="subtitle" id="subtitle"></div>
<div class="current" id="current"></div>
<div class="chart-box"><h2>Temperature Range</h2><canvas id="tempChart"></canvas></div>
<h2 style="font-size:14px;font-weight:600;color:#555;margin:8px 0;">Daily Forecast</h2>
<div class="forecast-grid" id="forecast"></div>

<script>
(function() {
  function drawTempChart(canvas, daily) {
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth * 2;
    const h = canvas.height = canvas.offsetHeight * 2;
    ctx.scale(2, 2);
    const cw = canvas.offsetWidth, ch = canvas.offsetHeight;
    const highs = daily.map(d => d.tempMax);
    const lows = daily.map(d => d.tempMin);
    const allTemps = [...highs, ...lows];
    const min = Math.min(...allTemps) - 2;
    const max = Math.max(...allTemps) + 2;
    const range = max - min || 1;
    const chartH = ch - 30;
    const n = daily.length;
    const stepX = (cw - 50) / Math.max(n - 1, 1);

    ctx.clearRect(0, 0, cw, ch);

    // Fill between high and low
    ctx.fillStyle = "rgba(67,97,238,0.1)";
    ctx.beginPath();
    highs.forEach((t, i) => {
      const x = 35 + i * stepX;
      const y = 10 + chartH - ((t - min) / range) * (chartH - 10);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    for (let i = lows.length - 1; i >= 0; i--) {
      const x = 35 + i * stepX;
      const y = 10 + chartH - ((lows[i] - min) / range) * (chartH - 10);
      ctx.lineTo(x, y);
    }
    ctx.closePath(); ctx.fill();

    // High line
    ctx.strokeStyle = "#ef6c00"; ctx.lineWidth = 2; ctx.beginPath();
    highs.forEach((t, i) => {
      const x = 35 + i * stepX;
      const y = 10 + chartH - ((t - min) / range) * (chartH - 10);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Low line
    ctx.strokeStyle = "#1565c0"; ctx.beginPath();
    lows.forEach((t, i) => {
      const x = 35 + i * stepX;
      const y = 10 + chartH - ((t - min) / range) * (chartH - 10);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Labels
    ctx.fillStyle = "#888"; ctx.font = "10px system-ui"; ctx.textAlign = "center";
    daily.forEach((d, i) => {
      const x = 35 + i * stepX;
      const label = d.date.slice(5); // MM-DD
      ctx.fillText(label, x, ch - 4);
    });
  }

  function render(data) {
    document.getElementById("title").textContent = (data.city || "Weather") + (data.country ? ", " + data.country : "");
    document.getElementById("subtitle").textContent = data.loading ? "Loading forecast..." : (data.daily.length + "-day forecast");

    const c = data.current;
    document.getElementById("current").innerHTML =
      '<div><div class="current-temp">' + c.temperature + (c.temperature !== "--" ? '°C' : '') + '</div></div>' +
      '<div class="current-details"><div class="current-condition">' + esc(c.condition) + '</div>' +
      '<div class="current-meta">' + (c.humidity !== "--" ? 'Humidity: ' + c.humidity + '% | Wind: ' + c.windSpeed + ' km/h' : 'Fetching live data...') + '</div></div>';

    if (data.daily && data.daily.length > 0) {
      drawTempChart(document.getElementById("tempChart"), data.daily);

      const grid = document.getElementById("forecast");
      grid.innerHTML = data.daily.map(d =>
        '<div class="day-card"><div class="day-date">' + d.date.slice(5) + '</div>' +
        '<div class="day-temps">' + d.tempMax + '° / ' + d.tempMin + '°</div>' +
        '<div class="day-condition">' + esc(d.condition) + '</div>' +
        (d.precipitation > 0 ? '<div class="day-precip">' + d.precipitation + ' mm</div>' : '') +
        '</div>'
      ).join("");
    }
  }

  function esc(s) { const d = document.createElement("div"); d.textContent = s||""; return d.innerHTML; }

  window.addEventListener("message", async (e) => {
    if (e.data && e.data.structuredContent) {
      var data = e.data.structuredContent;
      render(data);
      // If placeholder (loading=true), auto-fetch real data via refresh_weather
      if (data.loading && data.city && window.__mcpCallTool) {
        try {
          var result = await window.__mcpCallTool("refresh_weather", {
            city: data.city,
            days: data.days || 7
          });
          // tools/call result can be structured different ways
          var realData = null;
          if (result) {
            if (result.structuredContent) realData = result.structuredContent;
            else if (result.result && result.result.structuredContent) realData = result.result.structuredContent;
            else if (result.city) realData = result; // direct data
          }
          if (realData && realData.daily && realData.daily.length > 0) {
            render(realData);
          }
        } catch (err) {
          // Widget stays on loading state — text response still has the data
        }
      }
    }
  });
})();
</script>
</body>
</html>`;
}
