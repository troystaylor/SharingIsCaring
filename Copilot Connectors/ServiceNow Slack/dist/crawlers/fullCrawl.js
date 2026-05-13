"use strict";
// ── Full Crawl ──
// Fetches all indexed Slack content from ServiceNow and ingests into Microsoft Graph.
Object.defineProperty(exports, "__esModule", { value: true });
exports.runFullCrawl = runFullCrawl;
const slackContentClient_1 = require("../servicenow/slackContentClient");
const transformer_1 = require("../servicenow/transformer");
const connectionManager_1 = require("../references/connectionManager");
async function runFullCrawl() {
    const start = Date.now();
    console.log("[Crawl] ═══ Starting full crawl ═══");
    await (0, connectionManager_1.ensureConnection)();
    const records = await (0, slackContentClient_1.getAllSlackContent)();
    console.log(`[Crawl] Fetched ${records.length} records from ServiceNow`);
    if (records.length === 0) {
        console.log("[Crawl] No records to process");
        return { totalFetched: 0, succeeded: 0, failed: 0, durationMs: Date.now() - start };
    }
    const items = records.map((r) => (0, transformer_1.transformSlackRecord)(r));
    const { succeeded, failed } = await (0, connectionManager_1.upsertItemsBatch)(items);
    const durationMs = Date.now() - start;
    console.log(`[Crawl] ═══ Full crawl complete: ${succeeded} succeeded, ${failed} failed, ${durationMs}ms ═══`);
    return { totalFetched: records.length, succeeded, failed, durationMs };
}
//# sourceMappingURL=fullCrawl.js.map