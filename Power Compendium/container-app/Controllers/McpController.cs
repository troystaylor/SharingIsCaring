using Microsoft.AspNetCore.Mvc;
using LLMbook.Api.Services;

namespace LLMbook.Api.Controllers;

[ApiController]
[Route("api/mcp")]
public class McpController : ControllerBase
{
    private readonly BookService _book;
    private readonly McpHandler _mcp;

    public McpController(BookService book, McpHandler mcp)
    {
        _book = book;
        _mcp = mcp;
    }

    [HttpPost]
    public async Task<IActionResult> HandleMcp()
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        var userId = UserIdentity.GetUserId(Request);
        var displayName = UserIdentity.GetDisplayName(Request);

        var result = await _mcp.HandleAsync(body, userId, displayName, _book);
        return Content(result, "application/json");
    }
}
