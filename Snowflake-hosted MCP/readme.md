# Snowflake-hosted MCP

Passthrough connector that connects Microsoft Copilot Studio agents to a **Snowflake-managed MCP server**.

Snowflake hosts the Model Context Protocol server, owns tool discovery, and executes every tool. This connector contains **no `script.csx`** — it forwards JSON-RPC 2.0 traffic straight through to Snowflake and returns the response unmodified. The tools your agent sees are exactly the tools declared in your `MCP SERVER` object specification.

## Publisher: Troy Taylor

## Why there is no script

A Snowflake-managed MCP server is already a spec-compliant remote MCP server. Any transformation layer in the connector would:

- Rewrite the negotiated `protocolVersion` and break the handshake (Snowflake supports revision `2025-11-25`)
- Buffer `text/event-stream` responses, stalling streaming and risking the Power Platform request timeout
- Shadow real Snowflake tool names with locally fabricated ones
- Advertise prompts and resources that the server does not implement — **Snowflake currently supports tool capabilities only**

Passthrough is the correct design here.

## Prerequisites

- A Snowflake account with Cortex features enabled
- An `MCP SERVER` object created in a database and schema
- A Snowflake OAuth security integration (`TYPE = OAUTH`, `OAUTH_CLIENT = CUSTOM`)
- A least-privileged MCP access role, granted to each user who will connect
- Each connecting user must have both `DEFAULT_ROLE` and `DEFAULT_WAREHOUSE` set — **sessions fail to initialize when `DEFAULT_WAREHOUSE` is null**

## Supported tool types

Declared in the MCP server specification, not in this connector:

| Tool type | Purpose |
|-----------|---------|
| `CORTEX_AGENT_RUN` | Cortex Agent — governed orchestration over Analyst, Search, and custom tools |
| `CORTEX_ANALYST_MESSAGE` | Text-to-SQL over a semantic view (semantic **models** are not supported) |
| `CORTEX_SEARCH_SERVICE_QUERY` | Unstructured search over a Cortex Search Service |
| `SYSTEM_EXECUTE_SQL` | Direct SQL execution |
| `GENERIC` | Custom UDFs and stored procedures |

Snowflake recommends exposing a **Cortex Agent as the only client-facing tool** for governed business questions. Putting `SYSTEM_EXECUTE_SQL` on the same server lets the agent's semantic views and verified queries be bypassed — if you need direct SQL, expose it through a separate MCP server with its own least-privileged role.

## Setup

### 1. Create the MCP server

```sql
CREATE OR REPLACE MCP SERVER my_db.my_schema.my_mcp_server
FROM SPECIFICATION $$
tools:
  - title: "Governed business data agent"
    name: "business_data_agent"
    type: "CORTEX_AGENT_RUN"
    identifier: "my_db.my_schema.my_agent"
    description: "Answers governed business data questions."
$$;
```

Verify it exists:

```sql
SHOW MCP SERVERS IN SCHEMA my_db.my_schema;
DESCRIBE MCP SERVER my_db.my_schema.my_mcp_server;
```

### 2. Create the access role and grant privileges

Access to the MCP server does **not** grant access to its tools. Grant each one explicitly.

```sql
CREATE ROLE mcp_access_role;

GRANT DATABASE ROLE SNOWFLAKE.CORTEX_AGENT_USER TO ROLE mcp_access_role;
GRANT USAGE ON WAREHOUSE my_warehouse TO ROLE mcp_access_role;
GRANT USAGE ON DATABASE my_db TO ROLE mcp_access_role;
GRANT USAGE ON SCHEMA my_db.my_schema TO ROLE mcp_access_role;
GRANT USAGE ON MCP SERVER my_db.my_schema.my_mcp_server TO ROLE mcp_access_role;
GRANT USAGE ON AGENT my_db.my_schema.my_agent TO ROLE mcp_access_role;

-- Grant only what the agent actually uses
GRANT USAGE ON CORTEX SEARCH SERVICE my_db.my_schema.my_search_service TO ROLE mcp_access_role;
GRANT SELECT ON SEMANTIC VIEW my_db.my_schema.my_semantic_view TO ROLE mcp_access_role;
GRANT SELECT ON TABLE my_db.my_schema.my_table TO ROLE mcp_access_role;

GRANT ROLE mcp_access_role TO USER my_user;

ALTER USER my_user SET DEFAULT_ROLE = 'mcp_access_role' DEFAULT_WAREHOUSE = 'my_warehouse';
```

Do not grant the MCP access role to `PUBLIC`.

### 3. Create the OAuth security integration

Create the connector in Power Platform first so you know its redirect URI, then run:

```sql
CREATE OR REPLACE SECURITY INTEGRATION powerplatform_mcp_oauth
  TYPE = OAUTH
  OAUTH_CLIENT = CUSTOM
  ENABLED = TRUE
  OAUTH_CLIENT_TYPE = 'CONFIDENTIAL'
  OAUTH_REDIRECT_URI = 'https://global.consent.azure-apim.net/redirect/UNIQUE_IDENTIFIER_FOR_THIS_ENVIRONMENT'
  OAUTH_USE_SECONDARY_ROLES = NONE
  ALLOWED_ROLES_LIST = ('MCP_ACCESS_ROLE');
```

Retrieve the credentials — the integration name is case sensitive and must be uppercase:

```sql
SELECT SYSTEM$SHOW_OAUTH_CLIENT_SECRETS('POWERPLATFORM_MCP_OAUTH');
```

### 4. Update the connector files

In [apiDefinition.swagger.json](./apiDefinition.swagger.json):

| Field | Replace with |
|-------|--------------|
| `host` | Your account URL, e.g. `myorg-myaccount.snowflakecomputing.com` |
| `basePath` | `/api/v2/databases/<database>/schemas/<schema>/mcp-servers/<name>` |
| `authorizationUrl` | `https://<account_url>/oauth/authorize` |
| `tokenUrl` | `https://<account_url>/oauth/token-request` |

In [apiProperties.json](./apiProperties.json):

| Field | Replace with |
|-------|--------------|
| `clientId` / `clientSecret` | Values from `SYSTEM$SHOW_OAUTH_CLIENT_SECRETS` |
| `AuthorizationUrl` / `TokenUrl` / `RefreshUrl` | Same account URL as above |
| `redirectUrl` | The redirect URI shown on your connector's Security page |

> **Use hyphens, not underscores, in the account URL.** MCP servers have known connection issues with hostnames containing underscores.

### 5. Deploy

```powershell
pac auth create --environment <environment-id>
pac connector create --api-definition apiDefinition.swagger.json --api-properties apiProperties.json
```

Create a connection, complete the Snowflake consent screen, then add the connector to a Copilot Studio agent. The agent's tool list populates from `tools/list` against your MCP server.

## OAuth scopes and role behavior

OAuth scopes control only the **primary** role. Secondary roles are governed by `OAUTH_USE_SECONDARY_ROLES` on the security integration, not by scopes.

| Scope | Effect |
|-------|--------|
| `session:role:all` | Uses the user's `DEFAULT_ROLE`. Despite the name, it does **not** activate every role or secondary roles. This is the default advertised scope. |
| `session:role:<role_name>` | Uses that named role as primary. Must be advertised via `OAUTH_SCOPES_SUPPORTED`. |
| `session:role-any` | Any primary role granted to the user, when any-role mode is enabled. |

To pin a specific role instead of relying on `DEFAULT_ROLE`, advertise it and update both connector files:

```sql
ALTER SCHEMA my_db.my_schema SET OAUTH_SCOPES_SUPPORTED = 'session:role:MCP_ACCESS_ROLE';
```

The connector ships with `session:role:all` and `refresh_token`, which matches Snowflake's default and its least-privilege guidance.

### External OAuth (Microsoft Entra ID)

To authenticate against Entra ID instead of Snowflake OAuth, create an `EXTERNAL_OAUTH` security integration and bind it:

```sql
ALTER SCHEMA my_db.my_schema SET OAUTH_AUTHORIZATION_SERVER = external_oauth_entra;
```

Then point `AuthorizationUrl`, `TokenUrl`, and `RefreshUrl` at your Entra endpoints. Snowflake accepts tokens **only** from the bound integration — a Snowflake OAuth token is rejected once External OAuth is bound.

## Network policies

If your account has network policies enabled, allow the outbound IP addresses of the Power Platform / Azure API Management infrastructure. The request reaches Snowflake from Microsoft's infrastructure, not from the user's browser.

```sql
CREATE NETWORK RULE mcp_client_ingress_rule
  MODE = INGRESS
  TYPE = IPV4
  VALUE_LIST = ('<ip_1>', '<ip_2>');

ALTER NETWORK POLICY my_policy ADD ALLOWED_NETWORK_RULE_LIST = ('mcp_client_ingress_rule');
```

A blocked token request returns `invalid_client` from `/oauth/token-request` — the same error as bad credentials, so check the network policy before assuming the client ID or secret is wrong.

If your account uses PrivateLink, configure the connector with the **public** MCP server URL and set `USE_PRIVATELINK_FOR_AUTHORIZATION_ENDPOINT = TRUE` on the security integration.

## Known issues and limitations

- **Tool capabilities only.** Snowflake does not serve `prompts/*` or `resources/*`. Agents see tools and nothing else.
- **Large agent payloads.** `CORTEX_AGENT_RUN` returns all intermediate steps by design — reasoning traces, tool calls, search results, and citations — which can exceed 200 KB. Set `max_results` on the agent's search tool resources to reduce payload size.
- **Long-running tools.** Cortex Agent runs can approach the Power Platform request timeout. Set `query_timeout` on SQL and custom tools to fail fast.
- **Semantic views only.** Cortex Analyst through MCP supports semantic views, not semantic models.
- **No dynamic client registration.** Client ID and secret must be configured explicitly.
- **Recursion.** Snowflake enforces a maximum recursion depth of 10 invocations. Avoid configurations where an agent tool can call back into another agent.
- **One server per connector.** `basePath` is static. To target a second MCP server, deploy a second copy of the connector.

## Frequently asked questions

### Why does the agent see no tools?

Usage on the MCP server does not imply usage on its tools. Confirm the connecting user's effective role has `USAGE` on the MCP server **and** on every agent, search service, function, and procedure it exposes.

### Why does authentication succeed but every tool call fail?

Most often the user has no `DEFAULT_WAREHOUSE`, so the session cannot initialize. Set both defaults with `ALTER USER`.

### The consent screen says "secondary roles = ALL" — is that a problem?

No. When a client requests `session:role:all`, the consent screen may display that label even when the integration sets `OAUTH_USE_SECONDARY_ROLES = NONE`. The label is cosmetic; Snowflake enforces the integration setting.

### How do I point this at a different MCP server?

Edit `basePath` in [apiDefinition.swagger.json](./apiDefinition.swagger.json) and redeploy. If the new server lives in the same account, the OAuth configuration is unchanged — a single security integration can issue tokens valid across multiple MCP servers in that account.

### How is this different from the Snowflake connector in this repo?

The [Snowflake](../Snowflake/) connector carries a large `script.csx` that serves its own prompts, resources, and tools, and was built for a self-hosted MCP server. This connector is a passthrough to Snowflake's managed server and adds no transformation layer.

## Reference

- [Snowflake-managed MCP server](https://docs.snowflake.com/en/user-guide/snowflake-cortex/cortex-agents-mcp)
- [CREATE SECURITY INTEGRATION (Snowflake OAuth)](https://docs.snowflake.com/en/sql-reference/sql/create-security-integration-oauth-snowflake)
- [Account identifiers](https://docs.snowflake.com/en/user-guide/admin-account-identifier)
- [Getting Started with Managed Snowflake MCP Server](https://quickstarts.snowflake.com/guide/getting-started-with-snowflake-mcp-server/index.html)
