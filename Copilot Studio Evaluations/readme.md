# Copilot Studio Agent Evaluation

Automate agent evaluation workflows in Microsoft Copilot Studio. Manage test sets, trigger quality assessments, and retrieve detailed metrics including abstention, relevance, and completeness scores. Built on the [Power Platform REST API](https://learn.microsoft.com/en-us/rest/api/power-platform/copilotstudio/bots) with native MCP support for Copilot Studio agents and comprehensive Application Insights logging.

## Overview

This connector programmatically controls agent evaluation across Copilot Studio environments:

- **Retrieve test sets** — List all defined test sets for evaluation purposes
- **Start evaluations** — Trigger asynchronous quality assessment runs
- **Track progress** — Monitor evaluation execution state and completion
- **Analyze results** — Access quality metrics (abstention, relevance, completeness) and AI-generated explanations
- **Audit history** — Review all historical evaluation runs with timestamps and ownership

## Capabilities

### REST Operations (Power Automate)

| Operation | Method | Purpose |
|-----------|--------|---------|
| Get Agent Test Sets | GET | Retrieve test set inventory with metadata |
| Get Agent Test Set Details | GET | View individual test cases within a set |
| Start Agent Evaluation | GET | Trigger async evaluation run and get run ID |
| Get Agent Test Run Details | GET | Retrieve completed evaluation results |
| Get Agent Test Runs | GET | List all evaluation runs for an agent |

### MCP Tools (Copilot Studio)

All REST operations are available as MCP tools for Copilot Studio agents:

| Tool | Description |
|------|-------------|
| `get_test_sets` | List all test sets for an agent |
| `get_test_set_details` | Get specific test set with all test cases |
| `start_evaluation` | Launch evaluation run with optional authentication |
| `get_run_details` | Retrieve quality metrics and test results |
| `list_test_runs` | Get all historical evaluation runs |

## Authentication

OAuth 2.0 with Microsoft Entra ID — requires an app registration with Power Platform API access.

**Required Scope:** `https://api.powerplatform.com/.default`

### Prerequisites

1. **App Registration**
   - Register an application in Microsoft Entra ID
   - Configure "Power Platform API" permissions
   - Grant the `.default` scope
   - Note the Client ID to configure the connector

2. **Agent Identifiers**
   - Environment ID — The Dataverse environment containing your agent
   - Bot ID — The Copilot Studio agent identifier

3. **Test Sets**
   - Create test sets in Copilot Studio before running evaluations
   - Each test set must have an Active state to be evaluated

4. **(Optional) User Profiles**
   - For authenticating agent connections during evaluation, obtain the MCS Connection ID:
     1. Go to [Power Automate](https://make.powerautomate.com/)
     2. Open Connections page
     3. Select Microsoft Copilot Studio connection
     4. Copy the `mcsConnectionId` from the URL

## Configuration

### 1. Set Up App Registration

```powershell
# Create app registration for connector
$app = New-AzADApplication -DisplayName "Copilot Studio Agent Eval Connector"

# Grant Power Platform API permissions
# (Configure in Azure Portal or via Microsoft Graph)
```

### 2. Register the Connector

```powershell
# Validate connector
ppcv ./path/to/connector

# Create connector in Power Platform
pac connector create --publisher-name="Troy Taylor" --environment-url="https://org.crm.dynamics.com"
```

### 3. Create Connection in Power Automate

1. Go to Power Automate
2. Select **My Connections**
3. New Connection → Copilot Studio Agent Evaluation
4. Sign in with your Entra ID credentials
5. (Optional) Paste Application Insights Instrumentation Key

## Usage Examples

### Power Automate Flow

**Scenario: Evaluate agent quality nightly**

```
1. Trigger: Scheduled (daily at 2 AM)
2. Get Agent Test Sets
   - Environment ID: [your-env-id]
   - Bot ID: [your-agent-id]
3. For Each test set (value):
   4. Start Agent Evaluation
      - Environment ID: [env-id]
      - Bot ID: [bot-id]
      - Test Set ID: value/id
   5. Wait for completion (polling with Get Agent Test Run Details)
   6. Parse metrics and send results to admin email
```

### Copilot Studio Agent

**System Prompt:**
```
You are an agent quality auditor. When asked to evaluate an agent:
1. Use get_test_sets to find available test suites
2. Use start_evaluation to trigger an assessment
3. Wait 30-60 seconds for processing (use system time)
4. Use get_run_details to fetch the results
5. Summarize abstention (did agent answer?), relevance, and completeness scores
6. Recommend improvements if scores are low
```

**Example Conversation:**
```
User: "Evaluate my customer service agent with the compliance test set"
Agent:
  1. Lists test sets → finds "Compliance Testing"
  2. Starts evaluation run → gets runId: abc123
  3. Retrieves results → shows:
     - Abstention: 12% (acceptable, means agent deferred on edge cases)
     - Relevance: 94% (good)
     - Completeness: 87% (good, but some answers could be more thorough)
  4. Recommendation: "Consider adding more knowledge articles on policy exceptions"
```

## Application Insights Logging

The connector includes **hardcoded Application Insights telemetry** that logs automatically:

- **Operation events** — Request/response for each API call
- **MCP tool invocations** — Details of each Copilot Studio tool usage
- **Evaluation lifecycle** — Start, progress updates, completion
- **Errors and exceptions** — Detailed diagnostics with stack traces
- **Performance metrics** — Response times and operation success/failure

### Setup Application Insights

1. Create Application Insights resource in Azure
2. Copy the **Instrumentation Key**
3. Edit `script.csx` in the connector folder
4. Set `private const bool APP_INSIGHTS_ENABLED = true;`
5. Find the line: `private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";`
6. Replace `[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]` with your actual key
7. Redeploy the connector using `pac connector create`

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

**See all MCP requests:**
```kusto
customEvents
| where name == "MCP_Request"
| summarize Count = count() by tostring(customDimensions.Method)
```

**Track tool calls from Copilot Studio:**
```kusto
customEvents
| where name == "MCP_ToolCall"
| summarize Count = count() by tostring(customDimensions.ToolName)
```

**Find errors and exceptions:**
```kusto
customExceptions
| where tostring(customDimensions.Connector) == "Copilot Studio Agent Evaluation"
| project timestamp, outerType, outerMessage, customDimensions.Operation
```

**Monitor MCP response status:**
```kusto
customEvents
| where name startswith "MCP_Response"
| summarize Count = count() by tostring(customDimensions.Status)
```

## API Reference

### Get Agent Test Sets

**Path Parameter:**
- `environmentId` — Environment containing the agent
- `botId` — Agent identifier

**Response Body:**
```json
{
  "value": [
    {
      "id": "test-set-uuid",
      "displayName": "Customer Service Quality",
      "description": "Tests for accurate customer support responses",
      "state": "Active",
      "totalTestCases": 25,
      "auditInfo": {
        "createdBy": "user@org.onmicrosoft.com",
        "createdOn": "2024-04-15T10:30:00Z",
        "modifiedBy": "user@org.onmicrosoft.com",
        "modifiedOn": "2024-04-17T14:22:00Z"
      }
    }
  ]
}
```

### Start Agent Evaluation

**Path Parameters:**
- `environmentId` — Environment ID
- `botId` — Agent ID
- `testSetId` — Test set to evaluate

**Query Parameter (optional):**
- `mcsConnectionId` — Copilot Studio connection ID for authenticated evaluation

**Response Body:**
```json
{
  "runId": "run-uuid-12345",
  "state": "Running",
  "executionState": "ProcessingTestCases",
  "startTime": "2024-04-17T15:45:00Z",
  "lastUpdatedAt": "2024-04-17T15:45:10Z",
  "totalTestCases": 25,
  "testCasesProcessed": 5
}
```

### Get Agent Test Run Details

**Response includes:**
- `id` — Test run identifier
- `state` — Pending | Running | Completed | Failed
- `testCaseResults[]` — Array of individual test results
- `metricsResults[]` — Quality metrics (abstention, relevance, completeness)
- `aiResultReason` — AI explanation of overall performance

## Troubleshooting

| Issue | Resolution |
|-------|-----------|
| 401 Unauthorized | Verify Entra ID credentials; check app registration has Power Platform API scope |
| 404 Not Found | Confirm environment ID and bot ID are correct and accessible |
| Evaluation stuck Pending | Check agent is responsive; long evaluations may take several minutes |
| No MCP tools appear | Confirm connector is added to agent as an action; refresh the connection list |
| App Insights not logging | Verify instrumentation key is valid; check connector has network access |

## Limitations

- Evaluations are **asynchronous** — allow 2-5 minutes for completion depending on test set size
- Test sets must have **Active** state to be evaluated
- Only **200 test cases per set** are supported (Power Platform limit)
- Authentication requires **tenant admin** or User can grant Power Platform API access

## Related Resources

- [Copilot Studio Agent Evaluation Documentation](https://learn.microsoft.com/en-us/microsoft-copilot-studio/analytics-agent-evaluation-intro)
- [Run Evaluations Using Tools/Flows](https://learn.microsoft.com/en-us/microsoft-copilot-studio/analytics-agent-evaluation-automate-tools)
- [Power Platform REST API Reference](https://learn.microsoft.com/en-us/rest/api/power-platform/)
- [Application Insights Documentation](https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)

## Support

- **Author:** Troy Taylor
- **Email:** troy@troystaylor.com
- **GitHub:** https://github.com/troystaylor

**Issues or suggestions?** Open an issue in the [SharingIsCaring repository](https://github.com/troystaylor/SharingIsCaring).
