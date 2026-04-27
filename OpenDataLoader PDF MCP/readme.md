# OpenDataLoader PDF MCP

Power MCP connector for [OpenDataLoader PDF](https://github.com/opendataloader-project/opendataloader-pdf) — the #1-ranked open-source PDF parser (0.907 overall accuracy). Enables Copilot Studio agents to extract structured text, tables, and metadata from PDFs using deterministic local processing with optional AI hybrid mode.

## Prerequisites

- Azure subscription
- Azure CLI installed
- Power Platform environment with pac CLI

## Quick Deploy (Pre-built Image)

The fastest path — uses the pre-built image from GitHub Container Registry:

```powershell
cd "OpenDataLoader PDF MCP/infra"
.\deploy.ps1 -ResourceGroup rg-opendataloader -UseGhcrImage
```

This provisions Azure Container Apps infrastructure and deploys `ghcr.io/troystaylor/opendataloader-pdf-api:latest`. The script outputs your service URL and API key.

## Deploy (Build from Source)

Build the container image in your own Azure Container Registry:

```powershell
cd "OpenDataLoader PDF MCP/infra"
.\deploy.ps1 -ResourceGroup rg-opendataloader
```

### Deploy Script Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| ResourceGroup | Yes | — | Azure resource group (created if needed) |
| Location | No | westus2 | Azure region |
| ApiKey | No | auto-generated | API key for the service |
| SkipInfra | No | false | Skip Bicep deployment |
| SkipBuild | No | false | Skip container image build |
| ImageTag | No | latest | Container image tag |
| UseGhcrImage | No | false | Use pre-built GHCR image |

## Architecture

```
┌────────────────────┐     ┌──────────────────────────────┐
│  Copilot Studio    │     │  Azure Container Apps        │
│  Agent             │     │                              │
│                    │ MCP │  ┌──────────────────────┐    │
│  ┌──────────────┐  │────>│  │  Flask API (Python)  │    │
│  │ OpenDataLoader│  │     │  │  + OpenDataLoader PDF│    │
│  │ PDF MCP      │  │<────│  │  + Java 21 JRE       │    │
│  │ (connector)  │  │     │  └──────────────────────┘    │
│  └──────────────┘  │     │                              │
└────────────────────┘     └──────────────────────────────┘
```

Documents are processed entirely within the container — no external API calls, no data leaving the tenant.

## Azure Resources Deployed

| Resource | SKU | Purpose |
|----------|-----|---------|
| Container Registry | Basic | Stores container image |
| Log Analytics Workspace | PerGB2018 | Container logs |
| Application Insights | Web | Telemetry and monitoring |
| Container Apps Environment | — | Hosting environment |
| Container App | 1 CPU / 2Gi | OpenDataLoader PDF API |

## Connector Setup

After deployment, the script outputs the service URL and API key.

1. Update `apiDefinition.swagger.json` host field to your service FQDN
2. Deploy the connector:
   ```powershell
   pac connector create --settings-file apiProperties.json --api-definition apiDefinition.swagger.json --script script.csx
   ```
3. Create a connection using your API key

## MCP Tools

| Tool | Description |
|------|-------------|
| `convert_pdf` | Convert PDF to Markdown, JSON (with bounding boxes), HTML, or text |
| `extract_tables` | Extract tables with row/column structure and cell content |
| `get_page_elements` | Get elements with bounding boxes and semantic types for RAG citations |
| `check_accessibility` | Check PDF accessibility tags for EAA/ADA/Section 508 compliance |
| `get_server_info` | Get service version, capabilities, and configuration |

## Direct Operations

In addition to MCP tools for Copilot Studio, the connector exposes direct REST operations for Power Automate flows:

| Operation | Description |
|-----------|-------------|
| Convert PDF | Convert a PDF to the specified output format |
| Extract Tables | Extract tables from a PDF |
| Get Page Elements | Get structured elements with bounding boxes |
| Check PDF Accessibility | Check PDF structure tags |
| Get Server Info | Get service capabilities |

## Service API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health` | Health check (no auth) |
| GET | `/api/info` | Service capabilities |
| POST | `/api/convert` | Convert PDF to markdown/json/html/text |
| POST | `/api/tables` | Extract tables |
| POST | `/api/elements` | Get structured elements with bounding boxes |
| POST | `/api/accessibility` | Check accessibility tags |

All POST endpoints accept:

```json
{
    "source": "https://example.com/document.pdf",
    "sourceType": "url"
}
```

Or base64-encoded content:

```json
{
    "source": "<base64-encoded-pdf>",
    "sourceType": "base64"
}
```

## Use Cases

### PDF to Markdown for RAG

> "Convert this PDF to markdown so I can analyze its contents"

### Table Extraction

Extract structured tables from financial reports, invoices, or data sheets:

> "Extract all tables from this quarterly report PDF"

### Document Analysis with Citations

Get element-level data with bounding boxes for source citations:

> "Analyze this research paper and show me where each finding is located"

### Accessibility Compliance

Check if organizational PDFs meet accessibility standards:

> "Check if this PDF has proper accessibility tags for EAA compliance"

## Application Insights

The deploy script outputs the App Insights instrumentation key. To enable telemetry in the connector, edit `script.csx`:

```csharp
private const string APP_INSIGHTS_KEY = "your-instrumentation-key-here";
```

## GitHub Actions (CI/CD)

A workflow at `.github/workflows/opendataloader-pdf-build.yml` builds and publishes the container image to `ghcr.io/troystaylor/opendataloader-pdf-api`.

**Triggers:**
- Manual dispatch (`workflow_dispatch`) with optional version tag
- Push to `main` when files in `OpenDataLoader PDF MCP/container-app/` change

**Tags applied:** `latest`, git SHA

## Project Structure

```
OpenDataLoader PDF MCP/
├── apiDefinition.swagger.json    # Swagger with MCP + REST operations
├── apiProperties.json            # Connector properties (API key auth)
├── script.csx                    # MCP protocol handler
├── readme.md
├── container-app/
│   ├── app.py                    # Flask REST API wrapping opendataloader-pdf
│   ├── Dockerfile                # Python 3.12 + Java 21 JRE
│   └── requirements.txt
└── infra/
    ├── main.bicep                # Azure infrastructure
    └── deploy.ps1                # Deployment script
```

## OpenDataLoader PDF Capabilities

| Capability | Mode | License |
|-----------|------|---------|
| Text extraction with reading order | Local | Free (Apache 2.0) |
| Bounding boxes for every element | Local | Free |
| Table extraction (simple) | Local | Free |
| Table extraction (complex/borderless) | Hybrid | Free |
| Heading hierarchy detection | Local | Free |
| Image extraction with coordinates | Local | Free |
| OCR for scanned PDFs (80+ languages) | Hybrid | Free |
| Formula extraction (LaTeX) | Hybrid | Free |
| AI chart/image descriptions | Hybrid | Free |
| Prompt injection filtering | Local | Free |

## Links

- [OpenDataLoader PDF GitHub](https://github.com/opendataloader-project/opendataloader-pdf)
- [OpenDataLoader Documentation](https://opendataloader.org/)
- [Hybrid Mode Guide](https://opendataloader.org/docs/hybrid-mode)
- [JSON Schema Reference](https://opendataloader.org/docs/reference/json-schema)
