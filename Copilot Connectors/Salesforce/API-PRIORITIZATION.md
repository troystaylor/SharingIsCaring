# Salesforce API Prioritization for Copilot Connector

Analysis of all Salesforce APIs at https://developer.salesforce.com/docs/apis, prioritized for building a Microsoft 365 Copilot connector that ingests Salesforce data into Microsoft Graph.

## Prioritization Criteria

- **Relevance to Copilot**: How useful is the data for Copilot to reason over?
- **Coverage**: How many Salesforce customers use this API/data?
- **Connector fit**: How well does the data map to Microsoft Graph external items?
- **Implementation complexity**: How hard is it to integrate?

---

## P0 — Critical (Implement First)

### 1. REST API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.api_rest.meta/api_rest/intro_what_is_rest_api.htm
- **Purpose**: Primary API for CRUD operations on all Salesforce objects
- **Why P0**: This is the backbone. Provides access to Accounts, Contacts, Opportunities, Cases, Leads, Tasks, Events, and all custom objects. Every Salesforce org uses these objects.
- **Connector use**: Full crawl — query objects via REST, transform to external items, ingest into Graph
- **Key endpoints**:
  - `GET /services/data/vXX.0/sobjects/` — list available objects
  - `GET /services/data/vXX.0/sobjects/{Object}/describe` — object metadata
  - `GET /services/data/vXX.0/query/?q={SOQL}` — execute SOQL queries
  - `GET /services/data/vXX.0/sobjects/{Object}/{id}` — get individual record
- **Auth**: OAuth 2.0 (Connected App)
- **Rate limits**: Varies by edition, typically 100,000 API calls/24hr for Enterprise

### 2. SOQL and SOSL
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.soql_sosl.meta/soql_sosl/sforce_api_calls_soql_sosl_intro.htm
- **Purpose**: Query language (SOQL) and search language (SOSL) for Salesforce data
- **Why P0**: Used through REST API to efficiently query records for ingestion. SOQL provides structured queries; SOSL provides full-text search across objects.
- **Connector use**: Build SOQL queries for full and incremental crawls (e.g., `SELECT Id, Name, ... FROM Account WHERE LastModifiedDate > {timestamp}`)
- **Key patterns**:
  - `SELECT Id, Name, Description FROM Account WHERE LastModifiedDate >= YESTERDAY`
  - `SELECT Id, Subject, Description, Status FROM Case WHERE IsClosed = false`
  - Relationship queries for parent-child traversal

---

## P1 — High Priority (Implement for Production)

### 3. Bulk API 2.0
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.api_bulk_v2.meta/api_bulk_v2/bulk_ingest.htm
- **Purpose**: Load and query large volumes of data asynchronously
- **Why P1**: Essential for initial full crawl of large orgs. REST API has per-query row limits (2,000 default). Bulk API handles millions of records efficiently.
- **Connector use**: Initial full crawl for orgs with >10,000 records per object
- **Key advantages**:
  - Asynchronous processing
  - CSV-based, efficient for large datasets
  - Lower API call consumption vs. REST pagination
- **When to use**: Full crawl of orgs with large data volumes

### 4. Pub/Sub API (gRPC)
- **Docs**: https://developer.salesforce.com/docs/platform/pub-sub-api/overview
- **Purpose**: Publish and subscribe to platform events, Change Data Capture events
- **Why P1**: Enables real-time incremental crawl. Subscribe to Change Data Capture (CDC) events for objects being indexed. When a record changes in Salesforce, the connector receives the event and updates the corresponding Graph external item.
- **Connector use**: Incremental crawl — subscribe to CDC events for Accounts, Contacts, Opportunities, Cases
- **Key features**:
  - gRPC-based (modern, efficient)
  - Supports Change Data Capture for all standard and custom objects
  - Replay capability for catching up on missed events
- **Alternative**: Streaming API (older, CometD-based) — use Pub/Sub API for new implementations

### 5. Streaming API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.api_streaming.meta/api_streaming/intro_stream.htm
- **Purpose**: Subscribe to PushTopic events, generic events, platform events, and Change Data Capture
- **Why P1**: Fallback for incremental crawl if Pub/Sub API (gRPC) isn't feasible. CometD/Bayeux protocol.
- **Note**: Pub/Sub API is the preferred modern alternative. Include Streaming API as a fallback.

### 6. Reports and Dashboards REST API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.api_analytics.meta/api_analytics/sforce_analytics_rest_api_intro.htm
- **Purpose**: Access report definitions, run reports, get dashboard data
- **Why P1**: Making Salesforce reports searchable in Copilot is high value. Users frequently ask questions that reports answer.
- **Connector use**: Ingest report metadata, descriptions, and optionally summary data as external items

---

## P2 — Medium Priority (Enhance After Core)

### 7. GraphQL API
- **Docs**: https://developer.salesforce.com/docs/platform/graphql/overview
- **Purpose**: Query multiple objects and aggregate data in a single request
- **Why P2**: More efficient than REST for complex queries spanning multiple objects. Useful for optimizing crawl performance.
- **Connector use**: Optimize data retrieval by fetching related records in fewer API calls
- **Consideration**: Newer API, may not be available in all orgs

### 8. Connect REST API (Chatter)
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.chatterapi.meta/chatterapi/intro_what_is_chatter_connect.htm
- **Purpose**: Access Chatter feeds, groups, posts, comments, files
- **Why P2**: Chatter contains valuable tribal knowledge — internal discussions about deals, cases, customers
- **Connector use**: Ingest Chatter posts/comments as searchable items linked to their parent records

### 9. CRM Analytics REST API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.bi_dev_guide_rest.meta/bi_dev_guide_rest/bi_rest_overview.htm
- **Purpose**: Access datasets, dashboards, and lenses in CRM Analytics (Tableau CRM)
- **Why P2**: For orgs using CRM Analytics, this surfaces analytical insights

### 10. Metadata API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.api_meta.meta/api_meta/meta_types_list.htm
- **Purpose**: Retrieve org customization metadata (custom objects, fields, page layouts)
- **Why P2**: Useful for auto-discovery of custom objects to index. Can drive dynamic schema generation.
- **Connector use**: Discover what custom objects/fields exist, generate connector schema dynamically

---

## P3 — Future / Specialized (Based on Customer Need)

### 11. Knowledge / Articles
- Accessible via REST API (KnowledgeArticleVersion object)
- High value for service-oriented orgs — FAQ, troubleshooting guides

### 12. Field Service REST API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.field_service_dev.meta/field_service_dev/fsl_dev_introduction.htm
- For orgs using Field Service Lightning

### 13. CPQ API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.cpq_dev_api.meta/cpq_dev_api/cpq_api_get_started.htm
- For orgs using Salesforce CPQ (Configure, Price, Quote)

### 14. Einstein Discovery REST API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.bi_dev_guide_rest_sdd.meta/bi_dev_guide_rest_sdd/bi_rest_sdd_overview.htm
- AI predictions and insights

### 15. Loyalty Management API
- **Docs**: https://developer.salesforce.com/docs/atlas.en-us.loyalty.meta/loyalty/loyalty_api_overview.htm
- For retail/loyalty use cases

### 16. Subscription Management API
- **Docs**: https://developer.salesforce.com/docs/revenue/subscription-management/overview
- For subscription-based businesses

---

## Not Recommended for Copilot Connector

These APIs are either too specialized, not data-oriented, or not suitable for indexing:

| API | Reason to Skip |
|-----|---------------|
| SOAP API | REST API covers same functionality, REST is simpler |
| Tooling API | Developer tooling, not business data |
| User Interface API | UI metadata, not content |
| Lightning Console API | UI extension API |
| Heroku Platform API | Infrastructure, not business data |
| Quip APIs | Separate product; consider separate connector |
| Models API (Einstein GenAI) | LLM access, not data to index |
| Einstein Bot API | Bot management, not data |
| Einstein Vision/Language | ML inference, not data |
| Marketing Cloud APIs | Separate platform; consider separate connector |
| B2C/B2B Commerce APIs | Separate platform; consider separate connector |
| Industry-specific APIs | Only if customer is in that industry (Healthcare, Financial Services, Energy, etc.) |

---

## Recommended Implementation Phases

### Phase 1: Core CRM Data (MVP)
- REST API + SOQL for full crawl
- Objects: Account, Contact, Opportunity, Case, Lead
- Manual trigger for full crawl
- Schema: Map Salesforce fields to Graph external item properties

### Phase 2: Incremental Sync
- Pub/Sub API for Change Data Capture
- Automatic updates when records change in Salesforce
- Bulk API 2.0 for scheduled full re-crawl

### Phase 3: Extended Data
- Reports & Dashboards metadata
- Chatter posts/comments (Connect REST API)
- Knowledge Articles
- Custom objects (auto-discovery via Metadata API)

### Phase 4: Industry Add-ons
- CPQ, Field Service, Financial Services, etc. based on customer need
