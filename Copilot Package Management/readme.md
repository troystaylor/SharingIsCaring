# Copilot Package Management

Manage Microsoft 365 Copilot agents and apps using the Package Management API. This connector exposes the Microsoft Graph beta Package Management API for IT administrators to inventory, inspect, block, unblock, update, and reassign ownership of packages in the tenant catalog.

## Publisher: Troy Taylor

## Prerequisites

- A [Microsoft Agent 365](https://www.microsoft.com/microsoft-agent-365) license
- An Entra ID app registration with the following **delegated** permissions:
  - `CopilotPackages.Read.All` — for listing and viewing packages
  - `CopilotPackages.ReadWrite.All` — for blocking, unblocking, updating, and reassigning packages
- Global Administrator or appropriate admin role to manage packages

## Supported Operations

### Standard Operations (Power Automate)

| Operation | Description |
|-----------|-------------|
| **List Packages** | Retrieve all Copilot agents and apps in the tenant catalog. Supports `$filter` on `supportedHosts`, `elementTypes`, and `lastModifiedDateTime`. |
| **Get Package Details** | Get detailed metadata for a specific package including element details, categories, and access information. |
| **Update Package** | Update the allowed and acquired users/groups for a package. |
| **Block Package** | Block a package to prevent usage across the organization. |
| **Unblock Package** | Unblock a package to allow usage. |
| **Reassign Package** | Reassign package ownership to a different user. |

### MCP Tools (Copilot Studio)

The connector exposes an MCP endpoint with 6 tools:

| Tool | Description |
|------|-------------|
| `list_packages` | List all packages with optional filters for host, element type, and modified date. |
| `get_package_details` | Get detailed metadata for a specific package. |
| `block_package` | Block a package to prevent usage. |
| `unblock_package` | Unblock a package to allow usage. |
| `update_package_access` | Update allowed and acquired users/groups for availability and deployment control. |
| `reassign_package` | Reassign package ownership to a new user. |

## Obtaining Credentials

1. Go to [Entra ID App Registrations](https://entra.microsoft.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Create a new registration (or use an existing one)
3. Under **API Permissions**, add Microsoft Graph delegated permissions:
   - `CopilotPackages.Read.All`
   - `CopilotPackages.ReadWrite.All`
4. Grant admin consent for the permissions
5. Under **Authentication**, add the redirect URI: `https://global.consent.azure-apim.net/redirect`
6. Note the **Application (client) ID** for connector configuration

## Getting Started

1. Create the connector using [PAC CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction):
   ```
   pac connector create --settings-file apiProperties.json --api-definition-file apiDefinition.swagger.json --script-file script.csx
   ```
2. Update `apiProperties.json` with your app registration Client ID
3. Create a connection using your Microsoft 365 admin account

### Filter Examples

List only Copilot agents:
```
$filter=supportedHosts/any(h:h eq 'Copilot')
```

List packages with declarative agents:
```
$filter=elementTypes/any(h:h eq 'DeclarativeAgent')
```

List recently modified packages:
```
$filter=lastModifiedDateTime gt 2026-01-01T00:00:00Z
```

## Application Insights Logging

To enable Application Insights telemetry, edit `script.csx` and set the `APP_INSIGHTS_CONNECTION_STRING` constant to your Application Insights connection string:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=your-key;IngestionEndpoint=https://dc.services.visualstudio.com/";
```

## Known Issues and Limitations

- This API is in **beta** and subject to change. Do not use in production.
- Only available in the Global service cloud (not US Government or China).
- The `List packages` operation only supports **delegated** permissions (not application-only).
- Requires a Microsoft Agent 365 license.

## API Documentation

- [Package Management API Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/overview)
- [copilotPackage Resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/resources/copilotpackage)
- [copilotPackageDetail Resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/resources/copilotpackagedetail)
