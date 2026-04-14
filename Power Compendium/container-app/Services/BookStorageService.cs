using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LLMbook.Api.Models;

namespace LLMbook.Api.Services;

/// <summary>
/// Handles book page storage in Azure Blob Storage.
/// Layout: book-pages/{scope}/{category}/{pageId}.json
///   scope = "org" or "users/{userId}"
/// </summary>
public class BookStorageService
{
    private readonly BlobServiceClient _blobClient;
    private const string ContainerName = "wiki-pages";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BookStorageService(BlobServiceClient blobClient)
    {
        _blobClient = blobClient;
    }

    // ── Path helpers ──

    private static string ScopePath(string scope, string? userId) =>
        scope == "personal" && !string.IsNullOrEmpty(userId)
            ? $"users/{userId}"
            : "org";

    private static string PageBlobPath(string scope, string? userId, string pageId) =>
        $"{ScopePath(scope, userId)}/pages/{pageId}.json";

    private static string SourceBlobPath(string scope, string? userId, string sourceId) =>
        $"{ScopePath(scope, userId)}/sources/{sourceId}.json";

    private BlobContainerClient Container() =>
        _blobClient.GetBlobContainerClient(ContainerName);

    // ── Page CRUD ──

    public async Task<BookPage?> GetPageAsync(string pageId, string scope, string? userId)
    {
        var container = Container();
        var blob = container.GetBlobClient(PageBlobPath(scope, userId, pageId));

        if (!await blob.ExistsAsync())
            return null;

        var download = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<BookPage>(download.Value.Content.ToString(), JsonOpts);
    }

    public async Task<BookPage> WritePageAsync(BookPage page)
    {
        var container = Container();
        await container.CreateIfNotExistsAsync();

        page.LastUpdated = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(page, JsonOpts);
        var blob = container.GetBlobClient(PageBlobPath(page.Scope, page.UserId, page.PageId));

        await blob.UploadAsync(
            new BinaryData(json),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = new Dictionary<string, string>
                {
                    ["title"] = page.Title,
                    ["category"] = page.Category ?? "",
                    ["scope"] = page.Scope,
                    ["updatedBy"] = page.UpdatedBy ?? ""
                }
            });

        return page;
    }

    public async Task<bool> DeletePageAsync(string pageId, string scope, string? userId)
    {
        var container = Container();
        var blob = container.GetBlobClient(PageBlobPath(scope, userId, pageId));

        // Soft-delete: move to deleted/ prefix
        if (!await blob.ExistsAsync())
            return false;

        var download = await blob.DownloadContentAsync();
        var deletedBlob = container.GetBlobClient(
            $"{ScopePath(scope, userId)}/deleted/{pageId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
        await deletedBlob.UploadAsync(download.Value.Content, overwrite: true);
        await blob.DeleteAsync();

        return true;
    }

    public async Task<List<PageSummary>> ListPagesAsync(string scope, string? userId,
        string? category = null)
    {
        var container = Container();
        var prefix = $"{ScopePath(scope, userId)}/pages/";
        var pages = new List<PageSummary>();

        await foreach (var blobItem in container.GetBlobsAsync(
            traits: BlobTraits.Metadata, prefix: prefix))
        {
            var meta = blobItem.Metadata ?? new Dictionary<string, string>();

            meta.TryGetValue("category", out var pageCategory);
            pageCategory ??= "";

            if (category != null && !pageCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                continue;

            var pageId = Path.GetFileNameWithoutExtension(blobItem.Name);

            meta.TryGetValue("title", out var title);
            meta.TryGetValue("scope", out var pageScope);
            meta.TryGetValue("updatedBy", out var updatedBy);

            pages.Add(new PageSummary
            {
                PageId = pageId,
                Title = title ?? pageId,
                Category = pageCategory,
                Scope = pageScope ?? scope,
                UpdatedBy = updatedBy ?? "",
                LastUpdated = blobItem.Properties?.LastModified ?? DateTimeOffset.UtcNow,
                SourceCount = 0,
                LinkCount = 0
            });
        }

        return pages;
    }

    /// <summary>List pages from both org and personal scopes.</summary>
    public async Task<List<PageSummary>> ListAllPagesAsync(string? userId, string? category = null)
    {
        var orgPages = await ListPagesAsync("org", null, category);
        if (!string.IsNullOrEmpty(userId))
        {
            var personalPages = await ListPagesAsync("personal", userId, category);
            orgPages.AddRange(personalPages);
        }
        return orgPages;
    }

    // ── Source storage ──

    public async Task SaveSourceAsync(string sourceId, string scope, string? userId,
        IngestRequest source)
    {
        var container = Container();
        await container.CreateIfNotExistsAsync();

        var json = JsonSerializer.Serialize(new
        {
            sourceId,
            title = source.Title,
            content = source.Content,
            sourceUrl = source.SourceUrl,
            category = source.Category,
            ingestedAt = DateTimeOffset.UtcNow
        }, JsonOpts);

        var blob = container.GetBlobClient(SourceBlobPath(scope, userId, sourceId));
        await blob.UploadAsync(
            new BinaryData(json),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            });
    }

    // ── Log ──

    public async Task AppendLogAsync(string scope, string? userId, string entry)
    {
        var container = Container();
        await container.CreateIfNotExistsAsync();

        var logPath = $"{ScopePath(scope, userId)}/log.md";
        var blob = container.GetBlobClient(logPath);

        var existing = "";
        if (await blob.ExistsAsync())
        {
            var download = await blob.DownloadContentAsync();
            existing = download.Value.Content.ToString();
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var updated = $"## [{timestamp}] {entry}\n\n{existing}";
        await blob.UploadAsync(new BinaryData(Encoding.UTF8.GetBytes(updated)), overwrite: true);
    }
}
