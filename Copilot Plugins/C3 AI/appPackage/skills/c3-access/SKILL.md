---
name: c3-access
description: Check which C3 tools you can use, how the App MCP and Central MCP connectors differ, and what to do when a tool is missing or a call fails. Use first in any C3 AI task, and whenever a tool call returns an authorization error or the tool you expect is not listed.
---

# C3 access and tool discovery

C3 AI has two connectors. Pick the right one before calling anything.

| Connector | Use it for | Acts on |
| --- | --- | --- |
| **C3 App MCP** | Your own application's data, types, metrics, tests and custom tools | One running C3 application |
| **C3 Central MCP** | C3 platform documentation, Type and Transform generation, coding guidance | The C3 platform in general, not your data |

## Discover the tools

1. Read the live tool list from the connector. Don't assume a tool exists. App administrators choose which tools are enabled, and each application adds its own.
2. Tools that C3 seeds into every App MCP server:

   | Tool | Default | Purpose |
   | --- | --- | --- |
   | `runPyCode` | On | Run Python on the C3 platform |
   | `runJsCode` | On | Run JavaScript on the C3 platform |
   | `getC3TypeInformation` | Off | Type documentation by name |
   | `getEntityTypes` | Off | List entity types in the package |
   | `entityFieldRetriever` | Off | Public fields of an entity type |
   | `entityDataRetriever` | Off | Query entity data with filters and aggregation |
   | `entityDataModifier` | Off | Create, update and delete records |
   | `listTypeMetrics` | Off | Metrics available for time series data |
   | `runTest` | Off | Run a Python or JavaScript test, results in YAML |
   | `readPkgIssues` | Off | Validate types, implementations and seed data |
   | `upsertSeedData` | Off | Save seed data to the database |

3. Read each tool's description and input schema before the first call. Parameter names and required fields vary by application, so don't guess them.

## When something is missing

- **Tool not listed.** An App Admin must turn on **Enabled for MCP** under **MCP > Tools** in C3 AI Studio. If the user has toolkit filtering, the tool must also be in one of their toolkits. Tell the user which tool you need and stop.
- **401 or 403.** The user's C3 token is missing, expired or revoked. Tokens last 12 months. They generate a new one under **MCP > Access** and update the API key registration. Don't ask them to paste the token into chat.
- **No tools at all.** The MCP server may be switched off. Only an App Admin can turn it on from the **MCP > Tools** page header.
- **Wrong application.** The connector points at one application. If the user names a different app or environment, say so and don't answer from this one.

## Rules

- Calls run as the signed-in user under their C3 roles. If C3 denies something, report it. Don't try another tool to get around it.
- Never print or ask for a C3 token, and never put one in code you write.
- For anything that changes data or runs code, apply `c3-write-safety`.
- Users with the App Admin role can review every call in **MCP > Monitor**. Other users see only their own.
