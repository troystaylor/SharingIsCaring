# Work IQ for Power Automate

Typed Power Automate and Power Apps actions for [Microsoft Work IQ](https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq-api-overview) — Outlook mail, calendar, and contacts; Teams chats and channels; OneDrive and SharePoint files; people, groups, and Planner.

The name reflects the primary use case. As a standard custom connector it also works in Power Apps and Logic Apps.

Every call is permission-trimmed to the signed-in user. Work IQ can only return data that person can already open.

## Use this, or use a first-party connector?

Microsoft ships **nine certified Work IQ connectors**, and Copilot Studio can add Work IQ natively under **Tools** > **Add Tool** > **Model Context Protocol**. Use those first where they fit. This connector is a complement, not a replacement.

| Connector | Tier |
|-----------|------|
| Work IQ MCP (`shared_workiqmcp`) | Standard |
| Work IQ Mail, Calendar, Teams, Word, OneDrive, SharePoint, User, and Copilot MCP | Premium |

Every one of them is a **raw MCP passthrough**. Their operation shape is identical:

```
POST .../mcp    x-ms-agentic-protocol: mcp-streamable-1.0
GET  .../mcp    x-ms-visibility: internal
```

None of them expose a typed action.

| You want to | Use |
|-------------|-----|
| Give a Copilot Studio agent access to Work IQ | **Native Work IQ tool** in Copilot Studio. One click, no app registration. |
| Scope an agent to one data domain | A granular first-party connector such as **Work IQ Mail MCP**. |
| Build flows against named actions with typed inputs and outputs | **This connector.** |
| Ask a question and keep conversation context across steps | **This connector.** No first-party connector exposes the A2A endpoint. |

### What the difference looks like in a flow

`Work IQ MCP` does appear in the Power Automate designer — it declares `capabilities: actions`. But its only usable operation takes an untyped `queryRequest` body and returns an untyped `object`. To send one email you compose the envelope by hand:

```json
{"jsonrpc":"2.0","id":"...","method":"tools/call",
 "params":{"name":"do_action","arguments":{"actionUrl":"/me/sendMail","jsonBody":{...}}}}
```

…then add a **Parse JSON** action with a schema you write yourself, and handle the Server-Sent Events framing and the `content` versus `structuredContent` split described under [How the script works](#how-the-script-works).

This connector exposes the same capability as a **Do action** step with named fields, and returns a flattened result you bind to directly as dynamic content.

### The trade

The typed surface costs an Entra app registration and admin consent, which the first-party connectors do not require. That is a **one-time setup cost** weighed against a **per-flow authoring cost** that recurs in every flow you build. The break-even is low — roughly two flows.

You gain no licensing advantage. Custom connectors are premium in Power Automate, as are eight of the nine first-party connectors.

This connector does **not** expose an MCP endpoint. That job belongs to the first-party connectors, which do it nine times over.

## Operations

| Operation | Description |
|-----------|-------------|
| Ask Work IQ | Ask a natural language question. Returns an answer plus a Context ID for follow-ups. |
| Ask a Copilot agent | Ask through a specific Copilot agent, optionally attaching files. |
| List Copilot agents | List available agents and their IDs. |
| Search entity paths | Find the entity path for a kind of data before reading or writing it. |
| Get operation schema | Discover the fields an operation accepts. |
| Fetch entities | Read one or more entities by path. |
| Create entity | Create an entity, such as a calendar event. |
| Update entity | Change fields on an existing entity. |
| Delete entity | Delete an entity. |
| Do action | Run an action such as `/me/sendMail`. |
| Call function | Call a computed function such as `/me/calendarView/delta`. |

**Ask Work IQ** and **Ask a Copilot agent** differ in transport. Ask Work IQ uses the A2A endpoint and returns a Context ID to continue a conversation. Ask a Copilot agent uses the MCP `ask` tool, which can target a specific agent and attach files.

Work with entities in the order **Search entity paths → Get operation schema → read or write**. Paths are not memorable and writes reject unexpected fields, so guessing tends to fail.

`fetch_blob` is not exposed. It returns base64 file bytes up to 4 MB, which is better handled by a dedicated file connector.

### Write access is gated

Work IQ enforces a **per-tenant policy allowlist** over which paths a caller may write to. This is separate from the signed-in user's own permissions and from Microsoft 365 Copilot licensing — a user who can create an event in Outlook may still be refused by Work IQ.

Where writes are not permitted, **Create entity**, **Update entity**, **Delete entity**, and **Do action** return `Is Error` true with:

```
Access denied for POST /me/events. Path is not in the policy allowlist.
```

Check before you build. Run **Search entity paths** and look at the `operations` array for each path. In a tenant with no write entitlement, every path lists `fetch` and nothing else, and all four write actions are unusable.

The read actions — **Ask Work IQ**, **Ask a Copilot agent**, **List Copilot agents**, **Search entity paths**, **Fetch entities**, and **Call function** — are unaffected.

## Prerequisites

- A Microsoft 365 Copilot license for every user who connects. Work IQ runs on the Microsoft 365 Copilot Chat API and returns `403` without one.
- A tenant administrator who can grant admin consent. The required permission is admin-consent only — users cannot consent for themselves.
- Work IQ MCP not blocked in your tenant. See [Troubleshooting](#troubleshooting).
- For the write actions, a tenant whose Work IQ policy allowlist permits writes. See [Write access is gated](#write-access-is-gated). The read actions work without this.

## Set up the Entra app registration

Work IQ requires the delegated permission `WorkIQAgent.Ask`. Power Platform needs a confidential client, so you cannot reuse the public client ID shipped with the `workiq` CLI — register your own app.

1. In the Azure portal, go to **Microsoft Entra ID** > **App registrations** > **New registration**.
2. Name it, choose **Accounts in this organizational directory only**, and register it.
3. Under **Certificates & secrets**, create a client secret and copy the value.
4. Under **API permissions**, select **Add a permission** > **APIs my organization uses**, search for **Work IQ**, choose **Delegated permissions**, and select **WorkIQAgent.Ask**.
5. Select **Grant admin consent**.

The Work IQ API is a Microsoft first-party multi-tenant application. It appears under *APIs my organization uses* rather than *Microsoft APIs*. If you prefer the CLI:

```powershell
az ad app permission add --id <your-app-id> `
    --api fdcc1f02-fc51-4226-8753-f668596af7f7 `
    --api-permissions 0b1715fd-f4bf-4c63-b16d-5be31f9847c2=Scope

az ad app permission admin-consent --id <your-app-id>
```

Leave the redirect URI alone for now. Power Platform generates one during deployment, and you register it afterwards.

## Deploy the connector

Fill in your client ID and secret in a temporary copy of `apiProperties.json` rather than editing the committed file, which ships with placeholders:

```powershell
pac auth create --environment <your-environment-id>
pac connector create `
    --api-definition-file "./apiDefinition.swagger.json" `
    --api-properties-file "./apiProperties.temp.json" `
    --script-file "./script.csx" `
    --environment <your-environment-id>
```

The `--script-file` argument is required. Every operation in this connector is implemented by the script — the `/tools/*` paths exist only there and have no counterpart on the Work IQ service. Deploy without it and the connector is created successfully but every action fails.

## Register the generated redirect URI

Power Platform generates a redirect URI unique to this connector and environment, and OAuth consent fails until that exact URI is registered on your app.

Read it back after deployment:

```powershell
pac connector download --connector-id <connector-id> `
    --environment <your-environment-id> `
    --outputDirectory ./verify
# properties.connectionParameters.token.oAuthSettings.redirectUrl
```

Add it to the app registration, keeping the global redirect alongside it:

```powershell
az ad app update --id <your-app-id> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<per-connector-url>"
```

Deploying the same connector to another environment produces a different redirect URI. Register each one.

## Use it from Power Automate

Add **Ask Work IQ**, supply a **Question**, and read **Answer** from the output. Pass the returned **Context ID** into a later Ask Work IQ step to continue the same conversation.

For deterministic reads, use **Fetch entities** with a path such as `/me/messages?$select=subject,from&$top=25`. Keep reads small — entity paths accept OData, and a bare collection path can return very large responses.

To write, confirm the path is writable first:

1. **Search entity paths** with a filter such as `events`. Check the `operations` listed for the path you want.
2. **Get operation schema** for that path to discover the fields it accepts.
3. **Create entity**, **Update entity**, **Delete entity**, or **Do action** with a body matching that schema.

A path that lists only `fetch` cannot be written to. See [Write access is gated](#write-access-is-gated).

## How the script works

`script.csx` translates between Power Platform's request shape and the two protocols Work IQ speaks.

- **Entity and tool actions** become MCP `tools/call` envelopes posted to `/mcp`. Every declared property maps onto a tool argument of the same name, and empty values are dropped rather than sent as nulls the tool would reject.
- **Ask Work IQ** becomes an A2A `message/send` envelope posted to `/a2a`, and the returned task is flattened to **Answer**, **Context ID**, **Task ID**, and **State**.

### Why a script rather than policy templates

The `/mcp` endpoint will not return JSON. It rejects any request that does not accept Server-Sent Events, and always answers with them:

| `Accept` | Response |
|----------|----------|
| `application/json` | `406 Not Acceptable` |
| *(none)* | `406 Not Acceptable` |
| `application/json, text/event-stream` | `200` — `text/event-stream` |
| `*/*` | `200` — `text/event-stream` |

So every tool response arrives as `data:` lines wrapping a JSON-RPC message. Inside that message the payload is either `content[0].text` — a JSON-encoded *string*, not an object — or `structuredContent`, depending on the tool. `search_paths` and `fetch` use the latter.

No policy template decodes SSE or parses a JSON string into an object. Without the script, each action would return raw event-stream text and every flow would repeat the same unwrapping by hand. Policies can solve the routing — `routerequesttoendpoint` maps the `/tools/*` paths onto `/mcp` — but not the response handling.

The A2A endpoint is different: `/a2a` returns `application/json` and needs no `Accept` header. **Ask Work IQ** is scripted only to flatten the artifact tree into named outputs, not out of necessity.

Application Insights logging is present but disabled. Set `APP_INSIGHTS_ENABLED` to `true` and supply an instrumentation key to turn it on.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `AADSTS500011: resource principal not found` | The connection requests a resource that is not a registered identifier for Work IQ. | The resource must be `api://workiq.svc.cloud.microsoft`. The bare `https://workiq.svc.cloud.microsoft` host is not valid. |
| `AADSTS65001` or a consent prompt that never completes | Admin consent was not granted. | Grant admin consent for `WorkIQAgent.Ask`. Users cannot consent to it themselves. |
| Consent fails with a redirect URI mismatch | The generated per-connector redirect URI is not on the app registration. | Register it as shown above. |
| `403 Forbidden` or "caller not entitled" | The signed-in user has no Microsoft 365 Copilot license, or Work IQ is not enabled for them. | Assign a license and confirm availability in the Microsoft 365 admin center under **Copilot** > **Settings**. |
| Every call fails tenant-wide | Work IQ MCP is blocked. | In the Microsoft 365 admin center, go to **Agents** > **Tools** > **Work IQ MCP** and check its availability. |
| An action returns a raw JSON-RPC envelope | The connector was deployed without the script. | Redeploy with `--script-file ./script.csx`. |
| `Is Error` is true with an error message in `Text` | The tool rejected the request, usually a wrong path or a missing required field. | Run **Search entity paths** and **Get operation schema** and correct the input. |
| `Is Error` is true with "Path is not in the policy allowlist" | Work IQ restricts which paths a caller may write to, independently of the signed-in user's own permissions. | Run **Search entity paths** and check the operations listed for that path. A path that lists only `fetch` cannot be written to. Write access depends on tenant policy and Work IQ entitlement, not on this connector. |
| Intermittent `-32603 An error occurred` | Service-side throttling under rapid repeated calls. | Retry with a delay. Add a retry policy for bulk flows. |

## Preview status

Work IQ is in public preview and Microsoft documents it as a proprietary service whose features and APIs may change. Treat this connector as preview-grade and expect breaking changes.

Microsoft is actively expanding this area — nine certified Work IQ connectors already exist, spanning mail, calendar, Teams, Word, OneDrive, SharePoint, and more. If first-party typed actions ship for Power Automate, they will likely be the better choice and this connector becomes unnecessary.

The Work IQ API also exposes `WorkIQSettings.Read.All` and `WorkIQSettings.ReadWrite.All`. These back the administrative settings surfaced in the Microsoft 365 admin center and the Power Platform admin center, and there is no documented HTTP endpoint for them, so this connector does not use them. Manage those settings through the admin centers. See [Manage AI experience access to Business Applications in Work IQ](https://learn.microsoft.com/power-platform/admin/business-applications-work-iq/manage-ai-experience-access).

## References

- [Work IQ API overview](https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq-api-overview)
- [Work IQ MCP first-party connector](https://learn.microsoft.com/connectors/workiqmcp/)
- [Add Work IQ to a Copilot Studio agent](https://learn.microsoft.com/microsoft-copilot-studio/add-work-iq)
- [Work IQ plugin marketplace](https://github.com/microsoft/work-iq) — tenant admin enablement guide
- [Manage AI experience access](https://learn.microsoft.com/power-platform/admin/business-applications-work-iq/manage-ai-experience-access)
