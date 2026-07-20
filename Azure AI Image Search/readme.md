# Azure AI Image Search

A dual-mode Power Platform custom connector for natural language image search over your own image collections. Uses Azure AI Search multimodal embeddings for semantic retrieval and Azure Blob Storage for image delivery.

**Inspired by:** Pamela Fox's [Beyond text: Returning images and interactive apps from MCP servers](https://techcommunity.microsoft.com/blog/azuredevcommunityblog/beyond-text-returning-images-and-interactive-apps-from-mcp-servers/4535865) and the [Azure-Samples/image-search-aisearch](https://github.com/Azure-Samples/image-search-aisearch) reference implementation.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Power Platform / Copilot Studio                                │
│  ┌──────────────────┐  ┌──────────────────────────────────────┐ │
│  │  Power Automate  │  │  Copilot Studio (MCP)                │ │
│  │  Typed ops:      │  │  Tools: image_search,                │ │
│  │  SearchImages    │  │         search_by_image,             │ │
│  │  SearchByImage   │  │         get_image_details,           │ │
│  │  GetImageDetails │  │         display_images               │ │
│  │  GetImageUrl     │  │                                      │ │
│  │  UploadImage     │  │                                      │ │
│  └────────┬─────────┘  └──────────────────┬───────────────────┘ │
└───────────┼───────────────────────────────┼─────────────────────┘
            │        Custom Connector       │
            └───────────────────┬───────────┘
                                │ HTTPS + X-API-Key
            ┌───────────────────▼────────────────────┐
            │  Azure Container Apps (FastAPI + MCP)  │
            │  ┌─────────────────────────────────┐   │
            │  │  server.py                      │   │
            │  │  • /health — liveness probe     │   │
            │  │  • /mcp — MCP endpoint          │   │
            │  │  • /api/search — text search    │   │
            │  │  • /api/search-by-image — rev.  │   │
            │  │  • /api/images/{f} — details    │   │
            │  │  • /api/images/{f}/url — SAS    │   │
            │  │  • /api/upload — add images     │   │
            │  └──────────┬──────────┬───────────┘   │
            └─────────────┼──────────┼───────────────┘
                          │          │
         ┌────────────────▼──┐  ┌────▼──────────────────┐
         │  Azure AI Search  │  │  Azure Blob Storage   │
         │  (multimodal      │  │  (image files)        │
         │   embeddings)     │  │                       │
         └───────────────────┘  └───────────────────────┘
```

## Features

| Feature | Description |
|---------|-------------|
| Natural language search | Hybrid text + vector search with multimodal embeddings |
| Reverse image search | Find similar images by providing a URL or file |
| Image thumbnails | Resized previews returned as binary content for model inspection |
| Structured metadata | Filenames, AI descriptions, dimensions, format returned as JSON |
| MCP App carousel | Interactive image viewer (viewer.html resource, requires MCP App-capable client) |
| SAS URLs | Time-limited download links for full-resolution images |
| Image upload | Add images to the collection via API (indexed on next indexer run) |
| Lightweight mode | Return URLs + metadata only — no Blob Storage dependency |
| Dual-mode | MCP for Copilot Studio + typed operations for Power Automate |

## Prerequisites

1. Azure subscription
2. Azure AI Search service (Basic tier or higher, with semantic search enabled)
3. Azure AI Services resource (for multimodal image embeddings)
4. Azure Blob Storage account with an images container
5. Azure Container Registry (for building/hosting the server image)
6. Power Platform environment with custom connector permissions

> **Note:** Local Docker is NOT required — use `az acr build` to build directly in ACR.

## Setup

### 1. Prepare Your Image Index

Upload images to Azure Blob Storage, then create an AI Search index with:
- A vectorizer (Azure AI Vision multimodal embeddings via `aiServicesVision`)
- An `embedding` field (Collection(Edm.Single), 1024 dimensions, HNSW profile)
- A `verbalized_image` field (AI-generated text description per image)
- A `metadata_storage_name` field (blob filename)
- A `metadata_storage_path` field (full blob URL)
- An `id` field (key, with `keyword` analyzer — required for index projections)
- A `parent_id` field (filterable string — required for index projections)

The index needs an integrated vectorizer so queries are auto-vectorized:
```json
"vectorSearch": {
  "vectorizers": [{
    "name": "ai-vision-vectorizer",
    "kind": "aiServicesVision",
    "aiServicesVisionParameters": {
      "resourceUri": "https://your-ai-services.cognitiveservices.azure.com",
      "modelVersion": "2023-04-15"
    }
  }]
}
```

The [Azure-Samples/image-search-aisearch](https://github.com/Azure-Samples/image-search-aisearch) repo includes a complete indexing pipeline you can adapt.

### 2. Deploy the Backend

```powershell
# Build in ACR (no local Docker needed)
az acr build --registry yourregistry --image ai-image-search:latest --file server/Dockerfile server/

# Deploy infrastructure
cd infra
.\deploy.ps1 -ResourceGroup "rg-ai-image-search" -Location "westus2"
```

Or deploy via Bicep directly (includes ACR credentials):
```powershell
$acrPwd = az acr credential show --name yourregistry --query "passwords[0].value" -o tsv
az deployment group create --resource-group rg-ai-image-search `
    --template-file infra/main.bicep `
    --parameters containerImage="yourregistry.azurecr.io/ai-image-search:latest" `
                 apiKey="your-api-key" `
                 acrServer="yourregistry.azurecr.io" `
                 acrUsername="yourregistry" `
                 acrPassword=$acrPwd
```

This deploys:
- Azure Container Apps environment + app (the FastMCP server)
- Azure AI Search service (Basic tier)
- Storage account with images container

Save the API key and app URL from the output.

### 3. Configure the Connector

1. Update `script.csx` → set `BACKEND_HOST` to your ACA FQDN
2. Update `apiDefinition.swagger.json` → set `host` to your ACA FQDN
3. Deploy with PAC CLI:

```powershell
pac connector create `
    -df apiDefinition.swagger.json `
    -pf apiProperties.json `
    -sf script.csx `
    -e c4f149b0-9f42-e8c4-97d8-bc69b59f971c
```

### 4. Create Connection

In Power Automate or Copilot Studio, create a new connection using the API key from deployment.

## MCP Tools

When connected via Copilot Studio (MCP mode), the server exposes:

| Tool | Description |
|------|-------------|
| `image_search` | Search images by natural language query, returns thumbnails + metadata |
| `search_by_image` | Find visually similar images by providing an image URL |
| `get_image_details` | Get full metadata and larger preview for a specific image |
| `display_images` | Return selected images as ImageContent blocks for the agent/user |

### Two-Stage Pattern

The agent workflow follows the pattern from Pamela Fox's post:

1. **Search** — `image_search` returns thumbnails (for model inspection) + structured metadata
2. **Curate** — The agent reviews results, selects the best matches
3. **Display** — `display_images` renders selections in an interactive carousel app

## Typed Operations (Power Automate)

| Operation | Method | Path |
|-----------|--------|------|
| SearchImages | POST | `/api/search` |
| SearchByImage | POST | `/api/search-by-image` |
| GetImageDetails | GET | `/api/images/{filename}` |
| GetImageUrl | GET | `/api/images/{filename}/url` |
| UploadImage | POST | `/api/upload` |

## Server Configuration

Environment variables for the ACA container:

| Variable | Description |
|----------|-------------|
| `AZURE_SEARCH_ENDPOINT` | AI Search service URL |
| `AZURE_SEARCH_INDEX` | Index name |
| `AZURE_SEARCH_KEY` | AI Search admin key (or use managed identity) |
| `AZURE_BLOB_CONNECTION_STRING` | Blob Storage connection string (not needed in lightweight mode) |
| `AZURE_BLOB_CONTAINER` | Container name (default: `images`) |
| `LIGHTWEIGHT_MODE` | Set to `true` to return URLs only, no Blob fetching |
| `API_KEY` | API key for connector authentication |

## Local Development

```bash
cd server
pip install -r requirements.txt

# Set environment variables
export AZURE_SEARCH_ENDPOINT="https://your-search.search.windows.net"
export AZURE_SEARCH_INDEX="images"
export AZURE_SEARCH_KEY="your-key"
export AZURE_BLOB_CONNECTION_STRING="your-connection-string"

python server.py
```

The server starts on `http://localhost:8000`. Test with:
```bash
curl -X POST http://localhost:8000/api/search \
  -H "Content-Type: application/json" \
  -d '{"query": "sunset over mountains", "max_results": 3}'
```

## How It Differs from Azure MCP Server's AI Search Tools

Azure MCP Server already provides [AI Search tools](https://learn.microsoft.com/azure/developer/azure-mcp-server/tools/azure-ai-search) (`search index query`, `search knowledge base retrieve`, etc.), but those return raw JSON document fields — no image handling. This connector adds the image-specific layer:

- Fetches actual image bytes and returns `ImageContent` blocks
- Resizes images to thumbnails for token-efficient model inspection
- Supports reverse image search via `VectorizableImageUrlQuery`
- Generates time-limited SAS URLs for full-resolution download
- Provides an upload endpoint to grow the collection

## References

- [Beyond text: Returning images and interactive apps from MCP servers](https://techcommunity.microsoft.com/blog/azuredevcommunityblog/beyond-text-returning-images-and-interactive-apps-from-mcp-servers/4535865) — Pamela Fox (Microsoft)
- [Azure-Samples/image-search-aisearch](https://github.com/Azure-Samples/image-search-aisearch) — Reference implementation
- [MCP Apps specification](https://modelcontextprotocol.io/extensions/apps/overview)
- [Azure AI Search multimodal embeddings](https://learn.microsoft.com/azure/search/search-get-started-portal-import-vectors)
- [FastMCP framework](https://gofastmcp.com/)
