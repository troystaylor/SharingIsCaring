// ── Core CRM Objects ──

export interface SalesforceRecord {
  Id: string;
  attributes?: {
    type: string;
    url: string;
  };
  CreatedDate?: string;
  LastModifiedDate?: string;
  CreatedById?: string;
  LastModifiedById?: string;
  LastModifiedBy?: { Name?: string };
  IsDeleted?: boolean;
}

export interface Account extends SalesforceRecord {
  Name: string;
  Description?: string;
  Industry?: string;
  Website?: string;
  Phone?: string;
  BillingCity?: string;
  BillingState?: string;
  BillingCountry?: string;
  AnnualRevenue?: number;
  NumberOfEmployees?: number;
  Type?: string;
  OwnerId?: string;
}

export interface Contact extends SalesforceRecord {
  FirstName?: string;
  LastName: string;
  Name?: string;
  Email?: string;
  Phone?: string;
  Title?: string;
  Department?: string;
  AccountId?: string;
  MailingCity?: string;
  MailingState?: string;
  MailingCountry?: string;
  OwnerId?: string;
}

export interface Opportunity extends SalesforceRecord {
  Name: string;
  Description?: string;
  StageName: string;
  Amount?: number;
  CloseDate: string;
  Probability?: number;
  AccountId?: string;
  OwnerId?: string;
  Type?: string;
  LeadSource?: string;
  IsClosed?: boolean;
  IsWon?: boolean;
}

export interface Case extends SalesforceRecord {
  CaseNumber?: string;
  Subject?: string;
  Description?: string;
  Status: string;
  Priority?: string;
  Origin?: string;
  Type?: string;
  AccountId?: string;
  ContactId?: string;
  OwnerId?: string;
  IsClosed?: boolean;
  ClosedDate?: string;
}

export interface Lead extends SalesforceRecord {
  FirstName?: string;
  LastName: string;
  Name?: string;
  Company: string;
  Email?: string;
  Phone?: string;
  Title?: string;
  Status: string;
  Industry?: string;
  LeadSource?: string;
  Rating?: string;
  Description?: string;
  OwnerId?: string;
  IsConverted?: boolean;
  ConvertedAccountId?: string;
  ConvertedContactId?: string;
  ConvertedOpportunityId?: string;
}

// ── Reports & Dashboards (#6) ──

export interface ReportMetadata {
  id: string;
  name: string;
  description?: string;
  reportFormat: string;
  reportType: {
    type: string;
    label: string;
  };
  folderId?: string;
  folderName?: string;
  lastModifiedDate?: string;
  createdDate?: string;
  lastRunDate?: string;
}

export interface DashboardMetadata {
  id: string;
  name: string;
  description?: string;
  folderId?: string;
  folderName?: string;
  lastModifiedDate?: string;
  createdDate?: string;
}

// ── Phase 2: Task & Event ──

export interface Task extends SalesforceRecord {
  Subject?: string;
  Description?: string;
  Status: string;
  Priority?: string;
  ActivityDate?: string;
  WhoId?: string;
  WhatId?: string;
  OwnerId?: string;
  TaskSubtype?: string;
  IsClosed?: boolean;
  IsHighPriority?: boolean;
  CompletedDateTime?: string;
}

export interface Event extends SalesforceRecord {
  Subject?: string;
  Description?: string;
  Location?: string;
  StartDateTime?: string;
  EndDateTime?: string;
  DurationInMinutes?: number;
  WhoId?: string;
  WhatId?: string;
  OwnerId?: string;
  EventSubtype?: string;
  IsAllDayEvent?: boolean;
  ShowAs?: string;
}

// ── Phase 2: Product ──

export interface Product2 extends SalesforceRecord {
  Name: string;
  Description?: string;
  ProductCode?: string;
  Family?: string;
  IsActive?: boolean;
  ExternalId?: string;
}

export interface PricebookEntry extends SalesforceRecord {
  Name?: string;
  Pricebook2Id?: string;
  Product2Id?: string;
  UnitPrice?: number;
  IsActive?: boolean;
  UseStandardPrice?: boolean;
  ProductCode?: string;
  Product2?: { Name?: string };
  Pricebook2?: { Name?: string };
}

// ── Phase 3: Quote ──

export interface Quote extends SalesforceRecord {
  Name?: string;
  QuoteNumber?: string;
  Status?: string;
  Description?: string;
  OpportunityId?: string;
  AccountId?: string;
  ContactId?: string;
  TotalPrice?: number;
  GrandTotal?: number;
  Discount?: number;
  ExpirationDate?: string;
  OwnerId?: string;
}

export interface QuoteLineItem extends SalesforceRecord {
  QuoteId?: string;
  Product2Id?: string;
  Quantity?: number;
  UnitPrice?: number;
  TotalPrice?: number;
  Discount?: number;
  Description?: string;
  LineNumber?: string;
  Product2?: { Name?: string };
}

// ── Phase 3: Campaign ──

export interface Campaign extends SalesforceRecord {
  Name: string;
  Description?: string;
  Type?: string;
  Status?: string;
  StartDate?: string;
  EndDate?: string;
  BudgetedCost?: number;
  ActualCost?: number;
  ExpectedRevenue?: number;
  NumberOfContacts?: number;
  NumberOfLeads?: number;
  NumberSent?: number;
  IsActive?: boolean;
  OwnerId?: string;
  ParentId?: string;
}

export interface CampaignMember extends SalesforceRecord {
  CampaignId?: string;
  ContactId?: string;
  LeadId?: string;
  Status?: string;
  HasResponded?: boolean;
  FirstRespondedDate?: string;
  Campaign?: { Name?: string };
  Contact?: { Name?: string };
  Lead?: { Name?: string };
}

// ── Chatter / Connect (#8) ──

export interface ChatterFeedItem {
  id: string;
  type: string;
  body: {
    text: string;
    messageSegments?: Array<{
      type: string;
      text?: string;
    }>;
  };
  actor: {
    id: string;
    displayName: string;
    type: string;
  };
  parent?: {
    id: string;
    type: string;
    name?: string;
  };
  createdDate: string;
  modifiedDate?: string;
  commentCount?: number;
  likeCount?: number;
}

// ── CRM Analytics (#9) ──

export interface AnalyticsDataset {
  id: string;
  name: string;
  label: string;
  description?: string;
  createdDate?: string;
  lastModifiedDate?: string;
  datasetType?: string;
  folderId?: string;
  folderLabel?: string;
}

export interface AnalyticsDashboard {
  id: string;
  name: string;
  label: string;
  description?: string;
  createdDate?: string;
  lastModifiedDate?: string;
  folderId?: string;
  folderLabel?: string;
}

// ── Knowledge Articles (#11) ──

export interface KnowledgeArticle extends SalesforceRecord {
  Title: string;
  Summary?: string;
  ArticleNumber?: string;
  UrlName?: string;
  PublishStatus?: string;
  KnowledgeArticleId?: string;
  ArticleType?: string;
  [key: string]: unknown;
}

// ── Metadata (#10) ──

export interface SObjectDescribe {
  name: string;
  label: string;
  labelPlural: string;
  custom: boolean;
  queryable: boolean;
  searchable: boolean;
  createable: boolean;
  updateable: boolean;
  keyPrefix?: string;
  fields?: SObjectField[];
}

export interface SObjectField {
  name: string;
  label: string;
  type: string;
  length?: number;
  custom: boolean;
  nillable: boolean;
  filterable: boolean;
  sortable: boolean;
  referenceTo?: string[];
  relationshipName?: string;
}

// ── API Response Wrappers ──

export interface QueryResult<T extends SalesforceRecord> {
  totalSize: number;
  done: boolean;
  nextRecordsUrl?: string;
  records: T[];
}

export interface BulkJobInfo {
  id: string;
  operation: string;
  object: string;
  state:
    | "UploadComplete"
    | "InProgress"
    | "Aborted"
    | "JobComplete"
    | "Failed";
  numberRecordsProcessed?: number;
  numberRecordsFailed?: number;
}

// ── Change Data Capture (#4, #5) ──

export interface ChangeEvent {
  schema: string;
  payload: {
    ChangeEventHeader: {
      entityName: string;
      recordIds: string[];
      changeType: "CREATE" | "UPDATE" | "DELETE" | "UNDELETE";
      changeOrigin: string;
      transactionKey: string;
      sequenceNumber: number;
      commitTimestamp: number;
      commitNumber: number;
      commitUser: string;
      changedFields: string[];
    };
    [field: string]: unknown;
  };
  event: {
    replayId: number;
  };
}
