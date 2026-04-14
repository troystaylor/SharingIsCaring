using System.Text.Json;
using System.Text.Json.Nodes;
using LLMbook.Api.Models;

namespace LLMbook.Api.Services;

/// <summary>
/// Handles MCP JSON-RPC 2.0 protocol for native MCP clients (M365 Copilot, VS Code, etc.)
/// </summary>
public class McpHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<string> HandleAsync(string body, string? userId, string? displayName,
        BookService book)
    {
        JsonNode? request;
        try
        {
            request = JsonNode.Parse(body);
        }
        catch
        {
            return JsonRpcError(null, -32700, "Parse error");
        }

        var method = request?["method"]?.GetValue<string>();
        var requestId = request?["id"];
        var @params = request?["params"]?.AsObject();

        return method switch
        {
            "initialize" => JsonRpcSuccess(requestId, new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false }
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "power-compendium-mcp",
                    ["version"] = "1.0.0"
                }
            }),

            "initialized" or "notifications/initialized" or "notifications/cancelled" =>
                JsonRpcSuccess(requestId, new JsonObject()),

            "tools/list" => ToolsList(requestId),

            "tools/call" => await ToolsCall(requestId, @params, userId, displayName, book),

            "resources/list" => JsonRpcSuccess(requestId, new JsonObject
            {
                ["resources"] = new JsonArray()
            }),

            "ping" => JsonRpcSuccess(requestId, new JsonObject()),

            _ => JsonRpcError(requestId, -32601, "Method not found", method)
        };
    }

    // ── tools/list ──

    private string ToolsList(JsonNode? requestId)
    {
        var tools = new JsonArray
        {
            Tool("ingest_source",
                "Process a source document and integrate its knowledge into the book.",
                Props(("title", "string", "Title of the source document"),
                      ("content", "string", "Full text content of the source to ingest"),
                      ("sourceUrl", "string", "URL for citation tracking"),
                      ("category", "string", "Category: article, paper, meeting-notes, api-docs"),
                      ("scope", ScopeSchema())),
                "content"),

            Tool("query_book",
                "Ask a question against the book. Synthesizes an answer with citations.",
                Props(("question", "string", "The question to answer"),
                      ("saveAsPage", "boolean", "Save the answer as a book page. Default: false"),
                      ("scope", QueryScopeSchema())),
                "question"),

            Tool("lint_book",
                "Health-check the book for contradictions, stale claims, orphan pages, and gaps.",
                Props(("lintScope", "string", "Scope: all, recent, or a category"),
                      ("scope", ScopeSchema()))),

            Tool("list_pages",
                "List all book pages with metadata.",
                Props(("category", "string", "Filter by category"),
                      ("scope", QueryScopeSchema()))),

            Tool("read_page",
                "Read the full content of a book page.",
                Props(("pageId", "string", "The page ID to read"),
                      ("scope", ScopeSchema())),
                "pageId"),

            Tool("write_page",
                "Create or update a book page.",
                Props(("pageId", "string", "Page ID in kebab-case"),
                      ("title", "string", "Page title"),
                      ("content", "string", "Full markdown content"),
                      ("category", "string", "Page category"),
                      ("scope", ScopeSchema())),
                "pageId", "title", "content"),

            Tool("delete_page",
                "Soft-delete a book page.",
                Props(("pageId", "string", "The page ID to delete"),
                      ("scope", ScopeSchema())),
                "pageId"),

            Tool("promote_page",
                "Copy a page from personal book to the shared org book.",
                Props(("pageId", "string", "The personal page ID to promote")),
                "pageId"),

            Tool("ingest_skill",
                "Ingest a multi-file agent skill into the book. All files are processed together as a single skill, extracting concepts, entities, and cross-references.",
                Props(("skillName", "string", "Name of the skill being ingested"),
                      ("files", "array", "Array of {path, content} objects — each file in the skill"),
                      ("scope", ScopeSchema())),
                "skillName", "files"),

            Tool("ingest_from_url",
                "Fetch a skill or document from a URL and ingest it into the book. Supports GitHub repo tree URLs (fetches all markdown/code files) and direct file URLs.",
                Props(("url", "string", "URL to fetch the skill from (GitHub tree URL or direct file URL)"),
                      ("type", "string", "Content type: agent-skill (default) or document"),
                      ("scope", ScopeSchema())),
                "url")
        };

        return JsonRpcSuccess(requestId, new JsonObject { ["tools"] = tools });
    }

    // ── tools/call ──

    private async Task<string> ToolsCall(JsonNode? requestId, JsonObject? @params,
        string? userId, string? displayName, BookService book)
    {
        var toolName = @params?["name"]?.GetValue<string>();
        var args = @params?["arguments"]?.AsObject() ?? new JsonObject();

        try
        {
            var scope = args["scope"]?.GetValue<string>() ?? "org";
            string resultJson;

            switch (toolName)
            {
                case "ingest_source":
                {
                    var ingestReq = new IngestRequest
                    {
                        Title = args["title"]?.GetValue<string>(),
                        Content = args["content"]?.GetValue<string>() ?? "",
                        SourceUrl = args["sourceUrl"]?.GetValue<string>(),
                        Category = args["category"]?.GetValue<string>()
                    };
                    var response = await book.IngestAsync(ingestReq, scope, userId, displayName);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "query_book":
                {
                    var queryReq = new QueryRequest
                    {
                        Question = args["question"]?.GetValue<string>() ?? "",
                        SaveAsPage = args["saveAsPage"]?.GetValue<bool>() ?? false
                    };
                    var response = await book.QueryAsync(queryReq, scope, userId, displayName);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "lint_book":
                {
                    var response = await book.LintAsync(scope, userId);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "list_pages":
                {
                    var category = args["category"]?.GetValue<string>();
                    var response = await book.ListPagesAsync(scope, userId, category);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "read_page":
                {
                    var pageId = args["pageId"]?.GetValue<string>() ?? throw new ArgumentException("pageId required");
                    var page = await book.ReadPageAsync(pageId, scope, userId);
                    if (page == null) throw new ArgumentException($"Page '{pageId}' not found");
                    resultJson = JsonSerializer.Serialize(page, JsonOpts);
                    break;
                }

                case "write_page":
                {
                    var pageId = args["pageId"]?.GetValue<string>() ?? throw new ArgumentException("pageId required");
                    var writeReq = new WritePageRequest
                    {
                        Title = args["title"]?.GetValue<string>() ?? "",
                        Content = args["content"]?.GetValue<string>() ?? "",
                        Category = args["category"]?.GetValue<string>()
                    };
                    var page = await book.WritePageAsync(pageId, writeReq, scope, userId, displayName);
                    resultJson = JsonSerializer.Serialize(page, JsonOpts);
                    break;
                }

                case "delete_page":
                {
                    var pageId = args["pageId"]?.GetValue<string>() ?? throw new ArgumentException("pageId required");
                    var deleted = await book.DeletePageAsync(pageId, scope, userId);
                    if (!deleted) throw new ArgumentException($"Page '{pageId}' not found");
                    resultJson = JsonSerializer.Serialize(
                        new DeleteResponse { Deleted = true, PageId = pageId }, JsonOpts);
                    break;
                }

                case "promote_page":
                {
                    var pageId = args["pageId"]?.GetValue<string>() ?? throw new ArgumentException("pageId required");
                    if (string.IsNullOrEmpty(userId))
                        throw new InvalidOperationException("Authentication required for promote");
                    var response = await book.PromotePageAsync(pageId, userId, displayName);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "ingest_skill":
                {
                    var skillName = args["skillName"]?.GetValue<string>() ?? throw new ArgumentException("skillName required");
                    var filesNode = args["files"];
                    var files = new List<SkillFile>();
                    if (filesNode is JsonArray filesArray)
                    {
                        foreach (var f in filesArray)
                        {
                            files.Add(new SkillFile
                            {
                                Path = f?["path"]?.GetValue<string>() ?? "",
                                Content = f?["content"]?.GetValue<string>() ?? ""
                            });
                        }
                    }
                    var skillReq = new IngestSkillRequest { SkillName = skillName, Files = files };
                    var response = await book.IngestSkillAsync(skillReq, scope, userId, displayName);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                case "ingest_from_url":
                {
                    var url = args["url"]?.GetValue<string>() ?? throw new ArgumentException("url required");
                    var type = args["type"]?.GetValue<string>() ?? "agent-skill";
                    var urlReq = new IngestSkillFromUrlRequest { Url = url, Type = type };
                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerCompendium/1.0");
                    var response = await book.IngestSkillFromUrlAsync(urlReq, scope, userId, displayName, httpClient);
                    resultJson = JsonSerializer.Serialize(response, JsonOpts);
                    break;
                }

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return JsonRpcSuccess(requestId, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = resultJson }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return JsonRpcSuccess(requestId, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ── JSON-RPC Helpers ──

    private static string JsonRpcSuccess(JsonNode? id, JsonObject result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id?.DeepClone()
        };
        return response.ToJsonString();
    }

    private static string JsonRpcError(JsonNode? id, int code, string message, string? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id?.DeepClone()
        };
        return response.ToJsonString();
    }

    // ── Tool Definition Helpers ──

    private static JsonObject Tool(string name, string description,
        JsonObject properties, params string[] required)
    {
        var tool = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(r => JsonValue.Create(r)).ToArray())
            }
        };
        return tool;
    }

    // Factory methods that create a fresh JsonObject each time (System.Text.Json.Nodes
    // objects can only have one parent, so they can't be reused across tools)
    private static JsonObject ScopeSchema() => new()
    {
        ["type"] = "string",
        ["description"] = "book scope: org (shared) or personal (private). Default: org",
        ["enum"] = new JsonArray("org", "personal")
    };

    private static JsonObject QueryScopeSchema() => new()
    {
        ["type"] = "string",
        ["description"] = "book scope: org, personal, or all. Default: org",
        ["enum"] = new JsonArray("org", "personal", "all")
    };

    private static JsonObject Props(params (string name, string type, string desc)[] props)
    {
        var obj = new JsonObject();
        foreach (var (name, type, desc) in props)
        {
            obj[name] = new JsonObject { ["type"] = type, ["description"] = desc };
        }
        return obj;
    }

    private static JsonObject Props(params object[] mixed)
    {
        var obj = new JsonObject();
        foreach (var item in mixed)
        {
            if (item is (string name, string type, string desc))
            {
                obj[name] = new JsonObject { ["type"] = type, ["description"] = desc };
            }
            else if (item is (string sName, JsonObject schema))
            {
                obj[sName] = schema;
            }
        }
        return obj;
    }
}
