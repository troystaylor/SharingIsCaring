// ── Knowledge Articles Client (#11) ──
// Fetches Salesforce Knowledge articles via Knowledge REST API.

import { sfFetch } from "./restClient";
import { queryAll } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { KnowledgeArticle } from "../models/salesforceTypes";

interface KnowledgeArticleListResponse {
  articles: Array<{
    id: string;
    articleNumber: string;
    title: string;
    summary?: string;
    urlName: string;
    lastPublishedDate?: string;
    lastModifiedDate?: string;
    articleType?: string;
  }>;
  nextPageUrl?: string;
}

// ── List Published Articles (Knowledge REST API) ──

export async function listPublishedArticles(
  pageSize = 20,
  pageUrl?: string
): Promise<{ articles: KnowledgeArticle[]; nextPageUrl?: string }> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const url =
    pageUrl ||
    `/services/data/${apiVersion}/support/knowledgeArticles?pageSize=${pageSize}&order=desc&sort=LastPublishedDate`;

  const response = await sfFetch<KnowledgeArticleListResponse>(url);

  const articles: KnowledgeArticle[] = (response.articles || []).map((a) => ({
    Id: a.id,
    Title: a.title,
    Summary: a.summary,
    ArticleNumber: a.articleNumber,
    UrlName: a.urlName,
    PublishStatus: "Online",
    ArticleType: a.articleType,
    LastModifiedDate: a.lastModifiedDate,
  }));

  return {
    articles,
    nextPageUrl: response.nextPageUrl,
  };
}

export async function getAllPublishedArticles(maxArticles = 1000): Promise<KnowledgeArticle[]> {
  const allArticles: KnowledgeArticle[] = [];
  let nextUrl: string | undefined;

  do {
    const { articles, nextPageUrl } = await listPublishedArticles(20, nextUrl);
    allArticles.push(...articles);
    nextUrl = nextPageUrl;
  } while (nextUrl && allArticles.length < maxArticles);

  console.log(`[Knowledge] Fetched ${allArticles.length} published articles`);
  return allArticles.slice(0, maxArticles);
}

// ── Get Article Detail ──

export async function getArticleDetail(
  articleId: string
): Promise<Record<string, unknown>> {
  const apiVersion = getConfig().salesforce.apiVersion;
  return sfFetch<Record<string, unknown>>(
    `/services/data/${apiVersion}/support/knowledgeArticles/${articleId}`
  );
}

// ── SOQL-based Knowledge Query (for custom article types) ──

export async function queryKnowledgeArticles(
  articleType: string,
  fields: string[],
  sinceDate?: string
): Promise<KnowledgeArticle[]> {
  const allFields = [
    "Id",
    "Title",
    "Summary",
    "ArticleNumber",
    "UrlName",
    "PublishStatus",
    "KnowledgeArticleId",
    "LastModifiedDate",
    ...fields,
  ];
  const uniqueFields = [...new Set(allFields)];

  let soql = `SELECT ${uniqueFields.join(", ")} FROM ${articleType} WHERE PublishStatus = 'Online'`;
  if (sinceDate) {
    soql += ` AND LastModifiedDate > ${sinceDate}`;
  }
  soql += " ORDER BY LastModifiedDate ASC";

  return queryAll<KnowledgeArticle>(soql);
}
