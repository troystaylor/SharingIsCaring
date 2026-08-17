# Salesforce CRM Object Reference

Standard field API names and relationships for the objects these skills use.
Custom orgs vary — when a field is not listed here, or a query returns
`INVALID_FIELD`, call `getObjectSchema` with the `object-name` parameter to get
the org's real schema before retrying.

## Tools available on the connector

The plugin connects to the Salesforce-hosted `platform/sobject-all` server.

| Tool | Purpose | Parameters |
|---|---|---|
| `getObjectSchema` | Object list (no params) or full field schema for one object | `object-name` (optional) |
| `soqlQuery` | Run a SOQL query | `query` |
| `find` | Run a SOSL text search across objects | `search` |
| `getUserInfo` | Identity of the signed-in Salesforce user | none |
| `listRecentSobjectRecords` | Recently viewed/modified records of a type | `sobject-name` |
| `getRelatedRecords` | Child records of a parent via a relationship path | `sobject-name`, `id`, `relationship-path` |
| `createSobjectRecord` | Create a record | `sobject-name`, `body` |
| `updateSobjectRecord` | Update a record by Id | `sobject-name`, `id`, `body` |
| `updateRelatedSobjectRecord` | Update a child record via relationship | `sobject-name`, `id`, `relationship-path`, `body` |
| `deleteSobjectRecord` | Delete a record by Id | `sobject-name`, `id` |
| `deleteRelatedSobjectRecord` | Delete a child record via relationship | `sobject-name`, `id`, `relationship-path` |

## Id prefixes

| Prefix | Object |
|---|---|
| `001` | Account |
| `003` | Contact |
| `005` | User |
| `006` | Opportunity |
| `00Q` | Lead |
| `00T` | Task |
| `00U` | Event |
| `500` | Case |

Use the prefix to sanity-check that an Id belongs to the object you are about
to write to.

## Account

Key fields: `Id`, `Name`, `Type`, `Industry`, `AnnualRevenue`,
`NumberOfEmployees`, `Website`, `Phone`, `Description`, `OwnerId`, `Owner.Name`,
`ParentId`, `Rating`, `AccountSource`, `LastActivityDate`, `LastModifiedDate`.

Address fields: `BillingStreet`, `BillingCity`, `BillingState`,
`BillingPostalCode`, `BillingCountry`, and the matching `Shipping*` set.

Child relationships: `Opportunities`, `Contacts`, `Cases`, `Tasks`, `Notes`.

Required on create: `Name` only (unless the org adds validation rules).

## Opportunity

Key fields: `Id`, `Name`, `AccountId`, `Account.Name`, `StageName`, `Amount`,
`CloseDate`, `Probability`, `Type`, `LeadSource`, `NextStep`, `Description`,
`ForecastCategoryName`, `IsClosed`, `IsWon`, `OwnerId`, `Owner.Name`,
`CampaignId`, `CreatedDate`, `LastModifiedDate`, `LastActivityDate`.

Default `StageName` picklist values (orgs commonly customize these — confirm
with `getObjectSchema`): `Prospecting`, `Qualification`, `Needs Analysis`,
`Value Proposition`, `Id. Decision Makers`, `Perception Analysis`,
`Proposal/Price Quote`, `Negotiation/Review`, `Closed Won`, `Closed Lost`.

`ForecastCategoryName` values: `Omitted`, `Pipeline`, `Best Case`, `Commit`,
`Closed`.

Required on create: `Name`, `StageName`, `CloseDate`. `AccountId` is not
technically required but should always be set.

Note: `IsClosed`, `IsWon`, and `Probability` are derived from `StageName` — set
`StageName` and let Salesforce compute them. Do not write `IsClosed`/`IsWon`.

## Contact

Key fields: `Id`, `FirstName`, `LastName`, `Name`, `AccountId`, `Account.Name`,
`Title`, `Email`, `Phone`, `MobilePhone`, `Department`, `MailingCity`,
`MailingState`, `MailingCountry`, `ReportsToId`, `LeadSource`, `OwnerId`,
`Description`, `LastActivityDate`.

Required on create: `LastName`. `Name` is read-only and derived — write
`FirstName` and `LastName` instead.

## Lead

Key fields: `Id`, `FirstName`, `LastName`, `Name`, `Company`, `Title`, `Email`,
`Phone`, `Status`, `LeadSource`, `Rating`, `Industry`, `AnnualRevenue`,
`NumberOfEmployees`, `IsConverted`, `ConvertedAccountId`, `ConvertedContactId`,
`ConvertedOpportunityId`, `OwnerId`, `Owner.Name`, `CreatedDate`,
`LastActivityDate`.

Default `Status` values: `Open - Not Contacted`, `Working - Contacted`,
`Closed - Converted`, `Closed - Not Converted`.

Required on create: `LastName`, `Company`.

Lead conversion is a dedicated Salesforce API call and is **not** available
through the standard hosted sObject MCP tools. To convert, direct the user to
the Lead record in Salesforce, or have an admin expose a conversion Flow, Apex
invocable action, or `@AuraEnabled` method on a custom MCP server. Check
`tools/list` before assuming it is missing.

## Case

Key fields: `Id`, `CaseNumber`, `Subject`, `Description`, `Status`, `Priority`,
`Origin`, `Type`, `Reason`, `AccountId`, `ContactId`, `OwnerId`, `Owner.Name`,
`IsClosed`, `IsEscalated`, `CreatedDate`, `ClosedDate`, `LastModifiedDate`.

Default `Status` values: `New`, `Working`, `Escalated`, `Closed`.
Default `Priority` values: `High`, `Medium`, `Low`.

`CaseNumber` is auto-generated and read-only.

## Task (activities and follow-ups)

Key fields: `Id`, `Subject`, `Description`, `ActivityDate` (the due date),
`Status`, `Priority`, `WhoId` (Contact or Lead), `WhatId` (Account,
Opportunity, Case, or other related record), `AccountId` (read-only, derived),
`OwnerId`, `Owner.Name`, `IsClosed`, `TaskSubtype`, `CallDurationInSeconds`,
`CallDisposition`, `CreatedDate`.

Default `Status` values: `Not Started`, `In Progress`, `Completed`,
`Waiting on someone else`, `Deferred`.
Default `Priority` values: `High`, `Normal`, `Low`.

`TaskSubtype` values include `Task`, `Email`, `Call`, `ListEmail`. Set it to
`Call` when logging a phone call.

Required on create: `Subject` in practice; `Status` and `Priority` default.

`AccountId` on Task is populated automatically from `WhatId`/`WhoId` — filter
on it, but do not write to it.

## Event (meetings)

Key fields: `Id`, `Subject`, `Description`, `StartDateTime`, `EndDateTime`,
`DurationInMinutes`, `IsAllDayEvent`, `Location`, `WhoId`, `WhatId`, `OwnerId`.

Required on create: `StartDateTime` plus either `EndDateTime` or
`DurationInMinutes`. DateTime values are ISO-8601 UTC, e.g.
`2026-08-17T18:00:00Z`.

## OpportunityContactRole

Key fields: `Id`, `OpportunityId`, `ContactId`, `Role`, `IsPrimary`.
Use this to record buying-committee roles on a deal.

## SOQL date literals

`TODAY`, `YESTERDAY`, `TOMORROW`, `THIS_WEEK`, `LAST_WEEK`, `THIS_MONTH`,
`LAST_MONTH`, `THIS_QUARTER`, `LAST_QUARTER`, `NEXT_QUARTER`,
`THIS_FISCAL_QUARTER`, `NEXT_FISCAL_QUARTER`, `THIS_YEAR`,
`LAST_N_DAYS:n`, `NEXT_N_DAYS:n`, `LAST_N_MONTHS:n`, `NEXT_N_MONTHS:n`.

Date literals are unquoted: `CloseDate = THIS_QUARTER`, not `'THIS_QUARTER'`.
Explicit dates are also unquoted: `CloseDate < 2026-09-30`.
