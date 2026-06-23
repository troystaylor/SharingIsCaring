/**
 * Cohort Heatmap — port of ext-apps cohort-heatmap-server.
 * Interactive retention heatmap showing customer cohort data.
 * Synthetic demo data — includes disclaimer.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText, injectDisclaimer } from "../shared/disclaimer.js";

function generateCohortData() {
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];
  const cohorts = months.map((month, idx) => {
    const initial = 800 + Math.floor(Math.random() * 400);
    const periods: number[] = [100];
    for (let p = 1; p <= 6 - idx; p++) {
      const prev = periods[p - 1];
      periods.push(Math.round(prev * (0.6 + Math.random() * 0.25)));
    }
    return { month, initialUsers: initial, retention: periods };
  });
  return cohorts;
}

export function registerCohortHeatmap(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/cohort.html";

  server.resource("Cohort Heatmap UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectDisclaimer(cohortWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_cohort_heatmap",
    {
      description: "Display a customer retention cohort heatmap. Shows monthly cohorts with retention percentages over time.",
      inputSchema: {
        metric: z.enum(["retention", "revenue"]).optional().describe("Metric to display (default: retention)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const cohorts = generateCohortData();
      const metric = args.metric || "retention";

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Cohort Heatmap (${metric}): ${cohorts.length} monthly cohorts with retention data.`
        )}],
        structuredContent: { cohorts, metric },
      };
    }
  );
}

function cohortWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Cohort Heatmap</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:12px; }
  table { border-collapse:collapse; width:100%; }
  th { background:#f0f2f5; padding:8px; font-size:11px; color:#666; text-transform:uppercase; text-align:center; }
  td { padding:8px; text-align:center; font-size:12px; font-weight:600; border:1px solid #f0f2f5; }
  .cohort-label { text-align:left; font-weight:600; color:#333; }
  .users { font-size:11px; color:#888; font-weight:400; }
</style>
</head>
<body>
<h1>Cohort Retention Heatmap</h1>
<table id="heatmap"></table>

<script>
(function() {
  function getColor(pct) {
    if (pct >= 80) return "#1b5e20";
    if (pct >= 60) return "#2e7d32";
    if (pct >= 40) return "#66bb6a";
    if (pct >= 20) return "#a5d6a7";
    return "#c8e6c9";
  }

  function getBg(pct) {
    if (pct >= 80) return "#e8f5e9";
    if (pct >= 60) return "#c8e6c9";
    if (pct >= 40) return "#fff3e0";
    if (pct >= 20) return "#fff8e1";
    return "#ffebee";
  }

  function render(data) {
    const maxPeriods = Math.max(...data.cohorts.map(c => c.retention.length));
    const headers = ["Cohort", "Users", ...Array.from({length: maxPeriods}, (_, i) => "M" + i)];

    let html = "<thead><tr>" + headers.map(h => "<th>" + h + "</th>").join("") + "</tr></thead><tbody>";

    data.cohorts.forEach(c => {
      html += "<tr><td class='cohort-label'>" + c.month + "</td>";
      html += "<td class='users'>" + c.initialUsers + "</td>";
      c.retention.forEach(pct => {
        html += "<td style='background:" + getBg(pct) + ";color:" + getColor(pct) + "'>" + pct + "%</td>";
      });
      // Fill empty cells
      for (let i = c.retention.length; i < maxPeriods; i++) {
        html += "<td></td>";
      }
      html += "</tr>";
    });
    html += "</tbody>";
    document.getElementById("heatmap").innerHTML = html;
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
</script>
</body>
</html>`;
}
