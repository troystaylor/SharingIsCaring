# Microsoft Entra Agent ID

A dual-purpose Power Platform custom MCP connector for [Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/). It manages the identity lifecycle of AI agents through the first-class Microsoft Graph Agent ID APIs — no app-registration workarounds.

One connector, two ways to use it:

1. **MCP server** — the `InvokeMCP` operation speaks JSON-RPC 2.0 and exposes **73 tools**, 7 resources, and 5 prompts to any Model Context Protocol client, including Copilot Studio, Microsoft Foundry, and Agent Framework.
2. **REST actions** — **116 named operations** call Microsoft Graph directly for use in Power Automate flows and Logic Apps.

Both share one connection and one set of delegated Graph permissions. The agent identity lifecycle runs on **Graph v1.0**; agent risk, inherited permissions, Conditional Access what-if, and the agent registry are **beta** and clearly marked as such.

## The model this connector manages

Entra Agent ID has four layers, and they must be created in order. Getting this wrong is the most common source of opaque `400` responses.

| Order | Resource | Graph collection | What it is |
|---|---|---|---|
| 1 | `agentIdentityBlueprint` | `/applications/microsoft.graph.agentIdentityBlueprint` | The application template for a class of agent, carrying the permissions its agent identities inherit |
| 2 | `agentIdentityBlueprintPrincipal` | `/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal` | The record of a blueprint being added to a tenant. Required before any agent identity can be created from that blueprint here |
| 3 | `agentIdentity` | `/servicePrincipals/microsoft.graph.agentIdentity` | What the agent authenticates as. Conditional Access, sign-in logs, and access reviews all act on this object |
| 4 | `agentUser` | `/users/microsoft.graph.agentUser` | The optional user-shaped account for agents needing a mailbox, a Teams presence, or a place in the org chart |

The `provision_agent` tool runs steps 2 through 4 in a single call and reports what it did at each one, including any step that was already satisfied.

### Three rules worth knowing before you start

- **Create an agent identity with the blueprint's `appId`, not its object `id`.** The `agentIdentityBlueprintId` property takes the client ID. This is the single most common mistake, and Graph reports it as `AgentIdentity_IncompatibleParentType`.
- **Deletion cascades downward, not upward.** Deleting a blueprint or blueprint principal triggers an **asynchronous** background cleanup that soft-deletes every child agent identity and agent user — it can lag by hours or days, and shows up in audit logs as *Delete Agent Identities Task*. Deleting an agent identity on its own does **not** remove its agent user, so clean that up explicitly.
- **Everything soft-deletes with a 30 day restore window**, reachable through `list_deleted_agent_objects` and `restore_agent_object`. Two traps: restoring a blueprint principal *after* the cascade has run does not bring its children back — each must be restored individually; and soft-deleted objects keep consuming quota until permanently deleted.

## Relationship to the other agent connectors in this repository

| Connector | Surface | Purpose |
|---|---|---|
| **Entra Agent ID** (this) | Graph Agent ID APIs | The first-class agent identity lifecycle — blueprints, blueprint principals, agent identities, agent users — plus agent risk and the agent registry |
| [Agent 365 Blueprint](../Agent%20365%20Blueprint/readme.md) | Graph `/applications` | The earlier app-registration approach to Agent 365 blueprints, plus Work IQ permission grants |
| [Agent 365 MCP](../Agent%20365%20MCP/readme.md) | Agent 365 platform | Runtime access to Work IQ tools, MCPManagement, and AdminTools |

Use this connector for identity and governance. Use Agent 365 MCP for what the agent does at runtime.

## Prerequisites

1. A **Microsoft Entra app registration** with the delegated permissions below.
2. **Admin consent** — every `AgentIdentity*`, `AgentIdentityBlueprint*`, `AgentIdUser*`, and beta scope requires it.
3. The signed-in user must hold **Agent ID Administrator**, or **Agent ID Developer** for blueprint creation only. A principal that creates a blueprint or blueprint principal automatically becomes its owner and can then manage the derived agent identities without any role — audit owners as carefully as role assignments.

## Setup

### 1. Register an Entra application

1. Go to the [Azure Portal](https://portal.azure.com) → Microsoft Entra ID → App registrations.
2. **New registration**, name it (for example "Entra Agent ID Connector").
3. Supported account types: **Accounts in any organizational directory**.
4. Redirect URI (type Web): `https://global.consent.azure-apim.net/redirect`
5. **Register**, then copy the Application (client) ID and create a client secret.

### 2. Add API permissions

Add these **delegated** Microsoft Graph permissions:

| Scope | Unlocks |
|---|---|
| `AgentIdentity.ReadWrite.All` | Read and manage agent identities |
| `AgentIdentity.Create.All` | Create agent identities from a blueprint |
| `AgentIdentity.EnableDisable.All` | Enable and disable agent identities |
| `AgentIdentity.DeleteRestore.All` | Delete and restore agent identities |
| `AgentIdentityBlueprint.ReadWrite.All` | Read and manage blueprints, including their requested permissions |
| `AgentIdentityBlueprint.Create` | Register new blueprints |
| `AgentIdentityBlueprint.AddRemoveCreds.All` | Add and remove blueprint secrets and certificates |
| `AgentIdentityBlueprint.UpdateBranding.All` | Update blueprint name, description, and branding |
| `AgentIdentityBlueprint.DeleteRestore.All` | Delete and restore blueprints |
| `AgentIdentityBlueprintPrincipal.ReadWrite.All` | Read and manage blueprint principals |
| `AgentIdentityBlueprintPrincipal.Create` | Instantiate a blueprint into the tenant |
| `AgentIdentityBlueprintPrincipal.EnableDisable.All` | Enable and disable blueprint principals |
| `AgentIdentityBlueprintPrincipal.DeleteRestore.All` | Delete and restore blueprint principals |
| `AgentIdUser.ReadWrite.All` | Read and manage agent users, their sponsors, and their managers |
| `AppRoleAssignment.ReadWrite.All` | Grant and revoke application permissions for agents |
| `IdentityRiskyAgent.ReadWrite.All` | **Beta.** Read and remediate agent risk. Requires a Microsoft Agent 365 license. |
| `IdentityRiskEvent.Read.All` | **Beta.** Read the individual agent risk detections |
| `Policy.Read.ConditionalAccess` | **Beta.** Run the Conditional Access what-if evaluation |
| `AgentInstance.ReadWrite.All` | **Beta.** Read and manage agent registry instances |
| `AgentCollection.ReadWrite.All` | **Beta.** Manage agent registry collection membership, including quarantine |
| `Application.Read.All` | **Beta.** Read the permissions an agent inherits from its blueprint |
| `User.Read` | Sign in and read the administrator's profile |
| `offline_access` | Refresh token for a durable connection |

Grant admin consent for all of them. The six beta scopes are only needed if you intend to use the risk, inherited permission, Conditional Access, or registry tools — omit them for a v1.0-only deployment.

### 3. Import the connector

1. Edit `apiProperties.json` and replace `[YOUR_CLIENT_ID]` and `[YOUR_CLIENT_SECRET]`.
2. Import via Maker portal → Custom connectors → **Import an OpenAPI file** (`apiDefinition.swagger.json`), or deploy with `paconn create`.
3. Create a connection and sign in as an Agent ID Administrator.

## Using it as an MCP server

Add the connector to a Copilot Studio agent. Copilot Studio detects the `x-ms-agentic-protocol: mcp-streamable-1.0` endpoint and surfaces all 73 tools.

| Group | Tools | Graph version |
|---|---|---|
| Blueprints | 13 | v1.0 |
| Blueprint principals | 6 | v1.0 |
| Federation and inherited permissions | 6 | v1.0 |
| Agent identities | 18 | v1.0 |
| Agent users | 10 | v1.0 |
| Lifecycle, governance, and passthrough | 7 | v1.0 |
| Risk, inherited permissions, and registry | 13 | **beta** |

Tools worth knowing about:

- **`provision_agent`** — the whole ordered sequence in one call: instantiate the blueprint if needed, create the agent identity with its sponsors, optionally create the agent user and assign its manager.
- **`get_agent_overview`** — one call returns an agent's identity, its blueprint, its owners and sponsors, its app role grants, its group memberships, and its agent user. This is the tool to reach for in a governance review, and it is also the delegated-connection workaround for reading sponsors.
- **`list_blueprint_principal_agents`** — the blast radius of a blueprint. Run this before any blueprint deletion, since that cascades to every agent it lists.
- **`add_blueprint_federated_credential`** — configures workload identity federation so an agent on AWS, n8n, or Kubernetes authenticates with no stored secret.
- **`set_inheritable_permissions`** — controls which delegated scopes every agent on a blueprint picks up without a separate consent prompt.
- **`check_blocked_permissions`** — screens a proposed permission set against the list Entra refuses to grant to agents. Entra rejects a blocked permission with an opaque `400` that does not name the offender, so run this first.
- **`set_agent_identity_enabled`** — the reversible kill switch. Always prefer it over deletion when responding to a misbehaving agent.
- **`list_risky_agents`** *(beta)* — the starting point for an incident, covering agent identities, agent users, and blueprint principals together.
- **`list_agent_inherited_permissions`** *(beta)* — what an agent inherits from its blueprint. Auditing only its direct assignments understates its reach.
- **`agent_id_graph_request`** — a guarded passthrough for anything not named, with a `version` argument for beta endpoints, restricted to the identity surface so it cannot be used to read mail or files.

Five prompts ship with the server: `onboard_agent`, `federate_third_party_agent`, `audit_agent_governance`, `investigate_agent_risk`, and `offboard_agent`. Each encodes the confirmation gates and the ordering constraints so the model does not have to rediscover them.

Seven resources expose the lifecycle model, the third-party federation patterns, the beta surface and its licensing, the blocked permission list, the documented error codes, the connector's own scopes, and the directory roles each operation needs.

## Beta surface

Four areas are Microsoft Graph **beta** — subject to change and unsupported for production. The connector's `basePath` is `/v1.0` and it rewrites the version segment for these operations only, so v1.0 and beta calls never cross over.

| Area | Tools | Requires |
|---|---|---|
| **Agent risk** | `list_risky_agents`, `get_risky_agent`, `list_agent_risk_detections`, `get_agent_risk_detection`, `confirm_agents_compromised`, `confirm_agents_safe`, `dismiss_agent_risk` | A **Microsoft Agent 365 license**, `IdentityRiskyAgent.*`, `IdentityRiskEvent.Read.All`. Security Reader to read, Security Administrator to act. |
| **Inherited permissions** | `list_agent_inherited_permissions` | `Application.Read.All` / `Directory.Read.All` |
| **Conditional Access what-if** | `evaluate_conditional_access` | `Policy.Read.ConditionalAccess` |
| **Agent registry** | `list_agent_instances`, `get_agent_instance`, `list_agent_collections`, `quarantine_agent_instance` | `AgentInstance.*`, `AgentCollection.ReadWrite.All`. Agent Registry Administrator. |

Three things to know before relying on these:

- **Containment is not the same as blocking.** Confirming an agent compromised raises its risk level, and quarantining moves it in the registry — neither stops it authenticating. Only `set_agent_identity_enabled` does that.
- **Inherited permissions are invisible to the ordinary tools.** An agent's effective access is its own assignments *plus* what its blueprint grants. Auditing only the former understates its reach. Note also that this route places the type cast *before* the id, unlike every other agentIdentity path.
- **The agent registry is transitional.** Microsoft replaces it with the [Agent Registry powered by Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/admin/graph-api) from May 2026. Two collections are reserved in every tenant and are immutable: Global (`…0001`) and Quarantined (`…0002`).

## Third-party agents: AWS, n8n, and Kubernetes

Agents running outside Entra should not hold a client secret. A blueprint doubles as a **token factory** through workload identity federation: the external platform's own token is exchanged for an Entra token, with nothing stored on the agent.

| Pattern | How it works | Best for |
|---|---|---|
| **Workload identity federation** | The platform's native token — AWS STS, a Kubernetes service account, a GCP workload identity — is exchanged directly for an Entra token | AWS agents using STS and OIDC, and anywhere federation infrastructure already exists |
| **Auth SDK sidecar** | A companion container acquires tokens on the agent's behalf, so agent code never touches credentials | Containerized agents, AWS Bedrock in your own orchestration, local Docker Compose development |
| **Blueprint as token factory** | The blueprint trusts Entra itself and issues tokens for its own agent identities, supporting both app-only and on-behalf-of flows | Platforms with a community node that acquires tokens per run, such as n8n |

Configure the trust with `add_blueprint_federated_credential`:

- `platform: entra_agent_identity` builds the issuer from your tenant id and uses the agent identity as the subject — the token-factory pattern.
- `platform: custom` takes the issuer and subject that your external provider puts in its tokens. Get these from that platform's own configuration; a wrong value fails at runtime with an unhelpful error.

The audience defaults to `api://AzureADTokenExchange`, which is what Entra expects in the `aud` claim. A blueprint holds at most 20 credentials, and each issuer/subject pair must be unique.

The sidecar itself is deployed outside Power Platform — this connector provisions the identity it authenticates as. See [Integrate third-party agents](https://learn.microsoft.com/entra/agent-id/configure-third-party-agents), [Secure an Amazon Bedrock agent](https://learn.microsoft.com/entra/agent-id/integrate-aws-bedrock-agent), and [Secure an n8n agent](https://learn.microsoft.com/entra/agent-id/integrate-n8n-agent).

## Inheritable permissions

A blueprint can grant its agent identities delegated scopes automatically, with no separate consent prompt. Three patterns:

| Pattern | Effect |
|---|---|
| `enumerated` | Inherit only the scopes you list. Prefer this. |
| `all_allowed` | Inherit everything the resource application publishes. Rarely the right answer. |
| `none` | Inherit nothing from that resource. |

Read them with `list_inheritable_permissions` and change them with `set_inheritable_permissions`. Moving to a more restrictive pattern means agents still needing the removed scopes must obtain fresh consent, so check what is live before narrowing.

## Using it as REST actions

Every operation other than `InvokeMCP` is a plain Microsoft Graph call for Power Automate and Logic Apps — 90 on v1.0 and 27 on beta, with the connector rewriting the version segment for the latter. A typical onboarding flow:

```
1. CreateBlueprint          → displayName, sponsors@odata.bind
2. CreateBlueprintPrincipal → appId from step 1
3. CreateAgentIdentity      → agentIdentityBlueprintId = appId from step 1, sponsors@odata.bind
4. CreateAgentUser          → identityParentId = id from step 3
5. SetAgentUserManager      → @odata.id pointing at the human manager
```

Reference bodies use the OData bind syntax Graph expects:

```jsonc
// sponsors, on create
{ "sponsors@odata.bind": ["https://graph.microsoft.com/v1.0/users/{userId}"] }

// owners, sponsors, and managers, on $ref endpoints
{ "@odata.id": "https://graph.microsoft.com/v1.0/directoryObjects/{objectId}" }
```

## Known constraints

| Constraint | Effect |
|---|---|
| `agentIdentity` sponsors are application-permission only | `ListAgentIdentitySponsors`, `AddAgentIdentitySponsor`, and `RemoveAgentIdentitySponsor` return `403` on a delegated connection. Read sponsors with `get_agent_overview`, which expands them from the identity itself. |
| Blocked Graph permissions | A long list of high-risk permissions cannot be granted to agents. Requesting one returns `400` without naming it. Use `check_blocked_permissions`. |
| One agent user per agent identity | A second `CreateAgentUser` against the same `identityParentId` returns `400`. |
| No password authentication for agent users | Agent users cannot sign in with a password by design. |
| `$skip` unsupported on agent users | Page the `agentUser` collection with the returned `@odata.nextLink`. |
| Inherited permission collections are read-only | `GET` only; `POST`, `PATCH`, and `DELETE` return `405`. Paging and OData query parameters are unsupported. Change inheritance on the blueprint instead. |
| Reserved registry collections are immutable | Updating or deleting Global or Quarantined returns `403 collectionImmutable`; creating a collection with a matching name returns `409`. |
| Blueprint `passwordCredentials` are not patchable | Use `AddBlueprintPassword` and `RemoveBlueprintPassword`. The `secretText` is returned once and never again. Prefer federation over secrets for external agents. |
| Agent identities cannot hold credentials | Every secret, certificate, and federation trust lives on the blueprint. Graph reports violations as `AgentIdentity_CredentialsNotSupported`. |
| 20 federated identity credentials per blueprint | Each issuer/subject pair must also be unique on the blueprint. |
| Federated credential `name` is immutable | To rename a trust, delete it and create a new one. |
| Page size 100 | Blueprints, blueprint principals, and agent identities cap at 100 per page. |

## Documented limits

| Limit | Value |
|---|---|
| Agent identities per blueprint (app-only auth) | 250 |
| Federated identity credentials per blueprint | 20 |
| `managerApplications` per blueprint | 10 (Microsoft first-party apps only) |
| Sponsors per blueprint or agent identity | 100, of which at most 5 may be groups |
| Resource services in `requiredResourceAccess` | 50 |
| Total permissions in `requiredResourceAccess` | 400 |
| `displayName` / `description` length | 256 / 1024 characters |
| Soft-delete retention | 30 days |

Soft-deleted objects continue to count against quota. If you are at the 250-identity limit, deleting an agent frees nothing until it is permanently deleted — use `permanently_delete_agent_object` to reclaim room immediately.

## Error codes

The connector recognizes all 17 documented [agent identity platform error codes](https://learn.microsoft.com/entra/agent-id/identity-platform/error-codes) and appends the concrete remedy to any failed call, so a tool error tells the model what to do rather than just what broke. The same catalogue is exposed as the `entra-agent-id://errors/codes` resource. The ones you are most likely to hit:

| Code | Meaning |
|---|---|
| `AgentIdentity_AgentBlueprintPrincipalDoesNotExist` | No blueprint principal in this tenant — create one first, or use `provision_agent` |
| `AgentIdentity_IncompatibleParentType` | You passed the blueprint's object id instead of its `appId` |
| `AgentIdentity_CredentialsNotSupported` | Credentials belong on the blueprint, not the identity |
| `Agent_Directory_QuotaExceeded` | Above 95% of tenant quota — purge soft-deleted objects |
| `AgentIdentity_LimitExceeded` | At the agent identity ceiling, counting soft-deleted ones |
| `Error_AgentIdentitiesCreatingAgentIdentitiesNotAllowed` | An agent cannot create other agents |

## Calling Graph and Azure *as* an agent

This connector manages agent identities. If you are instead writing the agent runtime that authenticates *as* one of them, that is handled by [Microsoft.Identity.Web](https://learn.microsoft.com/entra/agent-id/call-api-microsoft-graph):

| Target | Approach |
|---|---|
| **Microsoft Graph** | `AddMicrosoftGraph()` + `AddAgentIdentities()`, then `options.WithAgentIdentity(agentId)` with `RequestAppToken = true` for autonomous or `false` for on-behalf-of-user, or `options.WithAgentUserIdentity(agentId, upn)` to act as the agent's user account |
| **Your own APIs** | [`IDownstreamApi`](https://learn.microsoft.com/entra/agent-id/call-api-custom?tabs=idownstream) with the same authentication options |
| **Azure services** | [`MicrosoftIdentityTokenCredential`](https://learn.microsoft.com/entra/agent-id/call-api-azure-services) from *Microsoft.Identity.Web.Azure*, which implements the Azure SDK `TokenCredential` interface for Storage, Key Vault, and the rest |
| **Non-.NET runtimes** | The [Microsoft Entra ID Auth SDK sidecar](https://learn.microsoft.com/entra/msidweb/agent-id-sdk/overview) |

Avoid client secrets in production — prefer federated identity credentials with a managed identity, or a certificate.

## Files

- `apiDefinition.swagger.json` — OpenAPI 2.0 with 117 operations (90 v1.0, 27 beta) and OAuth2 (Entra ID) security
- `apiProperties.json` — OAuth2 settings and the `scriptOperations` list
- `script.csx` — the MCP server and the REST forwarder

## Still out of scope

Agent activity surfaces through existing non-agent APIs: sign-in and audit logs carry new `agentType` and `blueprintId` filter properties, and access packages, access reviews, and lifecycle workflows all accept agent identities through the standard identity governance endpoints. Reach those with `agent_id_graph_request`, which permits `/auditLogs`, `/roleManagement`, and `/identityGovernance` paths on either Graph version.

## Application Insights

Telemetry is off by default. Set `APP_INSIGHTS_CONNECTION_STRING` at the top of `script.csx` to a full connection string to record operation completions, errors, and MCP events. Failures in telemetry are swallowed and never affect a request.

## References

- [Microsoft Entra Agent ID APIs in Microsoft Graph overview](https://learn.microsoft.com/graph/api/resources/agentid-platform-overview?view=graph-rest-1.0)
- [What is Microsoft Entra Agent ID?](https://learn.microsoft.com/entra/agent-id/what-is-microsoft-entra-agent-id)
- [Agent identity blueprints](https://learn.microsoft.com/entra/agent-id/agent-blueprint)
- [The agent's user account](https://learn.microsoft.com/entra/agent-id/agent-users)
- [Integrate third-party agents with Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/configure-third-party-agents)
- [Secure an Amazon Bedrock agent](https://learn.microsoft.com/entra/agent-id/integrate-aws-bedrock-agent)
- [Secure an n8n agent](https://learn.microsoft.com/entra/agent-id/integrate-n8n-agent)
- [Call Microsoft Graph API from an agent using .NET](https://learn.microsoft.com/entra/agent-id/call-api-microsoft-graph)
- [Call custom APIs from an agent using .NET](https://learn.microsoft.com/entra/agent-id/call-api-custom?tabs=idownstream)
- [Call Azure services from an agent using .NET Azure SDK](https://learn.microsoft.com/entra/agent-id/call-api-azure-services)
- [ID Protection for agents](https://learn.microsoft.com/entra/id-protection/concept-risky-agents)
- [Conditional Access for agents](https://learn.microsoft.com/entra/identity/conditional-access/agent-id)
- [Agent Registry convergence with Microsoft Agent 365](https://learn.microsoft.com/entra/agent-id/agent-registry-convergence)
- [Agent identity platform error codes](https://learn.microsoft.com/entra/agent-id/identity-platform/error-codes)
