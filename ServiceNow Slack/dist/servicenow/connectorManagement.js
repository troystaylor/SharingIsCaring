"use strict";
// ── ServiceNow Connector Management ──
// Read-only access to external content connector and crawl status.
// Connector creation is done through ServiceNow's admin UI.
Object.defineProperty(exports, "__esModule", { value: true });
exports.listSlackConnectors = listSlackConnectors;
exports.listCrawls = listCrawls;
exports.triggerCrawl = triggerCrawl;
const restClient_1 = require("./restClient");
async function listSlackConnectors() {
    console.log("[ConnMgmt] Listing Slack external content connectors");
    const records = await (0, restClient_1.query)("sn_ais_ext_content_connector", {
        sysparm_query: "source_typeLIKEslack",
        sysparm_fields: "sys_id,name,source_type,state,sys_updated_on,sys_created_on",
        sysparm_display_value: "true",
    });
    console.log(`[ConnMgmt] Found ${records.length} Slack connectors`);
    return records;
}
async function listCrawls(connectorSysId) {
    console.log("[ConnMgmt] Listing crawl records");
    const params = {
        sysparm_fields: "sys_id,connector,state,crawl_type,start_time,end_time,items_crawled,items_indexed,errors,sys_updated_on",
        sysparm_display_value: "true",
        sysparm_limit: 50,
        sysparm_orderby: "sys_updated_onDESC",
    };
    if (connectorSysId) {
        params.sysparm_query = `connector=${connectorSysId}`;
    }
    const records = await (0, restClient_1.query)("sn_ais_content_crawl", params);
    console.log(`[ConnMgmt] Found ${records.length} crawl records`);
    return records;
}
async function triggerCrawl(connectorSysId, crawlType = "full") {
    console.log(`[ConnMgmt] Triggering ${crawlType} crawl for connector ${connectorSysId}`);
    const record = await (0, restClient_1.createRecord)("sn_ais_content_crawl", {
        connector: connectorSysId,
        crawl_type: crawlType,
        state: "queued",
    });
    console.log(`[ConnMgmt] Crawl created: ${record.sys_id}`);
    return record;
}
//# sourceMappingURL=connectorManagement.js.map