# MCP Public Client PKCE

Script-free Power Platform custom connector template for connecting Copilot Studio to a remote MCP Streamable HTTP server using:

- OAuth authorization-code flow
- PKCE with `S256`
- Static public-client registration
- No configured client secret

This example is based on Microsoft's [MCP Streamable HTTP connector template](https://github.com/microsoft/PowerPlatformConnectors/tree/dev/custom-connectors/MCP-Streamable-HTTP).

> [!WARNING]
> `oauth2pkcewithdcr` is a hidden Power Platform identity provider. Its current refresh template includes `client_secret={ClientSecret}` even when the connector does not define `clientSecret`. Treat this example as experimental and validate refresh behavior against your authorization server before production use.

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | Defines the remote MCP Streamable HTTP operation and OAuth endpoints. |
| `apiProperties.json` | Configures the hidden public-client PKCE identity provider. |

There is no `script.csx`. Power Platform forwards MCP traffic directly to the remote server.

## Prerequisites

- A remote MCP server using Streamable HTTP.
- An OAuth authorization server that supports authorization code with PKCE `S256`.
- A static public-client registration with no client secret.
- A client ID, authorization endpoint, token endpoint, refresh endpoint, and required scopes.
- PAC CLI.
- Permission to create custom connectors in a Power Platform test environment.

The authorization server must issue refresh tokens if refresh behavior is part of the test.

## Configure the API definition

Edit `apiDefinition.swagger.json`:

1. Replace `your-mcp-server.example.com` with the MCP hostname, without `https://` or a path.
2. Replace `/mcp` with the MCP endpoint path.
3. Replace the authorization and token endpoint URLs.
4. Replace `mcp.tools` with the required OAuth scope.
5. If multiple scopes are required, add each scope to:
   - `securityDefinitions.oauth2_auth.scopes`
   - `security[0].oauth2_auth`

Keep:

```json
"x-ms-agentic-protocol": "mcp-streamable-1.0"
```

Keep the MCP operation at path `/` beneath `basePath`.

## Configure the connector properties

Edit `apiProperties.json`:

1. Replace `[YOUR_CLIENT_ID]` with the static public-client ID.
2. Replace `mcp.tools` with the required scope.
3. Replace `serverUrl` with the MCP server origin, without the MCP endpoint path.
4. Replace `authorizationUrl`, `tokenUrl`, and `refreshUrl`.
5. Leave `[YOUR_REDIRECT_URL]` unchanged before deployment. Power Platform replaces it with the generated per-connector callback URL.

Do not add:

```json
"clientSecret": "..."
```

The scope list must match the scopes in `apiDefinition.swagger.json`.

## Create the connector

Confirm the target environment:

```powershell
pac auth list
pac connector list --environment POWER_PLATFORM_ENVIRONMENT_ID_OR_URL
```

Create the connector:

```powershell
pac connector create `
    --environment POWER_PLATFORM_ENVIRONMENT_ID_OR_URL `
    --api-definition-file .\apiDefinition.swagger.json `
    --api-properties-file .\apiProperties.json
```

Do not pass `--script-file`.

Record the connector ID returned by PAC.

Power Platform validates both connector files during creation. Review and resolve any errors returned by PAC.

## Register the generated callback URL

Download the deployed connector:

```powershell
pac connector download `
    --environment POWER_PLATFORM_ENVIRONMENT_ID_OR_URL `
    --connector-id CONNECTOR_ID `
    --outputDirectory .\deployed-connector
```

Open `deployed-connector\apiProperties.json` and find:

```text
properties.connectionParameters.token.oAuthSettings.redirectUrl
```

Register that exact URL with the authorization server's public-client registration. It should use HTTPS and the `global.consent.azure-apim.net` host.

The callback is specific to the connector and Power Platform environment.

## Create and test the connection

1. Open [Power Automate](https://make.powerautomate.com).
2. Select the environment used by PAC CLI.
3. Open **Custom connectors**.
4. Edit **MCP Public Client PKCE**.
5. Open **Test** and select **New connection**.
6. If a client-secret field appears, leave it blank.
7. Complete authorization and consent.
8. Return to **Test** and refresh the connection list.

Then add the connector to Copilot Studio:

1. Open [Copilot Studio](https://copilotstudio.microsoft.com).
2. Select the same environment.
3. Open a non-production test agent.
4. Open **Tools** and select **Add a tool**.
5. Select **MCP Public Client PKCE** and its connection.
6. Add it to the agent.
7. Confirm that Copilot Studio discovers the remote MCP tools.
8. Invoke a safe, read-only tool.

## Validate refresh behavior

An initial successful tool call proves only that authorization-code PKCE and MCP connectivity worked.

To test refresh:

1. Keep the same Power Platform connection and agent.
2. Wait until the access token expires, plus a clock-skew buffer.
3. Do not repair or recreate the connection.
4. Invoke the same read-only tool again.
5. Inspect sanitized authorization-server logs for the refresh request.

If the normal access-token lifetime is too long, use a non-production client or policy with a shorter lifetime. Long-lived access tokens only postpone the refresh test.

Record only:

```text
Refresh request observed: yes/no
grant_type: refresh_token/other
client_id present: yes/no
client_secret presence: absent/empty/populated
HTTP Basic client authentication present: true/false
Refresh outcome: succeeded/rejected
OAuth error code, if any:
Post-refresh MCP tool outcome: succeeded/failed
```

Never record tokens, codes, verifiers, secrets, cookies, or authorization headers.

## Interpret the refresh result

| Observation | Interpretation |
|---|---|
| `client_secret` absent, no HTTP Basic authentication, refresh succeeds | Strict public-client refresh behavior works. |
| `client_secret` empty and refresh succeeds | Possible compatibility path only if the authorization-server owner approves empty-value handling. |
| `client_secret` empty and refresh fails | The current provider refresh template is incompatible. |
| `client_secret` populated | Confidential-client behavior is being applied. |
| HTTP Basic authentication present | Client authentication is being applied. |
| No refresh request observed | The test is inconclusive. |

The preferred result is:

```text
client_secret presence: absent
HTTP Basic client authentication present: false
Refresh outcome: succeeded
Post-refresh MCP tool outcome: succeeded
```

OAuth specifications do not require an authorization server to ignore an unexpected empty `client_secret`. Confirm empty-value behavior with the authorization-server owner rather than assuming empty and absent are equivalent.

## Update the connector

After changing either connector file:

```powershell
pac connector update `
    --environment POWER_PLATFORM_ENVIRONMENT_ID_OR_URL `
    --connector-id CONNECTOR_ID `
    --api-definition-file .\apiDefinition.swagger.json `
    --api-properties-file .\apiProperties.json
```

Delete and recreate existing Power Platform connections after changing OAuth configuration.

## References

- [Microsoft MCP Streamable HTTP connector template](https://github.com/microsoft/PowerPlatformConnectors/tree/dev/custom-connectors/MCP-Streamable-HTTP)
- [Update custom connector OAuth identity providers with PAC CLI](https://troystaylor.com/power%20platform/custom%20connectors/2026-08-07-update-custom-connector-oauth-identity-providers-pac-cli.html)
- [Create and test a custom connector](https://learn.microsoft.com/connectors/custom-connectors/define-blank#step-5-test-the-connector)
- [Connect a Copilot Studio agent to an existing MCP server](https://learn.microsoft.com/microsoft-copilot-studio/mcp-add-existing-server-to-agent)
- [RFC 6749: The OAuth 2.0 Authorization Framework](https://www.rfc-editor.org/rfc/rfc6749.html)
- [RFC 7636: Proof Key for Code Exchange by OAuth Public Clients](https://www.rfc-editor.org/rfc/rfc7636.html)
- [RFC 8252: OAuth 2.0 for Native Apps](https://www.rfc-editor.org/rfc/rfc8252.html)
