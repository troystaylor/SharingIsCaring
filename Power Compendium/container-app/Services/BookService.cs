using LLMbook.Api.Models;

namespace LLMbook.Api.Services;

/// <summary>
/// Core book operations shared by both REST functions and MCP handler.
/// Eliminates code duplication between the two surfaces.
/// </summary>
public class BookService
{
    private readonly BookStorageService _storage;
    private readonly BookSearchService _search;
    private readonly BookLlmService _llm;

    public BookService(BookStorageService storage, BookSearchService search, BookLlmService llm)
    {
        _storage = storage;
        _search = search;
        _llm = llm;
    }

    // ── Ingest ──

    public async Task<IngestResponse> IngestAsync(IngestRequest body, string scope,
        string? userId, string? displayName)
    {
        if (!InputValidation.IsWithinContentLimit(body.Content, out var contentErr))
            throw new ArgumentException(contentErr);

        var existingPages = scope == "all"
            ? await _storage.ListAllPagesAsync(userId)
            : await _storage.ListPagesAsync(scope, userId);

        var plan = await _llm.PlanIngestAsync(body.Content, body.Title, existingPages);

        var sourceId = $"src-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        await _storage.SaveSourceAsync(sourceId, scope, userId, body);

        var response = new IngestResponse
        {
            SourceId = sourceId,
            Summary = plan.Summary
        };

        // Collect pages for batch search indexing
        var pagesToIndex = new List<BookPage>();

        foreach (var create in plan.PagesToCreate)
        {
            var page = new BookPage
            {
                PageId = create.PageId,
                Title = create.Title,
                Content = create.Content,
                Category = create.Category,
                Scope = scope,
                UserId = scope == "personal" ? userId : null,
                UpdatedBy = displayName,
                Sources = [new SourceRef { SourceId = sourceId, Title = body.Title ?? "Untitled" }],
                CrossReferences = ExtractCrossRefs(create.Content)
            };
            await _storage.WritePageAsync(page);
            pagesToIndex.Add(page);
            response.PagesCreated.Add(new PageRef { PageId = page.PageId, Title = page.Title });
        }

        foreach (var update in plan.PagesToUpdate)
        {
            var existing = await _storage.GetPageAsync(update.PageId, scope, userId);
            if (existing == null) continue;

            existing.Content = update.NewContent;
            existing.UpdatedBy = displayName;
            existing.Sources.Add(new SourceRef { SourceId = sourceId, Title = body.Title ?? "Untitled" });
            existing.CrossReferences = ExtractCrossRefs(update.NewContent);

            await _storage.WritePageAsync(existing);
            pagesToIndex.Add(existing);
            response.PagesUpdated.Add(new PageChangeRef
            {
                PageId = existing.PageId,
                Title = existing.Title,
                ChangeDescription = update.ChangeDescription
            });
        }

        // Batch index all pages at once
        await _search.IndexPagesAsync(pagesToIndex);

        var safeTitle = InputValidation.SanitizeForLog(body.Title);
        var logEntry = $"ingest | {safeTitle} | " +
            $"{response.PagesCreated.Count} created, {response.PagesUpdated.Count} updated";
        response.LogEntry = logEntry;
        await _storage.AppendLogAsync(scope, userId, logEntry);

        return response;
    }

    // ── Query ──

    public async Task<QueryResponse> QueryAsync(QueryRequest body, string scope,
        string? userId, string? displayName)
    {
        if (!InputValidation.IsWithinQuestionLimit(body.Question, out var questionErr))
            throw new ArgumentException(questionErr);

        var searchResults = await _search.SearchAsync(body.Question, scope, userId);

        var relevantPages = new List<BookPage>();
        foreach (var summary in searchResults.Take(10))
        {
            var page = await _storage.GetPageAsync(summary.PageId, summary.Scope,
                summary.Scope == "personal" ? userId : null);
            if (page != null) relevantPages.Add(page);
        }

        var response = await _llm.SynthesizeAnswerAsync(body.Question, relevantPages);

        if (body.SaveAsPage)
        {
            var pageId = $"query-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            var page = new BookPage
            {
                PageId = pageId,
                Title = body.Question,
                Content = response.Answer,
                Category = "comparison",
                Scope = scope,
                UserId = scope == "personal" ? userId : null,
                UpdatedBy = displayName,
                CrossReferences = response.Citations.Select(c => c.PageId).ToList()
            };
            await _storage.WritePageAsync(page);
            await _search.IndexPageAsync(page);
            response.SavedPageId = pageId;
            var safeQuestion = InputValidation.SanitizeForLog(body.Question);
            await _storage.AppendLogAsync(scope, userId, $"query-saved | {safeQuestion}");
        }

        return response;
    }

    // ── Lint ──

    public async Task<LintResponse> LintAsync(string scope, string? userId)
    {
        var pageSummaries = await _storage.ListPagesAsync(scope, userId);
        var pages = new List<BookPage>();
        foreach (var summary in pageSummaries)
        {
            var page = await _storage.GetPageAsync(summary.PageId, scope,
                scope == "personal" ? userId : null);
            if (page != null) pages.Add(page);
        }

        if (pages.Count == 0)
            return new LintResponse { TotalPages = 0 };

        var response = await _llm.LintPagesAsync(pages);
        await _storage.AppendLogAsync(scope, userId,
            $"lint | {response.TotalPages} pages scanned, {response.Issues.Count} issues found");

        return response;
    }

    // ── Page CRUD ──

    public async Task<PageListResponse> ListPagesAsync(string scope, string? userId, string? category)
    {
        List<PageSummary> pages;
        if (scope == "all")
            pages = await _storage.ListAllPagesAsync(userId, category);
        else
            pages = await _storage.ListPagesAsync(scope, userId, category);

        return new PageListResponse { Pages = pages, TotalCount = pages.Count };
    }

    public Task<BookPage?> ReadPageAsync(string pageId, string scope, string? userId)
    {
        if (!InputValidation.IsValidPageId(pageId, out var err))
            throw new ArgumentException(err);
        return _storage.GetPageAsync(pageId, scope, scope == "personal" ? userId : null);
    }

    public async Task<BookPage> WritePageAsync(string pageId, WritePageRequest body,
        string scope, string? userId, string? displayName)
    {
        if (!InputValidation.IsValidPageId(pageId, out var pageIdErr))
            throw new ArgumentException(pageIdErr);
        if (!InputValidation.IsWithinContentLimit(body.Content, out var contentErr))
            throw new ArgumentException(contentErr);

        var existing = await _storage.GetPageAsync(pageId, scope,
            scope == "personal" ? userId : null);

        var page = existing ?? new BookPage
        {
            PageId = pageId,
            CreatedAt = DateTimeOffset.UtcNow,
            Scope = scope,
            UserId = scope == "personal" ? userId : null
        };

        page.Title = body.Title;
        page.Content = body.Content;
        page.Category = body.Category ?? page.Category ?? "";
        page.UpdatedBy = displayName;
        page.CrossReferences = ExtractCrossRefs(body.Content);

        await _storage.WritePageAsync(page);
        await _search.IndexPageAsync(page);
        return page;
    }

    public async Task<bool> DeletePageAsync(string pageId, string scope, string? userId)
    {
        if (!InputValidation.IsValidPageId(pageId, out var err))
            throw new ArgumentException(err);

        var deleted = await _storage.DeletePageAsync(pageId, scope,
            scope == "personal" ? userId : null);
        if (deleted) await _search.RemovePageAsync(pageId);
        return deleted;
    }

    public async Task<PromoteResponse> PromotePageAsync(string pageId, string userId,
        string? displayName)
    {
        if (!InputValidation.IsValidPageId(pageId, out var err))
            throw new ArgumentException(err);

        var personalPage = await _storage.GetPageAsync(pageId, "personal", userId);
        if (personalPage == null)
            throw new ArgumentException($"Page '{pageId}' not found in personal book");

        var existingOrg = await _storage.GetPageAsync(pageId, "org", null);
        var merged = existingOrg != null;

        var orgPage = existingOrg ?? new BookPage
        {
            PageId = pageId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        orgPage.Title = personalPage.Title;
        orgPage.Content = personalPage.Content;
        orgPage.Category = personalPage.Category;
        orgPage.Scope = "org";
        orgPage.UserId = null;
        orgPage.UpdatedBy = displayName;
        orgPage.Sources = personalPage.Sources;
        orgPage.CrossReferences = personalPage.CrossReferences;

        await _storage.WritePageAsync(orgPage);
        await _search.IndexPageAsync(orgPage);

        await _storage.AppendLogAsync("org", null,
            $"promote | {personalPage.Title} | promoted by {displayName}");

        return new PromoteResponse
        {
            Promoted = true,
            SourcePageId = pageId,
            OrgPageId = pageId,
            Merged = merged
        };
    }

    // ── Skill Ingest (Option B: multi-file) ──

    public async Task<IngestSkillResponse> IngestSkillAsync(IngestSkillRequest body, string scope,
        string? userId, string? displayName)
    {
        // Concatenate all files into one source with file headers
        var combined = string.Join("\n\n---\n\n",
            body.Files.Select(f => $"## File: {f.Path}\n\n{f.Content}"));

        if (!InputValidation.IsWithinContentLimit(combined, out var err))
            throw new ArgumentException(err);

        // Ingest as a single source with skill context
        var ingestReq = new IngestRequest
        {
            Title = $"Skill: {body.SkillName}",
            Content = $"# Agent Skill: {body.SkillName}\n\n" +
                $"This skill contains {body.Files.Count} files.\n\n{combined}",
            Category = "skill"
        };

        var result = await IngestAsync(ingestReq, scope, userId, displayName);

        return new IngestSkillResponse
        {
            SkillName = body.SkillName,
            FilesProcessed = body.Files.Count,
            Summary = result.Summary,
            PagesCreated = result.PagesCreated,
            PagesUpdated = result.PagesUpdated,
            LogEntry = result.LogEntry
        };
    }

    // ── Skill Ingest (Option C: URL-based) ──

    public async Task<IngestSkillResponse> IngestSkillFromUrlAsync(IngestSkillFromUrlRequest body,
        string scope, string? userId, string? displayName, HttpClient httpClient)
    {
        // Fetch the skill content from the URL
        // Supports GitHub repo URLs (raw content) and direct file URLs
        var files = new List<SkillFile>();
        var skillName = Path.GetFileName(body.Url.TrimEnd('/'));

        if (body.Url.Contains("github.com") && body.Url.Contains("/tree/"))
        {
            // GitHub tree URL → convert to API URL to list files
            // e.g., https://github.com/owner/repo/tree/main/path → https://api.github.com/repos/owner/repo/contents/path
            var apiUrl = ConvertGitHubTreeToApi(body.Url);
            var response = await httpClient.GetStringAsync(apiUrl);
            var items = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);

            foreach (var item in items.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                var name = item.GetProperty("name").GetString() ?? "";
                var downloadUrl = item.GetProperty("download_url").GetString();

                if (type == "file" && downloadUrl != null &&
                    (name.EndsWith(".md") || name.EndsWith(".csx") || name.EndsWith(".json") ||
                     name.EndsWith(".txt") || name.EndsWith(".yaml") || name.EndsWith(".yml")))
                {
                    var content = await httpClient.GetStringAsync(downloadUrl);
                    files.Add(new SkillFile { Path = name, Content = content });
                }
            }
        }
        else
        {
            // Direct URL → fetch as a single file
            var content = await httpClient.GetStringAsync(body.Url);
            var fileName = Path.GetFileName(new Uri(body.Url).AbsolutePath);
            files.Add(new SkillFile { Path = fileName, Content = content });
        }

        if (files.Count == 0)
            throw new ArgumentException("No skill files found at the provided URL");

        return await IngestSkillAsync(
            new IngestSkillRequest { SkillName = skillName, Files = files },
            scope, userId, displayName);
    }

    private static string ConvertGitHubTreeToApi(string treeUrl)
    {
        // https://github.com/owner/repo/tree/branch/path → https://api.github.com/repos/owner/repo/contents/path?ref=branch
        var uri = new Uri(treeUrl);
        var segments = uri.AbsolutePath.TrimStart('/').Split('/');
        // segments: [owner, repo, "tree", branch, ...path]
        if (segments.Length < 4 || segments[2] != "tree")
            throw new ArgumentException("Invalid GitHub tree URL format");

        var owner = segments[0];
        var repo = segments[1];
        var branch = segments[3];
        var path = string.Join("/", segments.Skip(4));

        return $"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}";
    }

    // ── Helpers ──

    private static List<string> ExtractCrossRefs(string content)
    {
        var refs = new List<string>();
        var start = 0;
        while ((start = content.IndexOf("[[", start, StringComparison.Ordinal)) >= 0)
        {
            var end = content.IndexOf("]]", start + 2, StringComparison.Ordinal);
            if (end < 0) break;
            refs.Add(content[(start + 2)..end]);
            start = end + 2;
        }
        return refs.Distinct().ToList();
    }
}
