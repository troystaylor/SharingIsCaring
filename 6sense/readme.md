# 6sense Revenue AI

## Overview

Custom connector for [6sense Revenue AI](https://6sense.com/) that provides B2B company identification, firmographic enrichment, lead scoring, people enrichment, and people search. Works with both **Copilot Studio** (via MCP) and **Power Automate** (via REST operations).

## Strategy

This connector has 7 tools (< 8 threshold) and ships with **per-tool MCP only**. No Mission Command variant is needed.

## Prerequisites

- 6sense platform subscription (specific packages required per API)
- API token from Settings > API Token Management in 6sense
- Appropriate API credits for the endpoints you intend to use

## Setup

1. Import the connector files into Power Platform
2. Create a new connection using your 6sense API token
3. **Copilot Studio**: Add the connector to your agent — the MCP endpoint exposes all 7 tools automatically
4. **Power Automate**: Use the individual REST operations listed below

## Authentication

Uses API Token authentication. The token is passed as `Authorization: Token <api_token>` header to all 6sense API endpoints.

## Operations

### MCP Endpoint (Copilot Studio)

| Operation | Description |
|-----------|-------------|
| `InvokeMCP` | MCP endpoint exposing all 7 tools below for Copilot Studio agents |

### REST Operations (Power Automate)

| Operation | Method | 6sense API | Credits | Description |
|-----------|--------|-----------|---------|-------------|
| `IdentifyCompany` | POST | Company Identification v3 | Company Identification | Identify anonymous website visitors by IPv4 address |
| `EnrichCompany` | POST | Company Firmographics v3 | Enrichment | Enrich leads with company firmographics (requires at least one of: email, domain, or company name) |
| `ScoreAndEnrichLead` | POST | Lead Scoring + Firmographics | Enrichment | Combined scoring and enrichment in one call |
| `ScoreLead` | POST | Lead Scoring | None | Score leads with predictive scores by email |
| `EnrichPeople` | POST | People Enrichment v2 | People Enrichment | Enrich contacts with person-level data (up to 25 per call) |
| `SearchPeople` | POST | People Search v2 | None | Search for B2B contacts by domain, title, location, etc. |
| `SearchPeopleDictionary` | GET | People Search Dictionary | None | Get filter values for people search |

### MCP Tool ↔ REST Operation Mapping

| MCP Tool | REST Operation |
|----------|---------------|
| `identify_company` | `IdentifyCompany` |
| `enrich_company` | `EnrichCompany` |
| `score_and_enrich_lead` | `ScoreAndEnrichLead` |
| `score_lead` | `ScoreLead` |
| `enrich_people` | `EnrichPeople` |
| `search_people` | `SearchPeople` |
| `search_people_dictionary` | `SearchPeopleDictionary` |

## Multi-Host Routing

6sense uses multiple API hosts. The script routes each operation to the correct host:

| Host | Operations |
|------|------------|
| `epsilon.6sense.com` | IdentifyCompany |
| `api.6sense.com` | EnrichCompany, EnrichPeople, SearchPeople, SearchPeopleDictionary |
| `scribe.6sense.com` | ScoreAndEnrichLead, ScoreLead |

## Rate Limits

- General APIs: 100 requests per minute
- People Search API: 10 queries per second
- People Enrichment API: 20 queries per second

## Input Validation

- `EnrichCompany` requires at least one identifier (`email`, `domain`, or `company`). Calls with no identifiers will return an error.

## Example Prompts (Copilot Studio)

- "Identify the company at IP address 203.0.113.50"
- "Enrich the lead with email john@acme.com"
- "Score the lead jane@example.com"
- "Search for VP-level contacts at 6sense.com in the US"
- "What filter values are available for people search on microsoft.com?"

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition with MCP + 7 REST operations |
| `apiProperties.json` | Connector metadata and script operation routing |
| `script.csx` | Per-tool MCP handler + REST operation routing |
| `readme.md` | This file |
