# Snowflake MCP Connector

Snowflake Model Context Protocol connector for Microsoft Copilot Studio providing comprehensive SQL execution, database management, and query optimization capabilities through a unified interface. This connector enables seamless integration of Snowflake's powerful data platform with Microsoft's AI-driven applications.

## Publisher: Troy Taylor

## Prerequisites

- **Snowflake Account**: Active Snowflake account (Standard edition or higher)
- **OAuth Integration**: Custom OAuth security integration configured in Snowflake
- **Network Policy**: Appropriate network policy binding for MCP server access
- **Credentials**: OAuth client ID and client secret from Snowflake security integration
- **MCP Server**: Snowflake MCP server deployed and configured

## Supported Operations

This connector implements the Model Context Protocol (MCP) with 556+ prompts covering comprehensive Snowflake functionality and 32 dynamic resources reflecting actual account state.

### tools/list
Retrieve complete list of 556+ available prompts/tools across all Snowflake feature areas including SQL execution, performance tuning, security management, and advanced capabilities.

### tools/get
Get detailed prompt template for any of the 556+ tools, including argument definitions, parameter specifications, and usage guidance for specific operations.

### tools/call
Execute/call any available tool with appropriate arguments. Supports all prompt-based operations including SQL queries, configuration management, and resource inspection.

### resources/list
Retrieve list of 32 dynamic resources organized by category (data objects, compute, security, pipelines, integration, monitoring) that reflect actual account state.

### resources/read
Read detailed information about specific resources by URI (e.g., `snowflake://databases`, `snowflake://account/roles`) with configuration and status details.

### notifications/initialized
Handle MCP initialization notifications. Connector automatically acknowledges initialization without forwarding to backend.

## Obtaining Credentials

1. **Navigate to Snowflake Account**:
   - Log in to your Snowflake account
   - Go to Admin > Security integrations

2. **Create OAuth Integration**:
   - Click "Create" and select "API Integration"
   - Configure as OAuth provider
   - Set redirect URI to your Power Automate connector URL

3. **Get Credentials**:
   - Copy the generated Client ID
   - Generate and copy the Client Secret
   - Note the authorization and token endpoint URLs

4. **Configure Connector**:
   - Update `apiProperties.json` with Client ID and Client Secret
   - Update `apiDefinition.swagger.json` with your Snowflake endpoint
   - Verify network policy allows Power Automate egress IPs

## Getting Started

1. **Deploy the Connector**:
   - Upload `apiDefinition.swagger.json` and `script.csx` to your custom connector
   - Configure authentication with your OAuth credentials
   - Test connectivity with a simple query

2. **Configure MCP Server**:
   - Ensure your Snowflake MCP server is running
   - Verify the server path in connector configuration
   - Test with `SHOW MCP SERVERS` command

3. **Use in Copilot Studio**:
   - Add the connector as an action in your copilot
   - Test with one of the 556+ available prompts
   - Monitor responses using Application Insights

## Known Issues and Limitations

- **Initial Chat Delay**: First chat attempt may show "SystemError" due to notification handling; retry resolves the issue
- **Protocol Version Enforcement**: Connector automatically converts protocol versions to 2025-06-18; ensure Snowflake MCP server supports this version
- **Network Policy Requirements**: All MCP requests must pass Snowflake network policy checks; whitelist Power Automate egress IPs
- **OAuth Scope Limitations**: Certain operations require ACCOUNTADMIN role; ensure OAuth integration has appropriate scopes
- **Query Timeout**: Large queries may timeout; implement result pagination for better performance
- **Resource URI Format**: Resource URIs must follow strict validation patterns; invalid formats are rejected for security

## Frequently Asked Questions

### How do I find my Snowflake account URL?

Your Snowflake account URL follows the format: `https://<account_id>.<region>.snowflakecomputing.com`. You can find it in the Snowflake console URL bar after login.

### What is the correct OAuth redirect URI format?

The redirect URI must exactly match your Power Automate custom connector's unique URL. It typically follows: `https://logic.powerappsportals.com/callbacks/<connector-id>`. Verify exact spelling and protocol (https only).

### Can I use the connector without MCP?

No, this connector requires an active Snowflake MCP server. Standard Snowflake drivers or APIs cannot be used directly with this connector. MCP server must be deployed and accessible at your configured endpoint.

### How do I enable Application Insights logging?

Update the instrumentation key in `script.csx` to your Application Insights instance. Logs include request transformations, protocol version conversions, and Snowflake response latency.

### What do I do if OAuth authentication fails?

Verify: (1) Client ID matches your security integration, (2) Client Secret is correct, (3) Scopes include `refresh_token`, (4) Network policy allows the redirect URI, (5) Security integration is ENABLED = TRUE.
