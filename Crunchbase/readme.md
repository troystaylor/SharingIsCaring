# Crunchbase MCP

Custom connector that exposes Crunchbase APIs via Model Context Protocol (MCP) for natural language tool discovery in Copilot Studio.

## Overview

The **Crunchbase MCP connector** integrates Crunchbase's business intelligence platform into Copilot Studio agents. Search for companies, people, and funding rounds with natural language queries and retrieve detailed entity data with relationships.

**MCP Tools:**
- `searchOrganizations` - Search companies with filters (location, industry, funding, etc.)
- `getOrganization` - Get detailed company data by UUID or permalink
- `searchPeople` - Search individuals in Crunchbase
- `getPerson` - Get detailed person data including roles and associations
- `searchFundingRounds` - Search funding rounds by amount, date, type
- `getAutocomplete` - Get autocomplete suggestions for quick lookups

## Files

- `apiDefinition.swagger.json` — OpenAPI 2.0 with `/mcp` operation and `x-ms-agentic-protocol: mcp-streamable-1.0`
- `apiProperties.json` — Connector metadata with API key authentication
- `script.csx` — MCP JSON-RPC implementation with 6 tools
- `readme.md` — This documentation

## Security

Configure API Key authentication:
1. Power Automate → Custom Connectors → [Crunchbase MCP]
2. Security tab → API Key
3. Parameter name: `X-cb-user-key`
4. Get your API key from Crunchbase Data (requires Enterprise or Applications license)

## Authentication Details

Crunchbase uses token-based authentication via:
- **Header:** `X-cb-user-key: YOUR_API_KEY`
- **Query param:** `user_key=YOUR_API_KEY`

The connector passes your API key to Crunchbase on every request.

## Rate Limits

- **200 calls/minute** (enforced by Crunchbase)
- Search results: max 1000 items per request (default 50)
- Card results: max 100 items per card (use pagination for more)

## Usage in Copilot Studio

When you add this connector to a Copilot Studio agent:

1. **Tool Discovery**: Copilot Studio detects the MCP endpoint and calls `tools/list` to discover available tools
2. **Natural Language Mapping**: The agent uses tool descriptions to understand when to invoke tools (e.g., "find companies in San Francisco" → `searchOrganizations`)
3. **Flexible Search**: Each search tool accepts Crunchbase filter syntax for advanced queries
4. **Entity Details**: Get complete company/person profiles including relationships (founders, funding rounds, etc.)

## Examples

### Search Companies by Location
```json
{
  "field_ids": ["identifier", "website", "categories", "total_funding_raised"],
  "query": [
    {
      "type": "predicate",
      "field_id": "location_identifiers",
      "operator_id": "includes",
      "values": ["sf"]
    }
  ],
  "limit": 10
}
```

### Get Company Profile with Founders
```json
{
  "entity_id": "crunchbase",
  "field_ids": ["identifier", "website", "categories", "founded_on", "total_funding_raised"],
  "card_ids": ["founders", "raised_funding_rounds"]
}
```

### Autocomplete for Quick Lookups
```json
{
  "query": "microsoft",
  "collection": "organizations"
}
```

## Available Field IDs

See [Crunchbase API Reference](https://data.crunchbase.com/v4/reference) for complete field lists by entity type:
- **Organizations:** identifier, website, categories, location_identifiers, total_funding_raised, founded_on, etc.
- **People:** identifier, title, location_identifiers, primary_email, etc.
- **Funding Rounds:** identifier, announced_on, money_raised, investment_type, num_investors, etc.

## Available Card IDs (Relationships)

- **Organizations:** founders, raised_funding_rounds, acquisitions, current_jobs, investors
- **People:** current_roles, past_roles, investors, portfolio_companies
- **Funding Rounds:** investors, funded_organization

## Pagination

Search results support keyset pagination:
- Use `after_id` (UUID of last item) to get next page
- Results default to 50 items; max 1000 per request

## Copilot Studio Workflow

1. **Query** → Agent interprets intent and selects appropriate tool
2. **Tool Call** → Agent invokes `tools/call` with tool name and parameters
3. **API Call** → Connector forwards request to Crunchbase API
4. **Response** → Agent formats results for conversation

The `field_ids` parameter lets you control which data is returned, optimizing for response size and agent token usage.

## License & Attribution

- Crunchbase Data API access requires an Enterprise or Applications license
- See [Crunchbase Terms](https://about.crunchbase.com/terms-of-service/) for data usage requirements
- Attribution required when sharing Crunchbase data publicly
