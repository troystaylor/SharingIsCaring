using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using LLMbook.Api.Models;

namespace LLMbook.Api.Services;

/// <summary>
/// Handles LLM processing for ingest, query, and lint operations.
/// Supports Azure OpenAI, OpenAI-compatible endpoints, and Foundry Local.
/// </summary>
public partial class BookLlmService
{
    private readonly ChatClient _chatClient;
    private readonly bool _supportsJsonMode;

    // Timeouts per operation type
    private static readonly TimeSpan IngestTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LintTimeout = TimeSpan.FromSeconds(180);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Create from Azure OpenAI client (managed identity auth).</summary>
    public BookLlmService(AzureOpenAIClient openAiClient, string deploymentName, bool supportsJsonMode = true)
    {
        _chatClient = openAiClient.GetChatClient(deploymentName);
        _supportsJsonMode = supportsJsonMode;
    }

    /// <summary>Create from generic OpenAI client (API key or unauthenticated).</summary>
    public BookLlmService(OpenAIClient openAiClient, string modelName, bool supportsJsonMode = true)
    {
        _chatClient = openAiClient.GetChatClient(modelName);
        _supportsJsonMode = supportsJsonMode;
    }

    /// <summary>Create directly from a ChatClient.</summary>
    public BookLlmService(ChatClient chatClient, bool supportsJsonMode = true)
    {
        _chatClient = chatClient;
        _supportsJsonMode = supportsJsonMode;
    }

    // ── Ingest ──

    /// <summary>
    /// Given a source document and the current book index, determine which pages
    /// to create or update and return structured instructions.
    /// </summary>
    public async Task<IngestPlan> PlanIngestAsync(string sourceContent, string? sourceTitle,
        List<PageSummary> existingPages)
    {
        var indexSummary = string.Join("\n",
            existingPages.Select(p => $"- {p.PageId}: {p.Title} ({p.Category})"));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
                You are a book maintenance agent. Given a source document and an index of existing book pages,
                determine what pages should be created or updated to integrate the source's knowledge.

                Rules:
                - Extract entities (specific things: people, products, APIs, services) → entity pages
                - Extract concepts (patterns, principles, ideas) → concept pages
                - Always create a source summary page for the ingested document
                - Update existing pages when new information is relevant to them
                - Add cross-references between related pages using [[page-id]] syntax
                - Use kebab-case for page IDs (e.g., azure-functions-overview)

                Respond with JSON only:
                {
                    "summary": "One-paragraph summary of the source",
                    "pagesToCreate": [
                        { "pageId": "...", "title": "...", "category": "entity|concept|source", "content": "Full markdown content" }
                    ],
                    "pagesToUpdate": [
                        { "pageId": "...", "changeDescription": "What to add", "newContent": "Full updated markdown content" }
                    ]
                }
                """),
            new UserChatMessage($"""
                SOURCE TITLE: {sourceTitle ?? "Untitled"}
                
                EXISTING book INDEX:
                {(string.IsNullOrEmpty(indexSummary) ? "(empty book)" : indexSummary)}

                SOURCE CONTENT:
                {sourceContent}
                """)
        };

        var json = await CompleteChatJsonAsync(messages, IngestTimeout);
        return JsonSerializer.Deserialize<IngestPlan>(json, JsonOpts)
            ?? throw new InvalidOperationException("Failed to parse ingest plan from LLM");
    }

    // ── Query ──

    /// <summary>
    /// Synthesize an answer from relevant book pages.
    /// </summary>
    public async Task<QueryResponse> SynthesizeAnswerAsync(string question,
        List<BookPage> relevantPages)
    {
        var context = string.Join("\n\n---\n\n",
            relevantPages.Select(p => $"## {p.Title} (ID: {p.PageId})\n{p.Content}"));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
                You are a knowledge base assistant. Answer the user's question using ONLY the book pages provided.
                If the book doesn't contain enough information, say so and rate confidence as "low".
                
                Always cite which pages informed your answer.
                
                Respond with JSON:
                {
                    "answer": "Your synthesized answer in markdown",
                    "citations": [
                        { "pageId": "...", "title": "...", "relevance": "How this page contributed" }
                    ],
                    "confidence": "high|medium|low"
                }
                """),
            new UserChatMessage($"""
                QUESTION: {question}

                book pageS:
                {(string.IsNullOrEmpty(context) ? "(no relevant pages found)" : context)}
                """)
        };

        var json = await CompleteChatJsonAsync(messages, QueryTimeout);
        return JsonSerializer.Deserialize<QueryResponse>(json, JsonOpts)
            ?? new QueryResponse { Answer = "Failed to generate answer", Confidence = "low" };
    }

    // ── Lint ──

    private const int LintBatchSize = 30;

    /// <summary>
    /// Analyze book pages for contradictions, staleness, orphans, and gaps.
    /// Pages are processed in batches to stay within token limits.
    /// </summary>
    public async Task<LintResponse> LintPagesAsync(List<BookPage> pages)
    {
        var allIssues = new List<LintIssue>();
        var allSuggestions = new List<string>();

        // Process in batches of LintBatchSize
        for (var i = 0; i < pages.Count; i += LintBatchSize)
        {
            var batch = pages.Skip(i).Take(LintBatchSize).ToList();
            var batchResult = await LintBatchAsync(batch);
            allIssues.AddRange(batchResult.Issues);
            allSuggestions.AddRange(batchResult.SuggestedQuestions);
        }

        return new LintResponse
        {
            TotalPages = pages.Count,
            Issues = allIssues,
            SuggestedQuestions = allSuggestions.Distinct().ToList()
        };
    }

    private async Task<LintResponse> LintBatchAsync(List<BookPage> pages)
    {
        var pagesJson = string.Join("\n\n---\n\n",
            pages.Select(p =>
                $"## {p.Title} (ID: {p.PageId}, category: {p.Category}, updated: {p.LastUpdated:yyyy-MM-dd})\n" +
                $"Cross-refs: {string.Join(", ", p.CrossReferences)}\n" +
                $"Sources: {p.Sources.Count}\n\n{p.Content}"));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
                You are a book health checker. Analyze the book pages for issues.
                
                Check for:
                - contradiction: Two pages make conflicting claims
                - stale: Page references outdated information or old dates
                - orphan: Page has no cross-references from other pages
                - missing-crossref: Pages discuss related topics but don't link to each other
                - gap: Important concepts mentioned but lacking their own page
                
                Also suggest questions the book cannot currently answer well.
                
                Respond with JSON:
                {
                    "issues": [
                        { "type": "contradiction|stale|orphan|missing-crossref|gap", "severity": "high|medium|low", "pageId": "...", "description": "...", "suggestedFix": "..." }
                    ],
                    "suggestedQuestions": ["Question the book can't answer yet"]
                }
                """),
            new UserChatMessage($"book pageS:\n\n{pagesJson}")
        };

        var json = await CompleteChatJsonAsync(messages, LintTimeout);
        return JsonSerializer.Deserialize<LintResponse>(json, JsonOpts)
            ?? new LintResponse();
    }

    // ── Shared Helpers ──

    /// <summary>
    /// Complete a chat expecting JSON output. Uses JSON response format when supported,
    /// otherwise extracts JSON from the response text (handles markdown fences).
    /// </summary>
    private async Task<string> CompleteChatJsonAsync(List<ChatMessage> messages, TimeSpan timeout)
    {
        var options = new ChatCompletionOptions();
        if (_supportsJsonMode)
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        var response = await _chatClient.CompleteChatAsync(messages, options,
            new CancellationTokenSource(timeout).Token);

        var text = response.Value.Content[0].Text;
        return _supportsJsonMode ? text : ExtractJson(text);
    }

    /// <summary>
    /// Extract JSON from a response that may be wrapped in markdown code fences.
    /// Handles ```json ... ```, ``` ... ```, and bare JSON.
    /// </summary>
    private static string ExtractJson(string text)
    {
        // Try to extract from markdown code fence
        var match = JsonFenceRegex().Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Try bare JSON (starts with { or [)
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return trimmed;

        // Last resort: find first { to last }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text; // Return as-is, let the deserializer fail with a clear error
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Singleline)]
    private static partial Regex JsonFenceRegex();
}

// ── Internal plan model ──

public class IngestPlan
{
    public string Summary { get; set; } = string.Empty;
    public List<IngestPageCreate> PagesToCreate { get; set; } = [];
    public List<IngestPageUpdate> PagesToUpdate { get; set; } = [];
}

public class IngestPageCreate
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class IngestPageUpdate
{
    public string PageId { get; set; } = string.Empty;
    public string ChangeDescription { get; set; } = string.Empty;
    public string NewContent { get; set; } = string.Empty;
}
