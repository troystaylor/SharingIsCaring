# Microsoft Foundry Embed

## Overview

The Microsoft Foundry Embed connector generates text and image embeddings using Cohere Embed v4 on Microsoft Foundry. It provides three tiers of functionality:

- **Basic** — Compute semantic similarity between any two inputs (text or image) with a single call. No vector store needed.
- **Intermediate** — Generate raw embedding vectors for text or images. Store and compare them however you like.
- **Advanced** — Index documents to Azure AI Search with auto-generated vectors, and perform vector search — a complete RAG retrieval pipeline from Power Automate.

## Prerequisites

1. An Azure subscription with access to Microsoft Foundry
2. Deploy **embed-v-4-0** from the [Foundry Model Catalog](https://ai.azure.com/explore/models?selectedCollection=Cohere)
3. Note the **Resource Name** and **API Key** from the deployment
4. *(Advanced only)* An [Azure AI Search](https://azure.microsoft.com/products/ai-services/ai-search) service with a vector-enabled index

## Connection Setup

| Parameter | Description | Example |
|---|---|---|
| **Resource Name** | The Azure AI Services resource name | `my-foundry-resource` |
| **API Key** | API key from the Foundry portal | (from Keys & Endpoint) |

## Operations

### Embed Text

Generate embedding vectors for one or more text strings.

| Parameter | Required | Description |
|---|---|---|
| Texts | Yes | Array of text strings (max 512 tokens each) |
| Input Type | No | `document` (default, for indexing) or `query` (for searching) |
| Dimensions | No | Vector size: 256, 512, 1024 (default), or 1536 |

### Embed Image

Generate an embedding vector for an image, optionally paired with text.

| Parameter | Required | Description |
|---|---|---|
| Image URL | Yes | Image URL or base64 data URI (PNG recommended) |
| Text | No | Optional text to pair with the image for a combined embedding |
| Dimensions | No | Vector size: 256, 512, 1024 (default), or 1536 |

### Compute Similarity

Compute semantic similarity between two inputs. Both inputs are embedded and compared using cosine similarity — no external vector store needed.

| Parameter | Required | Description |
|---|---|---|
| Input A | Yes | First input (text string or image URL) |
| Input A Type | No | `text` (default) or `image` |
| Input B | Yes | Second input |
| Input B Type | No | `text` (default) or `image` |

**Returns:** A similarity score (0-1) and a human-readable interpretation:

| Score Range | Interpretation |
|---|---|
| 0.8 - 1.0 | Very similar |
| 0.6 - 0.8 | Similar |
| 0.4 - 0.6 | Somewhat related |
| 0.2 - 0.4 | Loosely related |
| 0.0 - 0.2 | Unrelated |

**Cross-modal:** You can compare text against images. The model produces vectors in the same space for both modalities.

### Index Document to AI Search

Embed a text document and push it (with its vector) to an Azure AI Search index.

| Parameter | Required | Description |
|---|---|---|
| Document ID | Yes | Unique identifier for the document |
| Content | Yes | The text content to embed and index |
| Title | No | Optional document title (stored as metadata) |
| Search Endpoint | Yes | AI Search URL (e.g., `https://mysearch.search.windows.net`) |
| Search Index | Yes | Index name |
| Search API Key | Yes | AI Search admin API key |
| Vector Field | No | Vector field name in the index (default: `contentVector`) |

### Search Similar Documents

Embed a query and perform vector search against an Azure AI Search index.

| Parameter | Required | Description |
|---|---|---|
| Query | Yes | The search query text |
| Top K | No | Number of results (default: 5, max: 50) |
| Search Endpoint | Yes | AI Search URL |
| Search Index | Yes | Index name |
| Search API Key | Yes | AI Search query API key |
| Vector Field | No | Vector field name (default: `contentVector`) |

## AI Search Index Requirements

For the `IndexDocument` and `SearchSimilar` operations, your AI Search index must have:

- A string field named `id` (set as the key)
- A string field named `content`
- A `Collection(Edm.Single)` field for vectors (default name: `contentVector`) with dimensions matching the connector (1024)
- *(Optional)* A string field named `title`

## Example Workflows

### Simple Similarity Check (No AI Search)
1. User submits two product descriptions
2. `ComputeSimilarity` → returns 0.87 ("Very similar")
3. Flow flags them as potential duplicates

### RAG Pipeline with AI Search
1. **Index phase:** For each document in SharePoint, call `IndexDocument` to embed and push to AI Search
2. **Query phase:** User asks a question → `SearchSimilar` finds top 5 relevant documents → send to LLM with context

### Cross-Modal Search
1. User uploads an image of a product
2. `ComputeSimilarity` compares the image against product description text
3. Returns the closest matching product by description

## MCP Protocol Support

This connector includes an MCP endpoint for Copilot Studio with five tools:

| Tool | Description |
|---|---|
| `embed_text` | Generate text embeddings |
| `embed_image` | Generate image embeddings |
| `compute_similarity` | Compute similarity between two inputs |
| `index_document` | Embed and push to AI Search |
| `search_similar` | Vector search against AI Search |

## Model Details

| Feature | Details |
|---|---|
| **Model** | Cohere Embed v4 (`embed-v-4-0`) |
| **Input** | Text (512 tokens) and images (2M pixels) |
| **Output** | Vectors: 256, 512, 1024, or 1536 dimensions |
| **Languages** | 10: en, fr, es, it, de, pt-br, ja, ko, zh-cn, ar |
| **Deployment** | Global Standard (serverless, all regions) |

## Known Limitations

- Text input is limited to 512 tokens per string
- Image embeddings require PNG format; other formats may not work
- Batch image embeddings may not be supported — one image per call
- AI Search operations pass credentials as parameters (not stored in the connection) because they connect to a separate service
- Cosine similarity scores are relative — they compare two specific inputs, not absolute quality measures
- The default vector dimensions (1024) must match your AI Search index configuration

## Authors

- Troy Taylor, troy@troystaylor.com, https://github.com/troystaylor
