# ARD Discovery

Dual-mode Power Platform custom connector for [Agentic Resource Discovery (ARD)](https://github.com/nicholasgoulding/ard) — search registries, discover MCP servers and A2A agents, and invoke capabilities with automatic authentication.

## Features

- **Search** — Find MCP servers, A2A agents, and AI skills by natural language query with hybrid vector + keyword scoring
- **Explore** — Faceted browsing to discover what types, publishers, and tags are available
- **List** — Deterministic catalog browsing for developer portals
- **Proxy** — Invoke discovered MCP endpoints with three-tier automatic authentication (OBO, org token, per-user)
- **MCP Endpoint** — Copilot Studio integration via `x-ms-agentic-protocol: mcp-streamable-1.0`
- **Catalog Publishing** — Serves `/.well-known/ai-catalog.json` to participate in the ARD network
- **Web Crawling** — Timer-triggered crawl of registered domains with nested catalog support
- **Trust Verification** — DNS TXT, .well-known file, and JWS signature verification with 0-100 scoring

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Power Platform Custom Connector (script.csx)            │
│  - Typed operations: /search, /explore, /agents, /proxy  │
│  - MCP endpoint: /mcp (JSON-RPC 2.0)                     │
└──────────────────────────┬──────────────────────────────┘
                           │ HTTPS + x-api-key + Auth headers
┌──────────────────────────▼──────────────────────────────┐
│  Azure Functions Backend (.NET 8 Isolated)               │
│  ┌────────────┐ ┌─────────────┐ ┌────────────────────┐  │
│  │ Search     │ │ Explore     │ │ Proxy (3-tier auth)│  │
│  │ List       │ │ Catalog     │ │ OBO → Org → User   │  │
│  │ Health     │ │ Crawl       │ │ + Rate Limiter     │  │
│  └────────────┘ └─────────────┘ └────────────────────┘  │
│                                                          │
│  ┌─────────────────────────────────────────────────┐     │
│  │ ISearchIndex                                     │     │
│  │  ├─ TableStorageIndex (default, <100K entries)   │     │
│  │  └─ AiSearchIndex (vector + keyword, scalable)   │     │
│  └─────────────────────────────────────────────────┘     │
│                                                          │
│  ┌────────────────┐ ┌──────────────┐ ┌──────────────┐   │
│  │ TrustVerifier  │ │ CrawlState   │ │ TokenStore   │   │
│  │ DNS/JWS/.wk    │ │ Table Storage│ │ Table Storage│   │
│  └────────────────┘ └──────────────┘ └──────────────┘   │
└──────────────────────────────────────────────────────────┘
```

## Prerequisites

- Azure subscription
- Power Platform environment with custom connector support
- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/) for deployment

## Deployment

### 1. Deploy the Backend

```bash
cd backend
azd init
azd up
```

Configure environment variables when prompted:
- `BackendApiKey` — shared secret between connector and backend
- `CrawlDomains` — comma-separated domains to crawl (e.g., `contoso.com,fabrikam.com`)
- `OboClientId`, `OboClientSecret`, `OboTenantId` — for Tier 1 OBO auth (optional)

### 2. Deploy the Connector

```bash
pac connector create --api-definition-file apiDefinition.swagger.json \
  --api-properties-file apiProperties.json \
  --script-file script.csx \
  --environment c4f149b0-9f42-e8c4-97d8-bc69b59f971c
```

### 3. Configure Connection

When creating a connection, provide:
- **API Key** — the `BackendApiKey` value from Key Vault

## Operations

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| Search Capabilities | POST | /search | Semantic search with filters and federation |
| Explore Registry | POST | /explore | Faceted aggregation (type, publisher, tags) |
| List Agents | GET | /agents | Paginated browse with filtering |
| Invoke Capability | POST | /proxy | Proxy MCP calls with automatic auth |
| Invoke MCP | POST | /mcp | Copilot Studio MCP endpoint |

## MCP Tools (Copilot Studio)

| Tool | Description |
|------|-------------|
| `search_capabilities` | Search for MCP servers, A2A agents, and skills by natural language |
| `explore_registry` | Explore what types of resources and publishers are available |
| `invoke_capability` | Invoke a discovered capability; handles auth automatically |

## Authentication Tiers

The proxy resolves authentication automatically:

1. **Tier 1 (OBO)** — If the target is same-tenant Entra, exchanges the user's token via On-Behalf-Of flow. No user action needed.
2. **Tier 2 (Org Token)** — If an admin pre-connected the domain via `/connect`, uses the stored org-level token.
3. **Tier 3 (Per-User)** — Uses stored per-user tokens. If elicitation is enabled and no token exists, returns a sign-in prompt.

Rate limiting: 60 requests/minute per user on the proxy endpoint (HTTP 429 with Retry-After).

## Trust Scoring

Each crawled domain receives a trust score (0-100):

| Signal | Points | Verification Method |
|--------|--------|---------------------|
| HTTPS origin | +10 | All crawled domains use HTTPS |
| DNS TXT record | +20 | `_ard-verify.{domain}` TXT with `ard-verify=...` |
| .well-known file | +15 | `/.well-known/ard-verify.json` with matching domain |
| JWS catalog signature | +30 | RS256/ES256 signature in catalog `signature` field |

Trust levels: **none** (0-9), **basic** (10-39), **verified** (40-69), **high** (70-100)

Domain-URN mismatch protection: entries with `urn:air:evil.com:...` crawled from `good.com` are automatically rejected.

## Optional: Azure AI Search

For production workloads with >10K entries or when semantic search quality matters:

1. Create an Azure AI Search resource (Basic tier)
2. Create an Azure OpenAI deployment with `text-embedding-3-small`
3. Set app settings:
   - `AiSearchEndpoint` — e.g., `https://my-search.search.windows.net`
   - `AiSearchApiKey` — admin key
   - `AiSearchEmbeddingEndpoint` — Azure OpenAI embedding endpoint
   - `AiSearchEmbeddingKey` — Azure OpenAI key

When `AiSearchEndpoint` is set, the backend uses hybrid vector + keyword search with HNSW index.

## Configuration Reference

| Setting | Required | Description |
|---------|----------|-------------|
| `BackendApiKey` | Yes | Shared secret for connector-backend auth |
| `TokenStoreTableUri` | Yes | Azure Table Storage URI (managed identity) |
| `AzureWebJobsStorage__accountName` | Yes | Storage account name (managed identity) |
| `DefaultRegistryUrl` | No | Upstream registry for federated search |
| `CrawlDomains` | No | Comma-separated domains to crawl every 6 hours |
| `PublisherDomain` | No | Domain for this instance's ai-catalog.json |
| `EnableElicitation` | No | Enable MCP elicitation (`true`/`false`, default `false`) |
| `OboClientId` | No | Entra app registration for OBO |
| `OboClientSecret` | No | OBO client secret |
| `OboTenantId` | No | OBO tenant |
| `AiSearchEndpoint` | No | Azure AI Search endpoint (enables vector search) |
| `AiSearchApiKey` | No | AI Search admin key |
| `AiSearchEmbeddingEndpoint` | No | Azure OpenAI embedding endpoint |
| `AiSearchEmbeddingKey` | No | Embedding API key |

## Operational Endpoints

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /api/health` | None | Liveness probe with index stats |
| `POST /api/crawl-now` | API key | Manual crawl trigger (`?domains=a.com,b.com`) |
| `GET /api/robots.txt` | None | Agentmap directive for ARD crawlers |
| `GET /api/.well-known/ai-catalog.json` | None | This instance's published catalog |

## ARD Specification

- [ARD Spec](https://github.com/nicholasgoulding/ard)
- [MCP Specification](https://modelcontextprotocol.io/specification/)
