# Salesforce Client Credentials

## Overview

Salesforce connector using OAuth 2.0 **client_credentials** grant flow via Basic Authentication. Instead of interactive user login, this connector authenticates using a Salesforce Connected App's Client ID and Client Secret, making it ideal for server-to-server integrations, automated workflows, and service accounts.

Covers Core REST API (SOQL, sObject CRUD, Search), Composite API, Reports & Dashboards, Connect/Chatter APIs, Knowledge Articles, Search Suggestions, and Synonym Groups via Tooling API. MCP-enabled for Copilot Studio integration with dynamic sObject schema support.

## Prerequisites

### Salesforce Connected App Setup

1. In Salesforce Setup, go to **App Manager** and create a new Connected App
2. Enable **OAuth Settings**
3. Set the **Callback URL** to `https://global.consent.azure-apim.net/redirect` (not used by client_credentials but required by Salesforce)
4. Select the following OAuth scopes:
   - `api` — Access and manage your data
5. Enable **Client Credentials Flow**:
   - Under the Connected App settings, go to **Manage** → **Edit Policies**
   - Set **Permitted Users** to "Admin approved users are pre-authorized"
   - Under **Client Credentials Flow**, assign a **Run As** user — this is the user identity all API calls will execute as
5. Note the **Consumer Key** (Client ID) and **Consumer Secret** (Client Secret)

### Important Notes

- The `client_credentials` flow does not support refresh tokens — a new access token is acquired for each request
- All API operations execute under the identity of the "Run As" user configured in the Connected App
- The "Run As" user must have appropriate Salesforce permissions for the operations you intend to use

## Connection Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| **Client ID** | Consumer Key from the Connected App | `3MVG9...` |
| **Client Secret** | Consumer Secret from the Connected App | `ABC123...` |
| **Instance URL** | Your Salesforce My Domain without `https://` | `myorg.my.salesforce.com` |

## How It Works

1. Power Platform sends Client ID and Client Secret as Basic Authentication (encoded in the `Authorization: Basic base64(client_id:client_secret)` header)
2. The custom script forwards the Basic auth header directly to `https://{instanceUrl}/services/oauth2/token` with `grant_type=client_credentials` — [Salesforce natively accepts this format](https://help.salesforce.com/s/articleView?id=xcloud.remoteaccess_oauth_client_credentials_flow.htm&type=5)
3. Salesforce validates the credentials and returns an access token along with the canonical `instance_url`
4. The script uses the `instance_url` from the token response (not the connection parameter) for subsequent API calls, ensuring correct routing even if Salesforce redirects
5. The script forwards the original API request to Salesforce with `Authorization: Bearer {token}`

The token and instance URL are cached within a single request execution to avoid redundant token calls when an operation makes multiple Salesforce API calls (e.g., MCP tool calls).

## Supported Operations

### Query
- **Execute SOQL Query** — Run SOQL queries against any object
- **Get More Query Results** — Paginate through large result sets
- **Search** — Execute SOSL full-text search across objects

### Records
- **Create Record** — Create records in any sObject (with dynamic schema)
- **Get Record** — Retrieve a single record by ID
- **Update Record** — Update record fields (with dynamic schema)
- **Delete Record** — Delete a record

### Metadata
- **Describe Global** — List all available sObjects
- **Describe Object** — Get detailed field metadata for an sObject
- **Get Limits** — View org API limits and current usage

### Composite
- **Composite** — Execute multiple operations in a single request
- **Composite Batch** — Batch independent subrequests
- **Composite Tree** — Create related record trees
- **Composite Graph** — Execute dependent operations with references

### Analytics
- **List Reports / Get Report / Run Report** — Salesforce Reports
- **List Dashboards / Get Dashboard / Refresh Dashboard** — Dashboards

### Chatter
- **Get Feed / Post Feed Element** — Social collaboration
- **Get/List Users / Groups** — Chatter users and groups

### Knowledge
- **List / Get / Create / Update / Delete** Knowledge Articles
- **Search Suggestions / Title Matches** — AI-powered article search

### Case Management
- **Create / Get / Update Case** — Full case lifecycle
- **Case Timeline** — Comments, emails, and feed items
- **Send Case Email** — Outbound email on cases
- **KB Suggestions for Case** — Article recommendations
- **Draft KB Article from Case** — Create articles from resolved cases
- **Categorize Case** — Classify type, reason, and root cause

### Tooling
- **Tooling Query** — Query Salesforce metadata
- **Synonym Groups** — CRUD operations on search synonyms

### MCP (Copilot Studio)
- **Invoke Salesforce CC MCP** — 35 MCP tools for AI agent integration

## Known Issues and Limitations

- Each API request acquires a new OAuth token (no token caching across requests)
- The `client_credentials` flow requires Salesforce API version 51.0+ and a properly configured Connected App
- The "Run As" user must have a Salesforce license that supports API access
- All operations execute under the "Run As" user's permissions and profile
