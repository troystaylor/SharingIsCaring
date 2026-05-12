using DurableAgentWorkflows.Shared;
using static DurableAgentWorkflows.Shared.TextHelpers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI.Chat;

namespace DurableAgentWorkflows.Workflows.Triage;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  IT Helpdesk Triage Workflow                                               ║
// ║                                                                            ║
// ║  Pattern: Sequential → Fan-out/Fan-in → HITL Approval → Sequential        ║
// ║                                                                            ║
// ║  ParseTicket ──► [SentimentAgent, CategoryAgent, KBSearchAgent]            ║
// ║                          │                                                 ║
// ║                  MergeAnalysis                                             ║
// ║                          │                                                 ║
// ║                  ManagerApproval (HITL)                                    ║
// ║                          │                                                 ║
// ║                  AssignTicket                                              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public static class TriageWorkflowBuilder
{
    public static Workflow Build(ChatClient chatClient)
    {
        // ── Executors ────────────────────────────────────────────────────

        ParseTicketExecutor parseTicket = new();

        AIAgent sentimentAgent = chatClient.AsAIAgent(
            """
            You are a sentiment analyst for IT helpdesk tickets. Analyze the user's
            frustration level based on their language, tone, and urgency cues.

            Rate sentiment as one of: Calm, Mildly Frustrated, Frustrated,
            Very Frustrated, or Angry.

            Provide a 2-3 sentence explanation with specific evidence from the ticket.
            """,
            "SentimentAnalyst");

        AIAgent categoryAgent = chatClient.AsAIAgent(
            """
            You are an IT ticket categorizer. Classify the ticket into a category
            and subcategory using this taxonomy:

            - Hardware > Desktop, Laptop, Peripheral, Mobile
            - Software > Desktop App, Web App, OS, Driver
            - Network > Connectivity, VPN, DNS, Firewall
            - Security > Access, Malware, Phishing, Data Loss
            - Access/Permissions > Account, License, Role, MFA
            - Email > Outlook, Exchange, Calendar, Teams
            - Other > General, Training, Procurement

            Provide: Category > Subcategory, Priority (P1-P4), and confidence
            (High/Medium/Low). Be concise.
            """,
            "CategoryClassifier");

        AIAgent kbSearchAgent = chatClient.AsAIAgent(
            """
            You are a knowledge base search specialist. Based on the ticket
            description, suggest 1-3 relevant KB articles that might help
            resolve the issue.

            For each article, provide:
            - KB ID (e.g., KB-4421)
            - Title
            - Relevance (High/Medium/Low)
            - Brief explanation of why it's relevant

            If no relevant articles exist, say "No matching KB articles found"
            and suggest escalation to a specialist.
            """,
            "KBSearchAgent");

        MergeAnalysisExecutor mergeAnalysis = new();

        var managerApproval = RequestPort.Create<ApprovalRequest, ApprovalResponse>(
            "ManagerApproval");

        AssignTicketExecutor assignTicket = new();

        // ── Workflow Graph ───────────────────────────────────────────────

        return new WorkflowBuilder(parseTicket)
            .WithName("TriageTicket")
            .WithDescription(
                "Triage an IT helpdesk ticket: parse the issue, analyze sentiment, "
                + "classify category, and search KB articles in parallel, then merge "
                + "results for manager approval before team assignment.")
            .AddFanOutEdge(parseTicket,
                [sentimentAgent, categoryAgent, kbSearchAgent])
            .AddFanInBarrierEdge(
                [sentimentAgent, categoryAgent, kbSearchAgent], mergeAnalysis)
            .AddEdge(mergeAnalysis, managerApproval)
            .AddEdge(managerApproval, assignTicket)
            .Build();
    }
}

// ── Executors ────────────────────────────────────────────────────────────────

internal sealed class ParseTicketExecutor()
    : Executor<string, string>("ParseTicket")
{
    public override ValueTask<string> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // Enrich the raw ticket text with structured instructions for
        // the downstream AI agent executors.
        var enriched = $"""
            IT Helpdesk Ticket Submitted:
            ---
            {message.Trim()}
            ---
            Analyze this helpdesk ticket according to your evaluation criteria.
            """;

        return ValueTask.FromResult(enriched);
    }
}

internal sealed class MergeAnalysisExecutor()
    : Executor<IReadOnlyList<string>, ApprovalRequest>("MergeAnalysis")
{
    public override ValueTask<ApprovalRequest> HandleAsync(
        IReadOnlyList<string> results,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var sentiment = Truncate(results.Count > 0 ? results[0] : "No sentiment analysis");
        var category = Truncate(results.Count > 1 ? results[1] : "No category classification");
        var kbArticles = Truncate(results.Count > 2 ? results[2] : "No KB search results");

        var details = $"""
            === Triage Analysis ===

            SENTIMENT ANALYSIS:
            {sentiment}

            CATEGORY CLASSIFICATION:
            {category}

            KB ARTICLE MATCHES:
            {kbArticles}
            """;

        return ValueTask.FromResult(new ApprovalRequest(
            WorkflowName: "TriageTicket",
            Summary: "Ticket triage complete — review analysis and approve assignment",
            Details: TruncateDetails(details),
            RequestedAction: "Approve ticket assignment to the recommended team, "
                + "or reject with instructions for re-triage"));
    }
}

internal sealed class AssignTicketExecutor()
    : Executor<ApprovalResponse, string>("AssignTicket")
{
    public override ValueTask<string> HandleAsync(
        ApprovalResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (response.Approved)
        {
            return ValueTask.FromResult(
                $"✅ Ticket assigned to recommended team. "
                + $"Manager comments: {response.Comments}");
        }

        return ValueTask.FromResult(
            $"❌ Ticket assignment rejected. "
            + $"Manager feedback: {response.Comments}. "
            + "Ticket returned to queue for re-triage.");
    }
}
