// ── Full Crawl ──
// Fetches all indexed Slack content from ServiceNow and ingests into Microsoft Graph.

import { getAllSlackContent } from "../servicenow/slackContentClient";
import { transformSlackRecord } from "../servicenow/transformer";
import { upsertItemsBatch, ensureConnection } from "../references/connectionManager";

export interface CrawlResult {
  totalFetched: number;
  succeeded: number;
  failed: number;
  durationMs: number;
}

export async function runFullCrawl(): Promise<CrawlResult> {
  const start = Date.now();
  console.log("[Crawl] ═══ Starting full crawl ═══");

  await ensureConnection();

  const records = await getAllSlackContent();
  console.log(`[Crawl] Fetched ${records.length} records from ServiceNow`);

  if (records.length === 0) {
    console.log("[Crawl] No records to process");
    return { totalFetched: 0, succeeded: 0, failed: 0, durationMs: Date.now() - start };
  }

  const items = records.map((r) => transformSlackRecord(r));

  const { succeeded, failed } = await upsertItemsBatch(items);

  const durationMs = Date.now() - start;
  console.log(
    `[Crawl] ═══ Full crawl complete: ${succeeded} succeeded, ${failed} failed, ${durationMs}ms ═══`
  );

  return { totalFetched: records.length, succeeded, failed, durationMs };
}
