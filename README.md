# #SharingIsCaring

A collection of Power Platform custom connectors and MCP servers for Microsoft Graph APIs, document collaboration, and enterprise services.

## Connectors & MCP Servers

### SharePoint Embedded Connector

Manage Microsoft SharePoint Embedded file storage containers with 40 comprehensive operations.

**Features:**
- 40 operations across 6 categories (Container, File, Metadata, Versioning, Permissions, Search)
- Dual API version support: v1.0 (stable) and beta (latest features)
- OAuth 2.0 authentication with Microsoft Entra ID
- MCP protocol support for natural language tool discovery in Copilot Studio
- Policy-based automatic routing to correct API version

**Location:** [`SharePoint Embedded/`](SharePoint%20Embedded/)
- [Connector README](SharePoint%20Embedded/readme.md)
- [API Definition](SharePoint%20Embedded/apiDefinition.swagger.json)

### Agent 365 MCP

Integrate enterprise agent capabilities for Microsoft 365 scenarios through the Model Context Protocol.

**Location:** [`Agent 365 MCP/`](Agent%20365%20MCP/)

### Copilot Retrieval

Enable intelligent document retrieval and search capabilities for Copilot Studio.

**Location:** [`Copilot Retrieval/`](Copilot%20Retrieval/)

### PII Redactor Connector

Detect and redact personally identifiable information (PII) from text content with support for multiple regions and jurisdictions.

**Location:** [Connector Source](pii-redactor-connector/) (if available in workspace)

### Teams Transcript Connector

Process and manage Microsoft Teams meeting transcripts through a Power Platform connector.

**Location:** [Connector Source](teams-transcript-connector/) (if available in workspace)

### MCP Management Connector

Manage Model Context Protocol servers and tools programmatically.

**Location:** [Connector Source](mcp-management-connector/) (if available in workspace)

### MS Learn MCP

Query and retrieve Microsoft Learn documentation through the Model Context Protocol.

**Location:** [Connector Source](MS%20Learn%20MCP/) (if available in workspace)

## Getting Started

1. **Clone the repository**
   ```bash
   git clone https://github.com/troystaylor/SharingIsCaring.git
   cd SharingIsCaring
   ```

2. **Choose a connector or MCP server**
   - Review the README in the specific folder
   - Check apiDefinition.swagger.json for operation details

3. **Deploy to Power Platform (Connectors)**
   - Power Automate → Custom Connectors → Import OpenAPI
   - Upload the `apiDefinition.swagger.json` file
   - Configure connection parameters
   - Enable code if required (script.csx present)

4. **Deploy to Copilot Studio (MCP Servers)**
   - Configure the MCP endpoint
   - Set up authentication
   - Add as a custom tool in Copilot Studio

## Architecture

Each connector typically includes:
- **apiDefinition.swagger.json** - OpenAPI 2.0 specification with 40+ operations
- **apiProperties.json** - Connection parameters, policies, and branding
- **script.csx** - C# code for MCP protocol support (where applicable)
- **readme.md** - Comprehensive documentation

## Authentication

Most connectors use OAuth 2.0 with Microsoft Entra ID. Ensure:
- Application is registered in your tenant
- Required Graph API permissions are granted
- Admin consent is provided for delegated flows

## MCP Protocol Support

Connectors with MCP support expose operations as natural language tools discoverable in:
- Copilot Studio
- GitHub Copilot
- Claude Desktop
- Other MCP-compatible clients

MCP endpoints support:
- `initialize` - Establish protocol connection
- `tools/list` - Discover available tools
- `tools/call` - Execute tool operations
- `logging/setLevel` - Configure logging

## Development

Each connector includes:
- Complete operation specifications
- Input/output schemas
- Authentication configuration
- Policy templates for dynamic routing
- MCP tool definitions

Connectors are designed following Microsoft Power Platform best practices:
- OpenAPI 2.0 compliance for Power Platform certification
- x-ms-* extensions for Power Automate integration
- Proper error handling and validation
- Comprehensive documentation

## Resources

- [SharePoint Embedded Documentation](https://learn.microsoft.com/sharepoint/dev/embedded/overview)
- [Power Platform Custom Connectors](https://learn.microsoft.com/connectors/custom-connectors/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Microsoft Graph API](https://learn.microsoft.com/graph/overview)
- [Copilot Studio Documentation](https://learn.microsoft.com/microsoft-copilot-studio/)

## License

These connectors are provided as-is for use with Microsoft Power Platform.

## Support

For issues or questions:
- Check the connector-specific README files
- Review Microsoft Learn documentation
- Open an issue in this repository
- Contact the connector developer

## Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request with detailed description
