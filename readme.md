# #SharingIsCaring

A collection of Power Platform custom connectors, MCP servers, and connector code samples for Microsoft Graph APIs, document collaboration, and enterprise services.

## Connectors & MCP Servers

### Agent 365 MCP

Integrate enterprise agent capabilities for Microsoft 365 scenarios through the Model Context Protocol.

**Location:** [`Agent 365 MCP/`](Agent%20365%20MCP/)

### Connector-Code

Power Platform custom connector code samples and scripts for various authentication methods, data transformations, and API integrations.

**Features:**
- Bearer Token, JWT, OAuth 2.0, and PKCE authentication examples
- Data transformation scripts (JSON/XML, hashing, null handling)
- API-specific implementations (Tomorrow.io, Copilot Retrieval, Moneris)
- Copilot Instructions for VS Code workspace validation

**Location:** [`Connector-Code/`](Connector-Code/)
- [Connector-Code README](Connector-Code/README.md) - Full index of all code samples

### Copilot Retrieval

Enable intelligent document retrieval and search capabilities for Copilot Studio.

**Location:** [`Copilot Retrieval/`](Copilot%20Retrieval/)

### Graph Mail

Microsoft Graph Mail connector with MCP support for Copilot Studio agents. Optimized for token efficiency with bodyPreview defaults.

**Features:**
- 5 MCP tools: listMessages, getMessage, createMessage, sendMail, replyMessage
- Token-optimized with previewOnly parameter (defaults to true)
- Dual Graph API version support (v1.0 and beta)
- OAuth 2.0 authentication with Mail.Read and Mail.Send scopes
- Designed specifically for Copilot Studio natural language tool discovery

**Location:** [`Graph Mail/`](Graph%20Mail/)
- [Graph Mail README](Graph%20Mail/readme.md)
- [API Definition](Graph%20Mail/apiDefinition.swagger.json)

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
   - Power Automate  Custom Connectors  Import OpenAPI
   - Upload the `apiDefinition.swagger.json` file
   - Configure connection parameters
   - Enable code if required (script.csx present)

4. **Deploy to Copilot Studio (MCP Servers)**
   - Configure the MCP endpoint
   - Set up authentication
   - Add as a custom tool in Copilot Studio

## Architecture

Each connector typically includes:
- **apiDefinition.swagger.json** - OpenAPI 2.0 specification with operations
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
