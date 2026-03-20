// ── Transformer: Salesforce records → Graph External Items ──

import {
  Account,
  Contact,
  Opportunity,
  Case,
  Lead,
  Task,
  Event,
  Product2,
  PricebookEntry,
  Quote,
  QuoteLineItem,
  Campaign,
  CampaignMember,
  SalesforceRecord,
  ChatterFeedItem,
  KnowledgeArticle,
  ReportMetadata,
  DashboardMetadata,
  AnalyticsDataset,
  AnalyticsDashboard,
} from "../models/salesforceTypes";
import {
  ExternalItem,
  EVERYONE_ACL,
  ExternalItemContent,
} from "../models/graphTypes";
import { SObjectType } from "./soqlBuilder";
import { getConfig } from "../config/connectorConfig";
import { buildOwnerAcl } from "./userMapper";

function makeItemId(objectType: string, sfId: string): string {
  return `sf-${objectType.toLowerCase()}-${sfId}`;
}

function sfUrl(objectType: string, sfId: string): string {
  const config = getConfig().salesforce;
  return `${config.instanceUrl}/${sfId}`;
}

// ── Account ──

function transformAccount(record: Account): ExternalItem {
  const url = sfUrl("Account", record.Id);
  const parts: string[] = [record.Name];
  if (record.Industry) parts.push(`Industry: ${record.Industry}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Account", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title: record.Name,
      description: record.Description || "",
      url,
      objectType: "Account",
      salesforceId: record.Id,
      industry: record.Industry || "",
      website: record.Website || "",
      phone: record.Phone || "",
      billingCity: record.BillingCity || "",
      billingState: record.BillingState || "",
      billingCountry: record.BillingCountry || "",
      annualRevenue: record.AnnualRevenue || 0,
      numberOfEmployees: record.NumberOfEmployees || 0,
      accountType: record.Type || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Contact ──

function transformContact(record: Contact): ExternalItem {
  const name = record.Name || `${record.FirstName || ""} ${record.LastName}`.trim();
  const url = sfUrl("Contact", record.Id);
  const parts: string[] = [name];
  if (record.Title) parts.push(`Title: ${record.Title}`);
  if (record.Email) parts.push(`Email: ${record.Email}`);
  if (record.Department) parts.push(`Department: ${record.Department}`);

  return {
    id: makeItemId("Contact", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title: name,
      description: `${record.Title || ""} ${record.Department ? "at " + record.Department : ""}`.trim(),
      url,
      objectType: "Contact",
      salesforceId: record.Id,
      firstName: record.FirstName || "",
      lastName: record.LastName,
      email: record.Email || "",
      phone: record.Phone || "",
      contactTitle: record.Title || "",
      department: record.Department || "",
      accountId: record.AccountId || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Opportunity ──

function transformOpportunity(record: Opportunity): ExternalItem {
  const url = sfUrl("Opportunity", record.Id);
  const parts: string[] = [record.Name];
  if (record.StageName) parts.push(`Stage: ${record.StageName}`);
  if (record.Amount) parts.push(`Amount: $${record.Amount.toLocaleString()}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Opportunity", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title: record.Name,
      description: record.Description || "",
      url,
      objectType: "Opportunity",
      salesforceId: record.Id,
      stageName: record.StageName,
      amount: record.Amount || 0,
      closeDate: record.CloseDate,
      probability: record.Probability || 0,
      accountId: record.AccountId || "",
      opportunityType: record.Type || "",
      leadSource: record.LeadSource || "",
      isClosed: record.IsClosed || false,
      isWon: record.IsWon || false,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Case ──

function transformCase(record: Case): ExternalItem {
  const url = sfUrl("Case", record.Id);
  const title = record.Subject || `Case ${record.CaseNumber || record.Id}`;
  const parts: string[] = [title];
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.Priority) parts.push(`Priority: ${record.Priority}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Case", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title,
      description: record.Description || "",
      url,
      objectType: "Case",
      salesforceId: record.Id,
      caseNumber: record.CaseNumber || "",
      status: record.Status,
      priority: record.Priority || "",
      origin: record.Origin || "",
      caseType: record.Type || "",
      accountId: record.AccountId || "",
      contactId: record.ContactId || "",
      isClosed: record.IsClosed || false,
      closedDate: record.ClosedDate || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Lead ──

function transformLead(record: Lead): ExternalItem {
  const name = record.Name || `${record.FirstName || ""} ${record.LastName}`.trim();
  const url = sfUrl("Lead", record.Id);
  const parts: string[] = [name];
  if (record.Company) parts.push(`Company: ${record.Company}`);
  if (record.Title) parts.push(`Title: ${record.Title}`);
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Lead", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title: name,
      description: record.Description || "",
      url,
      objectType: "Lead",
      salesforceId: record.Id,
      firstName: record.FirstName || "",
      lastName: record.LastName,
      company: record.Company,
      email: record.Email || "",
      phone: record.Phone || "",
      leadTitle: record.Title || "",
      leadStatus: record.Status,
      industry: record.Industry || "",
      leadSource: record.LeadSource || "",
      rating: record.Rating || "",
      isConverted: record.IsConverted || false,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Task ──

function transformTask(record: Task): ExternalItem {
  const url = sfUrl("Task", record.Id);
  const title = record.Subject || `Task ${record.Id}`;
  const parts: string[] = [title];
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.Priority) parts.push(`Priority: ${record.Priority}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Task", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title,
      description: record.Description || "",
      url,
      objectType: "Task",
      salesforceId: record.Id,
      status: record.Status,
      priority: record.Priority || "",
      activityDate: record.ActivityDate || "",
      whoId: record.WhoId || "",
      whatId: record.WhatId || "",
      taskSubtype: record.TaskSubtype || "",
      isClosed: record.IsClosed || false,
      isHighPriority: record.IsHighPriority || false,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Event ──

function transformEvent(record: Event): ExternalItem {
  const url = sfUrl("Event", record.Id);
  const title = record.Subject || `Event ${record.Id}`;
  const parts: string[] = [title];
  if (record.Location) parts.push(`Location: ${record.Location}`);
  if (record.StartDateTime) parts.push(`Start: ${record.StartDateTime}`);
  if (record.EndDateTime) parts.push(`End: ${record.EndDateTime}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Event", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title,
      description: record.Description || "",
      url,
      objectType: "Event",
      salesforceId: record.Id,
      location: record.Location || "",
      startDateTime: record.StartDateTime || "",
      endDateTime: record.EndDateTime || "",
      durationInMinutes: record.DurationInMinutes || 0,
      whoId: record.WhoId || "",
      whatId: record.WhatId || "",
      eventSubtype: record.EventSubtype || "",
      isAllDayEvent: record.IsAllDayEvent || false,
      showAs: record.ShowAs || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Product2 ──

function transformProduct2(record: Product2): ExternalItem {
  const url = sfUrl("Product2", record.Id);
  const parts: string[] = [record.Name];
  if (record.ProductCode) parts.push(`Code: ${record.ProductCode}`);
  if (record.Family) parts.push(`Family: ${record.Family}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Product", record.Id),
    acl: EVERYONE_ACL,
    properties: {
      title: record.Name,
      description: record.Description || "",
      url,
      objectType: "Product",
      salesforceId: record.Id,
      productCode: record.ProductCode || "",
      productFamily: record.Family || "",
      isActive: record.IsActive || false,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── PricebookEntry ──

function transformPricebookEntry(record: PricebookEntry): ExternalItem {
  const url = sfUrl("PricebookEntry", record.Id);
  const productName = record.Product2?.Name || record.Name || "Pricebook Entry";
  const pricebookName = record.Pricebook2?.Name || "";
  const parts: string[] = [productName];
  if (pricebookName) parts.push(`Pricebook: ${pricebookName}`);
  if (record.UnitPrice !== undefined) parts.push(`Price: $${record.UnitPrice}`);

  return {
    id: makeItemId("PricebookEntry", record.Id),
    acl: EVERYONE_ACL,
    properties: {
      title: productName,
      description: `${productName} - ${pricebookName}`,
      url,
      objectType: "PricebookEntry",
      salesforceId: record.Id,
      productCode: record.ProductCode || "",
      unitPrice: record.UnitPrice || 0,
      isActive: record.IsActive || false,
      pricebookName: pricebookName,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Quote ──

function transformQuote(record: Quote): ExternalItem {
  const url = sfUrl("Quote", record.Id);
  const title = record.Name || `Quote ${record.QuoteNumber || record.Id}`;
  const parts: string[] = [title];
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.GrandTotal !== undefined) parts.push(`Grand Total: $${record.GrandTotal}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Quote", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title,
      description: record.Description || "",
      url,
      objectType: "Quote",
      salesforceId: record.Id,
      quoteNumber: record.QuoteNumber || "",
      quoteStatus: record.Status || "",
      totalPrice: record.TotalPrice || 0,
      grandTotal: record.GrandTotal || 0,
      discount: record.Discount || 0,
      expirationDate: record.ExpirationDate || "",
      opportunityId: record.OpportunityId || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── QuoteLineItem ──

function transformQuoteLineItem(record: QuoteLineItem): ExternalItem {
  const url = sfUrl("QuoteLineItem", record.Id);
  const productName = record.Product2?.Name || "Line Item";
  const title = `${productName} (Line ${record.LineNumber || record.Id})`;
  const parts: string[] = [title];
  if (record.Quantity) parts.push(`Qty: ${record.Quantity}`);
  if (record.UnitPrice !== undefined) parts.push(`Unit Price: $${record.UnitPrice}`);
  if (record.TotalPrice !== undefined) parts.push(`Total: $${record.TotalPrice}`);

  return {
    id: makeItemId("QuoteLineItem", record.Id),
    acl: EVERYONE_ACL,
    properties: {
      title,
      description: record.Description || "",
      url,
      objectType: "QuoteLineItem",
      salesforceId: record.Id,
      quoteId: record.QuoteId || "",
      quantity: record.Quantity || 0,
      unitPrice: record.UnitPrice || 0,
      totalPrice: record.TotalPrice || 0,
      discount: record.Discount || 0,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Campaign ──

function transformCampaign(record: Campaign): ExternalItem {
  const url = sfUrl("Campaign", record.Id);
  const parts: string[] = [record.Name];
  if (record.Type) parts.push(`Type: ${record.Type}`);
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.Description) parts.push(record.Description);

  return {
    id: makeItemId("Campaign", record.Id),
    acl: buildOwnerAcl(record.OwnerId),
    properties: {
      title: record.Name,
      description: record.Description || "",
      url,
      objectType: "Campaign",
      salesforceId: record.Id,
      campaignType: record.Type || "",
      campaignStatus: record.Status || "",
      startDate: record.StartDate || "",
      endDate: record.EndDate || "",
      budgetedCost: record.BudgetedCost || 0,
      actualCost: record.ActualCost || 0,
      expectedRevenue: record.ExpectedRevenue || 0,
      numberOfContacts: record.NumberOfContacts || 0,
      numberOfLeads: record.NumberOfLeads || 0,
      isActive: record.IsActive || false,
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── CampaignMember ──

function transformCampaignMember(record: CampaignMember): ExternalItem {
  const url = sfUrl("CampaignMember", record.Id);
  const campaignName = record.Campaign?.Name || "Campaign";
  const memberName = record.Contact?.Name || record.Lead?.Name || record.Id;
  const title = `${memberName} - ${campaignName}`;
  const parts: string[] = [title];
  if (record.Status) parts.push(`Status: ${record.Status}`);
  if (record.HasResponded) parts.push("Responded");

  return {
    id: makeItemId("CampaignMember", record.Id),
    acl: EVERYONE_ACL,
    properties: {
      title,
      description: `${memberName} membership in ${campaignName}`,
      url,
      objectType: "CampaignMember",
      salesforceId: record.Id,
      campaignId: record.CampaignId || "",
      memberStatus: record.Status || "",
      hasResponded: record.HasResponded || false,
      firstRespondedDate: record.FirstRespondedDate || "",
      lastModifiedDateTime: record.LastModifiedDate || "",
      lastModifiedBy: record.LastModifiedBy?.Name || "",
    },
    content: {
      value: parts.join("\n"),
      type: "text",
    },
  };
}

// ── Chatter Feed Item ──

export function transformChatterFeedItem(item: ChatterFeedItem): ExternalItem {
  const title = item.body.text.substring(0, 100) + (item.body.text.length > 100 ? "..." : "");
  return {
    id: `sf-chatter-${item.id}`,
    acl: EVERYONE_ACL,
    properties: {
      title,
      description: `Chatter post by ${item.actor.displayName}`,
      url: "",
      objectType: "ChatterPost",
      salesforceId: item.id,
      authorName: item.actor.displayName,
      authorId: item.actor.id,
      parentId: item.parent?.id || "",
      parentType: item.parent?.type || "",
      commentCount: item.commentCount || 0,
      likeCount: item.likeCount || 0,
      createdDate: item.createdDate,
      lastModifiedDateTime: item.modifiedDate || item.createdDate,
    },
    content: {
      value: item.body.text,
      type: "text",
    },
  };
}

// ── Report ──

export function transformReport(report: ReportMetadata): ExternalItem {
  return {
    id: `sf-report-${report.id}`,
    acl: EVERYONE_ACL,
    properties: {
      title: report.name,
      description: report.description || "",
      url: "",
      objectType: "Report",
      salesforceId: report.id,
      reportFormat: report.reportFormat,
      reportType: report.reportType.label,
      folderName: report.folderName || "",
      lastRunDate: report.lastRunDate || "",
      lastModifiedDateTime: report.lastModifiedDate || "",
    },
    content: {
      value: `${report.name}\nType: ${report.reportType.label}\nFormat: ${report.reportFormat}${report.description ? "\n" + report.description : ""}`,
      type: "text",
    },
  };
}

// ── Knowledge Article ──

export function transformKnowledgeArticle(article: KnowledgeArticle): ExternalItem {
  const config = getConfig().salesforce;
  return {
    id: `sf-knowledge-${article.Id}`,
    acl: EVERYONE_ACL,
    properties: {
      title: article.Title,
      description: article.Summary || "",
      url: article.UrlName
        ? `${config.instanceUrl}/articles/${article.UrlName}`
        : sfUrl("KnowledgeArticle", article.Id),
      objectType: "KnowledgeArticle",
      salesforceId: article.Id,
      articleNumber: article.ArticleNumber || "",
      publishStatus: article.PublishStatus || "",
      lastModifiedDateTime: article.LastModifiedDate || "",
    },
    content: {
      value: `${article.Title}\n${article.Summary || ""}`,
      type: "text",
    },
  };
}

// ── Analytics Dataset ──

export function transformAnalyticsDataset(dataset: AnalyticsDataset): ExternalItem {
  return {
    id: `sf-analytics-dataset-${dataset.id}`,
    acl: EVERYONE_ACL,
    properties: {
      title: dataset.label || dataset.name,
      description: dataset.description || "",
      url: "",
      objectType: "AnalyticsDataset",
      salesforceId: dataset.id,
      datasetType: dataset.datasetType || "",
      folderLabel: dataset.folderLabel || "",
      lastModifiedDateTime: dataset.lastModifiedDate || "",
    },
    content: {
      value: `${dataset.label || dataset.name}${dataset.description ? "\n" + dataset.description : ""}`,
      type: "text",
    },
  };
}

// ── Dashboard (Reports & Dashboards API) ──

export function transformDashboard(dashboard: DashboardMetadata): ExternalItem {
  return {
    id: `sf-dashboard-${dashboard.id}`,
    acl: EVERYONE_ACL,
    properties: {
      title: dashboard.name,
      description: dashboard.description || "",
      url: "",
      objectType: "Dashboard",
      salesforceId: dashboard.id,
      folderName: dashboard.folderName || "",
      lastModifiedDateTime: dashboard.lastModifiedDate || "",
    },
    content: {
      value: `${dashboard.name}${dashboard.description ? "\n" + dashboard.description : ""}`,
      type: "text",
    },
  };
}

// ── Analytics Dashboard (Wave/CRM Analytics) ──

export function transformAnalyticsDashboard(dashboard: AnalyticsDashboard): ExternalItem {
  return {
    id: `sf-analytics-dashboard-${dashboard.id}`,
    acl: EVERYONE_ACL,
    properties: {
      title: dashboard.label || dashboard.name,
      description: dashboard.description || "",
      url: "",
      objectType: "AnalyticsDashboard",
      salesforceId: dashboard.id,
      folderLabel: dashboard.folderLabel || "",
      lastModifiedDateTime: dashboard.lastModifiedDate || "",
    },
    content: {
      value: `${dashboard.label || dashboard.name}${dashboard.description ? "\n" + dashboard.description : ""}`,
      type: "text",
    },
  };
}

// ── Dispatcher ──

const CRM_TRANSFORMERS: Record<SObjectType, (record: SalesforceRecord) => ExternalItem> = {
  Account: (r) => transformAccount(r as Account),
  Contact: (r) => transformContact(r as Contact),
  Opportunity: (r) => transformOpportunity(r as Opportunity),
  Case: (r) => transformCase(r as Case),
  Lead: (r) => transformLead(r as Lead),
  Task: (r) => transformTask(r as Task),
  Event: (r) => transformEvent(r as Event),
  KnowledgeArticleVersion: (r) => transformKnowledgeArticle(r as KnowledgeArticle),
  Product2: (r) => transformProduct2(r as Product2),
  PricebookEntry: (r) => transformPricebookEntry(r as PricebookEntry),
  Quote: (r) => transformQuote(r as Quote),
  QuoteLineItem: (r) => transformQuoteLineItem(r as QuoteLineItem),
  Campaign: (r) => transformCampaign(r as Campaign),
  CampaignMember: (r) => transformCampaignMember(r as CampaignMember),
};

export function transformRecord(
  objectType: SObjectType,
  record: SalesforceRecord
): ExternalItem {
  const transformer = CRM_TRANSFORMERS[objectType];
  if (!transformer) {
    throw new Error(`No transformer for object type: ${objectType}`);
  }
  return transformer(record);
}
