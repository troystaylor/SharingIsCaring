using DurableAgentWorkflows.Shared;
using static DurableAgentWorkflows.Shared.TextHelpers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI.Chat;

namespace DurableAgentWorkflows.Workflows.DocReview;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Document Review Pipeline Workflow                                          ║
// ║                                                                            ║
// ║  Pattern: Sequential → Fan-out/Fan-in (4 parallel) → Chained HITL gates   ║
// ║                                                                            ║
// ║  IngestDocument ──► [LegalReviewer, ComplianceReviewer,                    ║
// ║                       BrandReviewer, TechnicalReviewer]                    ║
// ║                          │                                                 ║
// ║                  ConsolidateFeedback                                       ║
// ║                          │                                                 ║
// ║                  AuthorRevision (HITL #1)                                  ║
// ║                          │                                                 ║
// ║                  PrepareApproval                                           ║
// ║                          │                                                 ║
// ║                  FinalApproval (HITL #2)                                   ║
// ║                          │                                                 ║
// ║                  Publish                                                   ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public static class DocReviewWorkflowBuilder
{
    public static Workflow Build(ChatClient chatClient)
    {
        // ── Executors ────────────────────────────────────────────────────

        IngestDocumentExecutor ingestDocument = new();

        AIAgent legalReviewer = chatClient.AsAIAgent(
            """
            You are a legal reviewer. Check for liability, IP, privacy, and contractual risks.
            Reply in EXACTLY this format, max 3 findings:
            [Critical/Major/Minor] Finding text (one sentence)
            Keep total response under 150 words.
            """,
            "LegalReviewer");

        AIAgent complianceReviewer = chatClient.AsAIAgent(
            """
            You are a compliance reviewer. Check against GDPR, data retention, and required disclosures.
            Reply in EXACTLY this format, max 3 findings:
            [Critical/Major/Minor] Finding text (one sentence)
            Keep total response under 150 words.
            """,
            "ComplianceReviewer");

        AIAgent brandReviewer = chatClient.AsAIAgent(
            """
            You are a brand reviewer. Check tone, clarity, audience fit, and inclusive language.
            Reply in EXACTLY this format, max 3 findings:
            [Critical/Major/Minor] Finding text (one sentence)
            Keep total response under 150 words.
            """,
            "BrandReviewer");

        AIAgent technicalReviewer = chatClient.AsAIAgent(
            """
            You are a technical reviewer. Verify factual claims, version references, and prerequisites.
            Reply in EXACTLY this format, max 3 findings:
            [Critical/Major/Minor] Finding text (one sentence)
            Keep total response under 150 words.
            """,
            "TechnicalReviewer");

        ConsolidateFeedbackExecutor consolidateFeedback = new();

        var authorRevision = RequestPort.Create<ApprovalRequest, ApprovalResponse>(
            "AuthorRevision");

        PrepareApprovalExecutor prepareApproval = new();

        var finalApproval = RequestPort.Create<ApprovalRequest, ApprovalResponse>(
            "FinalApproval");

        PublishExecutor publish = new();

        // ── Workflow Graph ───────────────────────────────────────────────

        return new WorkflowBuilder(ingestDocument)
            .WithName("ReviewDocument")
            .WithDescription(
                "Review a document through parallel legal, compliance, brand, and "
                + "technical reviewers, consolidate feedback, then route through "
                + "author revision and final approval gates before publishing. "
                + "Demonstrates chained human-in-the-loop approval gates.")
            .AddFanOutEdge(ingestDocument,
                [legalReviewer, complianceReviewer, brandReviewer, technicalReviewer])
            .AddFanInBarrierEdge(
                [legalReviewer, complianceReviewer, brandReviewer, technicalReviewer],
                consolidateFeedback)
            .AddEdge(consolidateFeedback, authorRevision)
            .AddEdge(authorRevision, prepareApproval)
            .AddEdge(prepareApproval, finalApproval)
            .AddEdge(finalApproval, publish)
            .Build();
    }
}

// ── Executors ────────────────────────────────────────────────────────────────

internal sealed class IngestDocumentExecutor()
    : Executor<string, string>("IngestDocument")
{
    public override ValueTask<string> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Document content cannot be empty.");

        var prepared = $"""
            Review the following document for publishing readiness:
            ---
            {message.Trim()}
            ---
            Analyze this document according to your review criteria. Provide
            findings with severity ratings and specific recommendations.
            """;

        return ValueTask.FromResult(prepared);
    }
}

internal sealed class ConsolidateFeedbackExecutor()
    : Executor<IReadOnlyList<string>, ApprovalRequest>("ConsolidateFeedback")
{
    public override ValueTask<ApprovalRequest> HandleAsync(
        IReadOnlyList<string> results,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var legal = Truncate(results.Count > 0 ? results[0] : "No legal review");
        var compliance = Truncate(results.Count > 1 ? results[1] : "No compliance review");
        var brand = Truncate(results.Count > 2 ? results[2] : "No brand review");
        var technical = Truncate(results.Count > 3 ? results[3] : "No technical review");

        var details = $"""
            === Document Review Feedback ===

            LEGAL REVIEW:
            {legal}

            COMPLIANCE REVIEW:
            {compliance}

            BRAND REVIEW:
            {brand}

            TECHNICAL REVIEW:
            {technical}
            """;

        return ValueTask.FromResult(new ApprovalRequest(
            WorkflowName: "ReviewDocument",
            Summary: "Document review complete — address findings and submit revision",
            Details: TruncateDetails(details),
            RequestedAction: "Review all feedback, make necessary revisions, "
                + "and approve to proceed to final approval gate"));
    }
}

/// <summary>
/// Bridges the author's revision response into a new approval request
/// for the final approver. Enables chained HITL gates.
/// </summary>
internal sealed class PrepareApprovalExecutor()
    : Executor<ApprovalResponse, ApprovalRequest>("PrepareApproval")
{
    public override ValueTask<ApprovalRequest> HandleAsync(
        ApprovalResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var status = response.Approved
            ? "Author addressed feedback and approved for final review"
            : "Author requested additional review cycle";

        return ValueTask.FromResult(new ApprovalRequest(
            WorkflowName: "ReviewDocument",
            Summary: status,
            Details: Truncate(response.Comments),
            RequestedAction: "Approve or reject"));
    }
}

internal sealed class PublishExecutor()
    : Executor<ApprovalResponse, string>("Publish")
{
    public override ValueTask<string> HandleAsync(
        ApprovalResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (response.Approved)
        {
            return ValueTask.FromResult(
                $"✅ Document approved and published. "
                + $"Final reviewer notes: {response.Comments}");
        }

        return ValueTask.FromResult(
            $"❌ Document publication rejected. "
            + $"Feedback: {response.Comments}. "
            + "Returning to author for additional revisions.");
    }
}
