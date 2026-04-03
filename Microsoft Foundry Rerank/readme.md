# Microsoft Foundry Rerank

## Overview

The Microsoft Foundry Rerank connector reranks search results by semantic relevance using Cohere Rerank v4 models on Microsoft Foundry. Pass a query and a list of documents — the model scores each document's relevance and returns them in order from most to least relevant.

Use in RAG (Retrieval-Augmented Generation) pipelines between your initial retrieval step (SharePoint search, Dataverse query, Azure AI Search, or any other source) and sending context to an LLM. Reranking improves LLM response quality by ensuring the most relevant documents reach the prompt.

Supports 14 languages: English, French, Spanish, Italian, German, Portuguese, Japanese, Chinese, Arabic, Vietnamese, Hindi, Russian, Indonesian, and Dutch.

## Prerequisites

1. An Azure subscription with access to Microsoft Foundry
2. Deploy **Cohere-rerank-v4.0-pro** or **Cohere-rerank-v4.0-fast** from the [Foundry Model Catalog](https://ai.azure.com/explore/models?selectedCollection=Cohere)
3. Note the **Resource Name** (e.g., `my-foundry-resource` from `https://my-foundry-resource.services.ai.azure.com`) and **API Key** from the deployment

## Connection Setup

| Parameter | Description | Example |
|---|---|---|
| **Resource Name** | The Azure AI Services resource name | `my-foundry-resource` |
| **API Key** | API key from the Foundry portal | (from Keys & Endpoint) |

## Operations

### Rerank Documents

Rerank a list of documents by semantic relevance to a query. Returns all documents ordered by relevance score (highest first), enriched with the original document text.

| Parameter | Required | Description |
|---|---|---|
| Query | Yes | The search query to rank documents against |
| Documents | Yes | Array of document text strings (max 1,000 recommended) |
| Model | No | `Cohere-rerank-v4.0-pro` (default, best quality) or `Cohere-rerank-v4.0-fast` (lower latency) |
| Top N | No | Number of top results to return. Omit to return all. |
| Max Tokens Per Document | No | Maximum tokens per document before truncation (default: 4096) |

**Returns:**

| Field | Description |
|---|---|
| Results | Array of `{ index, relevance_score, document }` ordered by score |
| Request ID | Unique identifier for the request |
| Metadata | API version and billing info (search units consumed) |

### Rerank and Filter

Rerank documents and filter out those below a minimum relevance score. Returns only documents above the threshold, with counts of how many passed vs. were filtered.

| Parameter | Required | Description |
|---|---|---|
| Query | Yes | The search query to rank documents against |
| Documents | Yes | Array of document text strings |
| Minimum Score | Yes | Relevance score threshold (0-1). Documents below this are excluded. |
| Model | No | `Cohere-rerank-v4.0-pro` (default) or `Cohere-rerank-v4.0-fast` |
| Top N | No | Maximum results to return after filtering |
| Max Tokens Per Document | No | Maximum tokens per document before truncation (default: 4096) |

**Returns:**

| Field | Description |
|---|---|
| Results | Array of `{ index, relevance_score, document }` above the threshold |
| Total Input | Number of documents submitted |
| Total Passed | Number of documents above the minimum score |
| Total Filtered | Number of documents removed |

## Example Workflow

**Power Automate RAG pipeline:**

1. **Search** — Query SharePoint or Dataverse for relevant documents
2. **Rerank and Filter** — Pass search results + user question to this connector with `min_score: 0.5`
3. **Generate** — Send only the high-relevance documents to an LLM (via Foundry, OpenAI, or any chat connector) as context

This pipeline reduces hallucinations by ensuring the LLM only sees truly relevant documents, not just keyword-matched results.

## MCP Protocol Support

This connector includes an MCP endpoint for Copilot Studio integration with two tools:

| Tool | Description |
|---|---|
| `rerank_documents` | Rerank documents by relevance score |
| `rerank_and_filter` | Rerank and filter out documents below a threshold |

## Model Comparison

| Model | Best For | Latency | Quality |
|---|---|---|---|
| **Cohere-rerank-v4.0-pro** | Maximum relevance accuracy | Higher | Best |
| **Cohere-rerank-v4.0-fast** | High-throughput, latency-sensitive flows | Lower | Good |

## Pricing

Cohere Rerank is billed per **search unit**. One search unit = one query with up to 100 documents. Documents longer than 4,096 tokens (including the query) are split into chunks, where each chunk counts as a separate document.

## Known Limitations

- Maximum recommended 1,000 documents per request
- Long documents are automatically truncated to `max_tokens_per_doc` (default 4,096 tokens)
- The model reranks by text similarity — it does not understand document structure (tables, images)
- Relevance scores are relative within a single request — scores are not comparable across different queries

## Authors

- Troy Taylor, troy@troystaylor.com, https://github.com/troystaylor
