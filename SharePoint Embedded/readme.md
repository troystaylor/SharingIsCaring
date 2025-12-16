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

### File & Item Operations (12 Operations)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| Get Container Drive | GET | `/storage/fileStorage/containers/{id}/drive` | v1.0, beta | Get the drive associated with a container |
| List Container Items | GET | `/drives/{id}/items/{id}/children` | v1.0, beta | List files and folders in a container |
| Upload File | PUT | `/drives/{id}/items/{id}/content` | v1.0, beta | Upload a new file or update existing content |
| Delete File | DELETE | `/drives/{id}/items/{id}` | v1.0, beta | Soft delete a file or folder |
| Get Drive Item | GET | `/drives/{id}/items/{id}` | v1.0, beta | Get details of a file or folder |
| Move or Rename Item | PATCH | `/drives/{id}/items/{id}` | v1.0, beta | Move or rename a file or folder |
| Copy Item | POST | `/drives/{id}/items/{id}/copy` | v1.0, beta | Create a copy of a file or folder |
| Create Folder | POST | `/drives/{id}/items/{id}/children` | v1.0, beta | Create a new folder |
| List Recycle Bin Items | GET | `/storage/fileStorage/containers/{id}/recycleBin` | v1.0, beta | List deleted items |
| Get Recycle Bin Item | GET | `/storage/fileStorage/containers/{id}/recycleBin/items/{id}` | v1.0, beta | Get details of a deleted item |
| Restore Recycle Bin Item | POST | `/storage/fileStorage/containers/{id}/recycleBin/items/{id}/restore` | **beta** | Restore a deleted item |
| Get Download URL | GET | `/drives/{id}/items/{id}/@microsoft.graph.downloadUrl` | v1.0, beta | Get direct download URL for a file |

### Metadata & Columns (5 Operations)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| List Container Columns | GET | `/storage/fileStorage/containers/{id}/columns` | v1.0, beta | List custom columns for a container |
| Create Container Column | POST | `/storage/fileStorage/containers/{id}/columns` | v1.0, beta | Create a new custom column |
| Get Container Column | GET | `/storage/fileStorage/containers/{id}/columns/{id}` | v1.0, beta | Get column definition details |
| Update Container Column | PATCH | `/storage/fileStorage/containers/{id}/columns/{id}` | v1.0, beta | Update column properties |
| Delete Container Column | DELETE | `/storage/fileStorage/containers/{id}/columns/{id}` | v1.0, beta | Delete a custom column |

### Item Versioning & Sharing (5 Operations)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| List Item Versions | GET | `/drives/{id}/items/{id}/versions` | v1.0, beta | Get all versions of a file |
| Restore Item Version | POST | `/drives/{id}/items/{id}/versions/{id}/restore` | v1.0, beta | Restore a previous file version |
| Get Item Metadata | GET | `/drives/{id}/items/{id}/listitem/fields` | v1.0, beta | Get custom metadata values |
| Update Item Metadata | PATCH | `/drives/{id}/items/{id}/listitem/fields` | v1.0, beta | Update custom metadata values |
| Create Sharing Link | POST | `/drives/{id}/items/{id}/createLink` | v1.0, beta | Create a sharing link (view, edit, embed) |

### Permissions (5 Operations - Beta)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| List Container Permissions | GET | `/storage/fileStorage/containers/{id}/permissions` | **beta** | Get all permissions on a container |
| Grant Container Permission | POST | `/storage/fileStorage/containers/{id}/permissions` | **beta** | Grant permissions to users or groups |
| Update Container Permission | PATCH | `/storage/fileStorage/containers/{id}/permissions/{id}` | **beta** | Update a permission grant |
| Remove Container Permission | DELETE | `/storage/fileStorage/containers/{id}/permissions/{id}` | **beta** | Remove a permission grant |
| Invite to Share | POST | `/drives/{id}/items/{id}/invite` | v1.0, beta | Send sharing invitations |

### Search & Advanced (4 Operations - Beta)

| Operation | Method | Endpoint | API Version | Description |
|-----------|--------|----------|-------------|-------------|
| Search Containers | POST | `/search/query` | **beta** | Search containers by name or properties |
| List Migration Jobs | GET | `/storage/fileStorage/containerMigrationJobs` | **beta** | Get all migration jobs |
| Create Migration Job | POST | `/storage/fileStorage/containerMigrationJobs` | **beta** | Create a new migration job |
| Get Migration Job | GET | `/storage/fileStorage/containerMigrationJobs/{id}` | **beta** | Get migration job status and details |

## Connection Parameters

| Parameter | Type | Description | Values |
|-----------|------|-------------|--------|
| **token** | OAuth 2.0 | Microsoft Entra ID authentication | Azure AD app credentials |
| **apiVersion** | String | Microsoft Graph API version | `v1.0` (Stable), `beta` (Latest) |

## MCP Protocol Integration

All 40 operations are exposed as MCP tools for natural language discovery in Copilot Studio:

- **MCP Endpoint**: `/mcp` (JSON-RPC 2.0)
- **Protocol Version**: `2025-06-18`
- **Available Methods**: 
  - `initialize` - Initialize MCP connection
  - `tools/list` - Discover all 40 available tools
  - `tools/call` - Execute a tool operation
  - `logging/setLevel` - Configure logging

### Example MCP Tool Discovery
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}
```

Response includes all 40 operations with descriptions and input schemas for Copilot integration.

## Authentication

The connector uses OAuth 2.0 authentication with Microsoft Entra ID:

- **Scope**: `https://graph.microsoft.com/.default`
- **Permissions Required**: `FileStorageContainer.Selected` (Application)
- **Authentication Flow**: Azure AD Certificate Authentication
- **Tenant**: Consuming tenant where containers are created

### Required Graph Permissions

**Delegated (User)**
- `FileStorageContainer.Selected` - Access containers on consuming tenant

**Application (Service)**
- `FileStorageContainer.Selected` - Programmatic container access

## Policy Templates

### Route to Endpoint
Automatically routes requests to the correct API version endpoint based on the selected connection parameter. Unversioned paths are converted to versioned endpoints:
```
/storage/fileStorage@{apiVersion}/containers
```

### Set Query Parameter
Injects the selected API version into query parameters for version-specific behavior.

## Billing

SharePoint Embedded uses consumption-based billing:
- **Storage**: Per GB stored in containers
- **API Transactions**: Per Microsoft Graph API call
- **Egress**: Per GB of data downloaded

See [Billing Meters](https://learn.microsoft.com/sharepoint/dev/embedded/administration/billing/meters) for details.

## Getting Started

1. **Register Application**: Create a Microsoft Entra ID app with Graph permissions
   ```
   Graph Permissions: FileStorageContainer.Selected (Application)
   ```

2. **Grant Consent**: Admin consent required on consuming tenant
   - Navigate to Azure Portal → App Registrations
   - Grant admin consent for `FileStorageContainer.Selected`

3. **Create Container Type**: Define container type in owning tenant
   - Use REST API or PowerShell
   - Define properties and structure

4. **Register Container Type**: Register on consuming tenant
   - Link owning tenant container type
   - Enable for your tenant

5. **Deploy Connector**: Import to Power Platform
   - Power Automate → Custom Connectors → Import OpenAPI
   - Upload `apiDefinition.swagger.json`
   - Enable code on Code tab
   - Deploy

6. **Create Connection**: Create connector connection with credentials
   - Select API Version: `v1.0` (Stable) or `beta` (Latest)
   - Authenticate with service principal

## API Version Selection

### v1.0 (Stable) - Recommended for Production
- Generally available features with full support
- Stable endpoint contracts and behavior
- Full production support guarantee
- Includes all container CRUD, file operations, metadata, restore operations
- Best for: Business-critical applications requiring stability

### beta - For Latest Features
- Preview capabilities subject to change
- May change without notice
- Experimental features and early access
- Includes: Lock/unlock containers, advanced migrations, experimental metadata
- Best for: Development, testing, exploring new capabilities

## Common Use Cases

1. **Document Management** 
   - Store and organize documents within tenant boundaries
   - Apply custom metadata for classification
   - Leverage versioning and audit trails

2. **Multi-tenant Applications**
   - Isolated storage per customer in their own tenant
   - Secure data separation
   - Compliance with data residency requirements

3. **Content Governance**
   - Apply sensitivity labels and DLP policies
   - Manage retention and eDiscovery
   - Audit all access and modifications

4. **Collaboration**
   - Co-authoring with Office applications
   - Create and manage sharing links
   - Version history and rollback capabilities

5. **Compliance & Audit**
   - eDiscovery for legal holds
   - Retention policy enforcement
   - Comprehensive audit logging

## Troubleshooting

### API Version Conflicts
If operations fail with version mismatch:
1. Check selected API version in connection parameters
2. Verify endpoint availability for selected version
3. Consult [Microsoft Graph changelog](https://learn.microsoft.com/graph/changelog) for version-specific features
4. Some beta operations only work with `beta` API version selected

### Authentication Errors
- Ensure service principal has `FileStorageContainer.Selected` permission
- Verify admin consent granted on consuming tenant
- Check token expiration and refresh settings
- Confirm app registration exists in correct tenant

### Container Not Found
- Verify container exists in consuming tenant
- Check container hasn't been permanently deleted (use `beta` Permanent Delete)
- Confirm container type registration on consuming tenant
- List containers first to verify IDs

### MCP Protocol Issues
- Verify `/mcp` endpoint is accessible
- Check request format matches JSON-RPC 2.0 specification
- Ensure `jsonrpc: "2.0"` and `id` are included in requests
- Review `tools/list` response to verify tool discovery

## Resources

- [SharePoint Embedded Overview](https://learn.microsoft.com/sharepoint/dev/embedded/overview)
- [Microsoft Graph fileStorageContainer Resource](https://learn.microsoft.com/graph/api/resources/filestoragecontainer)
- [Drive and DriveItem Resources](https://learn.microsoft.com/graph/api/resources/drive)
- [SharePoint Embedded Authentication](https://learn.microsoft.com/sharepoint/dev/embedded/development/auth)
- [Billing and Usage Meters](https://learn.microsoft.com/sharepoint/dev/embedded/administration/billing/meters)
- [Microsoft Graph Changelog](https://learn.microsoft.com/graph/changelog)
- [Model Context Protocol](https://modelcontextprotocol.io/)

## Support

For issues or feature requests:
- [SharePoint Embedded Feedback](https://forms.microsoft.com/r/1YpGd2pAUS)
- [Microsoft Graph Issues](https://github.com/microsoftgraph/msgraph-sdk-dotnet/issues)
- [Power Platform Community](https://powerusers.microsoft.com/)
- This Repository Issues: Submit questions and feedback

## License

These connectors are provided as-is for use with Microsoft Power Platform.
