"use strict";
// ── Slack Content Client ──
// Queries indexed Slack content from ServiceNow AI Search indexed source table.
Object.defineProperty(exports, "__esModule", { value: true });
exports.getAllSlackContent = getAllSlackContent;
exports.getSlackContentSince = getSlackContentSince;
exports.getSlackContentCount = getSlackContentCount;
const restClient_1 = require("./restClient");
const connectorConfig_1 = require("../config/connectorConfig");
async function getAllSlackContent() {
    const tableName = (0, connectorConfig_1.getConfig)().servicenow.slackIndexedTable;
    console.log(`[SlackContent] Fetching all indexed content from ${tableName}`);
    const records = await (0, restClient_1.queryAll)(tableName, {
        sysparm_display_value: "false",
    });
    console.log(`[SlackContent] Total records fetched: ${records.length}`);
    return records;
}
async function getSlackContentSince(sinceDate) {
    const tableName = (0, connectorConfig_1.getConfig)().servicenow.slackIndexedTable;
    console.log(`[SlackContent] Fetching content modified since ${sinceDate} from ${tableName}`);
    const records = await (0, restClient_1.queryAll)(tableName, {
        sysparm_query: `sys_updated_on>${sinceDate}`,
        sysparm_display_value: "false",
    });
    console.log(`[SlackContent] Modified records fetched: ${records.length}`);
    return records;
}
async function getSlackContentCount() {
    const tableName = (0, connectorConfig_1.getConfig)().servicenow.slackIndexedTable;
    const records = await (0, restClient_1.query)(tableName, {
        sysparm_limit: 1,
        sysparm_fields: "sys_id",
    });
    // Table API doesn't return total count without X-Total-Count header
    // For an accurate count, we'd need the Aggregate API
    // This is a rough check — if we get 1 record, table has data
    return records.length;
}
//# sourceMappingURL=slackContentClient.js.map