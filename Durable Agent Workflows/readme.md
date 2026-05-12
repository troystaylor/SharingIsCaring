# Durable Agent Workflows

Three enterprise durable workflows built with [Microsoft Agent Framework (MAF)](https://github.com/microsoft/agent-framework), hosted on Azure Functions, exposed as MCP tools, and consumed by an M365 Copilot declarative agent.

```
M365 Copilot Declarative Agent
        │
        ▼  (native MCP plugin)
Azure Functions  /runtime/webhooks/mcp
        │
        ├── TriageTicket
        │     ParseTicket → [Sentiment, Category, KBSearch] → MergeAnalysis → ManagerApproval (HITL) → AssignTicket
        │
        ├── EvaluateResponse
        │     CaptureOutput → [Safety, Accuracy, Groundedness, Relevance] → AggregateScores → HumanReview (HITL) → PublishReport
        │
        └── ReviewDocument
              IngestDocument → [Legal, Compliance, Brand, Technical] → ConsolidateFeedback → AuthorRevision (HITL) → FinalApproval (HITL) → Publish
```

## Workflows

### IT Helpdesk Triage

Parses an IT helpdesk ticket, runs sentiment analysis, category classification, and KB article search in parallel using AI agents, merges results, and pauses for manager approval before assigning to a team.

**Patterns demonstrated:** Sequential, fan-out/fan-in (3 parallel AI agents), human-in-the-loop

### Agent Evaluation Pipeline

Evaluates an AI agent's response for safety, accuracy, groundedness, and relevance using four parallel evaluator agents, aggregates scores into a report, and pauses for human review.

**Patterns demonstrated:** Fan-out/fan-in (4 parallel AI agents), human-in-the-loop

### Document Review Pipeline

Routes a document through legal, compliance, brand, and technical AI reviewers in parallel, consolidates feedback, then passes through two sequential approval gates: author revision and final approval.

**Patterns demonstrated:** Fan-out/fan-in (4 parallel AI agents), chained human-in-the-loop (2 sequential RequestPorts)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [Docker](https://www.docker.com/) (for local DTS emulator)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) or `npx azurite` (local storage emulator)
- Azure OpenAI resource with a deployed chat model
- `Cognitive Services OpenAI User` role on your Azure OpenAI resource for your `az login` identity

## Local Development

### 1. Start the Durable Task Scheduler emulator

```bash
docker run -d --name dts-emulator \
  -p 8080:8080 -p 8082:8082 \
  mcr.microsoft.com/dts/dts-emulator:latest
```

- Port 8080: Scheduler endpoint (used by the app)
- Port 8082: Dashboard UI → open `http://localhost:8082`

### 4. Start Azurite

```bash
npx azurite --silent --blobPort 10000 --queuePort 10001 --tablePort 10002
```

### 3. Authenticate

```bash
az login
```

The app uses `AzureCliCredential` to authenticate with Azure OpenAI. Ensure your logged-in identity has the `Cognitive Services OpenAI User` role on your Azure OpenAI resource.

### 4. Configure settings

Edit `functions-app/local.settings.json`:

```json
{
  "Values": {
    "AZURE_OPENAI_ENDPOINT": "https://your-resource.openai.azure.com",
    "AZURE_OPENAI_DEPLOYMENT": "gpt-4o"
  }
}
```

### 5. Run

```bash
cd functions-app
func start
```

> **Important**: Always run `az login` before `func start`. The Functions worker caches the credential at startup — if your token is expired, the AI agents will get 401 errors.

### 6. Test a workflow

Start the triage workflow:

```http
POST http://localhost:7071/api/workflows/TriageTicket/run
Content-Type: text/plain
x-ms-wait-for-response: true

My Outlook keeps crashing when I open PDF attachments. This is the third time this week.
```

Without `x-ms-wait-for-response`, the call returns `202 Accepted` with a run ID immediately and the workflow runs in the background.

### 7. Respond to HITL gates

Check pending approvals:

```http
GET http://localhost:7071/api/workflows/TriageTicket/status/{runId}
```

Submit an approval:

```http
POST http://localhost:7071/api/workflows/TriageTicket/respond/{runId}
Content-Type: application/json

{
  "eventName": "ManagerApproval",
  "response": { "approved": true, "comments": "Assign to Desktop Engineering" }
}
```

### 8. Monitor in DTS Dashboard

Open `http://localhost:8082` to see workflow runs, executor timelines, and parallel execution.

## MCP Tools

When `exposeMcpToolTrigger: true` is set, the Functions host generates an MCP endpoint at `/runtime/webhooks/mcp`. Each workflow appears as a tool:

| MCP Tool | Workflow | Input |
|----------|----------|-------|
| `TriageTicket` | IT Helpdesk Triage | Ticket text |
| `EvaluateResponse` | Agent Evaluation Pipeline | Agent response (+ optional context) |
| `ReviewDocument` | Document Review Pipeline | Document text |

Any MCP client can discover and call these tools: M365 Copilot, Copilot Studio, VS Code, Claude Desktop, etc.

## Deployment

### Azure

```powershell
./infra/deploy.ps1 `
    -ResourceGroupName "rg-durable-workflows" `
    -AzureOpenAIEndpoint "https://your-resource.openai.azure.com" `
    -DtsConnectionString "Endpoint=https://your-scheduler.azurewebsites.net;TaskHub=default;Authentication=ManagedIdentity"
```

### M365 Declarative Agent

1. Update `agent-package/plugin.json` → set `spec.url` to your Functions MCP endpoint
2. Update `agent-package/manifest.json` → add your Functions domain to `validDomains`
3. Configure OAuth in Teams Developer Portal and update the `reference_id` in plugin.json
4. Upload the agent package via Teams Admin Center or Agents Toolkit

## Project Structure

```
Durable Agent Workflows/
├── functions-app/
│   ├── DurableAgentWorkflows.csproj
│   ├── Program.cs                          ← Workflow registration + hosting
│   ├── host.json
│   ├── local.settings.json
│   ├── .env.sample                         ← Environment variable documentation
│   ├── .gitignore
│   ├── Shared/
│   │   └── Models.cs                       ← ApprovalRequest, ApprovalResponse, TextHelpers
│   └── Workflows/
│       ├── Triage/
│       │   └── TriageWorkflow.cs           ← Builder + 4 executors
│       ├── Evaluation/
│       │   └── EvaluationWorkflow.cs       ← Builder + 4 executors
│       └── DocReview/
│           └── DocReviewWorkflow.cs        ← Builder + 5 executors
├── agent-package/
│   ├── manifest.json                       ← Teams manifest v1.26
│   ├── declarativeAgent.json               ← Agent config + instructions
│   └── plugin.json                         ← MCP plugin v2.4
└── infra/
    ├── main.bicep                          ← Functions + Storage + App Insights
    └── deploy.ps1                          ← Deployment script
```

## Known Issues

- **DurableTask CustomStatus 16KB limit**: The DurableTask extension limits `CustomStatus` to 16KB (UTF-16). With chained HITL gates (like ReviewDocument's AuthorRevision → FinalApproval), accumulated metadata across gates can exceed this limit. The project mitigates this with aggressive truncation of AI agent output in the `ApprovalRequest.Details` field. This is a framework-level limitation that MAF will likely address in a future release.

- **Azure RBAC propagation**: After assigning `Cognitive Services OpenAI User` to a new identity, the role can take up to 30 minutes to propagate across all Azure OpenAI backend nodes. During this window, some parallel AI agents may get 401 errors while others succeed.

## Key MAF APIs Used

| API | Purpose |
|-----|---------|
| `Executor<TIn, TOut>` | Unit of work in a workflow |
| `WorkflowBuilder` | Wire executors into a directed graph |
| `chatClient.AsAIAgent()` | Create an AI agent executor from a ChatClient |
| `AddFanOutEdge` | Send input to multiple executors in parallel |
| `AddFanInBarrierEdge` | Wait for all parallel executors to complete |
| `RequestPort.Create<TReq, TRes>()` | Human-in-the-loop approval gate |
| `ConfigureDurableWorkflows` | Register workflows with Azure Functions host |
| `exposeMcpToolTrigger: true` | Auto-generate MCP tool for each workflow |

## References

- [Durable Workflows in MAF (blog)](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/)
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [Workflow samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/03-workflows)
- [Azure Functions hosting samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/04-hosting/DurableWorkflows/AzureFunctions)
- [M365 Copilot MCP plugins](https://learn.microsoft.com/microsoft-365/copilot/extensibility/build-mcp-plugins)
