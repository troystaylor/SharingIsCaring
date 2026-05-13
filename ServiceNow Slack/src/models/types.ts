// ── Microsoft Graph External Connectors Types ──

export interface ExternalConnection {
  id: string;
  name: string;
  description: string;
  state?: "draft" | "ready" | "obsolete" | "limitExceeded";
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

// ── ServiceNow Slack content types ──

export interface SlackContentRecord {
  sys_id: string;
  sys_updated_on: string;
  sys_created_on: string;
  document_id?: string;
  title?: string;
  text?: string;
  url?: string;
  channel_name?: string;
  channel_id?: string;
  author_name?: string;
  author_id?: string;
  message_timestamp?: string;
  thread_timestamp?: string;
  has_attachments?: string;
  attachment_names?: string;
  reaction_count?: string;
  reply_count?: string;
  file_name?: string;
  file_size?: string;
  file_type?: string;
  content_type?: string; // "message" or "attachment"
}

export interface ServiceNowQueryResult<T> {
  result: T[];
}
