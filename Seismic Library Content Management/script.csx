using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string SERVER_NAME = "seismic-library-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2024-11-05";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync().ConfigureAwait(false);
            case "AddFile":
                return await HandleAddFile().ConfigureAwait(false);
            case "AddFileVersion":
                return await HandleAddFileVersion().ConfigureAwait(false);
            case "DownloadFile":
            case "DownloadFileVersion":
                return await HandleDownloadFile().ConfigureAwait(false);
            default:
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Download Handler
    // ═══════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleDownloadFile()
    {
        // Ensure redirect=false so we get JSON with downloadUrl
        var uri = this.Context.Request.RequestUri;
        var uriStr = uri.ToString();
        if (!uriStr.Contains("redirect="))
        {
            uriStr += (uriStr.Contains("?") ? "&" : "?") + "redirect=false";
            this.Context.Request.RequestUri = new Uri(uriStr);
        }
        else
        {
            uriStr = System.Text.RegularExpressions.Regex.Replace(uriStr, @"redirect=(true|false)", "redirect=false");
            this.Context.Request.RequestUri = new Uri(uriStr);
        }

        // Get the download URL from Seismic
        var urlResponse = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        var urlBody = await urlResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!urlResponse.IsSuccessStatusCode)
        {
            return urlResponse;
        }

        var urlJson = JObject.Parse(urlBody);
        var downloadUrl = urlJson.Value<string>("downloadUrl");

        if (string.IsNullOrEmpty(downloadUrl))
        {
            return urlResponse;
        }

        // Fetch the actual file content from the download URL
        try
        {
            var fileRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            var fileResponse = await this.Context.SendAsync(fileRequest, this.CancellationToken).ConfigureAwait(false);

            if (!fileResponse.IsSuccessStatusCode)
            {
                // Return just the URL if the download fails
                return urlResponse;
            }

            var fileBytes = await fileResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentBase64 = Convert.ToBase64String(fileBytes);
            var contentType = fileResponse.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            var result = new JObject
            {
                ["downloadUrl"] = downloadUrl,
                ["contentType"] = contentType,
                ["contentLength"] = fileBytes.Length,
                ["content"] = contentBase64
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(result.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
        }
        catch
        {
            // If download fails for any reason, fall back to just the URL
            return urlResponse;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  MCP Handler
    // ═══════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = JObject.Parse(body);

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
                return CreateJsonRpcSuccess(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCall(@params, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccess(requestId, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccess(requestId, new JObject());

            default:
                return CreateJsonRpcError(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken id)
    {
        var result = new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION
            }
        };
        return CreateJsonRpcSuccess(id, result);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var tools = new JArray
        {
            MakeTool("list_teamsites", "List all teamsites in the tenant", new JObject()),
            MakeTool("get_teamsite", "Get details of a specific teamsite", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" }
            }, new JArray { "teamsiteId" }),
            MakeTool("list_folder_items", "List files, folders, and URLs in a folder", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["folderId"] = new JObject { ["type"] = "string", ["description"] = "Folder ID (use 'root' for root)" }
            }, new JArray { "teamsiteId", "folderId" }),
            MakeTool("get_file_info", "Get metadata and properties for a file", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["fileId"] = new JObject { ["type"] = "string", ["description"] = "File ID" }
            }, new JArray { "teamsiteId", "fileId" }),
            MakeTool("get_folder_info", "Get metadata for a folder", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["folderId"] = new JObject { ["type"] = "string", ["description"] = "Folder ID" }
            }, new JArray { "teamsiteId", "folderId" }),
            MakeTool("query_items", "Search for items by name, type, or modification date", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["name"] = new JObject { ["type"] = "string", ["description"] = "Filter by item name" },
                ["type"] = new JObject { ["type"] = "string", ["description"] = "Filter by type: file, folder, or url" }
            }, new JArray { "teamsiteId" }),
            MakeTool("download_file", "Get the download URL for a file", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["fileId"] = new JObject { ["type"] = "string", ["description"] = "File ID" }
            }, new JArray { "teamsiteId", "fileId" }),
            MakeTool("create_folder", "Create a new folder in a teamsite", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["name"] = new JObject { ["type"] = "string", ["description"] = "Folder name" },
                ["parentFolderId"] = new JObject { ["type"] = "string", ["description"] = "Parent folder ID (use 'root' for root)" }
            }, new JArray { "teamsiteId", "name", "parentFolderId" }),
            MakeTool("delete_item", "Delete a file, folder, or URL from the library", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["itemId"] = new JObject { ["type"] = "string", ["description"] = "Item ID to delete" }
            }, new JArray { "teamsiteId", "itemId" }),
            MakeTool("update_file_info", "Update a file's name, description, owner, or folder", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["fileId"] = new JObject { ["type"] = "string", ["description"] = "File ID" },
                ["name"] = new JObject { ["type"] = "string", ["description"] = "New file name" },
                ["parentFolderId"] = new JObject { ["type"] = "string", ["description"] = "New parent folder ID" },
                ["description"] = new JObject { ["type"] = "string", ["description"] = "New description" }
            }, new JArray { "teamsiteId", "fileId" }),
            MakeTool("get_url_info", "Get metadata for a URL item", new JObject
            {
                ["teamsiteId"] = new JObject { ["type"] = "string", ["description"] = "Teamsite ID" },
                ["urlItemId"] = new JObject { ["type"] = "string", ["description"] = "URL item ID" }
            }, new JArray { "teamsiteId", "urlItemId" })
        };
        return CreateJsonRpcSuccess(id, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken id)
    {
        var toolName = @params["name"]?.ToString();
        var args = @params["arguments"] as JObject ?? new JObject();

        try
        {
            HttpResponseMessage apiResponse;
            var baseUrl = "https://api.seismic.com/integration/v2";

            switch (toolName)
            {
                case "list_teamsites":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites").ConfigureAwait(false);
                    break;
                case "get_teamsite":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}").ConfigureAwait(false);
                    break;
                case "list_folder_items":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/folders/{args["folderId"]}/items?limit=100&includeProperties=true").ConfigureAwait(false);
                    break;
                case "get_file_info":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/files/{args["fileId"]}").ConfigureAwait(false);
                    break;
                case "get_folder_info":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/folders/{args["folderId"]}").ConfigureAwait(false);
                    break;
                case "query_items":
                {
                    var qs = "?limit=100";
                    if (args["name"] != null) qs += $"&name={Uri.EscapeDataString(args["name"].ToString())}";
                    if (args["type"] != null) qs += $"&type={Uri.EscapeDataString(args["type"].ToString())}";
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/items{qs}").ConfigureAwait(false);
                    break;
                }
                case "download_file":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/files/{args["fileId"]}/content?redirect=false").ConfigureAwait(false);
                    break;
                case "create_folder":
                {
                    var folderBody = new JObject
                    {
                        ["name"] = args["name"],
                        ["parentFolderId"] = args["parentFolderId"]
                    };
                    apiResponse = await CallApi("POST", $"{baseUrl}/teamsites/{args["teamsiteId"]}/folders", folderBody).ConfigureAwait(false);
                    break;
                }
                case "delete_item":
                    apiResponse = await CallApi("DELETE", $"{baseUrl}/teamsites/{args["teamsiteId"]}/items/{args["itemId"]}").ConfigureAwait(false);
                    break;
                case "update_file_info":
                {
                    var patchBody = new JObject();
                    if (args["name"] != null) patchBody["name"] = args["name"];
                    if (args["parentFolderId"] != null) patchBody["parentFolderId"] = args["parentFolderId"];
                    if (args["description"] != null) patchBody["description"] = args["description"];
                    apiResponse = await CallApi("PATCH", $"{baseUrl}/teamsites/{args["teamsiteId"]}/files/{args["fileId"]}", patchBody).ConfigureAwait(false);
                    break;
                }
                case "get_url_info":
                    apiResponse = await CallApi("GET", $"{baseUrl}/teamsites/{args["teamsiteId"]}/urls/{args["urlItemId"]}").ConfigureAwait(false);
                    break;
                default:
                    return CreateToolError(id, $"Unknown tool: {toolName}");
            }

            var responseBody = await apiResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!apiResponse.IsSuccessStatusCode)
            {
                return CreateToolError(id, $"API returned {(int)apiResponse.StatusCode}: {responseBody}");
            }

            return CreateJsonRpcSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = responseBody }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateToolError(id, $"Tool execution failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> CallApi(string method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        foreach (var header in this.Context.Request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }
        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MCP Helpers
    // ═══════════════════════════════════════════════════════════════

    private static JObject MakeTool(string name, string description, JObject properties, JArray required = null)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required != null && required.Count > 0) schema["required"] = required;
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    }

    private HttpResponseMessage CreateJsonRpcSuccess(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateToolError(JToken id, string message)
    {
        return CreateJsonRpcSuccess(id, new JObject
        {
            ["content"] = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = message }
            },
            ["isError"] = true
        });
    }

    private async Task<HttpResponseMessage> HandleAddFile()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var json = JObject.Parse(body);

        var name = json.Value<string>("name");
        var parentFolderId = json.Value<string>("parentFolderId");
        var format = json.Value<string>("format");
        var contentBase64 = json.Value<string>("content");
        var description = json.Value<string>("description");
        var externalId = json.Value<string>("externalId");

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parentFolderId) ||
            string.IsNullOrEmpty(format) || string.IsNullOrEmpty(contentBase64))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"name, parentFolderId, format, and content are required.\"}",
                    Encoding.UTF8, "application/json")
            };
        }

        var metadata = new JObject
        {
            ["name"] = name,
            ["parentFolderId"] = parentFolderId,
            ["format"] = format
        };
        if (!string.IsNullOrEmpty(description)) metadata["description"] = description;
        if (!string.IsNullOrEmpty(externalId)) metadata["externalId"] = externalId;

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"content must be a valid base64-encoded string.\"}",
                    Encoding.UTF8, "application/json")
            };
        }

        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(metadata.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"), "metadata");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "content", name + "." + format);

        this.Context.Request.Content = multipart;
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleAddFileVersion()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var json = JObject.Parse(body);

        var contentBase64 = json.Value<string>("content");

        if (string.IsNullOrEmpty(contentBase64))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"content is required.\"}",
                    Encoding.UTF8, "application/json")
            };
        }

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"content must be a valid base64-encoded string.\"}",
                    Encoding.UTF8, "application/json")
            };
        }

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "content", "file");

        this.Context.Request.Content = multipart;
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }
}
