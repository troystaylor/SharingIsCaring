using Azure.AI.OpenAI;
using Azure.Identity;
using DurableAgentWorkflows.Workflows.DocReview;
using DurableAgentWorkflows.Workflows.Evaluation;
using DurableAgentWorkflows.Workflows.Triage;
using Microsoft.Agents.AI.Hosting.AzureFunctions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

// ── Configuration ────────────────────────────────────────────────────────────

string? endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
    ?? "gpt-4o";

ChatClient? chatClient = null;
if (!string.IsNullOrWhiteSpace(endpoint))
{
    AzureOpenAIClient openAiClient = new(new Uri(endpoint), new AzureCliCredential());
    chatClient = openAiClient.GetChatClient(deploymentName);
}

// ── Build Workflows ──────────────────────────────────────────────────────────
//
// Each workflow is exposed as:
//   - MCP tool at /runtime/webhooks/mcp  (for M365 Copilot / Copilot Studio)
//   - HTTP trigger at /api/workflows/{name}/run  (for Power Automate / direct)
//   - Status endpoint at /api/workflows/{name}/status/{runId}
//   - Respond endpoint at /api/workflows/{name}/respond/{runId}

if (chatClient is null)
    throw new InvalidOperationException(
        "AZURE_OPENAI_ENDPOINT is required. Set it in local.settings.json or app settings.");

Workflow triageWorkflow = TriageWorkflowBuilder.Build(chatClient);
Workflow evaluationWorkflow = EvaluationWorkflowBuilder.Build(chatClient);
Workflow docReviewWorkflow = DocReviewWorkflowBuilder.Build(chatClient);

// ── Host ─────────────────────────────────────────────────────────────────────

using IHost app = FunctionsApplication
    .CreateBuilder(args)
    .ConfigureFunctionsWebApplication()
    .ConfigureDurableWorkflows(workflows =>
    {
        workflows.AddWorkflow(triageWorkflow,
            exposeMcpToolTrigger: true,
            exposeStatusEndpoint: true);

        workflows.AddWorkflow(evaluationWorkflow,
            exposeMcpToolTrigger: true,
            exposeStatusEndpoint: true);

        workflows.AddWorkflow(docReviewWorkflow,
            exposeMcpToolTrigger: true,
            exposeStatusEndpoint: true);
    })
    .Build();

app.Run();
