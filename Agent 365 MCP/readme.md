# Agent 365 MCP Server

Power Platform custom connector that exposes the Microsoft Agent 365 management and Work IQ MCP servers via MCP protocol for Copilot Studio. This gives Power Platform agents the same capabilities that the [Agent 365 SDK and CLI](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/) provide to pro-code developers — blueprint management, governed Work IQ tool access, admin governance, and MCP server lifecycle operations.

## Context

Microsoft Agent 365 is a platform layer that enhances agents built on *any* framework (Semantic Kernel, LangChain, OpenAI, Claude, etc.) with enterprise capabilities: Entra-backed agent identity, OpenTelemetry observability, governed Work IQ tools for M365 data, and notifications via Teams/Outlook/email.

The [Agent 365 Skills](https://techcommunity.microsoft.com/blog/agent-365-blog/agent-365-skills-bring-your-agents-into-microsoft-agent-365-in-minutes/4529838) (`gh skill add microsoft/agent365-skills`) provide AI-guided onboarding for pro-code developers in GitHub Copilot, Claude Code, and VS Code. This connector provides the equivalent Power Platform on-ramp — letting Copilot Studio agents and Power Automate flows interact with the same Agent 365 management plane.

## What it does

- MCP protocol endpoint with `x-ms-agentic-protocol: mcp-streamable-1.0`
- Routes requests to 11 Agent 365 MCP servers based on tool name
- Exposes Work IQ tools (Mail, Calendar, Teams, SharePoint, Word) for governed M365 data access
- Provides MCPManagement tools for creating/updating MCP servers and agent tooling
- Includes AdminTools for Agent 365 governance operations
- Lets Copilot Studio agents discover and call tools via natural language

## Available MCP Servers

| Server | Description |
|--------|-------------|
| `mcpmanagement` | MCP server lifecycle — create, update, delete servers and tools |
| `admintools` | Agent 365 governance and administration |
| `searchtools` | Copilot Search across Microsoft 365 content |
| `me` | User profile (manager, reports, profile info) |
| `dataverse` | Dataverse CRUD and domain tools |
| `mail` | Outlook Mail (create, update, delete, reply) |
| `calendar` | Outlook Calendar (create/update events, accept/decline) |
| `odspremoteserver` | OneDrive/SharePoint file operations |
| `sharepointlisttools` | SharePoint list tools (list, create, update items) |
| `teams` | Teams (chat, channel, membership, messaging) |
| `word` | Word document tools (create/read, comments) |

## Files

- `apiDefinition.swagger.json` — OpenAPI 2.0 with MCP operation and OAuth2 security
- `apiProperties.json` — OAuth2 settings (no additional connection parameters)
- `script.csx` — MCP JSON-RPC proxy with tool routing to Agent 365 servers (hard-code `EnvId` before deploying)

## Prerequisites

1. **Azure AD App Registration** for OAuth2:
   - Create a new app registration in Azure portal
   - Add redirect URI: `https://global.consent.azure-apim.net/redirect`
   - API permissions: Add `https://agent365.svc.cloud.microsoft` as a custom API with `.default` scope
   - Copy the Application (client) ID
2. **Dataverse Environment ID**: Your Power Platform environment GUID (hard-coded in `script.csx`)

## Import & Deploy

1. Edit `script.csx` — set `EnvId` constant to your Dataverse environment GUID
2. Import via Maker portal → Custom connectors → Import OpenAPI (apiDefinition.swagger.json)
3. Security: Configure OAuth2 (AAD) with your app registration `clientId` and scope `https://agent365.svc.cloud.microsoft/.default`
4. Create a connection and test

## Copilot Studio Usage

- Add this connector to your agent; Copilot Studio detects the MCP endpoint
- Agent can call tools across all 11 servers via natural language
- Work IQ tools (mail, calendar, teams, word, sharepoint) enable governed M365 data access
- MCPManagement tools let agents create/manage other MCP servers programmatically

## Relationship to Agent 365 Skills

The [Agent 365 Skills](https://github.com/microsoft/agent365-skills) help pro-code developers onboard their agents into Agent 365 via coding assistants. This connector serves a complementary purpose:

| Surface | Audience | How it connects |
|---------|----------|-----------------|
| Agent 365 Skills | Developers in VS Code/Claude Code/GitHub Copilot | AI-guided setup, observability, Work IQ wiring, testing |
| Agent 365 CLI | Developers at the command line | Blueprint creation, deployment, publishing |
| **This connector** | Power Platform makers and Copilot Studio agents | MCP access to the same management plane and Work IQ tools |

## Notes

- Security: OAuth2 (Azure AD) using `https://agent365.svc.cloud.microsoft/.default` scope; headers are handled by the platform
- Protocol: JSON-RPC 2.0; methods `initialize`, `tools/list`, `tools/call` are forwarded
- Rate limits & permissions: Enforced by the Agent 365 server
- Agent blueprints and identity are managed server-side via Entra; this connector operates within that governance boundary
