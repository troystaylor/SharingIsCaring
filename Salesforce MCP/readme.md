# Salesforce MCP Connector

Comprehensive Power Platform custom connector for Salesforce REST API v66.0. Provides full access to Core REST API (SOQL, sObject CRUD, Search), Composite API, Reports & Dashboards, Connect/Chatter APIs, Knowledge Articles, Search Suggestions, and Synonym Groups via Tooling API.

**Features:**
- **50+ operations** covering all major Salesforce REST APIs including Knowledge and Tooling
- **Dynamic sObject Schema** - Body fields auto-populate based on selected object
- **MCP-enabled** for Copilot Studio integration with AI agents
- **Application Insights** telemetry for monitoring and debugging

## Prerequisites

### Salesforce Requirements

1. **Salesforce Edition** with API access (Enterprise, Unlimited, Developer, or Performance)
2. **My Domain** enabled (required for OAuth)
3. **API Enabled** permission for the connecting user

### Connected App Setup

This connector is validated with a provider-based OAuth configuration in `apiProperties.json`:
- `identityProvider`: `SalesforceV2`
- `customParameters.LoginUri`: `https://login.salesforce.com`
- `redirectMode`: `GlobalPerConnector`

> **Note:** As of Spring '26, Salesforce recommends External Client Apps over Connected Apps for new integrations.

#### Option 1: External Client App (Recommended)

1. Navigate to **Setup > Platform Tools > Apps > External Client App Manager**
2. Click **New External Client App**
3. Configure:
   - **Name**: Power Platform Connector
   - **Contact Email**: Your email
   - **Enable OAuth Settings**: Checked
   - **Callback URL**: Add the Power Platform redirect URL shown by your connector connection experience. For this connector family, a working example is:
     - `https://global.consent.azure-apim.net/redirect/new-5fsalesforce-abc`
   - **Selected OAuth Scopes**:
     - `Manage user data via APIs (api)`
     - `Perform requests at any time (refresh_token, offline_access)`
4. Save and copy the **Consumer Key** and **Consumer Secret**
5. Navigate to **Manage > OAuth Policies**:
   - **Refresh Token Policy**: Set to **"Refresh token is valid until revoked"**
   - **Permitted Users**: "All users may self-authorize" (or admin pre-authorized)

#### Option 2: Connected App (Legacy)

1. Navigate to **Setup > Platform Tools > Apps > App Manager**
2. Click **New Connected App**
3. Configure:
   - **Connected App Name**: Power Platform Connector
   - **API Name**: Power_Platform_Connector
   - **Contact Email**: Your email
   - **Enable OAuth Settings**: Checked
   - **Callback URL**: Add the Power Platform redirect URL shown by your connector connection experience. For this connector family, a working example is:
     - `https://global.consent.azure-apim.net/redirect/new-5fsalesforce-5f74dd22e1a08b664f`
   - **Selected OAuth Scopes**:
     - `Manage user data via APIs (api)`
     - `Perform requests at any time (refresh_token, offline_access)`
4. Save and wait 2-10 minutes for propagation
5. Copy the **Consumer Key** and **Consumer Secret**
6. Navigate to **Manage > OAuth Policies**:
   - **Refresh Token Policy**: Set to **"Refresh token is valid until revoked"**
   - **Permitted Users**: "All users may self-authorize" (or admin pre-authorized)

## Installation

### Using PAC CLI

```powershell
# Authenticate to your Power Platform environment
pac auth create --environment "https://yourorg.crm.dynamics.com"

# List existing connectors
pac connector list

# Create the connector
pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json

# Or update an existing connector
pac connector update --connector-id <CONNECTOR_ID> --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json
```

> Note: For this connector, use file arguments directly. Do not pass `--settings-file` unless the settings file is in PAC's expected GUID-based format.

### Manual Upload

1. Go to [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Data > Custom Connectors**
3. Click **+ New custom connector > Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json`
5. Configure connection settings with your OAuth credentials

## Configuration

Use the following OAuth properties pattern (validated in Power Platform Demo):

1. In `apiProperties.json` set:
  - `connectionParameters.token.type` = `oauthSetting`
  - `connectionParameters.token.oAuthSettings.identityProvider` = `SalesforceV2`
  - `connectionParameters.token.oAuthSettings.customParameters.LoginUri.value` = `https://login.salesforce.com`
  - `connectionParameters.token.oAuthSettings.redirectMode` = `GlobalPerConnector`
2. In `apiDefinition.swagger.json` set OAuth security endpoints to:
  - `authorizationUrl`: `https://login.salesforce.com/services/oauth2/authorize`
  - `tokenUrl`: `https://login.salesforce.com/services/oauth2/token`
3. Ensure scopes are aligned across both files (`api`, `refresh_token`).

In validation testing, OAuth sign-in launched Salesforce successfully even with a placeholder `clientId` and no explicit `clientSecret` in `apiProperties.json`. This suggests `SalesforceV2` may use provider-managed auth behavior. For production, still prefer a real Salesforce app registration and verify behavior in your target tenant.

### Security Tab Behavior

When using `SalesforceV2`, the custom connector **Security** tab can display **No Authentication** even though OAuth works at runtime in the **Test** tab. Treat PAC-deployed `apiProperties.json` as source of truth for this provider pattern.

## API Coverage

This connector includes **50+ operations** across 8 Salesforce API categories:

### Query API (3 operations)
| Operation | Description |
|-----------|-------------|
| `Query` | Execute SOQL query |
| `QueryMore` | Get next batch of query results |
| `Search` | Execute SOSL full-text search |

### Records API (5 operations)
| Operation | Description |
|-----------|-------------|
| `CreateRecord` | Create new record (dynamic fields) |
| `GetRecord` | Get record by ID |
| `UpdateRecord` | Update record fields (dynamic fields) |
| `DeleteRecord` | Delete a record |
| `GetObjectSchema` | Get JSON Schema for sObject (internal) |

### Metadata API (3 operations)
| Operation | Description |
|-----------|-------------|
| `DescribeGlobal` | List all sObjects |
| `DescribeObject` | Get object field metadata |
| `GetLimits` | Check API limits |

### Composite API (4 operations)
| Operation | Description |
|-----------|-------------|
| `Composite` | Execute multiple operations with references |
| `CompositeBatch` | Execute up to 25 independent requests |
| `CompositeTree` | Create parent-child records |
| `CompositeGraph` | Complex multi-graph operations |

### Analytics API (6 operations)
| Operation | Description |
|-----------|-------------|
| `ListReports` | List available reports |
| `GetReport` | Get report metadata |
| `RunReport` | Execute report with optional filters |
| `ListDashboards` | List available dashboards |
| `GetDashboard` | Get dashboard data |
| `RefreshDashboard` | Refresh dashboard components |

### Chatter API (11 operations)
| Operation | Description |
|-----------|-------------|
| `GetFeed` | Get Chatter feed elements |
| `PostFeedElement` | Post to Chatter |
| `GetFeedElement` | Get feed element by ID |
| `LikeFeedElement` | Like a feed element |
| `PostComment` | Add comment to feed element |
| `GetChatterUser` | Get user profile |
| `ListChatterUsers` | List/search users |
| `ListChatterGroups` | List/search groups |
| `GetChatterGroup` | Get group details |
| `JoinChatterGroup` | Join a group |
| `ListTopics` | List topics |

### Knowledge API (7 operations)

> **Note:** The Salesforce Knowledge Support API requires an `Accept-Language` HTTP header (e.g., `en-US`). The connector script handles this automatically using the `DEFAULT_LANGUAGE` constant defined at the top of `script.csx`. Change this value if your org uses a different language.

| Operation | Description |
|-----------|-------------|
| `ListKnowledgeArticles` | List Knowledge articles with search and pagination |
| `GetKnowledgeArticle` | Get full article content and metadata |
| `CreateKnowledgeArticle` | Create a new Knowledge article draft |
| `UpdateKnowledgeArticle` | Update a Knowledge article |
| `DeleteKnowledgeArticle` | Delete a Knowledge article |
| `SuggestSearchQueries` | Get search suggestions based on user query |
| `SuggestArticleTitles` | Get article title matches for search query |

### Synonyms API - Tooling (5 operations)
| Operation | Description |
|-----------|-------------|
| `ToolingQuery` | Execute SOQL query against Tooling API |
| `GetSynonymGroup` | Get synonym group details |
| `CreateSynonymGroup` | Create a search synonym group |
| `UpdateSynonymGroup` | Update a synonym group |
| `DeleteSynonymGroup` | Delete a synonym group |

## Dynamic sObject Schema

The connector includes dynamic schema support for record operations. When you select an sObject (e.g., Account, Contact), the body input fields automatically populate with the actual fields from that object.

**How it works:**
1. Select an sObject (e.g., `Account`, `Lead`, `Opportunity`)
2. The connector calls Salesforce's describe endpoint
3. Body parameters show object-specific fields with proper labels and types

**Supported operations:**
- CreateRecord
- UpdateRecord

## Example Usage

### Execute SOQL Query

```
GET /query?q=SELECT Id, Name, Industry FROM Account WHERE Industry = 'Technology' LIMIT 10
```

### Create an Account

```json
POST /sobjects/Account
{
  "Name": "Acme Corporation",
  "Industry": "Technology",
  "Website": "https://acme.com",
  "Phone": "555-1234"
}
```

### Update a Contact

```json
PATCH /sobjects/Contact/003XXXXXXXXXXXX
{
  "Phone": "555-5678",
  "Title": "VP of Sales"
}
```

### Execute Composite Request

```json
POST /composite
{
  "allOrNone": true,
  "compositeRequest": [
    {
      "method": "POST",
      "url": "/services/data/v66.0/sobjects/Account",
      "referenceId": "newAccount",
      "body": {
        "Name": "New Account"
      }
    },
    {
      "method": "POST",
      "url": "/services/data/v66.0/sobjects/Contact",
      "referenceId": "newContact",
      "body": {
        "LastName": "Smith",
        "AccountId": "@{newAccount.id}"
      }
    }
  ]
}
```

### Run a Report

```json
POST /analytics/reports/00OXXXXXXXXXXXX
{
  "reportMetadata": {
    "reportFilters": [
      {
        "column": "ACCOUNT.INDUSTRY",
        "operator": "equals",
        "value": "Technology"
      }
    ]
  }
}
```

### Post to Chatter

```json
POST /chatter/feed-elements
{
  "body": {
    "messageSegments": [
      {
        "type": "Text",
        "text": "Check out this new opportunity!"
      }
    ]
  },
  "feedElementType": "FeedItem",
  "subjectId": "005XXXXXXXXXXXX"
}
```

## SOQL Query Reference

Salesforce Object Query Language (SOQL) syntax:

| Clause | Example |
|--------|---------|
| SELECT | `SELECT Id, Name, Industry` |
| FROM | `FROM Account` |
| WHERE | `WHERE Industry = 'Technology'` |
| AND/OR | `WHERE Industry = 'Technology' AND AnnualRevenue > 1000000` |
| IN | `WHERE Industry IN ('Technology', 'Finance')` |
| LIKE | `WHERE Name LIKE 'Acme%'` |
| ORDER BY | `ORDER BY CreatedDate DESC` |
| LIMIT | `LIMIT 100` |
| OFFSET | `OFFSET 10` |

### Common SOQL Examples

```sql
-- Get accounts in technology industry
SELECT Id, Name, Industry FROM Account WHERE Industry = 'Technology' LIMIT 10

-- Get contacts with email
SELECT Id, FirstName, LastName, Email FROM Contact WHERE Email != null

-- Get opportunities closing this month
SELECT Id, Name, Amount, CloseDate FROM Opportunity WHERE CloseDate = THIS_MONTH

-- Get accounts with related contacts
SELECT Id, Name, (SELECT Id, Name FROM Contacts) FROM Account LIMIT 5
```

## Common sObjects

| Object | API Name | Description |
|--------|----------|-------------|
| Account | `Account` | Business accounts |
| Contact | `Contact` | Individual contacts |
| Lead | `Lead` | Sales leads |
| Opportunity | `Opportunity` | Sales opportunities |
| Case | `Case` | Support cases |
| Task | `Task` | Activities/tasks |
| Event | `Event` | Calendar events |
| User | `User` | Salesforce users |
| Campaign | `Campaign` | Marketing campaigns |
| Product2 | `Product2` | Products |
| Pricebook2 | `Pricebook2` | Price books |
| Order | `Order` | Orders |

## Copilot Studio Integration (MCP)

This connector includes Model Context Protocol (MCP) support for integration with Copilot Studio AI agents.

**Operation:** `InvokeMCP` (summary: "Invoke Salesforce MCP")

### Available MCP Tools

| Tool | Description |
|------|-------------|
| `query` | Execute SOQL queries |
| `search` | Execute SOSL searches |
| `get_record` | Get record by ID |
| `create_record` | Create new record |
| `update_record` | Update existing record |
| `delete_record` | Delete record |
| `list_objects` | List available sObjects |
| `describe_object` | Get object metadata |
| `get_limits` | Check API limits |
| `list_reports` | List reports |
| `run_report` | Execute report |
| `list_dashboards` | List dashboards |
| `post_to_chatter` | Post to Chatter |
| `get_chatter_feed` | Get feed elements |
| `composite` | Execute multiple operations |
| `list_knowledge_articles` | List Knowledge articles with search and pagination |
| `get_knowledge_article` | Get full article content by ID |
| `create_knowledge_article` | Create a Knowledge article draft |
| `update_knowledge_article` | Update a Knowledge article |
| `delete_knowledge_article` | Delete a Knowledge article |
| `search_suggestions` | Get synonym-aware search query suggestions |
| `suggest_article_titles` | Get article title matches for search |
| `list_synonym_groups` | List all search synonym groups |
| `get_synonym_group` | Get synonym group details |
| `create_synonym_group` | Create a search synonym group |
| `update_synonym_group` | Update a synonym group |
| `delete_synonym_group` | Delete a synonym group |

### Available MCP Resources

Resources provide reference documentation that AI agents can read to improve query construction and understand Salesforce concepts.

| Resource URI | Description |
|--------------|-------------|
| `salesforce://reference/soql` | Comprehensive SOQL guide including syntax, operators, date literals, aggregate functions, relationship queries, common objects with key fields, and examples |

The SOQL reference helps agents construct valid queries without trial and error by providing:
- Complete operator reference (comparison, string, set, logical)
- All date literals (TODAY, LAST_N_DAYS, THIS_MONTH, etc.)
- Aggregate functions with GROUP BY/HAVING
- Relationship query syntax (parent-to-child, child-to-parent)
- Common standard objects with key fields
- Best practices and tips

### Using with Copilot Studio

1. Create a new agent in Copilot Studio
2. Add this connector as an action
3. The AI agent will automatically discover available tools
4. Configure appropriate permissions for the service account

## Application Insights Logging

To enable telemetry and monitoring, configure Application Insights:

1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Update `script.csx` and set `APP_INSIGHTS_CONNECTION_STRING`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=xxx;IngestionEndpoint=https://xxx.in.applicationinsights.azure.com/";
```

### Logged Events

| Event | Properties |
|-------|------------|
| `RequestReceived` | CorrelationId, OperationId, Path |
| `RequestCompleted` | CorrelationId, StatusCode, DurationMs |
| `RequestError` | CorrelationId, ErrorMessage, ErrorType |
| `MCPRequest` | CorrelationId, Method, HasParams |
| `MCPToolCall` | CorrelationId, Tool, HasArguments |
| `MCPToolError` | CorrelationId, Tool, ErrorMessage |

## Troubleshooting

### Common Issues

1. **401 Unauthorized / INVALID_SESSION_ID**
   - Verify Consumer Key and Secret are correct
   - Ensure user has API access
   - Check Connected App is properly configured
   - Ensure the connector `host` matches your Salesforce My Domain (e.g., `mycompany.my.salesforce.com`, **not** `lightning.force.com`)
   - If you recently changed OAuth settings, **delete the existing connection and create a new one** — stale tokens are not automatically refreshed

2. **Security tab shows "No Authentication"**
  - Expected with the `SalesforceV2` provider pattern in some custom connector editor experiences
  - Verify actual auth configuration by downloading the connector with `pac connector download`
  - If Test tab connection works, runtime auth is configured even if the editor view is blank

3. **403 Forbidden**
   - User lacks object/field permissions
   - Check profile or permission set assignments

4. **invalid_grant Error**
   - Token may have expired
   - Re-authenticate the connection
   - Check IP restrictions on Connected App (set to **Relax IP restrictions** or add Power Platform IPs)
   - Verify **Refresh Token Policy** is set to "Refresh token is valid until revoked"

5. **REQUEST_LIMIT_EXCEEDED**
   - Org has hit daily API limits
   - Use `GetLimits` to check current usage
   - Consider using Composite API for batching

6. **INVALID_FIELD**
   - Field API name is incorrect
   - Field may not be accessible to the user
   - Use `DescribeObject` to verify field names

## Resources

- [Salesforce REST API Documentation](https://developer.salesforce.com/docs/atlas.en-us.api_rest.meta/api_rest/)
- [SOQL and SOSL Reference](https://developer.salesforce.com/docs/atlas.en-us.soql_sosl.meta/soql_sosl/)
- [Chatter REST API](https://developer.salesforce.com/docs/atlas.en-us.chatterapi.meta/chatterapi/)
- [Reports and Dashboards API](https://developer.salesforce.com/docs/atlas.en-us.api_analytics.meta/api_analytics/)
- [Knowledge REST API](https://developer.salesforce.com/docs/atlas.en-us.knowledge_dev.meta/knowledge_dev/)
- [Tooling API (Synonyms)](https://developer.salesforce.com/docs/atlas.en-us.api_tooling.meta/api_tooling/)
- [Power Platform Custom Connectors](https://docs.microsoft.com/en-us/connectors/custom-connectors/)
