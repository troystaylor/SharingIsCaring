// ── Schema Definition for Graph External Connection ──
// Maps ServiceNow-indexed Slack content to Microsoft Graph external item properties.

import { ExternalSchema, SchemaProperty } from "../models/types";

const schemaProperties: SchemaProperty[] = [
  {
    name: "title",
    type: "String",
    isSearchable: true,
    isQueryable: true,
    isRetrievable: true,
    labels: ["title"],
    aliases: ["subject", "name"],
  },
  {
    name: "description",
    type: "String",
    isSearchable: true,
    isRetrievable: true,
  },
  {
    name: "url",
    type: "String",
    isRetrievable: true,
    labels: ["url"],
  },
  {
    name: "channelName",
    type: "String",
    isSearchable: true,
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
    aliases: ["channel", "slackChannel"],
  },
  {
    name: "channelId",
    type: "String",
    isQueryable: true,
    isRetrievable: true,
    isExactMatchRequired: true,
  },
  {
    name: "authorName",
    type: "String",
    isSearchable: true,
    isQueryable: true,
    isRetrievable: true,
    aliases: ["author", "sender", "postedBy"],
  },
  {
    name: "authorId",
    type: "String",
    isQueryable: true,
    isRetrievable: true,
    isExactMatchRequired: true,
  },
  {
    name: "messageTimestamp",
    type: "DateTime",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
    labels: ["createdDateTime"],
  },
  {
    name: "threadTimestamp",
    type: "String",
    isQueryable: true,
    isRetrievable: true,
  },
  {
    name: "hasAttachments",
    type: "Boolean",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
  },
  {
    name: "attachmentNames",
    type: "String",
    isSearchable: true,
    isRetrievable: true,
    aliases: ["files", "fileNames"],
  },
  {
    name: "reactionCount",
    type: "Int64",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
  },
  {
    name: "replyCount",
    type: "Int64",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
  },
  {
    name: "contentType",
    type: "String",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
    aliases: ["itemType", "type"],
  },
  {
    name: "lastModifiedDateTime",
    type: "DateTime",
    isQueryable: true,
    isRetrievable: true,
    isRefinable: true,
    labels: ["lastModifiedDateTime"],
  },
];

export function getSchema(): ExternalSchema {
  return {
    baseType: "microsoft.graph.externalItem",
    properties: schemaProperties,
  };
}
