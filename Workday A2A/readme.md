# Workday A2A

Connect a Copilot Studio agent to the Workday Self-Service Agent by using the Agent2Agent (A2A) protocol. Your Copilot Studio agent hands HR requests to Workday, and Workday answers on behalf of the signed-in user.

Copilot Studio connects to A2A agents through a custom connector. This folder contains a connector template that you deploy with PAC CLI. You need PAC CLI because Workday requires OAuth authorization code with PKCE, and the **OAuth 2.0** option in the Copilot Studio **Add agent → A2A agent** form doesn't send PKCE. The template uses the `oauth2pkcewithdcr` identity provider, which sends PKCE (`S256`). That provider is hidden in the custom connector wizard, so you can only set it in `apiProperties.json` and deploy it with PAC CLI.

> [!WARNING]
> `oauth2pkcewithdcr` is a hidden Power Platform identity provider and isn't documented. Validate sign-in and token refresh in a test environment before you use it in production.

## How it works

```text
User ─▶ Copilot Studio agent ─▶ Workday Self-Service Agent connector (A2A, InvokeA2A)
                                        │  OAuth 2.0 authorization code + PKCE (per user)
                                        ▼
                         https://<region>.agent.workday.com/v1/a2a/<tenant-alias>/<agent-wid>
                                        │
                                        ▼
                             Workday Self-Service Agent
```

- The Copilot Studio orchestrator decides when to delegate to Workday based on the agent's description. Copilot Studio copies that description from the connector's operation `description` when you add the agent.
- Each user signs in to Workday the first time the agent calls Workday. Workday applies that user's security groups, because the Self-Service Agent runs in `Mode=Delegate`.

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | Connector definition. One `InvokeA2A` operation marked `x-ms-agentic-protocol: a2a-1.0`, with the Workday agent card embedded. |
| `apiProperties.json` | OAuth settings for the `oauth2pkcewithdcr` identity provider. |
| `agentDefinition.json` | Request body that registers your Copilot Studio agent in the Workday Agent System of Record. |

The connector has no `script.csx`.

## Prerequisites

**Workday**

- A Workday administrator account.
- Agent System of Record set up, and the Self-Service Agent registered and configured. See [Connect External Agents to Workday Using A2A](https://doc.workday.com/admin-guide/en-us/workday-ai/agents/external-agents/connect-external-agent-to-workday-using-a2a.html).
- Your **agent gateway hostname**, for example `<region-code>.agent.workday.com`.
- Your **tenant alias**, which is usually your tenant name.
- The **Workday ID (WID) of the Self-Service Agent**.
- The **platform ID** that Workday assigns to your external agent platform.

**Power Platform**

- A Power Platform environment with Copilot Studio.
- Permission to create custom connectors in that environment.
- [PAC CLI](https://aka.ms/PowerAppsCLI), signed in to your tenant (`pac auth create`).
- PowerShell 7. The examples below write UTF-8 files without a byte order mark.

## Step 1: Create an integration client for Agent System of Record

You use this client only to register your agent and retrieve its agent card. End users never use it.

1. Sign in to Workday as an administrator.
2. If you don't have a suitable integration system user (ISU), run the **Create Integration System User** task.
3. Run the **Register API Client for Integrations** task:
   1. In **Functional Area**, select **Agent System of Record**.
   2. Select **Include Workday Owned Scopes**.
   3. Save the **Client ID** and **Client Secret**.
4. On the new API client, select **Related Actions → Manage Refresh Tokens**.
5. Select your ISU, generate a new refresh token, and save it.

## Step 2: Get an ISU bearer token

Set your values once and reuse them in later steps:

```powershell
$gateway      = '<region-code>.agent.workday.com'
$tenantAlias  = '<tenant-alias>'
$agentWid     = '<self-service-agent-wid>'
$isuClientId  = '<integration-client-id>'
$isuSecret    = '<integration-client-secret>'
$refreshToken = '<isu-refresh-token>'

$isu = Invoke-RestMethod -Method Post `
    -Uri "https://$gateway/auth/oauth2/$tenantAlias/token" `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{
        grant_type    = 'refresh_token'
        refresh_token = $refreshToken
        client_id     = $isuClientId
        client_secret = $isuSecret
    }

$isuToken = $isu.access_token
```

If the response includes a new `refresh_token`, save it. Use the new one next time.

## Step 3: Register your Copilot Studio agent in Workday

1. Open `agentDefinition.json` and replace the placeholders:

   | Placeholder | Value |
   |---|---|
   | `name`, `description` | How your Copilot Studio agent appears in Workday |
   | `YOUR_AGENT_URL` | A URL that identifies your Copilot Studio agent |
   | `YOUR_AGENT_DOCS_URL` | Documentation for your agent |
   | `YOUR_ICON_URL` | A public icon URL |
   | `YOUR_PLATFORM_ID` | The platform ID from Workday |

   Keep the `skills` and `workdayConfig` sections as they are. They grant your agent the Self-Service Agent tool (`ssa-agent-as-a-tool`, resource `6bfadd7c014610000dffae8d77c00000`) in delegated mode.

2. Post the definition:

   ```powershell
   Invoke-RestMethod -Method Post `
       -Uri "https://$gateway/asor/v1/agentDefinition" `
       -Headers @{
           Authorization           = "Bearer $isuToken"
           'wd-agent-tenant-alias' = $tenantAlias
           'accept-encoding'       = 'identity'
       } `
       -ContentType 'application/json' `
       -InFile .\agentDefinition.json
   ```

## Step 4: Retrieve the Self-Service Agent card

```powershell
$cardUrl  = "https://$gateway/v1/a2a/$tenantAlias/$agentWid/.well-known/agent-card.json"
$cardResp = Invoke-WebRequest -Uri $cardUrl -Headers @{
    Authorization     = "Bearer $isuToken"
    'Accept-Encoding' = 'identity'
}

$cardResp.Content | Set-Content .\agent-card.json -Encoding utf8
($cardResp.Content | ConvertFrom-Json).url
```

The last line prints the agent's A2A endpoint, the `url` field of the card. The connector sends messages to that endpoint, not to the card URL.

## Step 5: Fill in the connector files

1. Replace the gateway and tenant placeholders in both connector files:

   ```powershell
   foreach ($file in 'apiDefinition.swagger.json', 'apiProperties.json') {
       (Get-Content $file -Raw) `
           -replace 'your-region\.agent\.workday\.com', $gateway `
           -replace 'YOUR_TENANT_ALIAS', $tenantAlias `
           -replace 'YOUR_AGENT_WID', $agentWid |
           Set-Content $file -Encoding utf8 -NoNewline
   }
   ```

2. Point the operation at the card's endpoint and embed the card:

   ```powershell
   $def      = Get-Content .\apiDefinition.swagger.json -Raw | ConvertFrom-Json
   $endpoint = [Uri](($cardResp.Content | ConvertFrom-Json).url)
   $op       = @($def.paths.PSObject.Properties)[0].Value

   $op.post.'x-ms-a2a-card-endpoint' = $cardUrl
   $op.post.'x-ms-a2a-card-json'     = $cardResp.Content

   $def.host  = $endpoint.Host
   $def.paths = [pscustomobject]@{ $endpoint.AbsolutePath = $op }

   $def | ConvertTo-Json -Depth 50 | Set-Content .\apiDefinition.swagger.json -Encoding utf8
   ```

   Workday's card endpoint requires a bearer token. Embedding the card in `x-ms-a2a-card-json` gives Copilot Studio the agent's name, description, and skills without calling that endpoint.

3. Optionally, edit the operation `description` in `apiDefinition.swagger.json`. Copilot Studio uses it as the agent's description, which the orchestrator reads to decide when to call Workday. Describe the requests Workday should handle in your organization. Copilot Studio copies the description when you add the agent in Step 9, so set it before then.

4. Leave `[YOUR_CLIENT_ID]` and `[YOUR_REDIRECT_URL]` in `apiProperties.json` for now. You get the client ID from Workday in Step 7, and Power Platform generates the redirect URL when you deploy.

In `apiProperties.json`, the `authorizationUrl` must have no query string. The identity provider adds `client_id`, `response_type=code`, `redirect_uri`, `state`, `code_challenge`, and `code_challenge_method=S256` itself.

## Step 6: Deploy the connector and get its redirect URL

1. Create the connector:

   ```powershell
   pac connector create `
       --environment <YOUR_ENVIRONMENT_ID> `
       --api-definition-file .\apiDefinition.swagger.json `
       --api-properties-file .\apiProperties.json
   ```

   Save the connector ID that PAC returns. Don't pass `--script-file`.

2. Download the deployed connector:

   ```powershell
   pac connector download `
       --environment <YOUR_ENVIRONMENT_ID> `
       --connector-id <CONNECTOR_ID> `
       --outputDirectory .\deployed
   ```

3. Open `deployed\apiProperties.json` and copy the value at `properties.connectionParameters.token.oAuthSettings.redirectUrl`. It looks like:

   ```text
   https://global.consent.azure-apim.net/redirect/<connector-and-environment-specific-path>
   ```

   This URL is unique to this connector in this environment. If you deploy to another environment, that connector gets its own redirect URL, and you need to register it in Workday too.

## Step 7: Configure and activate the agent in Workday

Complete Workday's **Configure and Activate the New Agent in Workday** step for the agent you registered in Step 3:

1. Set the **Redirect URI** to the redirect URL from Step 6.
2. Assign the security groups whose members can use the agent.
3. Save the agent's **Client ID** and **Client Secret**.

These are the agent's own OAuth credentials. They're not the integration client credentials from Step 1.

## Step 8: Add the client ID and secret to the connector

1. In `apiProperties.json`, replace `[YOUR_CLIENT_ID]` with the agent client ID from Step 7.
2. Add the client secret to a temporary copy of the file, so the secret never sits in your working folder:

   ```powershell
   $props = Get-Content .\apiProperties.json -Raw | ConvertFrom-Json
   $props.properties.connectionParameters.token.oAuthSettings |
       Add-Member -NotePropertyName clientSecret -NotePropertyValue '<agent-client-secret>' -Force
   $tempProps = Join-Path $env:TEMP 'workday-a2a-apiProperties.json'
   $props | ConvertTo-Json -Depth 30 | Set-Content $tempProps -Encoding utf8

   pac connector update `
       --environment <YOUR_ENVIRONMENT_ID> `
       --connector-id <CONNECTOR_ID> `
       --api-definition-file .\apiDefinition.swagger.json `
       --api-properties-file $tempProps

   Remove-Item $tempProps
   ```

   You can also enter the client secret on the **Security** tab when you edit the connector in Power Automate.

3. Confirm the deployment:

   ```powershell
   pac connector download `
       --environment <YOUR_ENVIRONMENT_ID> `
       --connector-id <CONNECTOR_ID> `
       --outputDirectory .\deployed

   $d = Get-Content .\deployed\apiDefinition.json -Raw | ConvertFrom-Json
   $p = Get-Content .\deployed\apiProperties.json -Raw | ConvertFrom-Json
   $op = @($d.paths.PSObject.Properties)[0]
   "endpoint:          https://$($d.host)$($op.Name)"
   "agentic protocol:  $($op.Value.post.'x-ms-agentic-protocol')"
   "identity provider: $($p.properties.connectionParameters.token.oAuthSettings.identityProvider)"
   "client ID:         $($p.properties.connectionParameters.token.oAuthSettings.clientId)"
   ```

   Expect your Workday A2A endpoint, `a2a-1.0`, `oauth2pkcewithdcr`, and your agent client ID.

## Step 9: Add the Workday agent to your Copilot Studio agent

1. Open [Copilot Studio](https://copilotstudio.microsoft.com) in the same environment.
2. Open your agent and go to **Agents → Add agent**.
3. Select **Workday Self-Service Agent** from the list. Don't select **A2A agent**, which would create a second connector without PKCE.
4. Create a connection. Sign in with a Workday account that belongs to one of the security groups from Step 7.
5. Select **Add and configure**.
6. If Copilot Studio offers a choice of credentials, select **end-user credentials**, so each user signs in to Workday as themselves.

If you prefer to edit agent YAML, for example with the Copilot Studio extension for Visual Studio Code, the added agent looks like this:

```yaml
kind: AgentToAgentTool
connectionReference: <your-agent-schema-name>.cr.<connection-reference>
connectorId: /providers/Microsoft.PowerApps/apis/<your-connector-api-name>
operationId: InvokeA2A
```

## Step 10: Test

1. In the **Test** pane, ask a question the Self-Service Agent can answer, for example *"How much time off do I have?"*
2. When prompted, sign in to Workday.
3. Confirm that the answer comes from Workday and reflects the signed-in user's data.

To check token refresh, wait until the Workday access token expires, then ask another question without recreating the connection. The answer should come back without a new sign-in prompt.

## Step 11: Give users access

- **Workday:** Make sure the users are in the security groups that you assigned in Step 7.
- **Copilot Studio:** Publish the agent and share it with the same users. Each user signs in to Workday the first time the agent calls it.

## Update the connector

After you change either connector file, run Step 8 again. If you change OAuth settings, delete and recreate existing connections.

When Workday changes the Self-Service Agent card, repeat Steps 2, 4, and 5.2, then update the connector.

Updating the connector doesn't change the description of an agent that's already added. To change how the orchestrator routes to Workday, edit the Workday agent's description on your agent's **Agents** page in Copilot Studio.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Workday sign-in page shows a redirect URI mismatch | The redirect URI in Workday doesn't match this connector's generated redirect URL | Repeat Step 6.2 and 6.3, and set the exact URL in Step 7 |
| Workday rejects the sign-in request because of a duplicate or invalid `response_type` | `authorizationUrl` includes `?response_type=code` | Remove the query string from `authorizationUrl` and update the connector |
| Workday sign-in shows an invalid client error | `clientId` is still `[YOUR_CLIENT_ID]`, or it's the integration client from Step 1 | Use the agent client ID from Step 7 and update the connector |
| Sign-in succeeds but the connection fails with `invalid_client` | The Workday agent client requires a client secret when exchanging the authorization code. This identity provider sends the secret only when refreshing tokens. | Confirm with your Workday administrator that the agent's OAuth client accepts PKCE without a secret on the code exchange |
| Connection works at first but fails after the access token expires | Refresh failed. The refresh request sends `client_id` and `client_secret`. | Make sure the client secret is set (Step 8), then recreate the connection |
| Workday returns 401 or 403 when the agent calls it | The user isn't in an assigned security group, or the agent isn't activated | Check Step 7 and the user's security groups |
| Copilot Studio shows a generic name or no description for the agent | `x-ms-a2a-card-json` is still `"null"` | Repeat Step 5.2 and update the connector |
| The orchestrator never calls Workday | The agent's description doesn't match the user's requests | Make the Workday agent's description more specific on your agent's **Agents** page in Copilot Studio |
| **Workday Self-Service Agent** isn't listed under **Agents → Add agent** | The connector is in a different environment, or `x-ms-agentic-protocol` isn't `a2a-1.0` | Deploy to the environment that contains your agent, and check the result of Step 8.3 |
| `pac connector create` fails with `InvalidScriptDefinitionUrlWithNonNullOperations` | `scriptOperations` isn't empty | Leave `scriptOperations` as `[]` |

## References

- [Connect External Agents to Workday Using A2A](https://doc.workday.com/admin-guide/en-us/workday-ai/agents/external-agents/connect-external-agent-to-workday-using-a2a.html)
- [Connect an agent available over the Agent2Agent (A2A) protocol](https://learn.microsoft.com/microsoft-copilot-studio/add-agent-agent-to-agent)
- [Agent2Agent (A2A) protocol](https://a2a-protocol.org/)
- [PAC CLI connector commands](https://learn.microsoft.com/power-platform/developer/cli/reference/connector)
- [RFC 7636: Proof Key for Code Exchange](https://www.rfc-editor.org/rfc/rfc7636.html)
