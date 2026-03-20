// ── Microsoft Graph External Connectors Types ──

export interface ExternalConnection {
  id: string;
  name: string;
  description: string;
  state?: "draft" | "ready" | "obsolete" | "limitExceeded";
  activitySettings?: {
    urlToItemResolvers?: Array<{
      "@odata.type": string;
      priority: number;
      itemId: string;
      urlMatchInfo: {
        baseUrls: string[];
        urlPattern: string;
      };
    }>;
  };
  searchSettings?: {
    searchResultTemplates?: Array<{
      id: string;
      priority: number;
      layout: object;
    }>;
  };
}

export interface SchemaProperty {
  name: string;
  type:
    | "String"
    | "Int64"
    | "Double"
    | "DateTime"
    | "Boolean"
    | "StringCollection";
  isSearchable?: boolean;
  isQueryable?: boolean;
  isRetrievable?: boolean;
  isRefinable?: boolean;
  isExactMatchRequired?: boolean;
  labels?: string[];
  aliases?: string[];
}

export interface ExternalSchema {
  baseType: string;
  properties: SchemaProperty[];
}

export interface AccessControlEntry {
  type: "user" | "group" | "everyone";
  value: string;
  accessType: "grant" | "deny";
}

export interface ExternalItemContent {
  value: string;
  type: "text" | "html";
}

export interface ExternalItem {
  id: string;
  acl: AccessControlEntry[];
  properties: Record<string, unknown>;
  content?: ExternalItemContent;
}

export const EVERYONE_ACL: AccessControlEntry[] = [
  {
    type: "everyone",
    value: "everyone",
    accessType: "grant",
  },
];
