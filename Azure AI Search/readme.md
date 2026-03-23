# Azure AI Search MCP

Power Mission Control connector for Azure AI Search. Exposes the full Azure AI Search REST API (2025-09-01) through 3 MCP tools with progressive discovery, plus 8 MCP resources for knowledge grounding.

## Prerequisites

- Azure AI Search service ([create one](https://learn.microsoft.com/azure/search/search-create-service-portal))
- Admin API key (for full access) or query API key (search only)
- Power Platform environment with Copilot Studio

## Setup

### 1. Create the Custom Connector

1. Go to [make.powerapps.com](https://make.powerapps.com) > **Custom connectors**
2. Select **New custom connector** > **Import an OpenAPI file**
3. Upload `apiDefinition.swagger.json`
4. On the **Code** tab, paste the contents of `script.csx`
5. Save and test

### 2. Create a Connection

When creating a connection, provide:

| Parameter | Value |
|---|---|
| **Search Service URL** | `https://your-service.search.windows.net` |
| **API Key** | Your admin or query API key from the Azure portal |

### 3. Add to Copilot Studio

1. Open your agent in Copilot Studio
2. Go to **Tools** > **Add a tool** > **Custom connector**
3. Select **Azure AI Search MCP**
4. The agent now has access to `scan_search`, `launch_search`, and `sequence_search`

## Tools

The connector exposes 3 tools to the Copilot Studio planner (~1,500 tokens):

| Tool | Purpose |
|---|---|
| `scan_search` | Scan for available operations matching your intent |
| `launch_search` | Launch any Azure AI Search API endpoint |
| `sequence_search` | Launch a sequence of multiple operations in one call |

### Planner Workflow

```
User: "Search my products index for wireless headphones"

1. Planner calls scan_search({query: "search documents"})
   → Returns: search_documents (POST /indexes/{indexName}/docs/search)

2. Planner calls launch_search({
     endpoint: "/indexes/products/docs/search",
     method: "POST",
     body: { search: "wireless headphones", queryType: "semantic" }
   })
   → Returns: matching documents with highlights and scores
```

## Operations (37)

### Indexes
| Operation | Endpoint | Method |
|---|---|---|
| list_indexes | /indexes | GET |
| create_index | /indexes | POST |
| get_index | /indexes/{indexName} | GET |
| update_index | /indexes/{indexName} | PUT |
| delete_index | /indexes/{indexName} | DELETE |
| get_index_statistics | /indexes/{indexName}/stats | GET |
| analyze_text | /indexes/{indexName}/analyze | POST |

### Search & Documents
| Operation | Endpoint | Method |
|---|---|---|
| search_documents | /indexes/{indexName}/docs/search | POST |
| get_document | /indexes/{indexName}/docs/{key} | GET |
| count_documents | /indexes/{indexName}/docs/$count | GET |
| index_documents | /indexes/{indexName}/docs/index | POST |
| autocomplete | /indexes/{indexName}/docs/autocomplete | POST |
| suggest | /indexes/{indexName}/docs/suggest | POST |

### Indexers
| Operation | Endpoint | Method |
|---|---|---|
| list_indexers | /indexers | GET |
| create_indexer | /indexers | POST |
| get_indexer | /indexers/{indexerName} | GET |
| update_indexer | /indexers/{indexerName} | PUT |
| delete_indexer | /indexers/{indexerName} | DELETE |
| run_indexer | /indexers/{indexerName}/run | POST |
| get_indexer_status | /indexers/{indexerName}/status | GET |
| reset_indexer | /indexers/{indexerName}/reset | POST |

### Data Sources
| Operation | Endpoint | Method |
|---|---|---|
| list_datasources | /datasources | GET |
| create_datasource | /datasources | POST |
| get_datasource | /datasources/{dataSourceName} | GET |
| update_datasource | /datasources/{dataSourceName} | PUT |
| delete_datasource | /datasources/{dataSourceName} | DELETE |

### Skillsets
| Operation | Endpoint | Method |
|---|---|---|
| list_skillsets | /skillsets | GET |
| create_skillset | /skillsets | POST |
| get_skillset | /skillsets/{skillsetName} | GET |
| update_skillset | /skillsets/{skillsetName} | PUT |
| delete_skillset | /skillsets/{skillsetName} | DELETE |

### Synonym Maps
| Operation | Endpoint | Method |
|---|---|---|
| list_synonym_maps | /synonymmaps | GET |
| create_synonym_map | /synonymmaps | POST |
| get_synonym_map | /synonymmaps/{synonymMapName} | GET |
| update_synonym_map | /synonymmaps/{synonymMapName} | PUT |
| delete_synonym_map | /synonymmaps/{synonymMapName} | DELETE |

### Admin
| Operation | Endpoint | Method |
|---|---|---|
| get_service_statistics | /servicestats | GET |

## Resources (8)

MCP resources provide knowledge grounding — the planner can read these to understand what's available before making tool calls.

| Resource | URI | Description |
|---|---|---|
| Search Indexes | `search://indexes` | All indexes in the service |
| Index Schema | `search://indexes/{indexName}/schema` | Field definitions, types, semantic config |
| Index Statistics | `search://indexes/{indexName}/stats` | Document count and storage size |
| Service Statistics | `search://service/stats` | Service-level usage and capacity |
| Data Sources | `search://datasources` | Configured data connections |
| Skillsets | `search://skillsets` | AI enrichment pipelines |
| Synonym Maps | `search://synonymmaps` | Query expansion rules |
| Indexer Status | `search://indexers/{indexerName}/status` | Run history, errors, documents processed |

## Architecture

```
Copilot Studio Agent
    │
    ├─ tools/list → [scan_search, launch_search, sequence_search]
    │
    ├─ resources/list → [search://indexes, search://service/stats, ...]
    │   └─ resources/read → index schemas, stats, data source configs
    │
    ├─ scan_search({query: "search documents"})
    │   └─ Searches embedded capability index (37 operations)
    │
    ├─ launch_search({endpoint, method, body})
    │   ├─ Builds URL: {serviceUrl}/{endpoint}?api-version=2025-09-01
    │   ├─ Injects api-key header from connection parameter
    │   ├─ Handles 429 retry with Retry-After
    │   └─ Summarizes response (strip HTML, truncate)
    │
    └─ sequence_search({requests: [...]})
        └─ Executes requests sequentially, returns aggregated results
```

## Authentication

This connector uses Azure AI Search API key authentication. The API key is stored as a connection parameter and injected as the `api-key` header on every request via the `CustomHeaders` mechanism in the Power Mission Control framework.

- **Admin key**: Full access to all operations (manage indexes, indexers, data sources, etc.)
- **Query key**: Read-only access to search and document retrieval operations

Get your keys from the Azure portal: **Search service** > **Settings** > **Keys**.

## Built With

[Power Mission Control Template v3](../Connector-Code/Power%20MCP%20Template%20v3/Power%20Mission%20Control%20Template/) — progressive API discovery for Copilot Studio agents.

## API Reference

- [Azure AI Search REST API (2025-09-01)](https://learn.microsoft.com/rest/api/searchservice/)
- [Data Plane Operations](https://learn.microsoft.com/rest/api/searchservice/operation-groups)
