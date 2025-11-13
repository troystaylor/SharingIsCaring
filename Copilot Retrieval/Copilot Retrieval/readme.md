# Microsoft 365 Copilot Retrieval
The Microsoft 365 Copilot Retrieval API allows you to retrieve relevant text extracts from SharePoint, OneDrive, and Copilot connectors content while respecting access controls and compliance settings. Use this API to ground your generative AI solutions with Microsoft 365 data while optimizing for context recall.

## Publisher: Troy Taylor

## Prerequisites
- Microsoft 365 Copilot add-on license (required for all users)
- Delegated permissions (work or school account only; application permissions not supported)

## Obtaining Credentials
This connector uses OAuth 2.0 authentication with Microsoft Entra ID. The following permissions are required:
- `Files.Read.All` - Required for SharePoint and OneDrive retrieval
- `Sites.Read.All` - Required for SharePoint and OneDrive retrieval
- `ExternalItem.Read.All` - Required for Copilot connectors retrieval

## Supported Operations
### Retrieve grounding data
Retrieves relevant text extracts from SharePoint, OneDrive, or Copilot connectors based on a natural language query. Returns up to 25 results with relevance scores and metadata.
### Invoke MCP Server
Invokes the Model Context Protocol (MCP) server for AI agent integration. Provides tools for retrieval operations (retrieve_from_sharepoint, retrieve_from_onedrive, retrieve_from_copilot_connectors, retrieve_multi_source).

## Known Issues and Limitations
- Requires Microsoft 365 Copilot add-on license for all users
- Query string limited to 1,500 characters
- Maximum 25 results per request
- Rate limit: 200 requests per user per hour
- File size limits: .docx/.pptx/.pdf max 512 MB, other extensions max 150 MB
- Semantic retrieval only for .doc, .docx, .pptx, .pdf, .aspx, .one files (SharePoint/OneDrive)
- Invalid KQL syntax executes without scoping (no error returned)
- Results optimized for context recall, not precision/ranking
