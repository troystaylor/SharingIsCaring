/**
 * Sales Dashboard — KPI cards, revenue chart, pipeline breakdown, top deals.
 * Tools: show_sales_dashboard (model+app), refresh_sales (app-only)
 * Mock data: 6 months revenue, 5 pipeline stages, 10 top deals at Zava Corp.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { tryElicitate } from "../shared/elicitation.js";
import { salesDashboardWidgetHtml } from "./widgets/sales-dashboard-widget.js";

const REGIONS = ["All", "North", "South", "East", "West"] as const;

function generateSalesData(region: string, _dateRange: string) {
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];
  const multiplier = region === "All" ? 1 : 0.3 + Math.random() * 0.2;
  const revenue = months.map((m) => ({
    month: m,
    value: Math.round((800000 + Math.random() * 400000) * multiplier),
  }));
  const totalRevenue = revenue.reduce((s, r) => s + r.value, 0);

  const pipeline = [
    { stage: "Prospecting", count: 34, value: 2800000 },
    { stage: "Qualification", count: 22, value: 1900000 },
    { stage: "Proposal", count: 15, value: 3200000 },
    { stage: "Negotiation", count: 8, value: 2100000 },
    { stage: "Closed Won", count: 12, value: 4500000 },
  ].map((s) => ({ ...s, value: Math.round(s.value * multiplier) }));

  const topDeals = [
    { name: "Northwind Traders — ERP Migration", value: 850000, stage: "Negotiation", rep: "Alex Rivera" },
    { name: "Contoso Ltd — Cloud Platform", value: 720000, stage: "Proposal", rep: "Jordan Chen" },
    { name: "Fabrikam Inc — Security Suite", value: 650000, stage: "Negotiation", rep: "Sam Patel" },
    { name: "Adventure Works — Data Analytics", value: 580000, stage: "Qualification", rep: "Taylor Kim" },
    { name: "Woodgrove Bank — Compliance Tool", value: 520000, stage: "Prospecting", rep: "Alex Rivera" },
    { name: "Litware Inc — DevOps Platform", value: 490000, stage: "Proposal", rep: "Jordan Chen" },
    { name: "Tailspin Toys — E-Commerce", value: 430000, stage: "Closed Won", rep: "Sam Patel" },
    { name: "Wingtip Toys — CRM Integration", value: 380000, stage: "Qualification", rep: "Taylor Kim" },
    { name: "Proseware — AI Assistant", value: 340000, stage: "Prospecting", rep: "Alex Rivera" },
    { name: "Datum Corp — Infrastructure", value: 290000, stage: "Closed Won", rep: "Jordan Chen" },
  ];

  return {
    region,
    kpis: {
      totalRevenue,
      winRate: 0.34,
      avgDealSize: 125000,
      openDeals: 79,
    },
    revenue,
    pipeline,
    topDeals,
  };
}

export function registerSalesDashboard(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/sales-dashboard.html";

  server.resource("Sales Dashboard UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: salesDashboardWidgetHtml(),
    }],
  }));

  server.registerTool(
    "show_sales_dashboard",
    {
      description: "Display an interactive sales dashboard for Zava Corp with revenue trends, pipeline breakdown, and top deals.",
      inputSchema: {
        region: z.enum(REGIONS).optional().describe("Region filter"),
        dateRange: z.string().optional().describe("Date range (e.g., 'Q2 2026')"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const elicited = await tryElicitate<{ region: string; dateRange: string }>(
        server, "Configure the sales dashboard.", {
          type: "object",
          properties: {
            dateRange: { type: "string", title: "Date Range", description: "Time period to display", default: "Q2 2026" },
            region: { type: "string", title: "Region", enum: [...REGIONS], default: "All" },
          },
          required: [],
        }
      );

      const region = elicited?.region || args.region || "All";
      const dateRange = elicited?.dateRange || args.dateRange || "Q2 2026";
      const data = generateSalesData(region, dateRange);

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Sales Dashboard (${region}, ${dateRange}): $${(data.kpis.totalRevenue / 1e6).toFixed(1)}M total revenue, ${data.kpis.openDeals} open deals, ${(data.kpis.winRate * 100).toFixed(0)}% win rate.`
        )}],
        structuredContent: { dateRange, ...data },
      };
    }
  );

  server.registerTool(
    "refresh_sales",
    {
      description: "Refresh the sales dashboard with new filters.",
      inputSchema: {
        region: z.enum(REGIONS).optional(),
        dateRange: z.string().optional(),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI, visibility: ["app"] } },
    },
    async (args) => {
      const data = generateSalesData(args.region || "All", args.dateRange || "Q2 2026");
      return {
        content: [{ type: "text" as const, text: disclaimerText(`Refreshed: ${args.region || "All"} region.`) }],
        structuredContent: { dateRange: args.dateRange || "Q2 2026", ...data },
      };
    }
  );
}
