// ── Full Crawl: Fetch all records and ingest into Graph ──

import { SObjectType, buildFullCrawlQuery } from "../custom/soqlBuilder";
import { queryAll, query } from "../custom/restClient";
import { bulkQuery } from "../custom/bulkClient";
import {
  transformRecord,
  transformReport,
  transformDashboard,
  transformChatterFeedItem,
  transformAnalyticsDataset,
  transformAnalyticsDashboard,
} from "../custom/transformer";
import { upsertItemsBatch, ensureConnection } from "../references/connectionManager";
import { SalesforceRecord, QueryResult } from "../models/salesforceTypes";
import { ExternalItem } from "../models/graphTypes";
import { loadUserMap, clearUserMap } from "../custom/userMapper";
import { listReportsWithDetails, listDashboards } from "../custom/reportsClient";
import { getAllCompanyFeedItems } from "../custom/chatterClient";
import { listDatasets, listAnalyticsDashboards } from "../custom/analyticsClient";
import { getConfig } from "../config/connectorConfig";

const PHASE1_OBJECTS: SObjectType[] = [
  "Account",
  "Contact",
  "Opportunity",
  "Case",
  "Lead",
];

const PHASE2_OBJECTS: SObjectType[] = [
  "Task",
  "Event",
  "KnowledgeArticleVersion",
  "Product2",
  "PricebookEntry",
];

const PHASE3_OBJECTS: SObjectType[] = [
  "Quote",
  "QuoteLineItem",
  "Campaign",
  "CampaignMember",
];

const ALL_OBJECTS: SObjectType[] = [
  ...PHASE1_OBJECTS,
  ...PHASE2_OBJECTS,
  ...PHASE3_OBJECTS,
];

const BULK_THRESHOLD = 10000;

export interface CrawlResult {
  objectType: string;
  totalFetched: number;
  succeeded: number;
  failed: number;
  durationMs: number;
}

// Check record count to decide REST vs Bulk
async function getRecordCount(objectType: SObjectType): Promise<number> {
  try {
    const apiVersion = getConfig().salesforce.apiVersion;
    const result = await query<SalesforceRecord>(
      `SELECT COUNT() FROM ${objectType}`
    );
    return result.totalSize;
  } catch {
    return 0; // If COUNT fails, fall through to REST
  }
}

async function crawlObject(objectType: SObjectType): Promise<CrawlResult> {
  const start = Date.now();
  console.log(`[Crawl] Starting full crawl for ${objectType}`);

  const soql = buildFullCrawlQuery(objectType);

  // Proactively choose Bulk API for large datasets
  let records: SalesforceRecord[];
  const count = await getRecordCount(objectType);

  if (count >= BULK_THRESHOLD) {
    console.log(`[Crawl] ${objectType}: ${count} records — using Bulk API 2.0`);
    records = await bulkQuery<SalesforceRecord>(soql);
  } else {
    try {
      records = await queryAll<SalesforceRecord>(soql);
    } catch {
      console.log(`[Crawl] ${objectType}: REST query failed, falling back to Bulk API`);
      records = await bulkQuery<SalesforceRecord>(soql);
    }
  }

  console.log(`[Crawl] ${objectType}: fetched ${records.length} records`);

  // Transform
  const items = records.map((r) => transformRecord(objectType, r));

  // Ingest
  const { succeeded, failed } = await upsertItemsBatch(items);

  const durationMs = Date.now() - start;
  console.log(`[Crawl] ${objectType}: done in ${durationMs}ms (${succeeded} ok, ${failed} failed)`);

  return {
    objectType,
    totalFetched: records.length,
    succeeded,
    failed,
    durationMs,
  };
}

export async function runFullCrawl(): Promise<CrawlResult[]> {
  console.log("[Crawl] ═══ Starting full crawl ═══");
  await ensureConnection();
  await loadUserMap();

  const results: CrawlResult[] = [];

  // ── CRM Objects (SOQL-based) ──
  for (const objectType of ALL_OBJECTS) {
    try {
      const result = await crawlObject(objectType);
      results.push(result);
    } catch (error) {
      console.error(`[Crawl] ${objectType}: SKIPPED — ${error instanceof Error ? error.message : error}`);
      results.push({
        objectType,
        totalFetched: 0,
        succeeded: 0,
        failed: 0,
        durationMs: 0,
      });
    }
  }

  // ── Reports & Dashboards ──
  results.push(await crawlExtended("Report", async () => {
    const reports = await listReportsWithDetails();
    return reports.map(transformReport);
  }));

  results.push(await crawlExtended("Dashboard", async () => {
    const dashboards = await listDashboards();
    return dashboards.map(transformDashboard);
  }));

  // ── Chatter ──
  results.push(await crawlExtended("ChatterPost", async () => {
    const feedItems = await getAllCompanyFeedItems(1000);
    return feedItems.map(transformChatterFeedItem);
  }));

  // ── CRM Analytics ──
  results.push(await crawlExtended("AnalyticsDataset", async () => {
    const datasets = await listDatasets();
    return datasets.map(transformAnalyticsDataset);
  }));

  results.push(await crawlExtended("AnalyticsDashboard", async () => {
    const dashboards = await listAnalyticsDashboards();
    return dashboards.map(transformAnalyticsDashboard);
  }));

  const totalItems = results.reduce((sum, r) => sum + r.totalFetched, 0);
  const totalSuccess = results.reduce((sum, r) => sum + r.succeeded, 0);
  const totalFailed = results.reduce((sum, r) => sum + r.failed, 0);
  const totalDuration = results.reduce((sum, r) => sum + r.durationMs, 0);

  console.log(`[Crawl] ═══ Full crawl complete ═══`);
  console.log(`[Crawl] Total: ${totalItems} items, ${totalSuccess} succeeded, ${totalFailed} failed, ${totalDuration}ms`);

  clearUserMap();
  return results;
}

// ── Helper for non-SOQL data sources ──

async function crawlExtended(
  label: string,
  fetchAndTransform: () => Promise<ExternalItem[]>
): Promise<CrawlResult> {
  const start = Date.now();
  console.log(`[Crawl] Starting extended crawl for ${label}`);

  try {
    const items = await fetchAndTransform();
    console.log(`[Crawl] ${label}: fetched ${items.length} items`);

    if (items.length === 0) {
      return { objectType: label, totalFetched: 0, succeeded: 0, failed: 0, durationMs: Date.now() - start };
    }

    const { succeeded, failed } = await upsertItemsBatch(items);
    const durationMs = Date.now() - start;
    console.log(`[Crawl] ${label}: done in ${durationMs}ms (${succeeded} ok, ${failed} failed)`);

    return { objectType: label, totalFetched: items.length, succeeded, failed, durationMs };
  } catch (error) {
    console.error(`[Crawl] ${label}: SKIPPED — ${error instanceof Error ? error.message : error}`);
    return { objectType: label, totalFetched: 0, succeeded: 0, failed: 0, durationMs: Date.now() - start };
  }
}

// ── Run only extended sources (Reports, Dashboards, Chatter, Analytics) ──

export type ExtendedSource = "reports" | "chatter" | "analytics" | "all";

export async function runExtendedCrawl(source: ExtendedSource = "all"): Promise<CrawlResult[]> {
  console.log(`[Crawl] ═══ Starting extended crawl (${source}) ═══`);
  await ensureConnection();

  const results: CrawlResult[] = [];

  if (source === "reports" || source === "all") {
    results.push(await crawlExtended("Report", async () => {
      const reports = await listReportsWithDetails();
      return reports.map(transformReport);
    }));
    results.push(await crawlExtended("Dashboard", async () => {
      const dashboards = await listDashboards();
      return dashboards.map(transformDashboard);
    }));
  }

  if (source === "chatter" || source === "all") {
    results.push(await crawlExtended("ChatterPost", async () => {
      const feedItems = await getAllCompanyFeedItems(1000);
      return feedItems.map(transformChatterFeedItem);
    }));
  }

  if (source === "analytics" || source === "all") {
    results.push(await crawlExtended("AnalyticsDataset", async () => {
      const datasets = await listDatasets();
      return datasets.map(transformAnalyticsDataset);
    }));
    results.push(await crawlExtended("AnalyticsDashboard", async () => {
      const dashboards = await listAnalyticsDashboards();
      return dashboards.map(transformAnalyticsDashboard);
    }));
  }

  console.log(`[Crawl] ═══ Extended crawl (${source}) complete ═══`);
  return results;
}
