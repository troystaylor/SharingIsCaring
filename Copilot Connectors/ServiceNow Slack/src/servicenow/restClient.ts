// ── ServiceNow REST Client ──
// Generic Table API client with pagination and auth header injection.

import { getAccessToken } from "../auth/servicenowAuth";
import { getConfig } from "../config/connectorConfig";
import { ServiceNowQueryResult } from "../models/types";

export async function query<T>(
  tableName: string,
  params: Record<string, string | number | boolean> = {}
): Promise<T[]> {
  const config = getConfig().servicenow;
  const token = await getAccessToken();

  const queryParams = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    queryParams.set(key, String(value));
  }

  const url = `${config.instanceUrl}/api/now/table/${tableName}?${queryParams.toString()}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `ServiceNow Table API query failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as ServiceNowQueryResult<T>;
  return data.result;
}

export async function queryAll<T>(
  tableName: string,
  params: Record<string, string | number | boolean> = {},
  pageSize = 1000
): Promise<T[]> {
  const allRecords: T[] = [];
  let offset = 0;

  while (true) {
    const pageParams = {
      ...params,
      sysparm_limit: pageSize,
      sysparm_offset: offset,
    };

    const records = await query<T>(tableName, pageParams);
    allRecords.push(...records);

    if (records.length < pageSize) {
      break;
    }

    offset += pageSize;
    console.log(`[SN] Fetched ${allRecords.length} records from ${tableName} so far...`);
  }

  return allRecords;
}

export async function getRecord<T>(
  tableName: string,
  sysId: string,
  params: Record<string, string | number | boolean> = {}
): Promise<T | null> {
  const config = getConfig().servicenow;
  const token = await getAccessToken();

  const queryParams = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    queryParams.set(key, String(value));
  }

  const url = `${config.instanceUrl}/api/now/table/${tableName}/${sysId}?${queryParams.toString()}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "application/json",
    },
  });

  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `ServiceNow Table API get failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as { result: T };
  return data.result;
}

export async function createRecord<T>(
  tableName: string,
  body: Record<string, unknown>
): Promise<T> {
  const config = getConfig().servicenow;
  const token = await getAccessToken();

  const url = `${config.instanceUrl}/api/now/table/${tableName}`;

  const response = await fetch(url, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `ServiceNow Table API create failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as { result: T };
  return data.result;
}
