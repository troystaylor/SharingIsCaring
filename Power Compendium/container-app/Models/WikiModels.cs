using System.Text.Json.Serialization;

namespace LLMbook.Api.Models;

public class BookPage
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Scope { get; set; } = "org";
    public string? UserId { get; set; }
    public string? UpdatedBy { get; set; }
    public List<SourceRef> Sources { get; set; } = [];
    public List<string> CrossReferences { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

public class SourceRef
{
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class PageSummary
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Scope { get; set; } = "org";
    public string? UpdatedBy { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public int SourceCount { get; set; }
    public int LinkCount { get; set; }
}

// ── Ingest ──

public class IngestRequest
{
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? Category { get; set; }
}

public class IngestResponse
{
    public string SourceId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<PageRef> PagesCreated { get; set; } = [];
    public List<PageChangeRef> PagesUpdated { get; set; } = [];
    public string LogEntry { get; set; } = string.Empty;
}

public class PageRef
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class PageChangeRef : PageRef
{
    public string ChangeDescription { get; set; } = string.Empty;
}

// ── Query ──

public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
    public bool SaveAsPage { get; set; } = false;
}

public class QueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = [];
    public string Confidence { get; set; } = "medium";
    public string? SavedPageId { get; set; }
}

public class Citation
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Relevance { get; set; } = string.Empty;
}

// ── Lint ──

public class LintRequest
{
    public string? LintScope { get; set; } = "all";
}

public class LintResponse
{
    public int TotalPages { get; set; }
    public List<LintIssue> Issues { get; set; } = [];
    public List<string> SuggestedQuestions { get; set; } = [];
}

public class LintIssue
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string PageId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SuggestedFix { get; set; } = string.Empty;
}

// ── Write / Delete / Promote ──

public class WritePageRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Category { get; set; }
}

public class DeleteResponse
{
    public bool Deleted { get; set; }
    public string PageId { get; set; } = string.Empty;
}

public class PromoteResponse
{
    public bool Promoted { get; set; }
    public string SourcePageId { get; set; } = string.Empty;
    public string OrgPageId { get; set; } = string.Empty;
    public bool Merged { get; set; }
}

public class PageListResponse
{
    public List<PageSummary> Pages { get; set; } = [];
    public int TotalCount { get; set; }
}

// ── Skill Ingest (Option B: multi-file, Option C: URL-based) ──

public class IngestSkillRequest
{
    public string SkillName { get; set; } = string.Empty;
    public List<SkillFile> Files { get; set; } = [];
}

public class SkillFile
{
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class IngestSkillFromUrlRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Type { get; set; } = "agent-skill";
}

public class IngestSkillResponse
{
    public string SkillName { get; set; } = string.Empty;
    public int FilesProcessed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<PageRef> PagesCreated { get; set; } = [];
    public List<PageChangeRef> PagesUpdated { get; set; } = [];
    public string LogEntry { get; set; } = string.Empty;
}
