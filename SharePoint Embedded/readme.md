# SharePoint Embedded Connector

A Power Platform custom connector for Microsoft SharePoint Embedded, enabling file storage container management and document collaboration through Microsoft Graph APIs with dual API version support and MCP protocol integration.

## Overview

SharePoint Embedded is a cloud-based file and document management system suitable for any application. This connector provides comprehensive access to:

- **File Storage Containers** - Create and manage containers for storing files and documents
- **File Operations** - Upload, download, delete, copy, move files within containers
- **Metadata Management** - Add custom columns for organizing and classifying content
- **Versioning & Sharing** - Full version history, sharing links, and collaborative features
- **Permissions** - Advanced permission management and delegation (beta)
- **Recycle Bin** - Recover deleted items and containers
- **Search & Migration** - Find containers and migrate content (beta)
- **Collaboration** - Leverage Microsoft 365 features like compliance and audit

## Key Features

### Dual API Version Support
- **v1.0 (Stable)** - Generally available production APIs with full support
- **beta (Latest)** - Preview capabilities with latest features
- Connection parameter allows runtime API version switching without code changes
- Policy-based automatic routing to correct endpoint

### 40 Comprehensive Operations
The connector exposes 40 operations organized into 6 categories:
- **Container Management** (9 ops) - CRUD, lock, unlock, permanent delete
- **File & Item Operations** (12 ops) - Upload, download, list, move, copy, restore
- **Metadata & Columns** (5 ops) - Define and manage custom metadata
- **Item Versioning & Sharing** (5 ops) - Version history, restore, sharing links
- **Permissions** (5 ops) - List, grant, update, remove permissions (beta)
- **Search & Advanced** (4 ops) - Container search, migration jobs (beta)

### MCP Protocol Support
- Natural language tool discovery in Copilot Studio
- All 40 operations exposed as discoverable MCP tools
- Seamless integration with AI agents and Copilot
- JSON-RPC 2.0 protocol at `/mcp` endpoint

### Policy-based Routing
- Automatic routing to correct API endpoint based on selected version
- Query parameter injection for version-specific behavior
- Transparent API version handling in Power Automate flows

## Architecture

```
SharePoint Embedded/
├── apiDefinition.swagger.json    # OpenAPI 2.0 with Microsoft extensions (40 ops)
├── apiProperties.json            # Connection parameters & policy templates
├── script.csx                    # MCP protocol implementation (C#)
└── readme.md                      # This file
```

## Complete Operation List

### Container Management (9 Operations)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| List Containers | GET | `/storage/fileStorage/containers` | v1.0, beta | Retrieve all file storage containers in the tenant |
| Create Container | POST | `/storage/fileStorage/containers` | v1.0, beta | Create a new file storage container |
| Get Container | GET | `/storage/fileStorage/containers/{id}` | v1.0, beta | Get details of a specific container |
| Update Container | PATCH | `/storage/fileStorage/containers/{id}` | v1.0, beta | Update container properties |
| Delete Container | DELETE | `/storage/fileStorage/containers/{id}` | v1.0, beta | Soft delete a container (recoverable) |
| Restore Container | POST | `/storage/fileStorage/containers/{id}/restore` | v1.0, beta | Restore a deleted container |
| Lock Container | POST | `/storage/fileStorage/containers/{id}/lock` | **beta** | Lock container to prevent modifications |
| Unlock Container | POST | `/storage/fileStorage/containers/{id}/unlock` | **beta** | Unlock a previously locked container |
| Permanently Delete Container | POST | `/storage/fileStorage/containers/{id}/permanentDelete` | **beta** | Permanently delete a container (non-recoverable) |

## Authentication

The connector uses OAuth 2.0 authentication with Microsoft Entra ID:

- **Scope**: `https://graph.microsoft.com/.default`
- **Permissions Required**: `FileStorageContainer.Selected` (Application)
- **Authentication Flow**: Azure AD Certificate Authentication
- **Tenant**: Consuming tenant where containers are created

## Getting Started

1. **Register Application**: Create a Microsoft Entra ID app with Graph permissions
2. **Grant Consent**: Admin consent required on consuming tenant
3. **Create Container Type**: Define container type in owning tenant
4. **Register Container Type**: Register on consuming tenant
5. **Deploy Connector**: Import to Power Platform
6. **Create Connection**: Create connector connection with credentials

## Resources

- [SharePoint Embedded Overview](https://learn.microsoft.com/sharepoint/dev/embedded/overview)
- [Microsoft Graph fileStorageContainer API](https://learn.microsoft.com/graph/api/resources/filestoragecontainer)
- [Model Context Protocol](https://modelcontextprotocol.io/)

## License

These connectors are provided as-is for use with Microsoft Power Platform.
