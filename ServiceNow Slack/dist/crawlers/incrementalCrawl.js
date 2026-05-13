"use strict";
// ── Incremental Crawl ──
// Fetches only Slack content modified since a given date from ServiceNow.
Object.defineProperty(exports, "__esModule", { value: true });
exports.runIncrementalCrawl = runIncrementalCrawl;
const slackContentClient_1 = require("../servicenow/slackContentClient");
const transformer_1 = require("../servicenow/transformer");
const connectionManager_1 = require("../references/connectionManager");
async function runIncrementalCrawl(sinceDate) {
    const start = Date.now();
    console.log(`[IncrCrawl] ═══ Starting incremental crawl since ${sinceDate} ═══`);
    await (0, connectionManager_1.ensureConnection)();
    const records = await (0, slackContentClient_1.getSlackContentSince)(sinceDate);
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
    const items = records.map((r) => (0, transformer_1.transformSlackRecord)(r));
    const { succeeded, failed } = await (0, connectionManager_1.upsertItemsBatch)(items);
    const durationMs = Date.now() - start;
    console.log(`[IncrCrawl] ═══ Incremental crawl complete: ${succeeded} succeeded, ${failed} failed, ${durationMs}ms ═══`);
    return { totalFetched: records.length, succeeded, failed, durationMs, sinceDate };
}
//# sourceMappingURL=incrementalCrawl.js.map