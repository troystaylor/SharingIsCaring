// ── Bulk API 2.0 Client (#3) ──
// For initial full crawl of large orgs (>10k records).

import { sfFetch, RateLimitError } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { getAccessToken, getInstanceUrl } from "../auth/salesforceAuth";
import { BulkJobInfo, SalesforceRecord } from "../models/salesforceTypes";
import { parse } from "csv-parse/sync";

interface BulkCreateJobRequest {
  operation: "query" | "queryAll";
  query: string;
  contentType?: "CSV";
  columnDelimiter?: "COMMA";
  lineEnding?: "LF";
}

export async function createBulkQueryJob(
  soql: string,
  includeDeleted = false
): Promise<BulkJobInfo> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const body: BulkCreateJobRequest = {
    operation: includeDeleted ? "queryAll" : "query",
    query: soql,
    contentType: "CSV",
    columnDelimiter: "COMMA",
    lineEnding: "LF",
  };

  return sfFetch<BulkJobInfo>(
    `/services/data/${apiVersion}/jobs/query`,
    { method: "POST", body }
  );
}

export async function getBulkJobStatus(jobId: string): Promise<BulkJobInfo> {
  const apiVersion = getConfig().salesforce.apiVersion;
  return sfFetch<BulkJobInfo>(
    `/services/data/${apiVersion}/jobs/query/${jobId}`
  );
}

export async function waitForJobCompletion(
  jobId: string,
  pollIntervalMs = 5000,
  maxWaitMs = 600000
): Promise<BulkJobInfo> {
  const start = Date.now();

  while (Date.now() - start < maxWaitMs) {
    const status = await getBulkJobStatus(jobId);
    console.log(`[Bulk] Job ${jobId}: state=${status.state}, processed=${status.numberRecordsProcessed}`);

    if (status.state === "JobComplete") return status;
    if (status.state === "Failed" || status.state === "Aborted") {
      throw new Error(`Bulk job ${jobId} ended with state: ${status.state}`);
    }

    await sleep(pollIntervalMs);
  }

  throw new Error(`Bulk job ${jobId} did not complete within ${maxWaitMs}ms`);
}

export async function getBulkJobResults<T extends SalesforceRecord>(
  jobId: string,
  locator?: string
): Promise<{ records: T[]; locator?: string; done: boolean }> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const token = await getAccessToken();
  const instanceUrl = await getInstanceUrl();

  let url = `${instanceUrl}/services/data/${apiVersion}/jobs/query/${jobId}/results`;
  if (locator) {
    url += `?locator=${encodeURIComponent(locator)}`;
  }

  const response = await fetch(url, {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "text/csv",
    },
  });

  if (response.status === 429) {
    const retryAfter = response.headers.get("Retry-After");
    throw new RateLimitError(
      "Bulk API rate limit",
      retryAfter ? parseInt(retryAfter, 10) : 60
    );
  }

  if (!response.ok) {
    throw new Error(`Bulk results fetch failed (${response.status})`);
  }

  const csvText = await response.text();
  const nextLocator = response.headers.get("Sforce-Locator");
  const done = !nextLocator || nextLocator === "null";

  const records = parse(csvText, {
    columns: true,
    skip_empty_lines: true,
    cast: true,
  }) as T[];

  return {
    records,
    locator: done ? undefined : (nextLocator ?? undefined),
    done,
  };
}

export async function getAllBulkJobResults<T extends SalesforceRecord>(
  jobId: string
): Promise<T[]> {
  const allRecords: T[] = [];
  let locator: string | undefined;
  let done = false;

  while (!done) {
    const result = await getBulkJobResults<T>(jobId, locator);
    allRecords.push(...result.records);
    locator = result.locator;
    done = result.done;
    console.log(`[Bulk] Fetched ${result.records.length} records (total: ${allRecords.length})`);
  }

  return allRecords;
}

export async function bulkQuery<T extends SalesforceRecord>(
  soql: string,
  includeDeleted = false
): Promise<T[]> {
  const job = await createBulkQueryJob(soql, includeDeleted);
  console.log(`[Bulk] Created query job ${job.id}`);

  await waitForJobCompletion(job.id);
  return getAllBulkJobResults<T>(job.id);
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
