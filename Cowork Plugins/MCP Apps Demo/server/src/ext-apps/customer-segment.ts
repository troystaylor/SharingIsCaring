/**
 * Customer Segmentation — port of ext-apps customer-segmentation-server.
 * Scatter/bubble chart showing 50 customers across 4 segments.
 * Synthetic demo data — includes disclaimer.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText, injectDisclaimer } from "../shared/disclaimer.js";

const SEGMENTS = ["Enterprise", "Mid-Market", "SMB", "Startup"];
const COLORS: Record<string, string> = {
  Enterprise: "#4361ee", "Mid-Market": "#7c3aed", SMB: "#f72585", Startup: "#ef6c00",
};

function generateCustomers() {
  const customers: Array<{ name: string; segment: string; revenue: number; satisfaction: number; tenure: number; }> = [];
  const names = [
    "Northwind", "Contoso", "Fabrikam", "Adventure Works", "Woodgrove", "Litware", "Tailspin",
    "Wingtip", "Proseware", "Datum", "Lucerne", "Margie", "Trey", "Wide World", "Coho",
    "Alpine", "Bellows", "Humongous", "Nod", "Relecloud", "Adatum", "Munson", "VanArsdel",
    "Lamna", "Fourth Coffee", "Graphic Design", "Blue Yonder", "City Power", "Oceana", "A. Datum",
    "Consolidated", "Southridge", "Treyserv", "Parnell", "Liberty", "Northridge", "Metro",
    "Terrapin", "Summit", "Cross-Platform", "Bravo", "Firefly", "Cascade", "Pinnacle",
    "Horizon", "Atlas", "Quantum", "Pioneer", "Nexus", "Sentinel",
  ];

  names.forEach((name, i) => {
    const segment = SEGMENTS[i % 4];
    const base = segment === "Enterprise" ? 500000 : segment === "Mid-Market" ? 150000 : segment === "SMB" ? 40000 : 15000;
    customers.push({
      name,
      segment,
      revenue: Math.round(base + Math.random() * base),
      satisfaction: Math.round((3 + Math.random() * 2) * 10) / 10,
      tenure: Math.round(1 + Math.random() * 8),
    });
  });
  return customers;
}

export function registerCustomerSegment(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/segment.html";

  server.resource("Customer Segmentation UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectDisclaimer(segmentWidgetHtml()),
    }],
  }));

  server.registerTool(
    "segment_customers",
    {
      description: "Display an interactive customer segmentation scatter chart. Shows 50 customers across 4 segments with revenue and satisfaction metrics.",
      inputSchema: {
        xAxis: z.enum(["revenue", "satisfaction", "tenure"]).optional().describe("X-axis metric"),
        yAxis: z.enum(["revenue", "satisfaction", "tenure"]).optional().describe("Y-axis metric"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const customers = generateCustomers();
      const xAxis = args.xAxis || "revenue";
      const yAxis = args.yAxis || "satisfaction";

      const segmentCounts: Record<string, number> = {};
      customers.forEach((c) => { segmentCounts[c.segment] = (segmentCounts[c.segment] || 0) + 1; });

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Customer Segmentation: ${customers.length} customers across ${SEGMENTS.length} segments. ` +
          Object.entries(segmentCounts).map(([s, c]) => `${s}: ${c}`).join(", ")
        )}],
        structuredContent: { customers, segments: SEGMENTS, colors: COLORS, xAxis, yAxis },
      };
    }
  );
}

function segmentWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Customer Segmentation</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:12px; }
  .legend { display:flex; gap:16px; margin-bottom:12px; flex-wrap:wrap; }
  .legend-item { display:flex; align-items:center; gap:4px; font-size:12px; color:#555; }
  .legend-dot { width:10px; height:10px; border-radius:50%; }
  canvas { width:100% !important; height:350px !important; background:#fff; border-radius:8px; }
  .tooltip { position:absolute; background:#333; color:#fff; padding:6px 10px; border-radius:4px;
    font-size:11px; pointer-events:none; display:none; z-index:10; }
</style>
</head>
<body>
<h1>Customer Segmentation</h1>
<div class="legend" id="legend"></div>
<canvas id="chart"></canvas>
<div class="tooltip" id="tooltip"></div>

<script>
(function() {
  let data = null;

  function render(d) {
    data = d;
    const legend = document.getElementById("legend");
    legend.innerHTML = d.segments.map(s =>
      '<div class="legend-item"><div class="legend-dot" style="background:' + (d.colors[s]||"#999") + '"></div>' + s + '</div>'
    ).join("");

    const canvas = document.getElementById("chart");
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth * 2;
    const h = canvas.height = canvas.offsetHeight * 2;
    ctx.scale(2, 2);
    const cw = canvas.offsetWidth, ch = canvas.offsetHeight;
    const pad = { top: 10, right: 20, bottom: 30, left: 50 };

    const xVals = d.customers.map(c => c[d.xAxis]);
    const yVals = d.customers.map(c => c[d.yAxis]);
    const xMin = Math.min(...xVals) * 0.9, xMax = Math.max(...xVals) * 1.1;
    const yMin = Math.min(...yVals) * 0.9, yMax = Math.max(...yVals) * 1.1;

    ctx.clearRect(0, 0, cw, ch);

    // Grid
    ctx.strokeStyle = "#eee"; ctx.lineWidth = 0.5;
    for (let i = 0; i < 5; i++) {
      const y = pad.top + (ch - pad.top - pad.bottom) * i / 4;
      ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(cw - pad.right, y); ctx.stroke();
    }

    // Points
    d.customers.forEach(c => {
      const x = pad.left + ((c[d.xAxis] - xMin) / (xMax - xMin)) * (cw - pad.left - pad.right);
      const y = pad.top + (1 - (c[d.yAxis] - yMin) / (yMax - yMin)) * (ch - pad.top - pad.bottom);
      ctx.fillStyle = d.colors[c.segment] || "#999";
      ctx.globalAlpha = 0.7;
      ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2); ctx.fill();
      ctx.globalAlpha = 1;
    });

    // Axis labels
    ctx.fillStyle = "#888"; ctx.font = "10px system-ui"; ctx.textAlign = "center";
    ctx.fillText(d.xAxis, cw / 2, ch - 4);
    ctx.save(); ctx.translate(10, ch / 2); ctx.rotate(-Math.PI / 2);
    ctx.fillText(d.yAxis, 0, 0); ctx.restore();
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;
}
