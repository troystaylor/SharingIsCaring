using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Connection parameter header injected by setheader policy
    private const string FunctionHostHeader = "X-Book-Function-Host";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync();

            case "IngestSource":
            case "QueryBook":
            case "LintBook":
            case "ListPages":
            case "ReadPage":
            case "WritePage":
            case "DeletePage":
            case "PromotePage":
                return await ForwardToFunctionAsync();

            default:
                return await Context.SendAsync(Context.Request, CancellationToken);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // REST forwarding — routes requests to the Azure Function App
    // ─────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> ForwardToFunctionAsync()
    {
        var functionHost = GetFunctionHost();
        if (string.IsNullOrEmpty(functionHost))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest,
                "Function App host not configured. Set the Function App Host connection parameter.");
        }

        var originalUri = Context.Request.RequestUri;
        var builder = new UriBuilder(originalUri)
        {
            Host = functionHost,
            Scheme = "https",
            Port = 443
        };
        Context.Request.RequestUri = builder.Uri;

        // Remove the injected header before forwarding
        Context.Request.Headers.Remove(FunctionHostHeader);

        return await Context.SendAsync(Context.Request, CancellationToken);
    }

    private string GetFunctionHost()
    {
        IEnumerable<string> values;
        if (Context.Request.Headers.TryGetValues(FunctionHostHeader, out values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // MCP Protocol Handler
    // ─────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var body = await Context.Request.Content.ReadAsStringAsync();

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error");
        }

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        switch (method)
        {
            case "initialize":
                return HandleInitialize(requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCall(@params, requestId);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId,
                    new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MCP: initialize
    // ─────────────────────────────────────────────────────────────

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "power-compendium-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ─────────────────────────────────────────────────────────────
    // MCP: tools/list — 8 tools
    // ─────────────────────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var scopeParam = new JObject { ["type"] = "string", ["description"] = "Book scope: org (shared organizational book) or personal (your private book). Default: org", ["enum"] = new JArray("org", "personal") };
        var scopeQueryParam = new JObject { ["type"] = "string", ["description"] = "Book scope to search: org, personal, or all (searches both). Default: org", ["enum"] = new JArray("org", "personal", "all") };

        var tools = new JArray
        {
            CreateTool("ingest_source",
                "Process a source document and integrate its knowledge into the book. Creates or updates book pages with extracted entities, concepts, and cross-references.",
                new JObject
                {
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "Title of the source document" },
                    ["content"] = new JObject { ["type"] = "string", ["description"] = "Full text content of the source to ingest" },
                    ["sourceUrl"] = new JObject { ["type"] = "string", ["description"] = "URL of the original source for citation tracking" },
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "Category: article, paper, meeting-notes, api-docs, book-chapter" },
                    ["scope"] = scopeParam.DeepClone()
                },
                new[] { "content" }),

            CreateTool("query_book",
                "Ask a question against the book. Searches relevant pages, synthesizes an answer with citations, and optionally saves the answer as a new book page.",
                new JObject
                {
                    ["question"] = new JObject { ["type"] = "string", ["description"] = "The question to answer from book knowledge" },
                    ["saveAsPage"] = new JObject { ["type"] = "boolean", ["description"] = "Save the answer as a new book page for future reference. Default: false" },
                    ["scope"] = scopeQueryParam.DeepClone()
                },
                new[] { "question" }),

            CreateTool("lint_book",
                "Health-check the book for contradictions between pages, stale claims, orphan pages with no inbound links, missing cross-references, and knowledge gaps. Returns issues with suggested fixes.",
                new JObject
                {
                    ["lintScope"] = new JObject { ["type"] = "string", ["description"] = "Lint scope: all, recent (last 7 days), or a specific category" },
                    ["scope"] = scopeParam.DeepClone()
                },
                Array.Empty<string>()),

            CreateTool("list_pages",
                "List all book pages with metadata including title, category, last updated date, source count, and cross-reference count.",
                new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "Filter by category: entity, concept, source, comparison, overview" },
                    ["scope"] = scopeQueryParam.DeepClone()
                },
                Array.Empty<string>()),

            CreateTool("read_page",
                "Read the full markdown content and metadata of a specific book page.",
                new JObject
                {
                    ["pageId"] = new JObject { ["type"] = "string", ["description"] = "The unique identifier of the book page to read" },
                    ["scope"] = scopeParam.DeepClone()
                },
                new[] { "pageId" }),

            CreateTool("write_page",
                "Create or update a book page with markdown content and metadata.",
                new JObject
                {
                    ["pageId"] = new JObject { ["type"] = "string", ["description"] = "The unique identifier for the page. Use kebab-case (e.g., azure-functions-overview)" },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "Page title" },
                    ["content"] = new JObject { ["type"] = "string", ["description"] = "Full markdown content for the page" },
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "Page category: entity, concept, source, comparison, overview" },
                    ["scope"] = scopeParam.DeepClone()
                },
                new[] { "pageId", "title", "content" }),

            CreateTool("delete_page",
                "Delete a book page. The page is soft-deleted and can be recovered.",
                new JObject
                {
                    ["pageId"] = new JObject { ["type"] = "string", ["description"] = "The unique identifier of the book page to delete" },
                    ["scope"] = scopeParam.DeepClone()
                },
                new[] { "pageId" }),

            CreateTool("promote_page",
                "Copy a page from your personal book to the shared organizational book. The original personal page is retained. Use this when personal knowledge is ready to be shared with the team.",
                new JObject
                {
                    ["pageId"] = new JObject { ["type"] = "string", ["description"] = "The page ID in your personal book to promote to the org book" }
                },
                new[] { "pageId" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ─────────────────────────────────────────────────────────────
    // MCP: tools/call — route to Azure Function
    // ─────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        try
        {
            var functionHost = GetFunctionHost();
            if (string.IsNullOrEmpty(functionHost))
            {
                throw new InvalidOperationException("Function App host not configured.");
            }

            string path;
            string httpMethod;
            JObject queryParams = null;

            // Extract scope (defaults to "org") — passed as query param to Function App
            var scope = arguments["scope"]?.ToString() ?? "org";

            switch (toolName)
            {
                case "ingest_source":
                    path = "/api/book/ingest";
                    httpMethod = "POST";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "query_book":
                    path = "/api/book/query";
                    httpMethod = "POST";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "lint_book":
                    path = "/api/book/lint";
                    httpMethod = "POST";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "list_pages":
                    path = "/api/book/pages";
                    httpMethod = "GET";
                    queryParams = new JObject { ["scope"] = scope };
                    if (arguments["category"] != null)
                    {
                        queryParams["category"] = arguments["category"];
                    }
                    break;

                case "read_page":
                    var readPageId = arguments["pageId"]?.ToString();
                    if (string.IsNullOrEmpty(readPageId))
                        throw new ArgumentException("pageId is required");
                    path = $"/api/book/pages/{Uri.EscapeDataString(readPageId)}";
                    httpMethod = "GET";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "write_page":
                    var writePageId = arguments["pageId"]?.ToString();
                    if (string.IsNullOrEmpty(writePageId))
                        throw new ArgumentException("pageId is required");
                    path = $"/api/book/pages/{Uri.EscapeDataString(writePageId)}";
                    httpMethod = "PUT";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "delete_page":
                    var deletePageId = arguments["pageId"]?.ToString();
                    if (string.IsNullOrEmpty(deletePageId))
                        throw new ArgumentException("pageId is required");
                    path = $"/api/book/pages/{Uri.EscapeDataString(deletePageId)}";
                    httpMethod = "DELETE";
                    queryParams = new JObject { ["scope"] = scope };
                    break;

                case "promote_page":
                    var promotePageId = arguments["pageId"]?.ToString();
                    if (string.IsNullOrEmpty(promotePageId))
                        throw new ArgumentException("pageId is required");
                    path = $"/api/book/pages/{Uri.EscapeDataString(promotePageId)}/promote";
                    httpMethod = "POST";
                    break;
                    path = $"/api/book/pages/{Uri.EscapeDataString(deletePageId)}";
                    httpMethod = "DELETE";
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            // Build and send the request to the Azure Function
            var result = await CallFunctionAsync(functionHost, path, httpMethod, arguments, queryParams);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Azure Function call helper
    // ─────────────────────────────────────────────────────────────

    private async Task<string> CallFunctionAsync(string host, string path,
        string httpMethod, JObject body, JObject queryParams = null)
    {
        var uriBuilder = new UriBuilder("https", host, 443, path);

        if (queryParams != null && queryParams.HasValues)
        {
            var queryParts = new List<string>();
            foreach (var prop in queryParams.Properties())
            {
                queryParts.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(prop.Value.ToString())}");
            }
            uriBuilder.Query = string.Join("&", queryParts);
        }

        var request = new HttpRequestMessage(new HttpMethod(httpMethod), uriBuilder.Uri);

        // Forward the original Authorization header
        if (Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = Context.Request.Headers.Authorization;
        }

        // Set body for non-GET/DELETE methods
        if (httpMethod != "GET" && httpMethod != "DELETE" && body != null)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        var response = await Context.SendAsync(request, CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Function returned {(int)response.StatusCode}: {responseBody}");
        }

        return responseBody;
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private JObject CreateTool(string name, string description,
        JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code,
        string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = (int)statusCode,
                ["message"] = message
            }
        };
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                error.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }
}