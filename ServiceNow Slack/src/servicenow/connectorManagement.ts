// ── ServiceNow Connector Management ──
// Read-only access to external content connector and crawl status.
// Connector creation is done through ServiceNow's admin UI.

import { query, createRecord } from "./restClient";

interface ConnectorRecord {
  sys_id: string;
  name: string;
  source_type: string;
  state: string;
  sys_updated_on: string;
  sys_created_on: string;
}

interface CrawlRecord {
  sys_id: string;
  connector: string;
  state: string;
  crawl_type: string;
  start_time: string;
  end_time: string;
  items_crawled: string;
  items_indexed: string;
  errors: string;
  sys_updated_on: string;
}

export async function listSlackConnectors(): Promise<ConnectorRecord[]> {
  console.log("[ConnMgmt] Listing Slack external content connectors");

  const records = await query<ConnectorRecord>("sn_ais_ext_content_connector", {
    sysparm_query: "source_typeLIKEslack",
    sysparm_fields: "sys_id,name,source_type,state,sys_updated_on,sys_created_on",
    sysparm_display_value: "true",
  });

  console.log(`[ConnMgmt] Found ${records.length} Slack connectors`);
  return records;
}

export async function listCrawls(connectorSysId?: string): Promise<CrawlRecord[]> {
  console.log("[ConnMgmt] Listing crawl records");

  const params: Record<string, string | number | boolean> = {
    sysparm_fields: "sys_id,connector,state,crawl_type,start_time,end_time,items_crawled,items_indexed,errors,sys_updated_on",
    sysparm_display_value: "true",
    sysparm_limit: 50,
    sysparm_orderby: "sys_updated_onDESC",
  };

  if (connectorSysId) {
    params.sysparm_query = `connector=${connectorSysId}`;
  }

  const records = await query<CrawlRecord>("sn_ais_content_crawl", params);

  console.log(`[ConnMgmt] Found ${records.length} crawl records`);
  return records;
}

export async function triggerCrawl(
  connectorSysId: string,
  crawlType: string = "full"
): Promise<CrawlRecord> {
  console.log(`[ConnMgmt] Triggering ${crawlType} crawl for connector ${connectorSysId}`);

  const record = await createRecord<CrawlRecord>("sn_ais_content_crawl", {
    connector: connectorSysId,
    crawl_type: crawlType,
    state: "queued",
  });

  console.log(`[ConnMgmt] Crawl created: ${record.sys_id}`);
  return record;
}
