using Microsoft.AspNetCore.Mvc;
using LLMbook.Api.Models;
using LLMbook.Api.Services;

namespace LLMbook.Api.Controllers;

[ApiController]
[Route("api/book")]
public class BookController : ControllerBase
{
    private readonly BookService _book;

    public BookController(BookService book)
    {
        _book = book;
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromQuery] string scope = "org", [FromBody] IngestRequest? body = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (scope == "personal" && string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "Cannot use personal scope without authentication" });
        if (body == null || string.IsNullOrWhiteSpace(body.Content))
            return BadRequest(new { error = "content is required" });

        try
        {
            var response = await _book.IngestAsync(body, scope, userId, displayName);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromQuery] string scope = "org", [FromBody] QueryRequest? body = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (body == null || string.IsNullOrWhiteSpace(body.Question))
            return BadRequest(new { error = "question is required" });

        try
        {
            var response = await _book.QueryAsync(body, scope, userId, displayName);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("lint")]
    public async Task<IActionResult> Lint([FromQuery] string scope = "org")
    {
        var userId = UserIdentity.GetUserId(Request);
        var response = await _book.LintAsync(scope, userId);
        return Ok(response);
    }

    [HttpGet("pages")]
    public async Task<IActionResult> ListPages([FromQuery] string scope = "org", [FromQuery] string? category = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var response = await _book.ListPagesAsync(scope, userId, category);
        return Ok(response);
    }

    [HttpGet("pages/{pageId}")]
    public async Task<IActionResult> ReadPage(string pageId, [FromQuery] string scope = "org")
    {
        var userId = UserIdentity.GetUserId(Request);
        try
        {
            var page = await _book.ReadPageAsync(pageId, scope, userId);
            if (page == null) return NotFound(new { error = $"Page '{pageId}' not found in {scope} book" });
            return Ok(page);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("pages/{pageId}")]
    public async Task<IActionResult> WritePage(string pageId, [FromQuery] string scope = "org", [FromBody] WritePageRequest? body = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (scope == "personal" && string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "Cannot use personal scope without authentication" });
        if (body == null || string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.Content))
            return BadRequest(new { error = "title and content are required" });

        try
        {
            var page = await _book.WritePageAsync(pageId, body, scope, userId, displayName);
            return Ok(page);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("pages/{pageId}")]
    public async Task<IActionResult> DeletePage(string pageId, [FromQuery] string scope = "org")
    {
        var userId = UserIdentity.GetUserId(Request);
        try
        {
            var deleted = await _book.DeletePageAsync(pageId, scope, userId);
            if (!deleted) return NotFound(new { error = $"Page '{pageId}' not found in {scope} book" });
            return Ok(new DeleteResponse { Deleted = true, PageId = pageId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("pages/{pageId}/promote")]
    public async Task<IActionResult> PromotePage(string pageId)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "Authentication required for promote" });

        try
        {
            var response = await _book.PromotePageAsync(pageId, userId, displayName);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ── Skill Ingest (Option B: multi-file) ──

    [HttpPost("ingest-skill")]
    public async Task<IActionResult> IngestSkill([FromQuery] string scope = "org",
        [FromBody] IngestSkillRequest? body = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (body == null || string.IsNullOrWhiteSpace(body.SkillName) || body.Files.Count == 0)
            return BadRequest(new { error = "skillName and at least one file are required" });

        try
        {
            var response = await _book.IngestSkillAsync(body, scope, userId, displayName);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Skill Ingest (Option C: URL-based) ──

    [HttpPost("ingest-from-url")]
    public async Task<IActionResult> IngestFromUrl([FromQuery] string scope = "org",
        [FromBody] IngestSkillFromUrlRequest? body = null)
    {
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        if (body == null || string.IsNullOrWhiteSpace(body.Url))
            return BadRequest(new { error = "url is required" });

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerCompendium/1.0");
            var response = await _book.IngestSkillFromUrlAsync(body, scope, userId, displayName, httpClient);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { error = $"Failed to fetch URL: {ex.Message}" });
        }
    }
}
