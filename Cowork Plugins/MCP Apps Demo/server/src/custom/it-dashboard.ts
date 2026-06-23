/**
 * IT Dashboard — incident tracking, SLA compliance, severity breakdown.
 * Tools: show_it_dashboard (model+app), refresh_incidents (app-only)
 * Mock data: 47 open incidents, 3 SLA breaches, 5 departments at Zava Corp.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { tryElicitate } from "../shared/elicitation.js";
import { itDashboardWidgetHtml } from "./widgets/it-dashboard-widget.js";

const DEPARTMENTS = ["All", "Engineering", "Sales", "Marketing", "Finance", "HR"] as const;
const SEVERITIES = ["All", "Critical", "High", "Medium", "Low"] as const;

function generateItData(department: string, severity: string) {
  const incidents = [
    { id: "INC-2401", title: "Production database failover", severity: "Critical", dept: "Engineering", status: "In Progress", sla: "Breached", age: "4h" },
    { id: "INC-2402", title: "SSO login failures", severity: "Critical", dept: "Engineering", status: "Investigating", sla: "At Risk", age: "2h" },
    { id: "INC-2403", title: "Email delivery delays", severity: "High", dept: "Sales", status: "In Progress", sla: "OK", age: "6h" },
    { id: "INC-2404", title: "VPN tunnel drops", severity: "High", dept: "Engineering", status: "Assigned", sla: "OK", age: "3h" },
    { id: "INC-2405", title: "CRM sync errors", severity: "High", dept: "Sales", status: "In Progress", sla: "Breached", age: "12h" },
    { id: "INC-2406", title: "Print server offline", severity: "Medium", dept: "Finance", status: "Assigned", sla: "OK", age: "1h" },
    { id: "INC-2407", title: "Slow dashboard load times", severity: "Medium", dept: "Marketing", status: "Investigating", sla: "OK", age: "5h" },
    { id: "INC-2408", title: "Badge reader malfunction", severity: "Medium", dept: "HR", status: "Assigned", sla: "OK", age: "2h" },
    { id: "INC-2409", title: "Laptop provisioning backlog", severity: "Low", dept: "HR", status: "Open", sla: "OK", age: "24h" },
    { id: "INC-2410", title: "Conference room AV issues", severity: "Low", dept: "Marketing", status: "Open", sla: "OK", age: "8h" },
    { id: "INC-2411", title: "API rate limit exceeded", severity: "High", dept: "Engineering", status: "Investigating", sla: "Breached", age: "7h" },
    { id: "INC-2412", title: "Expense report form broken", severity: "Medium", dept: "Finance", status: "Open", sla: "OK", age: "3h" },
  ];

  const filtered = incidents.filter((i) =>
    (department === "All" || i.dept === department) &&
    (severity === "All" || i.severity === severity)
  );

  const bySeverity = { Critical: 0, High: 0, Medium: 0, Low: 0 } as Record<string, number>;
  filtered.forEach((i) => bySeverity[i.severity]++);

  const slaBreaches = filtered.filter((i) => i.sla === "Breached").length;
  const slaCompliance = filtered.length > 0
    ? ((filtered.length - slaBreaches) / filtered.length * 100).toFixed(0)
    : "100";

  return {
    department, severity,
    totalOpen: filtered.length,
    slaBreaches,
    slaCompliance: Number(slaCompliance),
    bySeverity,
    incidents: filtered,
  };
}

export function registerItDashboard(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/it-dashboard.html";

  server.resource("IT Dashboard UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: itDashboardWidgetHtml(),
    }],
  }));

  server.registerTool(
    "show_it_dashboard",
    {
      description: "Display an org-wide IT incident dashboard for Zava Corp showing open incidents, SLA compliance, and severity breakdown.",
      inputSchema: {
        department: z.enum(DEPARTMENTS).optional().describe("Department filter"),
        severity: z.enum(SEVERITIES).optional().describe("Severity filter"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const elicited = await tryElicitate<{ department: string; severity: string }>(
        server, "Configure the IT dashboard.", {
          type: "object",
          properties: {
            department: { type: "string", title: "Department", enum: [...DEPARTMENTS], default: "All" },
            severity: { type: "string", title: "Severity", enum: [...SEVERITIES], default: "All" },
          },
          required: [],
        }
      );

      const department = elicited?.department || args.department || "All";
      const severity = elicited?.severity || args.severity || "All";
      const data = generateItData(department, severity);

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `IT Dashboard (${department}, ${severity}): ${data.totalOpen} open incidents, ${data.slaBreaches} SLA breaches, ${data.slaCompliance}% SLA compliance.`
        )}],
        structuredContent: data,
      };
    }
  );

  server.registerTool(
    "refresh_incidents",
    {
      description: "Refresh the IT dashboard with new filters.",
      inputSchema: {
        department: z.enum(DEPARTMENTS).optional(),
        severity: z.enum(SEVERITIES).optional(),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI, visibility: ["app"] } },
    },
    async (args) => {
      const data = generateItData(args.department || "All", args.severity || "All");
      return {
        content: [{ type: "text" as const, text: disclaimerText(`Refreshed IT dashboard: ${data.totalOpen} incidents.`) }],
        structuredContent: data,
      };
    }
  );
}
