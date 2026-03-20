// ── Chatter / Connect REST API Client (#8) ──
// Fetches Chatter feed items (posts, comments) from the Connect REST API.

import { sfFetch } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { ChatterFeedItem } from "../models/salesforceTypes";

interface ChatterFeedResponse {
  elements: ChatterFeedItem[];
  nextPageUrl?: string;
}

// ── Company Feed ──

export async function getCompanyFeed(
  pageSize = 50,
  pageUrl?: string
): Promise<{ items: ChatterFeedItem[]; nextPageUrl?: string }> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const url = pageUrl || `/services/data/${apiVersion}/chatter/feeds/company/feed-elements?pageSize=${pageSize}`;
  const response = await sfFetch<ChatterFeedResponse>(url);

  return {
    items: response.elements || [],
    nextPageUrl: response.nextPageUrl,
  };
}

export async function getAllCompanyFeedItems(maxItems = 500): Promise<ChatterFeedItem[]> {
  const allItems: ChatterFeedItem[] = [];
  let nextUrl: string | undefined;

  do {
    const { items, nextPageUrl } = await getCompanyFeed(50, nextUrl);
    allItems.push(...items);
    nextUrl = nextPageUrl;
  } while (nextUrl && allItems.length < maxItems);

  console.log(`[Chatter] Fetched ${allItems.length} company feed items`);
  return allItems.slice(0, maxItems);
}

// ── Record Feed ──

export async function getRecordFeed(
  recordId: string,
  pageSize = 25,
  pageUrl?: string
): Promise<{ items: ChatterFeedItem[]; nextPageUrl?: string }> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const url = pageUrl || `/services/data/${apiVersion}/chatter/feeds/record/${recordId}/feed-elements?pageSize=${pageSize}`;
  const response = await sfFetch<ChatterFeedResponse>(url);

  return {
    items: response.elements || [],
    nextPageUrl: response.nextPageUrl,
  };
}

// ── User Feed ──

export async function getUserFeed(
  userId: string,
  pageSize = 25
): Promise<ChatterFeedItem[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<ChatterFeedResponse>(
    `/services/data/${apiVersion}/chatter/feeds/user-profile/${userId}/feed-elements?pageSize=${pageSize}`
  );
  return response.elements || [];
}

// ── Search Chatter ──

export async function searchChatter(
  query: string,
  pageSize = 25
): Promise<ChatterFeedItem[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const encoded = encodeURIComponent(query);
  const response = await sfFetch<ChatterFeedResponse>(
    `/services/data/${apiVersion}/chatter/feed-elements?q=${encoded}&pageSize=${pageSize}`
  );
  return response.elements || [];
}
