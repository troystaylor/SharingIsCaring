# SharingIsCaring

A collection of Power Platform custom connectors with Model Context Protocol (MCP) integration for Microsoft Copilot Studio agents.

## Overview

This repository contains custom connectors that operate in dual mode:
- **Direct API operations** for Power Automate flows and Power Apps
- **MCP protocol support** for natural language tool invocation in Copilot Studio

All connectors use custom code (C# scripts) to implement both REST API handlers and MCP JSON-RPC 2.0 protocol, eliminating the need for external servers or Azure dependencies.

## Connectors

### Agent 365 MCP
[View Documentation](Agent%20365%20MCP/)

---

### Applications Insights Logging
[View Documentation](Applications%20Insights%20Logging/)

---

### Bookings

Microsoft Bookings connector with **30 MCP tools** for managing booking businesses, services, staff, customers, and appointments via Microsoft Graph API.

[View Documentation](Bookings/)

---

### Connector-Code

Collection of connector code samples and patterns.

[View Documentation](Connector-Code/)

---

### Copilot Licensing

Copilot Studio credits consumption monitoring and licensing management.

[View Documentation](Copilot%20Licensing/)

---

### Copilot Retrieval
[View Documentation](Copilot%20Retrieval/)

---

### Crunchbase

Access to Crunchbase company, people, and funding data.

[View Documentation](Crunchbase/)

---

### Dataverse Custom API

Execute custom Dataverse APIs via Power Platform connector.

[View Documentation](Dataverse%20Custom%20API/)

---

### Dataverse Power Agent
[View Documentation](Dataverse%20Power%20Agent/)

---

### Dataverse Power Orchestration Tools

**49 MCP tools** exposing comprehensive Dataverse operations plus orchestration capabilities.

**Features:**
- 45 Dataverse Web API operations across 11 categories (READ, WRITE, BULK, RELATIONSHIPS, METADATA, etc.)
- 4 orchestration tools for intelligent tool discovery and execution:
  - `discover_functions` — Find available tools/resources/prompts
  - `invoke_tool` — Trigger a specific tool
  - `orchestrate_plan` — Coordinate multi-step operations
  - `learn_patterns` — Retrieve organizational learning from Dataverse
- Dynamic tool loading from Dataverse table (`tst_agentinstructions`)
- Self-learning pattern recognition and storage
- OAuth 2.0 authentication with Microsoft Dataverse

[View Documentation](Dataverse%20Power%20Orchestration%20Tools/)

---

### Graph Calendar

Microsoft Graph Calendar operations with MCP support for Copilot Studio agents.

[View Documentation](Graph%20Calendar/)

---

### Graph Hashes

File hash computation and verification for Microsoft Graph files with **4 MCP tools**.

**Features:**
- **QuickXorHash** algorithm (OneDrive/SharePoint standard)
- **SHA1** and **CRC32** hash computation
- File integrity verification against Microsoft Graph stored hashes
- Change detection and deduplication

[View Documentation](Graph%20Hashes/)

---

### Graph Mail

Microsoft Graph Mail operations for email management via Power Platform.

[View Documentation](Graph%20Mail/)

---

### Microsoft Places

Microsoft Places API integration for workspace and location management.

[View Documentation](Microsoft%20Places/)

---

### Microsoft Users

Comprehensive user profile, organizational hierarchy, presence, and people discovery with **25 MCP tools**. Enhanced alternative to Microsoft's first-party User Profile MCP Server.

[View Documentation](Microsoft%20Users/)

---

### SharePoint Embedded

SharePoint Embedded container and content management.

[View Documentation](SharePoint%20Embedded/)

---

### Snowflake

Snowflake data warehouse connector for Power Platform.

[View Documentation](Snowflake/)

## Architecture

### Custom Code Connectors

Each connector follows this pattern:

```
connector-folder/
├── apiDefinition.swagger.json   # OpenAPI 2.0 with x-ms-* extensions
├── apiProperties.json           # Marks operations using scriptOperations
├── script.csx                   # C# (.NET Standard 2.0) dual-mode handler
└── readme.md                    # Documentation
```

### Dual-Mode Implementation

**script.csx** routes requests by path:
- `/mcp` endpoint → MCP JSON-RPC 2.0 protocol (`initialize`, `tools/list`, `tools/call`)
- `/operation` endpoints → Direct REST API for Power Automate/Power Apps

### MCP Protocol Support

Connectors marked with `"x-ms-agentic-protocol": "mcp-streamable-1.0"` in OpenAPI definitions expose:
- **tools/list** — Returns available MCP tools with descriptions and JSON schemas
- **tools/call** — Executes tools by name with natural language arguments
- **initialize** — Capability negotiation with Copilot Studio

## Getting Started

### Prerequisites

- Power Platform environment
- Appropriate Microsoft 365 licenses
- Microsoft Graph API permissions (varies by connector)
- Power Automate or Copilot Studio license

### Deployment

1. Navigate to [Power Platform maker portal](https://make.powerapps.com)
2. Select target environment
3. **Data** → **Custom connectors** → **New custom connector** → **Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json` from connector folder
5. On **Code** tab, enable custom code
6. Paste contents of `script.csx`
7. **Create connector**
8. Test connection with OAuth flow

### Using in Power Automate

Add connector actions to flows like any other connector. Operations appear as standard actions with IntelliSense support.

### Using in Copilot Studio

1. Add connector to your Copilot Studio environment
2. Create connection with appropriate permissions
3. Enable actions in agent's knowledge sources
4. Agent automatically discovers and invokes tools based on natural language

## Technical Details

### Branding

All connectors use **Microsoft Orange Red** (`#da3b01`) as the brand color in `apiProperties.json`.

### Authentication

- **OAuth 2.0** with Microsoft Entra ID (Azure AD)
- Scopes vary by connector (Graph API, Dataverse, etc.)
- No API keys or connection strings required

### Limitations

- **Power Platform execution limits** — 5-10 second timeout for connector operations
- **File size limits** — Base64 encoding overhead, typically 50 MB max
- **Read-only where applicable** — Some connectors don't support write operations
- **Graph API rate limits** — Standard Microsoft Graph throttling applies

## Development

### Validation

```powershell
# Install Power Platform CLI
paconn login  # Device code flow

# Validate connector
cd connector-folder
paconn validate --api-def apiDefinition.swagger.json
```

### Testing

Connectors cannot run locally. Deploy to Power Platform and test via:
- Flow action tester in Power Automate
- "Test" tab in connector designer
- Copilot Studio agent conversations

## Contributing

This repository represents production connectors in use. Contributions welcome for:
- Bug fixes
- Documentation improvements
- New MCP tool suggestions
- Performance optimizations

## License

MIT License - see individual connector folders for specific licensing details.

## Contact

**Developer**: Troy Taylor  
**Email**: troytaylor@microsoft.com  
**Organization**: Microsoft

## Resources

- [Power Platform Custom Connectors](https://learn.microsoft.com/connectors/custom-connectors/)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Microsoft Graph API](https://learn.microsoft.com/graph/)
- [Copilot Studio Documentation](https://learn.microsoft.com/microsoft-copilot-studio/)
- [Power Automate Connector Development](https://learn.microsoft.com/connectors/custom-connectors/define-blank)

---

⭐ **Star this repo** if you find these connectors useful!
