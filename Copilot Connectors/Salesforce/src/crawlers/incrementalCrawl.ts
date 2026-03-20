// ── Incremental Crawl ──
// Uses SOQL with LastModifiedDate filter to sync only changed records.
// Also supports real-time CDC via Pub/Sub API.

import { SObjectType, buildFullCrawlQuery, buildDeletedQuery } from "../custom/soqlBuilder";
import { queryAll } from "../custom/restClient";
import { transformRecord } from "../custom/transformer";
import { upsertItemsBatch, deleteItem, ensureConnection } from "../references/connectionManager";
import { subscribe, getCdcTopic, CdcEventHandler } from "../custom/pubsubClient";
import { SalesforceRecord, ChangeEvent } from "../models/salesforceTypes";
import { loadUserMap } from "../custom/userMapper";

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

export interface IncrementalCrawlResult {
  objectType: string;
  upserted: number;
  deleted: number;
  failed: number;
  durationMs: number;
}

// ── SOQL-based incremental crawl ──

export async function runIncrementalCrawl(
  sinceDate: string
): Promise<IncrementalCrawlResult[]> {
  console.log(`[IncrCrawl] ═══ Starting incremental crawl since ${sinceDate} ═══`);
  await ensureConnection();
  await loadUserMap();

  const results: IncrementalCrawlResult[] = [];

  for (const objectType of ALL_OBJECTS) {
    const start = Date.now();

    // Fetch modified records
    let records: SalesforceRecord[];
    try {
      const soql = buildFullCrawlQuery(objectType, sinceDate);
      records = await queryAll<SalesforceRecord>(soql);
    } catch (error) {
      console.error(`[IncrCrawl] ${objectType}: SKIPPED — ${error instanceof Error ? error.message : error}`);
      results.push({ objectType, upserted: 0, deleted: 0, failed: 0, durationMs: Date.now() - start });
      continue;
    }
    console.log(`[IncrCrawl] ${objectType}: ${records.length} modified records`);

    // Upsert modified
    const items = records.map((r) => transformRecord(objectType, r));
    const { succeeded, failed } = items.length > 0
      ? await upsertItemsBatch(items)
      : { succeeded: 0, failed: 0 };

    // Fetch and delete removed records
    let deleted = 0;
    try {
      const deleteSoql = buildDeletedQuery(objectType, sinceDate);
      const deletedRecords = await queryAll<SalesforceRecord>(deleteSoql);

      for (const record of deletedRecords) {
        try {
          const itemId = `sf-${objectType.toLowerCase()}-${record.Id}`;
          await deleteItem(itemId);
          deleted++;
        } catch (err) {
          console.error(`[IncrCrawl] Failed to delete ${record.Id}: ${err}`);
        }
      }
    } catch {
      // queryAll on deleted records may fail if IsDeleted not queryable
      console.log(`[IncrCrawl] ${objectType}: skip deleted record check`);
    }

    const durationMs = Date.now() - start;
    results.push({
      objectType,
      upserted: succeeded,
      deleted,
      failed,
      durationMs,
    });
  }

  const totalUpsert = results.reduce((s, r) => s + r.upserted, 0);
  const totalDelete = results.reduce((s, r) => s + r.deleted, 0);
  console.log(`[IncrCrawl] ═══ Complete: ${totalUpsert} upserted, ${totalDelete} deleted ═══`);

  return results;
}

// ── CDC-based real-time sync via Pub/Sub API ──

export async function startCdcSync(): Promise<{ cancel: () => void }> {
  await ensureConnection();

  const cancels: Array<() => void> = [];

  for (const objectType of PHASE1_OBJECTS) {
    const topic = getCdcTopic(objectType);

    const handler: CdcEventHandler = async (event: ChangeEvent) => {
      const header = event.payload.ChangeEventHeader;
      console.log(
        `[CDC] ${header.entityName} ${header.changeType}: ${header.recordIds.join(", ")}`
      );

      if (header.changeType === "DELETE") {
        for (const id of header.recordIds) {
          const itemId = `sf-${header.entityName.toLowerCase()}-${id}`;
          try {
            await deleteItem(itemId);
          } catch (err) {
            console.error(`[CDC] Delete failed for ${itemId}: ${err}`);
          }
        }
      } else {
        // CREATE, UPDATE, UNDELETE — re-fetch and upsert
        for (const id of header.recordIds) {
          try {
            const soql = `SELECT ${getFieldsForType(objectType)} FROM ${objectType} WHERE Id = '${id}'`;
            const records = await queryAll<SalesforceRecord>(soql);
            if (records.length > 0) {
              const item = transformRecord(objectType, records[0]);
              await upsertItemsBatch([item]);
            }
          } catch (err) {
            console.error(`[CDC] Upsert failed for ${id}: ${err}`);
          }
        }
      }
    };

    const { cancel } = await subscribe(topic, handler);
    cancels.push(cancel);
  }

  console.log("[CDC] Real-time sync started for all Phase 1 objects");

  return {
    cancel: () => {
      cancels.forEach((c) => c());
      console.log("[CDC] Real-time sync stopped");
    },
  };
}

function getFieldsForType(objectType: SObjectType): string {
  // Import dynamically to avoid circular dependency issues in simple use cases
  const { getFieldsForObject } = require("../custom/soqlBuilder");
  return getFieldsForObject(objectType).join(", ");
}
