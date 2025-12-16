# #SharingIsCaring

A collection of Power Platform custom connectors for Microsoft Graph APIs and document collaboration services.

## Connectors

### SharePoint Embedded Connector

Manage Microsoft SharePoint Embedded file storage containers with 40 comprehensive operations including:

- **Container Management** - Create, list, update, restore, and delete containers
- **File Operations** - Upload, download, list, and delete files within containers  
- **Metadata Management** - Create custom columns for document classification
- **Versioning & Sharing** - Full version history and sharing controls
- **Permissions** - Advanced permission management and delegation (beta)
- **Search & Migration** - Container search and content migration capabilities (beta)

**Features:**
- Dual API version support: v1.0 (stable) and beta (latest features)
- OAuth 2.0 authentication with Microsoft Entra ID
- MCP protocol support for natural language tool discovery in Copilot Studio
- Policy-based automatic routing to correct API version

**Location:** [`sharepoint-embedded-connector/`](sharepoint-embedded-connector/)

**Quick Links:**
- [Connector README](sharepoint-embedded-connector/readme.md)
- [API Definition](sharepoint-embedded-connector/apiDefinition.swagger.json)
- [Microsoft Learn - SharePoint Embedded](https://learn.microsoft.com/sharepoint/dev/embedded/overview)

## Getting Started

1. Clone this repository
2. Choose a connector directory
3. Deploy to Power Platform using the Custom Connectors import feature
4. Configure authentication and required permissions
5. Use in Power Automate flows or Copilot Studio agents

## Authentication

All connectors use OAuth 2.0 with Microsoft Entra ID. Ensure:
- Application is registered in your tenant
- Required Graph API permissions are granted
- Admin consent is provided for delegated flows

## License

These connectors are provided as-is for use with Microsoft Power Platform.

## Support

For issues or questions:
- Check the connector-specific README files
- Review Microsoft Learn documentation
- Open an issue in this repository
