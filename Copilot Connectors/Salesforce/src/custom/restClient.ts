import { getAccessToken, getInstanceUrl } from "../auth/salesforceAuth";
import { getConfig } from "../config/connectorConfig";
import { QueryResult, SalesforceRecord } from "../models/salesforceTypes";

interface RequestOptions {
  method?: string;
  body?: unknown;
  headers?: Record<string, string>;
}

async function sfFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const token = await getAccessToken();
  const instanceUrl = await getInstanceUrl();

  const url = `${instanceUrl}${path}`;
  const response = await fetch(url, {
    method: options.method || "GET",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      ...options.headers,
    },
    body: options.body ? JSON.stringify(options.body) : undefined,
  });

  if (response.status === 401) {
    // Token may have expired mid-request; caller should retry after refresh
    throw new TokenExpiredError("Salesforce returned 401 — token expired");
  }

  if (response.status === 429) {
    const retryAfter = response.headers.get("Retry-After");
    throw new RateLimitError(
      `Salesforce rate limit exceeded. Retry after: ${retryAfter || "unknown"}`,
      retryAfter ? parseInt(retryAfter, 10) : 60
    );
  }

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `Salesforce API error (${response.status} ${response.statusText}): ${errorBody}`
    );
  }

  return response.json() as Promise<T>;
}

export class TokenExpiredError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TokenExpiredError";
  }
}

export class RateLimitError extends Error {
  retryAfterSeconds: number;
  constructor(message: string, retryAfterSeconds: number) {
    super(message);
    this.name = "RateLimitError";
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

// ── REST API (#1) ──

export async function query<T extends SalesforceRecord>(
  soql: string
): Promise<QueryResult<T>> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const encoded = encodeURIComponent(soql);
  return sfFetch<QueryResult<T>>(
    `/services/data/${apiVersion}/query?q=${encoded}`
  );
}

export async function queryMore<T extends SalesforceRecord>(
  nextRecordsUrl: string
): Promise<QueryResult<T>> {
  return sfFetch<QueryResult<T>>(nextRecordsUrl);
}

export async function queryAll<T extends SalesforceRecord>(
  soql: string
): Promise<T[]> {
  const records: T[] = [];
  let result = await query<T>(soql);
  records.push(...result.records);

  while (!result.done && result.nextRecordsUrl) {
    result = await queryMore<T>(result.nextRecordsUrl);
    records.push(...result.records);
  }

  return records;
}

export async function getRecord<T extends SalesforceRecord>(
  sobject: string,
  id: string,
  fields?: string[]
): Promise<T> {
  const apiVersion = getConfig().salesforce.apiVersion;
  let path = `/services/data/${apiVersion}/sobjects/${sobject}/${id}`;
  if (fields?.length) {
    path += `?fields=${fields.join(",")}`;
  }
  return sfFetch<T>(path);
}

export async function describeSObject(sobject: string): Promise<unknown> {
  const apiVersion = getConfig().salesforce.apiVersion;
  return sfFetch(`/services/data/${apiVersion}/sobjects/${sobject}/describe`);
}

export async function search(sosl: string): Promise<{ searchRecords: SalesforceRecord[] }> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const encoded = encodeURIComponent(sosl);
  return sfFetch(`/services/data/${apiVersion}/search?q=${encoded}`);
}

export { sfFetch };
