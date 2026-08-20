# Copilot Studio Bots

**Publisher: Troy Taylor**

Complete coverage of the [Power Platform API Bots operations](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/bots) for Microsoft Copilot Studio — all 13 documented operations — plus a tenant-wide agent inventory that identifies which agents run on the GitHub Copilot harness. Evaluation, governance, discovery, and containment in a single connector, with native MCP support for Copilot Studio agents and optional Application Insights logging.

## Overview

This connector covers three related concerns:

- **Maker evaluation** — Manage test sets, trigger quality assessments, retrieve metrics, and download agent snapshots
- **Administration** — Quarantine and release agents, control the connector consent bypass, reassign ownership, and delete agents
- **Inventory and containment** — Find which agents run on the expensive GitHub Copilot harness, then quarantine the ones you choose

The first two come from the documented Bots API. The third reads an undocumented resource query API, because the Bots API exposes no harness field — see [Agent Inventory and Containment](#agent-inventory-and-containment).

### Operation count

Three different totals appear in this document and in tooling output. They reconcile like this:

| Operations | | Count |
|---|---|---|
| Documented Bots API operations | evaluation + administration | 13 |
| `List Agents` | agent inventory, not part of the Bots API | +1 |
| Internal dropdown sources | `Get Environment List`, `Get Agent List` | +2 |
| **REST operations** | what appears in the Power Automate action list, minus the internal two | **16** |
| `Invoke Copilot Studio Bots MCP` | the JSON-RPC endpoint | +1 |
| **Total in the OpenAPI definition** | what `ppcv` and PAC CLI report | **17** |

Separately, the connector exposes **16 MCP tools** to Copilot Studio. That number matches the REST count by coincidence, not by construction: the REST side includes two internal dropdown operations that are not tools, and the MCP side includes two containment tools — `find_containment_candidates` and `contain_agents` — that have no REST equivalent. The two differences happen to cancel out.

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
| Get Agent Quarantine Status | GET | Get Bot Quarantine Status |
| Quarantine Agent | POST | Set Bot As Quarantined |
| Release Agent From Quarantine | POST | Set Bot As Unquarantined |
| Get Connector Consent Bypass | GET | Get Connector Consent Bypass |
| Set Connector Consent Bypass | PUT | Set Connector Consent Bypass |
| Reassign Agent Owner | POST | Reassign Copilot Agent |
| Delete Agent | DELETE | Delete Copilot Agent |
| List Agents | GET | *(not a Bots operation — see Agent Inventory)* |
| Get Environment List | GET | *(internal — backs the environment dropdown)* |
| Get Agent List | GET | *(internal — backs the agent dropdown)* |

The internal `Get Environment List` operation is hidden from the action picker and exists only to populate dropdowns.

### MCP Tools (Copilot Studio)

All 13 operations are available as MCP tools:

| Tool | Description |
|------|-------------|
| `get_test_sets` | List all test sets for an agent |
| `get_test_set_details` | Get a specific test set with all test cases |
| `start_evaluation` | Launch an evaluation run with optional authentication |
| `list_test_runs` | Get all historical evaluation runs |
| `get_run_details` | Retrieve quality metrics and test results |
| `download_evaluation_snapshot` | Download the agent content snapshot for a run |
| `get_quarantine_status` | Read the quarantine state of an agent |
| `quarantine_agent` | Quarantine an agent |
| `unquarantine_agent` | Release an agent from quarantine |
| `get_connector_consent_bypass` | Read the admin consent bypass setting |
| `set_connector_consent_bypass` | Set the admin consent bypass setting |
| `reassign_agent` | Reassign the agent owner |
| `delete_agent` | Permanently delete an agent — requires `confirm: true` |
| `list_agents` | Inventory agents with their harness, sharing, and publish state |
| `find_containment_candidates` | Read-only. Find GitHub Copilot harness agents worth quarantining |
| `contain_agents` | Quarantine an explicit list of agents — requires `confirm: true` |

## Safety Notes

Because this connector exposes destructive administrative operations to agents, three guards are built in:

- **`delete_agent` refuses to run without `confirm: true`.** Deletion is irreversible and the API has no undelete, so an agent cannot delete a copilot from an ambiguous instruction.
- **`contain_agents` accepts no filter and requires `confirm: true`.** It takes only an explicit list of agents, so no single call can quarantine a population the caller has not first seen and named.
- **`reassign_agent` returns `204 No Content`.** An empty body would read like a failure to a calling agent, so the tool converts it to `{ "status": 204, "succeeded": true }`.

If you want makers to run evaluations without any access to the administrative operations, restrict this connector to admin environments with a DLP policy. Note that the API itself returns **403** to non-administrators for the administrative operations regardless of connector configuration.

## Snapshot Downloads

`download_evaluation_snapshot` returns a binary ZIP. The connector reads it as bytes rather than text so the archive is never corrupted:

- Files **4 MB or smaller** are returned as an MCP `resource` with a base64 `blob`
- **Larger files** return metadata only, with a pointer to the REST operation — use that in a flow to stream the file to SharePoint or OneDrive instead of through an agent conversation
- The file name comes from the `Content-Disposition` header, falling back to `evaluation-snapshot-{testRunId}.zip`

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

## Authentication

OAuth 2.0 with Microsoft Entra ID — requires an app registration with Power Platform API access.

**Required Scope:** `https://api.powerplatform.com/.default`

### Prerequisites

1. **App Registration**
   - Register an application in Microsoft Entra ID
   - Configure "Power Platform API" permissions
   - Grant the `.default` scope
   - Replace `REPLACE_WITH_CLIENT_ID` in `apiProperties.json` with the Client ID

2. **Permissions**
   - Evaluation operations require maker access to the agent
   - Administrative operations require Power Platform or Dynamics 365 administrator privileges
   - The agent inventory reads tenant-wide resources, so it also requires administrator privileges

3. **Agent Identifiers**
   - Environment ID — The Dataverse environment containing your agent
   - Bot ID — The Copilot Studio agent identifier
   - Both are dropdowns in the designer, so you rarely need to supply them by hand

4. **(Optional) User Profiles**
   - For authenticating agent connections during evaluation, obtain the MCS Connection ID:
     1. Go to [Power Automate](https://make.powerautomate.com/)
     2. Open the Connections page
     3. Select the Microsoft Copilot Studio connection
     4. Copy the `mcsConnectionId` from the URL

## Configuration

### 1. Set Up App Registration

```powershell
# Create app registration for connector
$app = New-AzADApplication -DisplayName "Copilot Studio Bots Connector"

# Grant Power Platform API permissions
# (Configure in Azure Portal or via Microsoft Graph)
```

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
- **The agent inventory depends on an undocumented API.** `List Agents`, the agent dropdown, and both containment tools read `resourcequery` at `api-version=2022-03-01-preview`, which returns the resource provider's raw property bag. `isCLIAgent` is not in the published reference and can change shape or disappear without notice. Use it for reporting and chargeback triage, not as a hard enforcement gate.
- **`isCLIAgent` is a string with three values.** Compare against `'true'`, `'false'`, and `'unknown'`. A flow condition testing it as a boolean silently never matches, and `'unknown'` must never be treated as `'false'`.
- **`isCLIAgent: 'false'` covers two harnesses.** It cannot separate Standard from Copilot Chat. To confirm a single agent definitively, clone it and read `template` and `recognizer.kind`.
- **The inventory pages to a ceiling.** It reads 1,000 agents per page and stops after 10 pages. Check `truncated` before treating a result as a complete estate, and scope to an environment if it is `true`.
- **`contain_agents` reports partial failures inside the payload.** `isError` is set only when every agent in the batch failed, so always read `succeeded`, `failed`, and the per-agent `results` rather than relying on `isError` alone.

## Troubleshooting

| Status | Meaning | What to do |
|--------|---------|------------|
| 400 | Malformed body | Check `adminConsentBypass` is a boolean and `NewOwnerAadUserId` is a valid Entra object ID |
| 401 | Token invalid or expired | Reauthorize the connection; confirm the app registration has Power Platform API permission |
| 403 | Caller lacks tenant admin rights | Sign in as a Power Platform or Dynamics 365 administrator for the administrative operations |
| 404 | Agent, environment, test set, or run not found | Verify the Environment ID and Agent ID; a 404 on snapshot usually means the run ID is wrong or the run never completed |
| 422 | An evaluation run is already in progress | Wait for the current run to reach `Completed` or `Failed`, then retry |
| 500 | Service-side failure on reassign | Retry; if it persists confirm the new owner exists and has access to the environment |
| Empty agent dropdown | The inventory query failed or returned nothing | The picker swallows errors to avoid breaking the designer. Call `List Agents` directly to see the underlying error |
| `List Agents` returns 400 | The resource query was rejected | Usually a schema change in the undocumented preview API. Confirm you can still read agents in PPAC, then check whether the query shape has changed |
| `truncated: true` | More than 10,000 agents matched | Scope the call to a single environment |

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
| `apiDefinition.swagger.json` | OpenAPI definition for the 16 REST operations plus the MCP endpoint |
| `apiProperties.json` | OAuth 2.0 configuration and script operation registration |
| `script.csx` | MCP JSON-RPC 2.0 handler, agent inventory and containment, binary snapshot handling, and optional telemetry |
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

## Author

**Troy Taylor**

- Email: troy@troystaylor.com
- GitHub: [troystaylor](https://github.com/troystaylor)
