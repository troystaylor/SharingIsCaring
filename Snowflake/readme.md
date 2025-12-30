# Snowflake MCP Connector

This repository contains the Power Automate custom connector implementation for Snowflake's Managed MCP Server.

## Setup

For complete setup instructions, refer to the official Snowflake documentation:
**[Snowflake Cortex Agents - MCP Integration Guide](https://docs.snowflake.com/en/user-guide/snowflake-cortex/cortex-agents-mcp)**

This guide covers:
- MCP server configuration in Snowflake
- OAuth integration setup
- Network policy requirements
- Connector deployment to Power Automate

## Files in This Repository

- **[apiDefinition.swagger.json](apiDefinition.swagger.json)** — Connector API definition (customize with your Snowflake endpoints)
- **[apiProperties.json](apiProperties.json)** — OAuth and connector metadata (customize with your credentials)
- **[script.csx](script.csx)** — Request/response transformation logic with Application Insights logging

## Quick Reference

- **Connector Endpoint**: POST to your Snowflake MCP server endpoint (configure in `apiDefinition.swagger.json`)
- **Authentication**: OAuth 2.0 via Snowflake custom OAuth integration (configure in `apiProperties.json`)
- **Protocol**: MCP (Model Context Protocol) v2025-06-18
- **Script**: Handles JSON-RPC ID normalization, protocol version enforcement, notification acknowledgment, and Application Insights logging

## Troubleshooting

### SystemError on First Chat Attempt
**Problem**: Copilot Studio returns "Error code: SystemError" on the first chat, but works on the second attempt.

**Root Cause**: The `notifications/initialized` request is being converted to a full `tools/list` response. Copilot Studio expects either no response or a simple acknowledgment for notifications.

**Solution**: Verify the `script.csx` returns a minimal acknowledgment `{"jsonrpc":"2.0","method":"notifications/initialized"}` for notification requests without forwarding to Snowflake.

### OAuth Authentication Failures
**Problem**: "not authorized" or "invalid_grant" errors from the OAuth token endpoint.

**Checklist**:
- Verify the OAuth client ID in `apiProperties.json` matches your Snowflake security integration
- Ensure OAuth scopes include both `refresh_token` and `session:role:ACCOUNTADMIN`
- Check that the security integration is `ENABLED = TRUE` and type `CUSTOM`
- Confirm the redirect URI in `apiProperties.json` exactly matches your connector's unique URL

### Protocol Version Mismatches
**Problem**: Snowflake rejects requests with `protocolVersion: "2024-11-05"`.

**Solution**: The `script.csx` automatically enforces `protocolVersion: "2025-06-18"` on all initialize requests. Verify the script is deployed and check Application Insights logs for transformation events.

### Network Policy Errors
**Problem**: "Network policy is required" or "Requestor IP not allowed".

**Solution**:
- Ensure your Snowflake network policy is bound to the user executing MCP requests
- Verify the policy includes all Power Automate egress IPs for your region
- Wait 30-60 seconds after applying policy changes for Snowflake to activate them

### Tool Discovery Issues
**Problem**: SYSTEM_EXECUTE_SQL tool not found or not discoverable.

**Checklist**:
- Verify MCP server exists: `SHOW MCP SERVERS;`
- Check tool configuration: `DESCRIBE MCP SERVER <server_name>;`
- Verify grants are set on the server
- Confirm the connector is using the correct database/schema/server path in `apiDefinition.swagger.json`

### Debugging with Application Insights
Enable logging by configuring the instrumentation key in `script.csx`. Events logged include:
- `OriginalRequest` — Raw request from Copilot Studio
- `TransformedRequest` — Request after transformations (ID normalization, protocol version, etc.)
- `SnowflakeResponse` — Response from Snowflake MCP server with latency
- `NotificationHandled` — Notification acknowledgment

Check the `Transformations` field to verify protocol adaptations are working.

## Support

For official setup guidance, refer to the [Snowflake Cortex Agents - MCP Integration Guide](https://docs.snowflake.com/en/user-guide/snowflake-cortex/cortex-agents-mcp).

For connector-specific issues, review the troubleshooting section above or check Application Insights logs.
