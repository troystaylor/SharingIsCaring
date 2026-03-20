// ── Reports & Dashboards Client (#6) ──
// Fetches report metadata and dashboard info from Salesforce Analytics API.

import { sfFetch } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { ReportMetadata, DashboardMetadata } from "../models/salesforceTypes";

interface ReportListItem {
  id: string;
  name: string;
  describeUrl: string;
  instancesUrl: string;
}

interface ReportListResponse {
  [key: string]: ReportListItem;
}

interface ReportDescribeResponse {
  reportMetadata: {
    id: string;
    name: string;
    description?: string;
    reportFormat: string;
    reportType: {
      type: string;
      label: string;
    };
  };
  reportExtendedMetadata?: unknown;
}

interface DashboardListResponse {
  dashboards: Array<{
    id: string;
    name: string;
    description?: string;
    folderId?: string;
    folderName?: string;
    lastModifiedDate?: string;
    createdDate?: string;
  }>;
}

// ── Reports ──

export async function listReports(): Promise<ReportMetadata[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const raw = await sfFetch<ReportListResponse>(
    `/services/data/${apiVersion}/analytics/reports`
  );

  const reports: ReportMetadata[] = [];
  for (const [id, item] of Object.entries(raw)) {
    reports.push({
      id,
      name: item.name,
      reportFormat: "TABULAR", // Default; describe call will reveal actual format
      reportType: { type: "", label: "" },
    });
  }

  return reports;
}

export async function describeReport(reportId: string): Promise<ReportMetadata> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<ReportDescribeResponse>(
    `/services/data/${apiVersion}/analytics/reports/${reportId}/describe`
  );

  const meta = response.reportMetadata;
  return {
    id: meta.id,
    name: meta.name,
    description: meta.description,
    reportFormat: meta.reportFormat,
    reportType: meta.reportType,
  };
}

export async function listReportsWithDetails(): Promise<ReportMetadata[]> {
  const reports = await listReports();
  const detailed: ReportMetadata[] = [];

  for (const report of reports) {
    try {
      const detail = await describeReport(report.id);
      detailed.push(detail);
    } catch (err) {
      console.error(`[Reports] Failed to describe report ${report.id}: ${err}`);
      detailed.push(report);
    }
  }

  return detailed;
}

// ── Dashboards ──

export async function listDashboards(): Promise<DashboardMetadata[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<DashboardListResponse>(
    `/services/data/${apiVersion}/analytics/dashboards`
  );

  return response.dashboards.map((d) => ({
    id: d.id,
    name: d.name,
    description: d.description,
    folderId: d.folderId,
    folderName: d.folderName,
    lastModifiedDate: d.lastModifiedDate,
    createdDate: d.createdDate,
  }));
}
