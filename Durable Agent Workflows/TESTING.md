# Testing Plan: Durable Agent Workflows

## Stage 0: Build Validation

Confirm the project compiles against the MAF NuGet packages.

```powershell
cd "Durable Agent Workflows/functions-app"
dotnet restore
dotnet build
```

**Expected**: Clean build.

**Risk items to watch for:**
- `IReadOnlyList<string>` may not be the correct fan-in barrier input type — compiler error will tell us
- `exposeMcpToolTrigger` / `exposeStatusEndpoint` parameter names may differ
- `RequestPort.Create<TReq, TRes>()` API shape

## Stage 1: DTS Emulator + Function Host Startup

Verify the Functions host starts and registers all endpoints.

```powershell
# Terminal 1 — DTS emulator
docker run -d --name dts-emulator -p 8080:8080 -p 8082:8082 mcr.microsoft.com/dts/dts-emulator:latest

# Terminal 2 — Functions host
cd "Durable Agent Workflows/functions-app"
func start
```

### Verify in `func start` output

- [ ] All three workflow HTTP triggers registered: `TriageTicket`, `EvaluateResponse`, `ReviewDocument`
- [ ] MCP endpoint registered at `/runtime/webhooks/mcp`
- [ ] Status endpoints registered for each workflow
- [ ] Respond endpoints registered for each workflow
- [ ] No startup errors about DTS connection

### DTS Dashboard (`http://localhost:8082`)

- [ ] Dashboard loads and shows the connected task hub

## Stage 2: Workflow Execution — TriageTicket

Full sequential → fan-out → fan-in → HITL → sequential pipeline.

### 2a. Start the workflow (async)

```http
POST http://localhost:7071/api/workflows/TriageTicket/run
Content-Type: text/plain

My Outlook keeps crashing when I try to open PDF attachments. This is the third time this week and it's blocking my quarterly report. I'm really frustrated.
```

**Expected**: `202 Accepted` with JSON body containing `runId`.

### 2b. Check DTS Dashboard

- [ ] Workflow run appears with name `TriageTicket`
- [ ] `dafx-ParseTicket` activity completes first
- [ ] `dafx-SentimentAnalyst`, `dafx-CategoryClassifier`, `dafx-KBSearchAgent` run **in parallel** (overlapping timelines)
- [ ] `dafx-MergeAnalysis` runs after all three complete
- [ ] Workflow pauses at `ManagerApproval` — status shows "Waiting for external event"

### 2c. Check pending status

```http
GET http://localhost:7071/api/workflows/TriageTicket/status/{runId}
```

**Expected**: Response shows workflow is suspended at `ManagerApproval` with the `ApprovalRequest` payload visible.

### 2d. Submit HITL approval

```http
POST http://localhost:7071/api/workflows/TriageTicket/respond/{runId}
Content-Type: application/json

{
  "eventName": "ManagerApproval",
  "response": { "approved": true, "comments": "Assign to Desktop Engineering, priority P2" }
}
```

**Expected**: Workflow resumes, `dafx-AssignTicket` runs, workflow completes with success message.

### 2e. Submit HITL rejection (separate run)

Repeat 2a, then at 2d:

```json
{ "eventName": "ManagerApproval", "response": { "approved": false, "comments": "Needs more info — ask user for Outlook version" } }
```

**Expected**: Completes with rejection message and re-triage instructions.

## Stage 3: Workflow Execution — EvaluateResponse

4-way fan-out evaluation + HITL review gate.

### 3a. Start

```http
POST http://localhost:7071/api/workflows/EvaluateResponse/run
Content-Type: text/plain

Question: How do I deploy a containerized Python app to Azure?
Context: The user is asking about Azure Container Apps and Azure Kubernetes Service.
Agent Response: Azure Functions supports .NET, Node.js, Python, and Java. To deploy, use az functionapp create with the --runtime flag. You can also use Docker containers with Azure Container Instances.
```

### 3b. DTS Dashboard checks

- [ ] 4 evaluator activities run in parallel: `dafx-SafetyEvaluator`, `dafx-AccuracyEvaluator`, `dafx-GroundednessEvaluator`, `dafx-RelevanceEvaluator`
- [ ] `dafx-AggregateScores` runs after barrier
- [ ] Workflow pauses at `HumanReview`

### 3c. Review the aggregate report

```http
GET http://localhost:7071/api/workflows/EvaluateResponse/status/{runId}
```

**Expected**: Report shows all four evaluation scores. Accuracy and relevance should be low (the agent response doesn't match the containerization question).

### 3d. Complete HITL

```json
{ "eventName": "HumanReview", "response": { "approved": false, "comments": "Accuracy score too low — agent answered about Functions instead of Container Apps" } }
```

## Stage 4: Workflow Execution — ReviewDocument (Chained HITL)

4-way fan-out + **two sequential** HITL gates.

### 4a. Start

```http
POST http://localhost:7071/api/workflows/ReviewDocument/run
Content-Type: text/plain

Our new AI-powered analytics platform processes customer data in real-time across all regions, providing insights that help sales teams close deals 40% faster with zero additional training. Contact sales@zava.com for a free trial.
```

### 4b. DTS Dashboard — first pause

- [ ] 4 reviewer activities run in parallel
- [ ] `dafx-ConsolidateFeedback` runs after barrier
- [ ] Workflow pauses at **AuthorRevision** (HITL gate #1)

### 4c. Author submits revision

```json
{ "eventName": "AuthorRevision", "response": { "approved": true, "comments": "Added GDPR disclaimer, removed unsubstantiated 40% claim, clarified data regions" } }
```

**Expected**: Workflow resumes and immediately pauses at **FinalApproval** (HITL gate #2).

### 4d. Final approver responds

```json
{ "eventName": "FinalApproval", "response": { "approved": true, "comments": "Looks good after revisions, approved for publishing" } }
```

**Expected**: Workflow runs `dafx-Publish` and completes.

### 4e. Test rejection at gate #2

Run again, approve at AuthorRevision, then reject at FinalApproval:

```json
{ "eventName": "FinalApproval", "response": { "approved": false, "comments": "Legal team flagged new issue with the free trial language" } }
```

**Expected**: Completes with rejection message and instructions to return for additional revisions.

## Stage 5: MCP Endpoint Validation

Verify MCP tool discovery and invocation.

### 5a. tools/list

```http
POST http://localhost:7071/runtime/webhooks/mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}
```

**Expected**: JSON-RPC response with three tools: `TriageTicket`, `EvaluateResponse`, `ReviewDocument`. Each tool has:

- [ ] `name` matching workflow `.WithName()`
- [ ] `description` matching workflow `.WithDescription()`
- [ ] `inputSchema` with at least an `input` string parameter

### 5b. tools/call

```http
POST http://localhost:7071/runtime/webhooks/mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "TriageTicket",
    "arguments": {
      "input": "Can't connect to VPN from home office"
    }
  }
}
```

**Expected**: Returns a run ID or the workflow result (depending on whether the MCP handler blocks until completion).

### 5c. initialize

```http
POST http://localhost:7071/runtime/webhooks/mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 0,
  "method": "initialize",
  "params": { "protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": { "name": "test", "version": "1.0" } }
}
```

**Expected**: Valid JSON-RPC response with server info and capabilities including `tools`.

## Stage 6: Edge Cases and Error Handling

| Test | Input | Expected |
|------|-------|----------|
| Empty ticket | `POST .../TriageTicket/run` with empty body | Error or graceful "no ticket text provided" |
| Empty document | `POST .../ReviewDocument/run` with blank text | `ArgumentException` from `IngestDocumentExecutor` |
| Invalid run ID | `GET .../status/nonexistent-id` | 404 or "run not found" |
| Respond to wrong event name | `POST respond/{runId}` with `"eventName": "WrongName"` | Error, workflow stays paused |
| Respond to completed workflow | `POST respond/{runId}` after workflow is done | Error or no-op |
| Concurrent workflows | Start all 3 workflows simultaneously | All run independently on DTS |

## Stage 7: Infrastructure Validation (pre-deploy)

```powershell
# Bicep lint
az bicep build --file "Durable Agent Workflows/infra/main.bicep"

# Agent package schema validation (manual)
# Verify manifest.json against https://developer.microsoft.com/json-schemas/teams/v1.26/MicrosoftTeams.schema.json
# Verify declarativeAgent.json against v1.6 schema
# Verify plugin.json against v2.4 schema
```

## Stage 8: Azure Deployment

Only after Stages 0-6 pass locally.

1. Run `deploy.ps1` → verify Bicep deploys cleanly
2. Repeat Stage 2-4 tests against the deployed URL
3. Verify MCP endpoint (Stage 5) works from external clients
4. Sideload the agent package in Teams → test conversation starters in M365 Copilot
