# Copilot Studio Bots

**Publisher: Troy Taylor**

Complete coverage of the [Power Platform API Bots operations](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/bots) for Microsoft Copilot Studio. All 13 documented operations in a single connector, spanning maker evaluation and tenant administration, with native MCP support for Copilot Studio agents and optional Application Insights logging.

## Overview

The Bots operation group covers two related concerns, and this connector exposes both:

- **Maker evaluation** — Manage test sets, trigger quality assessments, retrieve metrics, and download agent snapshots
- **Administration** — Quarantine and release agents, control the connector consent bypass, reassign ownership, and delete agents

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

| Get Environment List | GET | *(internal — backs the environment dropdown)* |

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

## Safety Notes

Because this connector exposes destructive administrative operations to agents, two guards are built in:

- **`delete_agent` refuses to run without `confirm: true`.** Deletion is irreversible and the API has no undelete, so an agent cannot delete a copilot from an ambiguous instruction.
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
| Test Set ID | `Get Agent Test Sets` | Cascades — pick the environment and agent first |
| Test Run ID | `Get Agent Test Runs` | Cascades — pick the environment and agent first |

The environment picker is backed by an internal operation on the path `/metadata/environments`. It is marked `x-ms-visibility: internal`, so it does not appear as a usable action; the script rewrites it to `https://api.powerplatform.com/environmentmanagement/environments`. That endpoint reports the environment identifier inconsistently — sometimes as a bare GUID in `name`, sometimes as an ARM-style path in `id` — so the connector normalizes both to the bare GUID the Bots operations expect.

**Agent ID is not a dropdown.** The Bots API has no list-agents operation, and the only way to enumerate agents tenant-wide is the undocumented `resourcequery` preview API. Rather than take a dependency on a preview endpoint, this connector leaves Agent ID as free text. You can find it in the Copilot Studio URL, or list agents with the [Power Platform Admin](../Power%20Platform%20Admin/) connector.

**Owner ID is not a dropdown** either — resolving users requires Microsoft Graph, which is a different host and cannot back a picker in this connector.

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

3. **Agent Identifiers**
   - Environment ID — The Dataverse environment containing your agent
   - Bot ID — The Copilot Studio agent identifier

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
# Validate connector
ppcv "./Copilot Studio Bots"

# Create connector in Power Platform
pac connector create --publisher-name="Troy Taylor" --environment-url="https://org.crm.dynamics.com"
```

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
2. List agents via the Power Platform Admin connector
3. For Each agent whose owner is disabled in Entra ID:
   4. Reassign Agent Owner
      - New Owner Entra User ID: [governance service account object ID]
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

Never call delete_agent unless an administrator explicitly asks for permanent
deletion and names the agent.
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

## Troubleshooting

| Status | Meaning | What to do |
|--------|---------|------------|
| 400 | Malformed body | Check `adminConsentBypass` is a boolean and `NewOwnerAadUserId` is a valid Entra object ID |
| 401 | Token invalid or expired | Reauthorize the connection; confirm the app registration has Power Platform API permission |
| 403 | Caller lacks tenant admin rights | Sign in as a Power Platform or Dynamics 365 administrator for the administrative operations |
| 404 | Agent, environment, test set, or run not found | Verify the Environment ID and Agent ID; a 404 on snapshot usually means the run ID is wrong or the run never completed |
| 422 | An evaluation run is already in progress | Wait for the current run to reach `Completed` or `Failed`, then retry |
| 500 | Service-side failure on reassign | Retry; if it persists confirm the new owner exists and has access to the environment |

MCP tool calls surface these as `isError: true` with the status code in the message text rather than as JSON-RPC errors, so a Copilot Studio agent can read and explain the failure. JSON-RPC errors (`-32601`, `-32602`) are reserved for unknown methods and unknown or malformed tool calls.

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

**Find errors and exceptions:**
```kusto
exceptions
| where tostring(customDimensions.connector) == "Copilot Studio Bots"
| project timestamp, outerType, outerMessage, customDimensions.operation
```

## API Reference

All operations target `https://api.powerplatform.com/copilotstudio` with `api-version=2024-10-01`, where `{base}` is `/environments/{environmentId}/bots/{botId}`.

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
| Get Environment List (internal) | `GET /environmentmanagement/environments` |

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition for the 13 REST operations plus the MCP endpoint |
| `apiProperties.json` | OAuth 2.0 configuration and script operation registration |
| `script.csx` | MCP JSON-RPC 2.0 handler, binary snapshot handling, and optional telemetry |
| `readme.md` | This document |

## Related Connectors

| Connector | Coverage |
|-----------|----------|
| [Power Platform Admin](../Power%20Platform%20Admin/) | Environment settings and agent inventory, including a read-only `isQuarantined` flag sourced from the inventory API |
| [Copilot Studio Analytics](../Copilot%20Studio%20Analytics/) | Dataverse-based conversation transcripts and session analytics |
| [Copilot Package Management](../Copilot%20Package%20Management/) | Microsoft 365 Copilot package catalog: block, unblock, and reassign |

## History

This connector supersedes the earlier `Copilot Studio Evaluations` and `Copilot Studio Agent Administration` folders, which split the same API group across two connectors. They were merged so that a single connection covers the whole Bots surface, matching the pattern used by Copilot Package Management.

## Author

**Troy Taylor**

- Email: troy@troystaylor.com
- GitHub: [troystaylor](https://github.com/troystaylor)
