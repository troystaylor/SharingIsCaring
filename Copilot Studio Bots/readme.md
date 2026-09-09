# Copilot Studio Bots

Complete coverage of the [Power Platform API Bots operations](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/bots) for Microsoft Copilot Studio — all 13 documented operations — plus agent channel manifest export, a tenant-wide agent inventory that identifies which agents run on the GitHub Copilot harness, and Microsoft Entra Agent ID migration. Evaluation, governance, discovery, packaging, identity migration, and containment in a single connector, with native MCP support for Copilot Studio agents and optional Application Insights logging.

## Overview

This connector covers five related concerns:

- **Maker evaluation** — Manage test sets, trigger quality assessments, retrieve metrics, and download agent snapshots
- **Administration** — Quarantine and release agents, control the connector consent bypass, reassign ownership, and delete agents
- **Channel packaging** — Download the manifest package an agent publishes to a channel, for archival or inspection
- **Inventory and containment** — Find which agents run on the expensive GitHub Copilot harness, then quarantine the ones you choose
- **Entra Agent ID migration** — Move agents from their legacy app-registration identity to a Microsoft Entra Agent ID, and roll back if validation fails

The first two come from the documented Bots API. Channel packaging calls the documented [Download Agent Channel Manifest](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/agent-channels/download-agent-channel-manifest) endpoint, which lives under `agent-channels` rather than `bots`. The fourth reads an undocumented resource query API, because the Bots API exposes no harness field — see [Agent Inventory and Containment](#agent-inventory-and-containment). The fifth calls migration endpoints that share the Bots host and base path but are documented separately — see [Entra Agent ID Migration](#entra-agent-id-migration).

### Operation count

Several different totals appear in this document and in tooling output. They reconcile like this:

| Operations | | Count |
|---|---|---|
| Documented Bots API operations | evaluation + administration | 13 |
| `Download Agent Channel Manifest` | documented, but under `agent-channels` rather than `bots` | +1 |
| `List Agents` | agent inventory, not part of the Bots API | +1 |
| Agent identity migration | `Migrate Agent Identity`, `Roll Back Agent Identity` — not part of the Bots API | +2 |
| **Usable REST actions** | what you actually pick from in the designer | **17** |
| `Invoke Copilot Studio Bots MCP` | the JSON-RPC endpoint — it also appears in the action list, but ignore it in a flow | +1 |
| Internal dropdown sources | `Get Environment List`, `Get Agent List` — hidden by `x-ms-visibility: internal` | +2 |
| **Total in the OpenAPI definition** | what `ppcv` and PAC CLI report | **20** |

Separately, the connector exposes **19 MCP tools** to Copilot Studio. That is the 17 usable REST actions mapped one-to-one, plus two containment tools — `find_containment_candidates` and `contain_agents` — that compose several calls and have no single REST equivalent.

## Capabilities

### REST Operations (Power Automate)

| Operation | Method | Docs operation |
|-----------|--------|----------------|
| Get Agent Test Sets | GET | List Maker Evaluation Test Sets |
| Get Agent Test Set Details | GET | Get Maker Evaluation Test Set |
| Start Agent Evaluation | POST | Run Maker Evaluation Test Set |
| Get Agent Test Runs | GET | List Maker Evaluation Test Runs |
| Get Agent Test Run Details | GET | Get Maker Evaluation Test Run |
| Download Agent Evaluation Snapshot | GET | Download Maker Evaluation Snapshot |
| Download Agent Channel Manifest | GET | Download Agent Channel Manifest *(agent-channels, not Bots)* |
| Get Agent Quarantine Status | GET | Get Bot Quarantine Status |
| Quarantine Agent | POST | Set Bot As Quarantined |
| Release Agent From Quarantine | POST | Set Bot As Unquarantined |
| Get Connector Consent Bypass | GET | Get Connector Consent Bypass |
| Set Connector Consent Bypass | PUT | Set Connector Consent Bypass |
| Reassign Agent Owner | POST | Reassign Copilot Agent |
| Delete Agent | DELETE | Delete Copilot Agent |
| List Agents | GET | *(not a Bots operation — see Agent Inventory)* |
| Migrate Agent Identity To Entra Agent ID | POST | *(not a Bots operation — see Entra Agent ID Migration)* |
| Roll Back Agent Identity To App Registration | POST | *(not a Bots operation — see Entra Agent ID Migration)* |
| Get Environment List | GET | *(internal — backs the environment dropdown)* |
| Get Agent List | GET | *(internal — backs the agent dropdown)* |

The internal `Get Environment List` operation is hidden from the action picker and exists only to populate dropdowns.

### MCP Tools (Copilot Studio)

The 13 documented Bots operations are available as MCP tools, alongside inventory, containment, and identity migration:

| Tool | Description |
|------|-------------|
| `get_test_sets` | List all test sets for an agent |
| `get_test_set_details` | Get a specific test set with all test cases |
| `start_evaluation` | Launch an evaluation run with optional authentication |
| `list_test_runs` | Get all historical evaluation runs |
| `get_run_details` | Retrieve quality metrics and test results |
| `download_evaluation_snapshot` | Download the agent content snapshot for a run |
| `download_agent_channel_manifest` | Download the channel manifest package an agent publishes (M365 only) |
| `get_quarantine_status` | Read the quarantine state of an agent |
| `quarantine_agent` | Quarantine an agent |
| `unquarantine_agent` | Release an agent from quarantine |
| `get_connector_consent_bypass` | Read the admin consent bypass setting |
| `set_connector_consent_bypass` | Set the admin consent bypass setting |
| `reassign_agent` | Reassign the agent owner |
| `delete_agent` | Permanently delete an agent — requires `confirm: true` |
| `migrate_agent_identity` | Migrate an agent to a Microsoft Entra Agent ID |
| `rollback_agent_identity` | Revert an agent to its legacy app-registration identity |
| `list_agents` | Inventory agents with their harness, sharing, and publish state |
| `find_containment_candidates` | Read-only. Find GitHub Copilot harness agents worth quarantining |
| `contain_agents` | Quarantine an explicit list of agents — requires `confirm: true` |

## Safety Notes

Because this connector exposes destructive administrative operations to agents, three guards are built in:

- **`delete_agent` refuses to run without `confirm: true`.** Deletion is irreversible and the API has no undelete, so an agent cannot delete a copilot from an ambiguous instruction.
- **`contain_agents` accepts no filter and requires `confirm: true`.** It takes only an explicit list of agents, so no single call can quarantine a population the caller has not first seen and named.
- **`reassign_agent` returns `204 No Content`.** An empty body would read like a failure to a calling agent, so the tool converts it to `{ "status": 204, "succeeded": true }`.

If you want makers to run evaluations without any access to the administrative operations, restrict this connector to admin environments with a DLP policy. Note that the API itself returns **403** to non-administrators for the administrative operations regardless of connector configuration.

## Binary Downloads

Two tools return a binary ZIP: `download_evaluation_snapshot` and `download_agent_channel_manifest`. Both share one code path that reads the payload as bytes rather than text, so the archive is never corrupted:

- Files **4 MB or smaller** are returned as an MCP `resource` with a base64 `blob`
- **Larger files** return metadata only, with a pointer to the matching REST operation — use that in a flow to stream the file to SharePoint or OneDrive instead of through an agent conversation
- The file name comes from the `Content-Disposition` header, falling back to `evaluation-snapshot-{testRunId}.zip` or `agent-channel-manifest-{channel}.zip`

> **File names are sanitized, not trusted.** `Content-Disposition` is service-controlled data that ends up in an MCP resource URI. The connector reduces it to a bare file name — stripping any directory component so a traversal like `../../etc/evil.zip` becomes `evil.zip` — replaces characters that would break a URI, forces a `.zip` extension, and caps the length at 120 characters. If the header is missing or malformed, the generated fallback is used instead.

### Agent channel manifest

`download_agent_channel_manifest` calls the documented [Download Agent Channel Manifest](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/agent-channels/download-agent-channel-manifest) endpoint. It is synchronous — the ZIP comes back on a `200`, with no polling.

| Input | Required | Notes |
|-------|----------|-------|
| `environmentId` | Yes | Environment picker in the designer |
| `botId` | Yes | Agent picker, cascading from the environment |
| `channelName` | No | Defaults to `M365` |
| `includeAgentSchema` | No | Omitted from the request entirely when not supplied |

> **Only the M365 channel is supported.** Microsoft documents `M365` as the sole valid channel and publishes no error responses for this endpoint at all, so an undocumented channel value has no defined failure mode. The connector rejects anything else up front rather than sending a request whose outcome is unspecified. When Microsoft documents more channels, add them to `SupportedAgentChannels` in `script.csx`.

> **Authorization is evaluated before validation.** Live testing confirmed the endpoint returns **403** for an invalid channel name and for a nonexistent agent ID, not 400 or 404, when the caller lacks `CopilotStudio.MakerOperations.Read`. A 403 therefore does not prove the agent or channel is wrong — fix the permission first, then retry before concluding anything about the identifiers. The connector's own channel check runs client-side, so it still catches a bad channel regardless of permission state.

Useful in a flow for agent ALM: run it on a schedule and write the ZIP to SharePoint to keep a versioned record of what each agent publishes, or call it before and after a change to diff the manifest.

## Dynamic Dropdowns

Rather than making you paste GUIDs, most identifier fields are pickers in the Power Automate and Logic Apps designers:

| Field | Populated from | Notes |
|-------|----------------|-------|
| Environment ID | `GetEnvironmentDropdown` (internal) | Lists every environment you can access, by display name |
| Agent ID | `GetAgentDropdown` (internal) | Cascades from the environment. GitHub Copilot harness agents are labelled in the list |
| Test Set ID | `Get Agent Test Sets` | Cascades — pick the environment and agent first |
| Test Run ID | `Get Agent Test Runs` | Cascades — pick the environment and agent first |

The environment picker is backed by an internal operation on the path `/metadata/environments`. It is marked `x-ms-visibility: internal`, so it does not appear as a usable action; the script rewrites it to `https://api.powerplatform.com/environmentmanagement/environments`. That endpoint reports the environment identifier inconsistently — sometimes as a bare GUID in `name`, sometimes as an ARM-style path in `id` — so the connector normalizes both to the bare GUID the Bots operations expect.

The agent picker works the same way from `/metadata/agents`, backed by the resource query inventory. Because it already knows the harness, it appends `(GitHub Copilot)` to those agents in the list — so you can see which agent is the expensive one at the moment you pick it.

**Owner ID is not a dropdown** — resolving users requires Microsoft Graph, which is a different host and cannot back a picker in this connector.

## Agent Inventory and Containment

The Bots API can quarantine an agent but cannot tell you which agents are worth quarantining — it exposes no harness, template, or recognizer field. So this connector also reads the Power Platform **resource query** API to inventory agents, which makes discovery and containment a single connector with a single connection.

### Identifying the GitHub Copilot harness

`List Agents` returns `isCLIAgent` for every agent:

| `isCLIAgent` | `harness` | Cost profile |
|---|---|---|
| `"true"` | GitHub Copilot | 100–500+ Copilot Credits per task, billed regardless of M365 Copilot licensing |
| `"false"` | Standard or Copilot Chat | 1–20 credits per run, no charge for licensed employees |
| `"unknown"` | unknown | Not reported — investigate, do not assume |

Three things to know before you build on this:

- **It is a string, not a boolean.** A flow condition testing it as a boolean silently never matches.
- **`"unknown"` is not `"false"`.** An absent field is not evidence of the cheap harness, and that is the direction you least want a cost report to guess in.
- **`"false"` covers two harnesses.** It cannot separate Standard from Copilot Chat. The `harness` field says `Standard or Copilot Chat` rather than inventing precision the source does not have.

To confirm a single agent definitively, clone it with the VS Code extension: the GitHub Copilot harness projects `template: cliagent-1.0.0` and `recognizer.kind: CLICopilotRecognizer`, against `default-2.1.0` and `GenerativeAIRecognizer` for Standard.

### The plan/apply split

Discovery and containment are deliberately separate tools:

**`find_containment_candidates`** is read-only and quarantines nothing. It returns each matching agent with the `reasons` it qualified, plus `skippedUnknownHarness` and `skippedAlreadyQuarantined` counts so you can see what it declined to touch.

| Parameter | Default | Effect |
|---|---|---|
| `environmentId` | all | Scope to one environment |
| `requireTenantWide` | `true` | Only agents shared with the entire tenant |
| `requireNeverPublished` | `false` | Only agents that have never been published |
| `includeUnknownHarness` | `false` | Include agents whose harness was not reported |

**`contain_agents`** takes an explicit array of `{ environmentId, botId }` pairs and `confirm: true`. **It accepts no filter by design** — a caller cannot quarantine a population it has not first seen and named, so no single call can sweep the estate. Failures are per-agent: one 403 does not abandon the rest of the batch, and the result reports `succeeded`, `failed`, and a per-agent breakdown.

Quarantine is reversible with `unquarantine_agent`, which is why containment is the safe first response rather than deletion.

### Example: contain expensive, tenant-wide, unpublished agents

```
User: "Find GitHub Copilot harness agents shared with everyone that were never published"
Agent:
  1. find_containment_candidates { requireNeverPublished: true }
     → 2 candidates, each with reasons
       [githubCopilotHarness, sharedWithEntireTenant, neverPublished]
     → skippedUnknownHarness: 1   (reported, not contained)
  2. Presents them with owners and environments
User: "Quarantine both"
  3. contain_agents { agents: [ {...}, {...} ], confirm: true }
     → succeeded: 2
```

### Caveats

- **Undocumented API.** `resourcequery` returns the resource provider's raw property bag, and `isCLIAgent` is not in the published reference. It can change shape or disappear without notice. Use it for reporting and chargeback triage, not as a hard enforcement gate.
- **Paging ceiling.** The connector pages 1,000 rows at a time and stops after 10 pages, setting `truncated: true` rather than silently returning a partial estate. Scope to an environment if you hit it.
- **`SkipToken` does not work.** The service returns one but it never advances, so the connector pages with `Skip` offsets and orders by a unique tiebreaker to keep the window stable.

## Entra Agent ID Migration

Copilot Studio automatically creates a [Microsoft Entra Agent ID](https://learn.microsoft.com/microsoft-copilot-studio/admin-use-entra-agent-identities) for every agent created after May 2026. Agents created before that still carry a legacy app registration. Microsoft will migrate them automatically in a future update, but you can migrate on your own schedule to validate agents against Conditional Access first.

These two operations wrap the [migration API](https://learn.microsoft.com/microsoft-copilot-studio/govern-migrate-api-entra-agent-identity). They live on the same host, base path, and connection as the Bots operations, but are documented separately from the Bots REST reference.

| Operation | Endpoint | Terminal status |
|---|---|---|
| `Migrate Agent Identity To Entra Agent ID` | `POST .../api/agentidentitymigration/migrate` | `Migrated`, `AlreadyMigrated` |
| `Roll Back Agent Identity To App Registration` | `POST .../api/agentidentitymigration/rollback` | `RolledBack`, `NotMigrated` |

Neither takes a body. Both use the same environment and agent dropdowns as every other operation.

Migration converts the identity **in place** — the agent keeps its application (client) ID, so channel registrations and connectors continue to resolve to the same identifier. The response also reports the new `agentIdentityId` and `servicePrincipalObjectId` for lookup in the Microsoft Entra admin center.

### Before you migrate

- **Preview.** The manual migration path is a preview feature and the response shape can change.
- **Requires a tenant admin.** Power Platform, Dynamics 365, or Global Administrator. Non-administrators get **403**.
- **Migrate in batches.** Microsoft's guidance is to pilot a small set of noncritical agents first, validate each across its channels, actions, connectors, and authentication flows, then expand. Don't sweep the estate in one pass.
- **`AlreadyMigrated` is not an error.** Re-running against a migrated agent is safe and idempotent.
- **Throttling is expected on bulk runs.** The connector surfaces `429` rather than retrying, so back off and retry in your flow.

### Example: staged migration

```
User: "Migrate the agents in my sandbox environment to Entra Agent ID"
Agent:
  1. list_agents { environmentId: "<sandbox>" }
     → presents the agents with owners and publish state
User: "Start with the two unpublished ones"
  2. migrate_agent_identity { environmentId, botId }   × 2
     → status: Migrated, agentIdentityId: <guid>
User: "The second one broke its Teams channel — undo it"
  3. rollback_agent_identity { environmentId, botId }
     → status: RolledBack
```

Rollback is why migration is reasonable to expose to an agent at all: unlike `delete_agent`, it is reversible with a single call, which is the same reasoning that leaves `quarantine_agent` ungated.

## Authentication

OAuth 2.0 with Microsoft Entra ID — requires an app registration with Power Platform API access.

**Required Scope:** `https://api.powerplatform.com/.default`

### Prerequisites

1. **App Registration**
   - Register an application in Microsoft Entra ID
   - Add the delegated **Power Platform API** permissions listed below (resource ID `8578e004-a5c6-46e7-913e-12f58912df43`) and grant admin consent
   - Replace `REPLACE_WITH_CLIENT_ID` in `apiProperties.json` with the Client ID

2. **Delegated permissions**

   The Bots endpoints split across two permission families. Which one you need depends on the operation, not the connector — granting only one leaves the other half returning **403**.

   | Operations | Required delegated permission |
   |---|---|
   | Evaluation: test sets, test runs, snapshots | `CopilotStudio.MakerOperations.Read` |
   | Start an evaluation run | `CopilotStudio.MakerOperations.ReadWrite` |
   | Download Agent Channel Manifest | `CopilotStudio.MakerOperations.Read` |
   | Quarantine, consent bypass, reassign | `CopilotStudio.AdminActions.Invoke` |
   | Delete agent | `CopilotStudio.MakerOperations.Delete` |
   | Agent inventory and containment | Power Platform admin role (no documented `resourcequery` scope) |

   > **These names are not in Microsoft's [permission reference](https://learn.microsoft.com/power-platform/admin/programmability-permission-reference).** That page lists only `CopilotStudio.AdminActions.Invoke` and `CopilotStudio.Copilots.Invoke`. The Power Platform API service principal actually publishes eight `CopilotStudio.*` delegated scopes, and the endpoints enforce the undocumented ones. The table above was derived from live `403` responses, which name the accepted permissions in `innererror.message`:
   >
   > ```json
   > {"code":"Forbidden","innererror":{"code":"InsufficientDelegatedPermissions",
   >  "message":"Application missing required delegated permissions: [CopilotStudio.MakerOperations.Read, CopilotStudio.MakerOperations.ReadWrite, All.All.ReadWrite]"}}
   > ```
   >
   > If an operation returns 403 with a permission you do not recognize, read `innererror.message` — it names exactly what to grant. `All.All.ReadWrite` also appears in those messages but is **not** a grantable scope on this service principal; ignore it and grant the specific `CopilotStudio.*` permission instead.

   Add a permission by ID:

   ```powershell
   # CopilotStudio.MakerOperations.Read
   az ad app permission add --id <appId> `
     --api 8578e004-a5c6-46e7-913e-12f58912df43 `
     --api-permissions 4bbe06bc-340b-4b7c-8d99-775c5becc1c7=Scope
   az ad app permission admin-consent --id <appId>
   ```

3. **Roles**
   - Evaluation operations require maker access to the agent
   - Administrative operations require Power Platform or Dynamics 365 administrator privileges
   - The agent inventory reads tenant-wide resources, so it also requires administrator privileges

4. **Agent Identifiers**
   - Environment ID — The Dataverse environment containing your agent
   - Bot ID — The Copilot Studio agent identifier
   - Both are dropdowns in the designer, so you rarely need to supply them by hand

5. **(Optional) User Profiles**
   - For authenticating agent connections during evaluation, obtain the MCS Connection ID:
     1. Go to [Power Automate](https://make.powerautomate.com/)
     2. Open the Connections page
     3. Select the Microsoft Copilot Studio connection
     4. Copy the `mcsConnectionId` from the URL

## Configuration

### 1. Set Up App Registration

```powershell
# Create the app registration
az ad app create --display-name "Copilot Studio Bots Connector" `
  --sign-in-audience AzureADMyOrg `
  --web-redirect-uris "https://global.consent.azure-apim.net/redirect"

# Grant the delegated Power Platform API permissions this connector needs.
# See Prerequisites above for which operations require which permission.
$ppapi = "8578e004-a5c6-46e7-913e-12f58912df43"
az ad app permission add --id <appId> --api $ppapi --api-permissions `
  4bbe06bc-340b-4b7c-8d99-775c5becc1c7=Scope `   # CopilotStudio.MakerOperations.Read
  d5061c0f-71ab-40de-ad68-1855ecedc721=Scope `   # CopilotStudio.MakerOperations.ReadWrite
  db2c958a-27e1-4238-8eb8-610a8b5d8436=Scope     # CopilotStudio.AdminActions.Invoke

az ad app permission admin-consent --id <appId>
```

Add `CopilotStudio.MakerOperations.Delete` (`0793f40a-abd0-427e-9b9e-00b7dab6b50c`) only if you intend to use `Delete Agent`.

> After deploying, the platform generates a per-connector redirect URL that must also be registered on the app registration or consent fails. Read it back with `pac connector download` and add it — see [Register the Connector](#2-register-the-connector).

### 2. Register the Connector

```powershell
# Validate all three files against the official schemas
ppcv "./Copilot Studio Bots"

# Deploy. --script-file is mandatory: apiProperties.json declares scriptOperations,
# and the service rejects the deployment without a script to route them to.
pac connector create `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Two failures are worth knowing about before you run this:

| Error | Cause |
|-------|-------|
| `InvalidScriptDefinitionUrlWithNonNullOperations` | `--script-file` was omitted while `scriptOperations` is non-empty. Add the script, or strip `scriptOperations` in a throwaway copy for a REST-only deployment. |
| `CustomScriptProvisioningFailed` / `FindAndAssignFunctionApp` | The region has no unassigned function app for custom code. Creating a new script-enabled connector draws from that pool; updating an existing one reuses its assignment and usually still works. |

Without the script the 13 Bots operations still work — they pass through untouched — but the MCP endpoint, both dropdowns, `List Agents`, and the containment tools do not.

To update an existing connector, add `--connector-id`:

```powershell
pac connector update `
    --connector-id 00000000-0000-0000-0000-000000000000 `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Deploying with `REPLACE_WITH_CLIENT_ID` still in place succeeds, but the connector is unusable because no connection can be created. Set the real client ID first.

### 3. Create Connection in Power Automate

1. Go to Power Automate
2. Select **My Connections**
3. New Connection → Copilot Studio Bots
4. Sign in with your Entra ID credentials

## Usage Examples

### Power Automate Flow

**Scenario: Evaluate nightly, quarantine on regression**

```
1. Trigger: Scheduled (daily at 2 AM)
2. Get Agent Test Sets
3. For Each test set  (leave Apply-to-each concurrency OFF — see Known Issues)
   4. Start Agent Evaluation
   5. Poll Get Agent Test Run Details until state is Completed or Failed
   6. Count test cases where relevance or completeness is false
   7. Condition: failing count above your threshold
      8. Quarantine Agent
      9. Download Agent Evaluation Snapshot and save to SharePoint
      10. Email the agent owner with the failing test cases and aiResultReason
```

This is the workflow that motivated a single connector — it spans evaluation and administration in one flow, with one connection.

**Scenario: Archive what each agent publishes to Microsoft 365**

```
1. Trigger: Scheduled (weekly)
2. List Agents  (leave Environment ID blank to sweep the tenant)
3. Filter array: channels contains 'Microsoft 365 Copilot'
4. For Each remaining agent:
   5. Download Agent Channel Manifest
      - Environment ID:      item()?['environmentId']
      - Agent ID:            item()?['botId']
      - Channel:             M365
      - Include Agent Schema: Yes
   6. Create file in SharePoint
      - Name:    concat(item()?['displayName'], '-', utcNow('yyyy-MM-dd'), '.zip')
      - Content: body('Download_Agent_Channel_Manifest')
```

Pipe the manifest straight from the action into the storage connector. Holding it in a
variable first can hit flow content limits on larger packages. Run the same flow before and
after a change to diff what an agent actually publishes.

**Scenario: Reclaim agents owned by departed employees**

```
1. Trigger: Scheduled (weekly)
2. List Agents  (leave Environment ID blank to sweep the tenant)
3. For Each agent in agents[] whose ownerId is disabled in Entra ID:
   4. Reassign Agent Owner
      - Environment ID: item()?['environmentId']
      - Agent ID:       item()?['botId']
      - New Owner Entra User ID: [governance service account object ID]
```

**Scenario: Report the cost exposure of the GitHub Copilot harness**

```
1. Trigger: Scheduled (monthly)
2. List Agents  (blank Environment ID)
3. Filter array: isCLIAgent is equal to 'true'
4. Create an HTML table of displayName, environmentName, ownerId, lastPublishedAt
5. Email it to the platform team
6. Separately, filter isCLIAgent equal to 'unknown' and list those as
   "harness unresolved — investigate", never as Standard
```

### Copilot Studio Agent

**System Prompt:**
```
You are a Copilot Studio quality and governance assistant.

To assess an agent:
1. Use get_test_sets to find available test suites
2. Use start_evaluation to trigger an assessment
3. Wait 30-60 seconds, then use get_run_details for the results
4. Report how many test cases passed, and for each failure quote the
   aiResultReason. Abstention, relevance, and completeness are true/false
   per test case, not scores, so never report them as percentages.

To contain a problem agent:
1. Use get_quarantine_status to check whether it is already contained
2. Use quarantine_agent to block end user access during investigation
3. Use get_connector_consent_bypass and report an enabled bypass as a risk
4. Use unquarantine_agent once an administrator confirms it is safe

To audit the estate for cost exposure:
1. Use list_agents, or find_containment_candidates for a narrower sweep
2. isCLIAgent is 'true', 'false', or 'unknown' — never a boolean. 'true' means
   the GitHub Copilot harness, which bills Copilot Credits per task. 'false'
   means Standard or Copilot Chat and cannot distinguish between them.
3. Report 'unknown' as unresolved and worth investigating. Never describe it
   as Standard, because the platform did not say so.
4. To act, call contain_agents with the specific agents the administrator
   approved. It takes no filter, so name them explicitly.

Never call delete_agent unless an administrator explicitly asks for permanent
deletion and names the agent. Prefer quarantine, which is reversible.
```

## Reading the Results

`Get Agent Test Run Details` / `get_run_details` returns a run object whose results nest three levels deep:

```
run
├── state              Pending | Running | Completed | Failed
├── totalTestCases
└── testCaseResults[]
    ├── testCaseId
    ├── state          Passed | Failed | Error
    ├── errorReason    populated when the case failed
    ├── aiResultReason AI-generated explanation of the outcome
    └── metricsResults[]
        ├── type       e.g. GeneralQuality, Hallucination
        └── result
            ├── abstention    boolean — did the agent decline to answer?
            ├── relevance     boolean — was the answer relevant?
            └── completeness  boolean — was the answer complete?
```

**These three metrics are booleans per test case, not scores.** There is no percentage or 0-1 confidence value in the response. To produce a quality figure, aggregate across `testCaseResults` yourself — for example, "relevance false in 3 of 20 cases". Treat `abstention: true` as neutral rather than a failure; a well-behaved agent should abstain on out-of-scope questions.

`aiResultReason` is the most useful field for a human or an agent to summarize, since it explains *why* a case landed where it did.

### Inventory and containment responses

`List Agents` / `list_agents` returns one object, **not** a paged list — apply-to-each over `agents`:

```
agentCount      how many agents came back
truncated       true when the page ceiling was hit and agents are missing
environmentId   the environment scanned, or null for the whole tenant
agents[]
├── botId                          use this as Agent ID everywhere else
├── displayName, schemaName
├── environmentId, environmentName, environmentType, environmentIsManaged
├── createdAt, createdBy, lastPublishedAt   empty lastPublishedAt = never published
├── ownerId
├── isQuarantined                  boolean
├── isCLIAgent                     'true' | 'false' | 'unknown'  (string)
├── harness                        readable label derived from isCLIAgent
├── sharedWithViewersEntireTenant  boolean
├── sharedWithViewersUserCount, sharedWithEditorsUserCount
└── channels[]
```

`find_containment_candidates` wraps the same agent objects with the reasons each qualified, and reports what it declined to touch:

```
candidateCount, scannedCount, truncated
skippedUnknownHarness       agents whose harness was not reported
skippedAlreadyQuarantined   agents already contained
criteria                    the filters actually applied, echoed back
candidates[]                agent objects, each with an added reasons[] array
                            e.g. [githubCopilotHarness, sharedWithEntireTenant,
                                  neverPublished]
```

`contain_agents` reports per-agent outcomes:

```
requested, succeeded, failed
results[]
├── environmentId, botId
├── succeeded   boolean
├── status      HTTP status from the quarantine call
└── error       present only on failure
```

Read `succeeded` and `failed` rather than the MCP `isError` flag, which is set only when the whole batch failed.

## Notes for Flow Authors

- **`isCLIAgent` is a string.** Compare against `'true'`, `'false'`, `'unknown'`. A condition testing it as a boolean silently never matches.
- **`List Agents` returns an object, not an array.** Apply-to-each over `agents`, and check `truncated` before treating the result as a complete estate.
- **`channels` and `reasons` are string arrays.** Flatten with `join(item()?['reasons'], ', ')` for a table or email body.
- **Empty `lastPublishedAt` means never published**, not "unknown date".
- **Quarantine and consent bypass are writes with no ETag.** Avoid running them on a fan-out loop over the same agent.
- **Leave Apply-to-each concurrency off** when starting evaluations, since only one run per agent is allowed at a time.

## Known Issues and Limitations

- **One evaluation run at a time per agent.** `Start Agent Evaluation` returns **422** if a run is already in progress. When looping over multiple test sets in Power Automate, leave Apply-to-each concurrency **off** and poll each run to completion before starting the next, or the second iteration will fail.
- **Evaluation is asynchronous.** `Start Agent Evaluation` returns a run ID immediately; results are not available until `state` reaches `Completed`. Poll `Get Agent Test Run Details`, and treat `Failed` as a terminal state so a polling loop cannot spin forever.
- **Test sets must be Active** to be evaluated. Inactive sets are returned by `Get Agent Test Sets` but will not run.
- **Snapshots over 4 MB are not inlined into MCP responses.** The tool returns metadata and a pointer instead. Use the REST operation in a flow for large archives.
- **Deletion is permanent.** There is no undelete operation in this API. The `delete_agent` MCP tool requires `confirm: true`; the REST `Delete Agent` operation has no such guard, so treat it carefully in flows.
- **Administrative operations require tenant admin rights.** Non-administrators receive **403** regardless of connector or DLP configuration.
- **Quarantine is not deletion.** A quarantined agent stays editable for makers and administrators; it is only blocked for end users.
- **Enabling connector consent bypass removes an end user safeguard.** Audit which connections the agent uses before turning it on.
- **`api-version` is pinned to `2024-10-01`.** It is exposed as a parameter with that default; the MCP tools always send it.
- **Dropdowns are a designer-only convenience.** MCP tools receive raw IDs, so an agent calling `quarantine_agent` still needs the environment and agent GUIDs. Dynamic values do not apply to MCP tool arguments.
- **Binary downloads have a 4 MB inline ceiling.** `Download Agent Evaluation Snapshot` and `Download Agent Channel Manifest` return the ZIP inline to an agent only up to 4 MB; beyond that the MCP tool returns metadata and tells you to use the REST action. In a flow, pipe the REST action's output straight into a storage connector rather than through a variable.
- **`Download Agent Channel Manifest` accepts only the `M365` channel.** The connector rejects other values before calling, since the endpoint documents no error responses. `includeAgentSchema` is omitted from the request entirely when you leave it blank, rather than sent as `false`.
- **The agent inventory depends on an undocumented API.** `List Agents`, the agent dropdown, and both containment tools read `resourcequery` at `api-version=2022-03-01-preview`, which returns the resource provider's raw property bag. `isCLIAgent` is not in the published reference and can change shape or disappear without notice. Use it for reporting and chargeback triage, not as a hard enforcement gate.
- **That API is also intermittently unreliable.** Live testing caught it returning `400 Bad Request` with body `"KQLOM format is wrong or it cannot be null"` for a byte-identical payload that had succeeded moments earlier. It is not throttling — the status is 400 with no `Retry-After`. The connector retries that specific failure up to three times with backoff, alongside `429` and `5xx`; a genuine client error still fails on the first attempt. Exhausted retries are reported as `after 3 attempts`.
- **`isCLIAgent` is a string with three values.** Compare against `'true'`, `'false'`, and `'unknown'`. A flow condition testing it as a boolean silently never matches, and `'unknown'` must never be treated as `'false'`.
- **`isCLIAgent: 'false'` covers two harnesses.** It cannot separate Standard from Copilot Chat. To confirm a single agent definitively, clone it and read `template` and `recognizer.kind`.
- **The inventory pages to a ceiling.** It reads 1,000 agents per page and stops after 10 pages. Check `truncated` before treating a result as a complete estate, and scope to an environment if it is `true`.
- **`contain_agents` reports partial failures inside the payload.** `isError` is set only when every agent in the batch failed, so always read `succeeded`, `failed`, and the per-agent `results` rather than relying on `isError` alone.
- **Entra Agent ID migration is a preview API.** `Migrate Agent Identity` and `Roll Back Agent Identity` are documented outside the Bots REST reference and their response shape can change. Migration is reversible with rollback, but it affects live agents — pilot a small batch and validate before expanding.
- **Migration is single-agent.** There is no batch endpoint, so a bulk run means one call per agent. Expect **429** throttling and back off between calls.

## Troubleshooting

| Status | Meaning | What to do |
|--------|---------|------------|
| 400 | Malformed body | Check `adminConsentBypass` is a boolean and `NewOwnerAadUserId` is a valid Entra object ID |
| 401 | Token invalid or expired | Reauthorize the connection; confirm the app registration has Power Platform API permission |
| 403 | Either the app registration lacks the delegated permission, or the caller lacks tenant admin rights | Read `innererror.code`. `InsufficientDelegatedPermissions` names the accepted scopes in `innererror.message` — grant one of those. Otherwise sign in as a Power Platform or Dynamics 365 administrator |
| 404 | Agent, environment, test set, or run not found | Verify the Environment ID and Agent ID; a 404 on snapshot usually means the run ID is wrong or the run never completed |
| 422 | An evaluation run is already in progress | Wait for the current run to reach `Completed` or `Failed`, then retry |
| 500 | Service-side failure on reassign | Retry; if it persists confirm the new owner exists and has access to the environment |
| Empty agent dropdown | The inventory query failed or returned nothing | The picker swallows errors to avoid breaking the designer. Call `List Agents` directly to see the underlying error |
| `List Agents` returns 400 | The resource query was rejected | Usually a schema change in the undocumented preview API. Confirm you can still read agents in PPAC, then check whether the query shape has changed |
| `truncated: true` | More than 10,000 agents matched | Scope the call to a single environment |
| 429 on migrate or rollback | The service is throttling identity migration | Wait and retry. The connector does not retry for you |
| `status: AlreadyMigrated` | The agent already has an Entra Agent ID | Expected and idempotent, not a failure |
| `status: NotMigrated` on rollback | The agent was never migrated | Expected, not a failure |

MCP tool calls surface these as `isError: true` with the status code in the message text rather than as JSON-RPC errors, so a Copilot Studio agent can read and explain the failure. JSON-RPC errors (`-32601`, `-32602`) are reserved for unknown methods and unknown or malformed tool calls.

The composite tools — `list_agents`, `find_containment_candidates`, and `contain_agents` — are the exception. They report outcomes inside the payload and set `isError` only when the entire operation failed, so a batch where some agents were quarantined and others were rejected returns `isError: false` with the failures itemized in `results`.

## Application Insights Logging

The connector includes **hardcoded Application Insights telemetry**, disabled by default:

- **MCP requests** — Method and request ID for each JSON-RPC call
- **MCP tool invocations** — Tool name and backend HTTP status
- **Errors and exceptions** — Detailed diagnostics with stack traces

### Setup Application Insights

1. Create an Application Insights resource in Azure
2. Copy the **Instrumentation Key**
3. Edit `script.csx` in the connector folder
4. Set `private const bool APP_INSIGHTS_ENABLED = true;`
5. Replace `[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]` with your actual key
6. Redeploy the connector using `pac connector create`

**Example:**
```csharp
// BEFORE
private const bool APP_INSIGHTS_ENABLED = false;
private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";

// AFTER
private const bool APP_INSIGHTS_ENABLED = true;
private const string APP_INSIGHTS_KEY = "12345678-1234-1234-1234-123456789012";
```

**To disable logging:** Set `APP_INSIGHTS_ENABLED = false`.

### Log Query Examples (Azure Portal)

**Audit every tool call, especially administrative ones:**
```kusto
customEvents
| where name == "MCP_ToolCall"
| summarize Count = count() by tostring(customDimensions.tool), tostring(customDimensions.status)
```

`status` is the backend HTTP status code for single-call tools, and `ok` or `failed` for the composite tools (`list_agents`, `find_containment_candidates`, `contain_agents`), which issue several calls per invocation.

**Find every containment action taken:**
```kusto
customEvents
| where name == "MCP_ToolCall"
| where tostring(customDimensions.tool) in ("contain_agents", "quarantine_agent", "delete_agent")
| project timestamp, tool = tostring(customDimensions.tool), status = tostring(customDimensions.status)
| order by timestamp desc
```

**Find errors and exceptions:**
```kusto
exceptions
| where tostring(customDimensions.connector) == "Copilot Studio Bots"
| project timestamp, outerType, outerMessage, customDimensions.operation
```

## API Reference

The 13 Bots operations target `https://api.powerplatform.com/copilotstudio` with `api-version=2024-10-01`, where `{base}` is `/environments/{environmentId}/bots/{botId}`.

| Operation | Endpoint |
|-----------|----------|
| Get Agent Test Sets | `GET {base}/api/makerevaluation/testsets` |
| Get Agent Test Set Details | `GET {base}/api/makerevaluation/testsets/{testSetId}` |
| Start Agent Evaluation | `POST {base}/api/makerevaluation/testsets/{testSetId}/run` |
| Get Agent Test Runs | `GET {base}/api/makerevaluation/testruns` |
| Get Agent Test Run Details | `GET {base}/api/makerevaluation/testruns/{testRunId}` |
| Download Agent Evaluation Snapshot | `GET {base}/api/makerevaluation/testruns/{testRunId}/snapshot` |
| Get Agent Quarantine Status | `GET {base}/api/botQuarantine` |
| Quarantine Agent | `POST {base}/api/botQuarantine/SetAsQuarantined` |
| Release Agent From Quarantine | `POST {base}/api/botQuarantine/SetAsUnquarantined` |
| Get Connector Consent Bypass | `GET {base}/api/connectorConsentBypass` |
| Set Connector Consent Bypass | `PUT {base}/api/connectorConsentBypass` |
| Reassign Agent Owner | `POST {base}/api/botAdminOperations/reassign` |
| Delete Agent | `DELETE {base}/api/botAdminOperations` |

Two further operations share the same host, base path, and connection, but are documented outside the Bots REST reference. They are preview:

| Operation | Endpoint |
|-----------|----------|
| Migrate Agent Identity To Entra Agent ID | `POST {base}/api/agentidentitymigration/migrate` |
| Roll Back Agent Identity To App Registration | `POST {base}/api/agentidentitymigration/rollback` |

One documented operation shares the host and base path but sits under `agents/{id}` rather than `bots/{id}`, so it does not use `{base}`:

| Operation | Endpoint |
|-----------|----------|
| Download Agent Channel Manifest | `GET /environments/{environmentId}/agents/{botId}/channels/{channelName}/download` |

Three operations reach different Power Platform API surfaces on the same host. Their swagger paths are facades that the script rewrites, which is why they work despite the connector's `/copilotstudio` base path:

| Operation | Swagger path | Actual endpoint |
|-----------|--------------|-----------------|
| List Agents | `/agents` | `POST /resourcequery/resources/query?api-version=2022-03-01-preview` |
| Get Environment List *(internal)* | `/metadata/environments` | `GET /environmentmanagement/environments?api-version=2024-10-01` |
| Get Agent List *(internal)* | `/metadata/agents` | `POST /resourcequery/resources/query?api-version=2022-03-01-preview` |

All of them authenticate with the same token, because every surface sits behind the `https://api.powerplatform.com` AAD resource.

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition for the 17 usable REST actions, two internal dropdown sources, and the MCP endpoint |
| `apiProperties.json` | OAuth 2.0 configuration and script operation registration |
| `script.csx` | MCP JSON-RPC 2.0 handler, agent inventory and containment, Entra Agent ID migration, binary ZIP handling for snapshots and channel manifests, and optional telemetry |
| `readme.md` | This document |

## Related Connectors

| Connector | Coverage |
|-----------|----------|
| [Power Platform Admin](../Power%20Platform%20Admin/) | Environment settings and the original tenant-wide agent inventory this connector's `List Agents` is ported from |
| [Copilot Studio Analytics](../Copilot%20Studio%20Analytics/) | Dataverse-based conversation transcripts and session analytics |
| [Copilot Package Management](../Copilot%20Package%20Management/) | Microsoft 365 Copilot package catalog: block, unblock, and reassign |

## History

This connector supersedes the earlier `Copilot Studio Evaluations` and `Copilot Studio Agent Administration` folders, which split the same API group across two connectors. They were merged so that a single connection covers the whole Bots surface, matching the pattern used by Copilot Package Management.

The agent inventory was later ported from the [Power Platform Admin](../Power%20Platform%20Admin/) connector so that discovery and containment live together: the Bots API can quarantine an agent but cannot tell you which agents are worth quarantining. That port is the connector's only dependency on an undocumented API.

### 1.1

Added the two [Entra Agent ID migration](#entra-agent-id-migration) operations — `Migrate Agent Identity To Entra Agent ID` and `Roll Back Agent Identity To App Registration` — with matching `migrate_agent_identity` and `rollback_agent_identity` MCP tools. They sit on the Bots host and base path but are documented separately from the Bots REST reference, and are preview.

### 1.2

Added [Download Agent Channel Manifest](#agent-channel-manifest) — the July 2026 `agent-channels` endpoint — as both a REST action and the `download_agent_channel_manifest` MCP tool. Only the `M365` channel is supported; anything else is rejected client-side because the endpoint publishes no error responses at all.

Both binary downloads now share one code path. Downloaded file names are sanitized before use: the previous handler trimmed quotes off `Content-Disposition` and passed the result straight into an MCP resource URI, so a service-supplied name containing a path or URI delimiter went through intact. Names are now reduced to a bare, bounded `.zip` file name.

### 1.2.1

Two defects found by testing against the live API:

- **Environment fields were read from the wrong place.** `GET /environmentmanagement/environments` returns `displayName`, `type`, `state`, and `url` at the top level; the connector read them from a nested `properties` object that no environment actually returns. The environment picker fell back to showing raw GUIDs. Fields are now read flat-first with a nested fallback, so both shapes work.
- **The agent inventory had no retry.** The undocumented `resourcequery` API intermittently returns `400` with `"KQLOM format is wrong or it cannot be null"` for a payload it accepted moments earlier — reproduced with a byte-identical body. A single blip failed `List Agents`, the agent picker, and both containment tools. That specific failure is now retried up to three times, alongside `429` and `5xx`.

## Author

**Troy Taylor**

- Email: troy@troystaylor.com
- GitHub: [troystaylor](https://github.com/troystaylor)
