# Kensho Connector

A Power Platform custom connector for the **S&P Global / Kensho LLM-Ready API (kFinance)** Model Context Protocol (MCP) server. It lets Copilot Studio agents and Power Automate flows call Kensho's financial-data MCP tools using the OAuth 2.0 Authorization Code flow.

## OAuth endpoints

| Setting | Value |
|---|---|
| Authorization URL | `https://kfinance.kensho.com/integrations/authorize` |
| Token URL | `https://kfinance.kensho.com/integrations/token` |
| Refresh URL | `https://kfinance.kensho.com/integrations/token` |
| Scopes | `kensho:app:kfinance` `offline_access` |
| Token auth | `client_secret_post` / `client_secret_basic` |
| Grant types | `authorization_code`, `refresh_token` |

`offline_access` returns the refresh token so users authenticate once and later calls refresh silently.

## Prerequisites

1. A Kensho LLM-Ready API account (Okta credentials are emailed when you sign up for a trial/account).
2. An OAuth client registered with Kensho. Obtain a **Client ID** (and secret, if issued) via Kensho's registration endpoint (`https://kfinance.kensho.com/integrations/register`) or by contacting Kensho support.
3. The Power Platform consent redirect URL allowlisted with Kensho:
   `https://global.consent.azure-apim.net/redirect`

## Setup

1. Replace `[INSERT_YOUR_KENSHO_CLIENT_ID]` in `apiProperties.json` with your Kensho Client ID.
2. Add the Kensho client secret during connector creation (the secret is never committed to source). Kensho authenticates the token request with `client_secret_post` / `client_secret_basic`.
3. Deploy the connector (see below) and create a connection. You will be taken through the Microsoft and Kensho Okta login flow.

## Deploy

Deploy with the Power Platform CLI (PAC):

```powershell
pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json
```

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | OpenAPI definition with the `/integrations/mcp` MCP operation (`x-ms-agentic-protocol: mcp-streamable-1.0`) and OAuth 2.0 security |
| `apiProperties.json` | Connector metadata and the Generic OAuth 2.0 connection parameters |

