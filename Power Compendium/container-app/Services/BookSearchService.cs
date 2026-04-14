using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using LLMbook.Api.Models;

namespace LLMbook.Api.Services;

/// <summary>
/// Indexes and searches book pages via Azure AI Search.
/// </summary>
public class BookSearchService
{
    private readonly SearchClient _searchClient;

    public BookSearchService(SearchClient searchClient)
    {
        _searchClient = searchClient;
    }

    /// <summary>Index or update a book page in the search index.</summary>
    public async Task IndexPageAsync(BookPage page)
    {
        await IndexPagesAsync([page]);
    }

    /// <summary>Batch index multiple book pages in a single call.</summary>
    public async Task IndexPagesAsync(List<BookPage> pages)
    {
        if (pages.Count == 0) return;

        var docs = pages.Select(page => new SearchDocument(new Dictionary<string, object>
        {
            ["pageId"] = page.PageId,
            ["title"] = page.Title,
            ["content"] = page.Content,
            ["category"] = page.Category ?? "",
            ["scope"] = page.Scope,
            ["userId"] = page.UserId ?? "",
            ["updatedBy"] = page.UpdatedBy ?? "",
            ["lastUpdated"] = page.LastUpdated.ToString("o"),
            ["crossReferences"] = string.Join(", ", page.CrossReferences),
            ["sourceCount"] = page.Sources.Count,
            ["linkCount"] = page.CrossReferences.Count
        })).ToArray();

        await _searchClient.MergeOrUploadDocumentsAsync(docs);
    }

    /// <summary>Remove a page from the search index.</summary>
    public async Task RemovePageAsync(string pageId)
    {
        var doc = new SearchDocument(new Dictionary<string, object>
        {
            ["pageId"] = pageId
        });

        await _searchClient.DeleteDocumentsAsync(new[] { doc });
    }

    /// <summary>
    /// Search for pages relevant to a question.
    /// Filters by scope and optionally userId for personal books.
    /// </summary>
    public async Task<List<PageSummary>> SearchAsync(string query, string scope, string? userId)
    {
        string? filter = scope switch
        {
            "org" => "scope eq 'org'",
            "personal" when userId != null => $"scope eq 'personal' and userId eq '{EscapeFilter(userId)}'",
            "all" when userId != null => $"(scope eq 'org') or (scope eq 'personal' and userId eq '{EscapeFilter(userId)}')",
            _ => "scope eq 'org'"
        };

        var options = new SearchOptions
        {
            Filter = filter,
            Size = 20,
            Select = { "pageId", "title", "category", "scope", "updatedBy", "lastUpdated", "sourceCount", "linkCount" },
            QueryType = SearchQueryType.Simple
        };

        var results = await _searchClient.SearchAsync<SearchDocument>(query, options);
        var pages = new List<PageSummary>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var doc = result.Document;
            pages.Add(new PageSummary
            {
                PageId = doc.GetString("pageId"),
                Title = doc.GetString("title"),
                Category = doc.GetString("category"),
                Scope = doc.GetString("scope"),
                UpdatedBy = doc.GetString("updatedBy"),
                LastUpdated = DateTimeOffset.TryParse(doc.GetString("lastUpdated"), out var dt)
                    ? dt : DateTimeOffset.UtcNow,
                SourceCount = doc.TryGetValue("sourceCount", out object? sc) && sc is int sci ? sci : 0,
                LinkCount = doc.TryGetValue("linkCount", out object? lc) && lc is int lci ? lci : 0
            });
        }

        return pages;
    }

    private static string EscapeFilter(string value) =>
        value.Replace("'", "''");
}
