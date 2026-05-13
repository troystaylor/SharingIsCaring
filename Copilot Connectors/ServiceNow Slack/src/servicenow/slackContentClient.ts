// ── Slack Content Client ──
// Queries indexed Slack content from ServiceNow AI Search indexed source table.

import { queryAll, query } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { SlackContentRecord } from "../models/types";

export async function getAllSlackContent(): Promise<SlackContentRecord[]> {
  const tableName = getConfig().servicenow.slackIndexedTable;

  console.log(`[SlackContent] Fetching all indexed content from ${tableName}`);

  const records = await queryAll<SlackContentRecord>(tableName, {
    sysparm_display_value: "false",
  });

  console.log(`[SlackContent] Total records fetched: ${records.length}`);
  return records;
}

export async function getSlackContentSince(
  sinceDate: string
): Promise<SlackContentRecord[]> {
  const tableName = getConfig().servicenow.slackIndexedTable;

  console.log(`[SlackContent] Fetching content modified since ${sinceDate} from ${tableName}`);

  const records = await queryAll<SlackContentRecord>(tableName, {
    sysparm_query: `sys_updated_on>${sinceDate}`,
    sysparm_display_value: "false",
  });

  console.log(`[SlackContent] Modified records fetched: ${records.length}`);
  return records;
}

export async function getSlackContentCount(): Promise<number> {
  const tableName = getConfig().servicenow.slackIndexedTable;

  const records = await query<SlackContentRecord>(tableName, {
    sysparm_limit: 1,
    sysparm_fields: "sys_id",
  });

  // Table API doesn't return total count without X-Total-Count header
  // For an accurate count, we'd need the Aggregate API
  // This is a rough check — if we get 1 record, table has data
  return records.length;
}
