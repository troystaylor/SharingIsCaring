# SharePoint Embedded Connector

A Power Platform custom connector for Microsoft SharePoint Embedded, enabling file storage container management and document collaboration through Microsoft Graph APIs.

## Overview

SharePoint Embedded is a cloud-based file and document management system suitable for any application. This connector provides API-only access to:

- **File Storage Containers** - Create and manage containers for storing files and documents
- **File Operations** - Upload, download, delete files within containers
- **Metadata Management** - Add custom columns for organizing content
- **Collaboration** - Leverage Microsoft 365 features like versioning, sharing, and compliance

## Key Features

### Dual API Version Support
- **v1.0** - Stable production APIs with full GA support
- **beta** - Latest features including advanced metadata and migration capabilities
- Connection parameter allows switching between versions without code changes

### Core Operations (40 Total)
- **Container Management**: Create, list, update, restore, and delete containers
- **File Management**: Upload, download, list, and delete files
- **Metadata**: Create custom columns for document classification and organization
- **Versioning & Sharing**: Full version history and sharing controls
- **Permissions**: Advanced permission management and delegation
- **Search & Migration**: Container search and content migration capabilities

### MCP Protocol Support
- Natural language tool discovery in Copilot Studio
- All 40 operations exposed as discoverable MCP tools
- Seamless integration with Copilot agents

## Authentication

The connector uses OAuth 2.0 authentication with Microsoft Entra ID:

- **Scope**: `https://graph.microsoft.com/.default`
- **Permissions Required**: `FileStorageContainer.Selected` (Application)
- **Tenant**: Consuming tenant where containers are created

## Getting Started

1. **Register Application**: Create a Microsoft Entra ID app with Graph permissions
2. **Grant Consent**: Admin consent required on consuming tenant
3. **Create Container Type**: Define container type in owning tenant
4. **Register Container Type**: Register on consuming tenant
5. **Connect**: Use connector in Power Automate with v1.0 or beta API

## Common Use Cases

1. **Document Management** - Store and organize documents in tenant boundary
2. **Multi-tenant Apps** - Isolated storage per customer in their tenant
3. **Content Governance** - Apply sensitivity labels and DLP policies
4. **Collaboration** - Co-authoring with Office, sharing, versioning
5. **Compliance** - eDiscovery, retention policies, audit logging

## Resources

- [SharePoint Embedded Overview](https://learn.microsoft.com/sharepoint/dev/embedded/overview)
- [Microsoft Graph fileStorageContainer API](https://learn.microsoft.com/graph/api/resources/filestoragecontainer)
- [SharePoint Embedded Authentication](https://learn.microsoft.com/sharepoint/dev/embedded/development/auth)
