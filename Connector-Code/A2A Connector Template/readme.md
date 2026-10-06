# A2A Connector Template

Connect a Copilot Studio agent to any agent that supports the [Agent2Agent (A2A) protocol](https://a2a-protocol.org/) by using a custom connector that you deploy with PAC CLI.

Copilot Studio can create an A2A connection for you under **Agents → Add agent → A2A agent**, with **None**, **API key**, or **OAuth 2.0** authentication. Use this template instead when you need any of the following:

- **Microsoft Entra ID** sign-in to an API protected by Entra ID
- **OAuth 2.0 with PKCE**, which the **OAuth 2.0** option in Copilot Studio doesn't send
- An agent card that requires authentication to read
- A connector definition you keep in source control and deploy to several environments

## How Copilot Studio uses the connector

- The connector has one operation, `InvokeA2A`, marked `x-ms-agentic-protocol: a2a-1.0`. Copilot Studio sends every A2A request to that operation.
- The operation path is the agent's **message endpoint**, the `url` in its agent card. It isn't the agent card URL.
- After you deploy the connector, it's listed in Copilot Studio under **Agents → Add agent**, alongside the built-in options.
- When you add it, Copilot Studio uses the connector's `info.title` as the agent's name and the operation's `description` as its description. The orchestrator reads the description to decide when to call the agent. Copilot Studio copies both values when you add the agent, so later connector updates don't change them.
- Copilot Studio's documentation shows A2A 0.3 requests (`message/send`), so make sure your agent accepts A2A 0.3.

These connector fields aren't documented by Microsoft. They match what Copilot Studio creates when you add an A2A agent in the portal.

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | Connector definition with the `InvokeA2A` operation |
| `auth/*.apiProperties.json` | Connection settings, one file for each authentication option |
| `Set-A2AAgentCard.ps1` | Sets the connector's host and path from the agent card, and optionally embeds the card |

The connector has no `script.csx`.

## Authentication options

| Option | File | Identity provider | Use when | Status |
|---|---|---|---|---|
| None | `none.apiProperties.json` | — | The agent doesn't require authentication | Deploys |
| API key | `api-key.apiProperties.json` | — | The agent takes a key in a header | Deploys |
| OAuth 2.0 | `oauth2.apiProperties.json` | `oauth2` | Authorization code with a client secret | Deploys |
| Microsoft Entra ID | `entra-id.apiProperties.json` | `aad` | The agent's API is protected by Entra ID | Tested end to end |
| OAuth 2.0 with PKCE | `oauth2-pkce.apiProperties.json` | `oauth2pkcewithdcr` | The authorization server requires PKCE (`S256`) | Deploys |

**Deploys** means Power Platform accepts the connector and Copilot Studio lists it. Test sign-in and a call to your agent before you rely on it.

> [!WARNING]
> `oauth2pkcewithdcr` is a hidden Power Platform identity provider and isn't documented. It sends PKCE on sign-in, but it sends the client secret only when refreshing tokens. Validate sign-in and token refresh against your authorization server before production use.

## Prerequisites

- Your agent's A2A endpoint or agent card URL, plus a token for the card if the card requires one
- A Power Platform environment with Copilot Studio, and permission to create custom connectors
- [PAC CLI](https://aka.ms/PowerAppsCLI), signed in to your tenant (`pac auth create`)
- PowerShell 7
- For OAuth options: a client registration with the authorization server, which you finish after deploying

## Step 1: Copy the template

Copy this folder to a new folder for your agent, then work in the copy:

```powershell
Copy-Item -Recurse '.\A2A Connector Template' '.\My A2A Agent'
Set-Location '.\My A2A Agent'
```

## Step 2: Choose authentication

1. Copy the file for your option over `apiProperties.json`, for example:

   ```powershell
   Copy-Item .\auth\entra-id.apiProperties.json .\apiProperties.json
   ```

2. Replace `securityDefinitions` and `security` at the bottom of `apiDefinition.swagger.json` with the block for your option.

   **None** — keep the template's empty values:

   ```json
   "securityDefinitions": {},
   "security": []
   ```

   **API key** — set `name` to the header your agent expects. Use `"in": "query"` if the agent takes the key as a query parameter.

   ```json
   "securityDefinitions": {
     "api_key": { "type": "apiKey", "in": "header", "name": "X-API-Key" }
   },
   "security": [ { "api_key": [] } ]
   ```

   **OAuth 2.0** and **OAuth 2.0 with PKCE** — use your authorization server's endpoints:

   ```json
   "securityDefinitions": {
     "oauth2_auth": {
       "type": "oauth2",
       "flow": "accessCode",
       "authorizationUrl": "https://your-auth-server.example.com/oauth2/authorize",
       "tokenUrl": "https://your-auth-server.example.com/oauth2/token",
       "scopes": {}
     }
   },
   "security": [ { "oauth2_auth": [] } ]
   ```

   **Microsoft Entra ID**:

   ```json
   "securityDefinitions": {
     "oauth2_auth": {
       "type": "oauth2",
       "flow": "accessCode",
       "authorizationUrl": "https://login.microsoftonline.com/common/oauth2/authorize",
       "tokenUrl": "https://login.microsoftonline.com/common/oauth2/authorize",
       "scopes": {}
     }
   },
   "security": [ { "oauth2_auth": [] } ]
   ```

## Step 3: Point the connector at your agent

Run `Set-A2AAgentCard.ps1` from the folder that contains `apiDefinition.swagger.json`:

```powershell
# Card is public
.\Set-A2AAgentCard.ps1 -CardUrl 'https://agent.example.com/a2a/.well-known/agent-card.json'

# Card requires a token
.\Set-A2AAgentCard.ps1 -CardUrl $cardUrl -BearerToken $token

# No card; you know the endpoint
.\Set-A2AAgentCard.ps1 -Endpoint 'https://agent.example.com/a2a'
```

The script reads the endpoint from the card's `url`, or from the first JSON-RPC entry in `supportedInterfaces` for A2A 1.0 cards. It sets the connector's `host` and operation path to that endpoint and records the card URL in `x-ms-a2a-card-endpoint`. Use `-Endpoint` to override the card's endpoint.

**Optional:** add `-EmbedCard` to store the card in `x-ms-a2a-card-json`. Copilot Studio takes the agent's name and description from the connector whether or not you embed the card. Re-run the script when the agent's card changes.

## Step 4: Name and describe the agent

In `apiDefinition.swagger.json`, set:

| Field | Shown in Copilot Studio as |
|---|---|
| `info.title` | The agent's name, and the connector's name |
| The operation's `description` | The agent's description, which the orchestrator uses to decide when to call it |
| The operation's `summary` | Keep it the same as `info.title` |

Write the description for the orchestrator. Say what the agent does and which requests it should handle, for example *"Answers questions about the signed-in user's email, meetings, and files."*

In `apiProperties.json`, replace `[YOUR_AGENT_PUBLISHER]` with the organization that runs the agent.

## Step 5: Fill in authentication values

Leave `[YOUR_REDIRECT_URL]` as it is. Power Platform generates the redirect URL when you deploy.

**API key:** nothing to fill in. Users enter the key when they create a connection.

**OAuth 2.0:** in `apiProperties.json`, set `clientId`, `scopes`, `authorizationUrl`, `tokenUrl`, and `refreshUrl`. Use the same authorization and token URLs as in Step 2.

**Microsoft Entra ID:**

1. Create an app registration in your tenant:
   1. Under **Certificates & secrets**, create a client secret.
   2. Under **API permissions**, add the delegated permission that your agent's API requires, and grant admin consent.
2. In `apiProperties.json`:
   - Set `clientId` to the app's **Application (client) ID**.
   - Replace `[YOUR_RESOURCE_URI]` with the **Application ID URI** of your agent's API, for example `api://agent.example.com`. It appears in three places.
   - Replace `[YOUR_SCOPE]` with the delegated permission name.

**OAuth 2.0 with PKCE:** in `apiProperties.json`, set `clientId`, `serverUrl` (your agent's origin), `authorizationUrl`, `tokenUrl`, and `refreshUrl`. Don't put a query string on `authorizationUrl`; the identity provider adds `response_type`, `code_challenge`, and the other parameters itself.

## Step 6: Deploy and register the redirect URL

1. Create the connector:

   ```powershell
   pac connector create `
       --environment <YOUR_ENVIRONMENT_ID> `
       --api-definition-file .\apiDefinition.swagger.json `
       --api-properties-file .\apiProperties.json
   ```

   Save the connector ID. Don't pass `--script-file`.

2. **For OAuth options**, download the deployed connector and read its redirect URL:

   ```powershell
   pac connector download `
       --environment <YOUR_ENVIRONMENT_ID> `
       --connector-id <CONNECTOR_ID> `
       --outputDirectory .\deployed

   (Get-Content .\deployed\apiProperties.json -Raw | ConvertFrom-Json).
       properties.connectionParameters.token.oAuthSettings.redirectUrl
   ```

3. Register that URL with your authorization server. For Entra ID:

   ```powershell
   az ad app update --id <YOUR_CLIENT_ID> --web-redirect-uris `
       "https://global.consent.azure-apim.net/redirect" "<redirect-url-from-step-2>"
   ```

   The URL is unique to this connector in this environment. Register each environment's URL.

## Step 7: Add the client secret

Skip this step for **None** and **API key**.

Add the secret to a temporary copy of `apiProperties.json`, so it never sits in your working folder:

```powershell
$props = Get-Content .\apiProperties.json -Raw | ConvertFrom-Json
$props.properties.connectionParameters.token.oAuthSettings |
    Add-Member -NotePropertyName clientSecret -NotePropertyValue '<your-client-secret>' -Force
$tempProps = Join-Path $env:TEMP 'a2a-apiProperties.json'
$props | ConvertTo-Json -Depth 30 | Set-Content $tempProps -Encoding utf8

pac connector update `
    --environment <YOUR_ENVIRONMENT_ID> `
    --connector-id <CONNECTOR_ID> `
    --api-definition-file .\apiDefinition.swagger.json `
    --api-properties-file $tempProps

Remove-Item $tempProps
```

You can also enter the secret on the **Security** tab when you edit the connector in Power Automate.

## Step 8: Add the agent in Copilot Studio

1. Open [Copilot Studio](https://copilotstudio.microsoft.com) in the same environment.
2. Open your agent and go to **Agents → Add agent**.
3. Select your connector by its `info.title`. Don't select **A2A agent**, which creates a separate connector.
4. Create a connection and sign in.
5. Select **Add and configure**.

In the agent's YAML, the added agent looks like this:

```yaml
kind: AgentToAgentTool
connectionReference: <your-agent-schema-name>.cr.<connection-reference>
connectorId: /providers/Microsoft.PowerApps/apis/<your-connector-api-name>
operationId: InvokeA2A
```

## Step 9: Test

In the **Test** pane, ask for something only your A2A agent can do. Ask for the task, not the tool: *"What meetings do I have today?"* works better than *"Use My Agent to check my meetings."* Copilot Studio passes the request to your agent, which doesn't know the name you gave it in Copilot Studio.

If the first question right after you add the agent isn't routed to it, wait a minute and ask again.

## Example: Work IQ

[Work IQ](https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq/a2a/quickstart) is a Microsoft 365 A2A agent protected by Entra ID, and its card requires a token. Values for this template:

| Setting | Value |
|---|---|
| Authentication option | Microsoft Entra ID |
| Agent card | `https://workiq.svc.cloud.microsoft/a2a/.well-known/agent-card.json` (requires a token) |
| Endpoint from the card | `https://workiq.svc.cloud.microsoft/a2a` |
| `[YOUR_RESOURCE_URI]` | `api://workiq.svc.cloud.microsoft` |
| `[YOUR_SCOPE]` | `WorkIQAgent.Ask` |
| App registration permission | **Work IQ** (under **APIs my organization uses**) → delegated **WorkIQAgent.Ask**, with admin consent |
| `[YOUR_AGENT_PUBLISHER]` | `Microsoft` |

Your tenant must be enabled for Work IQ, and each user needs to be in a usage-based billing plan. Because the card requires a token, run Step 3 with `-Endpoint 'https://workiq.svc.cloud.microsoft/a2a'`, or with `-CardUrl` and a `-BearerToken` for the Work IQ API.

For an OAuth 2.0 with PKCE example, see [Workday A2A](../../Workday%20A2A/).

## Update the connector

After you change either file, run `pac connector update` as in Step 7. If you change OAuth settings, delete and recreate existing connections.

Connector updates don't change an agent you've already added. To change the agent's description, edit it on your agent's **Agents** page in Copilot Studio.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| The connector isn't listed under **Agents → Add agent** | It's in a different environment, or the operation isn't marked `a2a-1.0` | Deploy to the environment that contains your agent. Check that `x-ms-agentic-protocol` is `a2a-1.0`. |
| `Set-A2AAgentCard.ps1` reports HTTP 401 | The card requires a token, or the token is expired or for the wrong API | Pass a valid `-BearerToken`, or use `-Endpoint` |
| `pac connector create` fails with `InvalidScriptDefinitionUrlWithNonNullOperations` | `scriptOperations` isn't empty | Leave `scriptOperations` as `[]` |
| Sign-in fails with a redirect URI mismatch | The connector's redirect URL isn't registered | Repeat Step 6.2 and 6.3 |
| Sign-in fails with `AADSTS65001` | Admin consent wasn't granted | Grant admin consent for the app's API permission |
| The agent returns 401 | The token is for the wrong API | Check `[YOUR_RESOURCE_URI]` against the API's Application ID URI |
| The orchestrator doesn't call the agent right after you add it | Copilot Studio hasn't picked up the new agent yet | Wait a minute and ask again |
| The orchestrator never calls the agent | The description doesn't match users' requests | Edit the agent's description on the **Agents** page in Copilot Studio |
| The agent says it doesn't have the tool you named | Copilot Studio passed your words to the agent, which doesn't know its name in Copilot Studio | Ask for the task, not the tool |
| The agent returns a "method not found" error | The agent accepts only A2A 1.0 requests | Check whether the agent can accept A2A 0.3 requests |

## References

- [Connect an agent available over the Agent2Agent (A2A) protocol](https://learn.microsoft.com/microsoft-copilot-studio/add-agent-agent-to-agent)
- [Agent2Agent (A2A) protocol](https://a2a-protocol.org/)
- [Work IQ A2A quickstart](https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq/a2a/quickstart)
- [PAC CLI connector commands](https://learn.microsoft.com/power-platform/developer/cli/reference/connector)
