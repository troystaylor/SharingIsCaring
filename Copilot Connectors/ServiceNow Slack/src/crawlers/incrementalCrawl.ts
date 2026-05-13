// ── Incremental Crawl ──
// Fetches only Slack content modified since a given date from ServiceNow.

import { getSlackContentSince } from "../servicenow/slackContentClient";
import { transformSlackRecord } from "../servicenow/transformer";
import { upsertItemsBatch, ensureConnection } from "../references/connectionManager";

export interface IncrementalCrawlResult {
  totalFetched: number;
  succeeded: number;
  failed: number;
  durationMs: number;
  sinceDate: string;
}

export async function runIncrementalCrawl(
  sinceDate: string
): Promise<IncrementalCrawlResult> {
  const start = Date.now();
  console.log(`[IncrCrawl] ═══ Starting incremental crawl since ${sinceDate} ═══`);

  await ensureConnection();

  const records = await getSlackContentSince(sinceDate);
  console.log(`[IncrCrawl] Fetched ${records.length} modified records`);

  if (records.length === 0) {
    console.log("[IncrCrawl] No modified records to process");
    return {
      totalFetched: 0,
      succeeded: 0,
      failed: 0,
      durationMs: Date.now() - start,
      sinceDate,
    };
  }

  const items = records.map((r) => transformSlackRecord(r));

  const { succeeded, failed } = await upsertItemsBatch(items);

  const durationMs = Date.now() - start;
  console.log(
    `[IncrCrawl] ═══ Incremental crawl complete: ${succeeded} succeeded, ${failed} failed, ${durationMs}ms ═══`
  );

  return { totalFetched: records.length, succeeded, failed, durationMs, sinceDate };
}
