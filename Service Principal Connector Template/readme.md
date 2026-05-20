# Service Principal API Connector

This connector demonstrates how to authenticate to any API using an Azure AD service principal (client credentials flow).

## Overview
- Works with any API registered in Azure AD that supports service principal authentication (not just Microsoft Graph).
- Uses OAuth 2.0 client credentials flow to obtain tokens.
- Update the OAuth2 resource/scope and endpoints for your target API.

## How to Use
1. Register your API in Azure AD and grant permissions to your service principal.
2. Update the connector's OAuth2 settings (resource/scope) to match your API.
3. Update the OpenAPI definition and script as needed for your API endpoints.

## Files
- `apiDefinition.swagger.json` — OpenAPI definition
- `apiProperties.json` — Connector properties and OAuth config
- `script.csx` — C# script for request/response transformation

## Additional Guidance

### Registering Your API and Service Principal in Azure AD
- Register your API as an "App registration" in Azure AD (Microsoft Entra ID).
- Register a second app for the connector (client) and grant it permissions to the API.
- For step-by-step instructions, see:  
  [Create a custom connector for a web API (Set up Microsoft Entra ID authentication)](https://learn.microsoft.com/connectors/custom-connectors/create-web-api-connector#set-up-microsoft-entra-id-authentication)

### Configuring OAuth 2.0 in the Connector
- Use the client credentials (service principal) flow.
- Set the resource URL or scope to your API's App ID URI (e.g., `api://{your-api-client-id}/.default`).
- For Graph, use `https://graph.microsoft.com/.default`.
- See:  
  [Authenticate your API and connector with Microsoft Entra ID](https://learn.microsoft.com/connectors/custom-connectors/azure-active-directory-authentication)

### Security Best Practices
- Store secrets in Azure Key Vault when possible.
- Rotate client secrets regularly and update the connector before expiration.
- See:  
  [Manage authentication within Service Connector](https://learn.microsoft.com/azure/service-connector/how-to-manage-authentication)

### Managed Identity (Optional)
- For Azure-hosted connectors, you can use managed identity instead of a client secret.
- See:  
  [Use Microsoft MCP Server for Enterprise from Copilot Studio](https://learn.microsoft.com/graph/mcp-server/use-enterprise-mcp-server-copilot-studio#configure-the-custom-connector-in-power-apps)

## Adaptation Notes
- This pattern is not limited to Microsoft Graph. It can be used for any Azure AD-secured API.
- For Graph, set the resource/scope to `https://graph.microsoft.com/.default`. For other APIs, use their App ID URI or scope.
