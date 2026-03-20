// ── SOQL Query Builder (#2) ──
// Constructs SOQL queries for each Salesforce object type.

export interface SoqlOptions {
  fields: string[];
  sobject: string;
  where?: string;
  orderBy?: string;
  limit?: number;
  offset?: number;
}

export function buildQuery(options: SoqlOptions): string {
  let soql = `SELECT ${options.fields.join(", ")} FROM ${options.sobject}`;

  if (options.where) {
    soql += ` WHERE ${options.where}`;
  }
  if (options.orderBy) {
    soql += ` ORDER BY ${options.orderBy}`;
  }
  if (options.limit !== undefined) {
    soql += ` LIMIT ${options.limit}`;
  }
  if (options.offset !== undefined) {
    soql += ` OFFSET ${options.offset}`;
  }

  return soql;
}

// ── Pre-built queries for Phase 1 objects ──

const ACCOUNT_FIELDS = [
  "Id", "Name", "Description", "Industry", "Website", "Phone",
  "BillingCity", "BillingState", "BillingCountry",
  "AnnualRevenue", "NumberOfEmployees", "Type", "OwnerId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const CONTACT_FIELDS = [
  "Id", "FirstName", "LastName", "Name", "Email", "Phone",
  "Title", "Department", "AccountId",
  "MailingCity", "MailingState", "MailingCountry", "OwnerId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const OPPORTUNITY_FIELDS = [
  "Id", "Name", "Description", "StageName", "Amount", "CloseDate",
  "Probability", "AccountId", "OwnerId", "Type", "LeadSource",
  "IsClosed", "IsWon",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const CASE_FIELDS = [
  "Id", "CaseNumber", "Subject", "Description", "Status", "Priority",
  "Origin", "Type", "AccountId", "ContactId", "OwnerId",
  "IsClosed", "ClosedDate",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const LEAD_FIELDS = [
  "Id", "FirstName", "LastName", "Name", "Company", "Email", "Phone",
  "Title", "Status", "Industry", "LeadSource", "Rating", "Description",
  "OwnerId", "IsConverted", "ConvertedAccountId", "ConvertedContactId",
  "ConvertedOpportunityId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

// ── Phase 2 objects ──

const TASK_FIELDS = [
  "Id", "Subject", "Description", "Status", "Priority",
  "ActivityDate", "WhoId", "WhatId", "OwnerId", "TaskSubtype",
  "IsClosed", "IsHighPriority", "CompletedDateTime",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const EVENT_FIELDS = [
  "Id", "Subject", "Description", "Location",
  "StartDateTime", "EndDateTime", "DurationInMinutes",
  "WhoId", "WhatId", "OwnerId", "EventSubtype",
  "IsAllDayEvent", "ShowAs",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const KNOWLEDGE_ARTICLE_FIELDS = [
  "Id", "Title", "Summary", "ArticleNumber", "UrlName",
  "PublishStatus", "KnowledgeArticleId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name",
];

const PRODUCT2_FIELDS = [
  "Id", "Name", "Description", "ProductCode", "Family", "IsActive",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const PRICEBOOK_ENTRY_FIELDS = [
  "Id", "Name", "Pricebook2Id", "Product2Id", "UnitPrice",
  "IsActive", "UseStandardPrice", "ProductCode",
  "Product2.Name", "Pricebook2.Name",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

// ── Phase 3 objects ──

const QUOTE_FIELDS = [
  "Id", "Name", "QuoteNumber", "Status", "Description",
  "OpportunityId", "AccountId", "ContactId",
  "TotalPrice", "GrandTotal", "Discount", "ExpirationDate", "OwnerId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const QUOTE_LINE_ITEM_FIELDS = [
  "Id", "QuoteId", "Product2Id", "Quantity", "UnitPrice",
  "TotalPrice", "Discount", "Description", "LineNumber",
  "Product2.Name",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const CAMPAIGN_FIELDS = [
  "Id", "Name", "Description", "Type", "Status",
  "StartDate", "EndDate", "BudgetedCost", "ActualCost",
  "ExpectedRevenue", "NumberOfContacts", "NumberOfLeads",
  "NumberSent", "IsActive", "OwnerId", "ParentId",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

const CAMPAIGN_MEMBER_FIELDS = [
  "Id", "CampaignId", "ContactId", "LeadId",
  "Status", "HasResponded", "FirstRespondedDate",
  "Campaign.Name", "Contact.Name", "Lead.Name",
  "CreatedDate", "LastModifiedDate", "LastModifiedBy.Name", "IsDeleted",
];

export type SObjectType =
  | "Account"
  | "Contact"
  | "Opportunity"
  | "Case"
  | "Lead"
  | "Task"
  | "Event"
  | "KnowledgeArticleVersion"
  | "Product2"
  | "PricebookEntry"
  | "Quote"
  | "QuoteLineItem"
  | "Campaign"
  | "CampaignMember";

const FIELD_MAP: Record<SObjectType, string[]> = {
  Account: ACCOUNT_FIELDS,
  Contact: CONTACT_FIELDS,
  Opportunity: OPPORTUNITY_FIELDS,
  Case: CASE_FIELDS,
  Lead: LEAD_FIELDS,
  Task: TASK_FIELDS,
  Event: EVENT_FIELDS,
  KnowledgeArticleVersion: KNOWLEDGE_ARTICLE_FIELDS,
  Product2: PRODUCT2_FIELDS,
  PricebookEntry: PRICEBOOK_ENTRY_FIELDS,
  Quote: QUOTE_FIELDS,
  QuoteLineItem: QUOTE_LINE_ITEM_FIELDS,
  Campaign: CAMPAIGN_FIELDS,
  CampaignMember: CAMPAIGN_MEMBER_FIELDS,
};

export function buildFullCrawlQuery(
  sobject: SObjectType,
  sinceDate?: string
): string {
  const fields = FIELD_MAP[sobject];
  const where = sinceDate
    ? `LastModifiedDate > ${sinceDate}`
    : undefined;

  return buildQuery({
    fields,
    sobject,
    where,
    orderBy: "LastModifiedDate ASC",
  });
}

export function buildDeletedQuery(
  sobject: SObjectType,
  sinceDate: string
): string {
  const fields = ["Id", "LastModifiedDate"];
  return buildQuery({
    fields,
    sobject,
    where: `IsDeleted = true AND LastModifiedDate > ${sinceDate}`,
  });
}

export function getFieldsForObject(sobject: SObjectType): string[] {
  return FIELD_MAP[sobject];
}
