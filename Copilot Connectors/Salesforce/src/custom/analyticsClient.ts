// ── CRM Analytics Client (#9) ──
// Fetches datasets, dashboards, and lenses from the Salesforce Wave API.

import { sfFetch } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { AnalyticsDataset, AnalyticsDashboard } from "../models/salesforceTypes";

interface WaveDatasetListResponse {
  datasets: Array<{
    id: string;
    name: string;
    label: string;
    description?: string;
    createdDate?: string;
    lastModifiedDate?: string;
    datasetType?: string;
    folder?: {
      id: string;
      label: string;
    };
  }>;
  nextPageUrl?: string;
}

interface WaveDashboardListResponse {
  dashboards: Array<{
    id: string;
    name: string;
    label: string;
    description?: string;
    createdDate?: string;
    lastModifiedDate?: string;
    folder?: {
      id: string;
      label: string;
    };
  }>;
  nextPageUrl?: string;
}

interface WaveLensListResponse {
  lenses: Array<{
    id: string;
    name: string;
    label: string;
    description?: string;
    createdDate?: string;
    lastModifiedDate?: string;
    datasetId?: string;
  }>;
  nextPageUrl?: string;
}

// ── Datasets ──

export async function listDatasets(): Promise<AnalyticsDataset[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<WaveDatasetListResponse>(
    `/services/data/${apiVersion}/wave/datasets`
  );

  return response.datasets.map((d) => ({
    id: d.id,
    name: d.name,
    label: d.label,
    description: d.description,
    createdDate: d.createdDate,
    lastModifiedDate: d.lastModifiedDate,
    datasetType: d.datasetType,
    folderId: d.folder?.id,
    folderLabel: d.folder?.label,
  }));
}

// ── Dashboards (Analytics/Wave) ──

export async function listAnalyticsDashboards(): Promise<AnalyticsDashboard[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<WaveDashboardListResponse>(
    `/services/data/${apiVersion}/wave/dashboards`
  );

  return response.dashboards.map((d) => ({
    id: d.id,
    name: d.name,
    label: d.label,
    description: d.description,
    createdDate: d.createdDate,
    lastModifiedDate: d.lastModifiedDate,
    folderId: d.folder?.id,
    folderLabel: d.folder?.label,
  }));
}

// ── Lenses (saved explorations) ──

export interface AnalyticsLens {
  id: string;
  name: string;
  label: string;
  description?: string;
  createdDate?: string;
  lastModifiedDate?: string;
  datasetId?: string;
}

export async function listLenses(): Promise<AnalyticsLens[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<WaveLensListResponse>(
    `/services/data/${apiVersion}/wave/lenses`
  );

  return response.lenses.map((l) => ({
    id: l.id,
    name: l.name,
    label: l.label,
    description: l.description,
    createdDate: l.createdDate,
    lastModifiedDate: l.lastModifiedDate,
    datasetId: l.datasetId,
  }));
}

// ── SAQL Query ──

export interface SaqlQueryResult {
  results: {
    records: Array<Record<string, unknown>>;
  };
  query: string;
}

export async function runSaqlQuery(saql: string): Promise<SaqlQueryResult> {
  const apiVersion = getConfig().salesforce.apiVersion;
  return sfFetch<SaqlQueryResult>(
    `/services/data/${apiVersion}/wave/query`,
    {
      method: "POST",
      body: { query: saql },
    }
  );
}
