using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// .NET Skills connector: dual-mode MCP + typed operations for dotnet/skills repository.
/// MCP tools: list_plugins, list_skills, get_skill, search_skills
/// Typed ops: ListPlugins, GetPluginDetails, ListSkills, GetSkill, SearchSkills
/// </summary>
public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string GITHUB_API = "https://api.github.com";
    private const string REPO_OWNER = "dotnet";
    private const string REPO_NAME = "skills";
    private const string DEFAULT_BRANCH = "main";
    private const string PLUGINS_PATH = "plugins";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        switch (operationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync().ConfigureAwait(false);
            case "ListPlugins":
                return await HandleListPluginsAsync().ConfigureAwait(false);
            case "GetPluginDetails":
                return await HandleGetPluginDetailsAsync().ConfigureAwait(false);
            case "SearchSkills":
                return await HandleSearchSkillsAsync().ConfigureAwait(false);
            case "GetSkill":
                return await HandleGetSkillAsync().ConfigureAwait(false);
            default:
                // Pass through for ListSkills (no transformation needed)
                return await ForwardToGitHub().ConfigureAwait(false);
        }
    }

    // ==================== MCP HANDLER ====================

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString() ?? "";
        var id = request["id"];

        switch (method)
        {
            case "initialize":
                return CreateMcpResponse(id, new JObject
                {
                    ["protocolVersion"] = "2025-03-26",
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = ".NET Skills MCP Server",
                        ["version"] = "1.0.0"
                    }
                });

            case "notifications/initialized":
                return CreateMcpResponse(id, new JObject());

            case "tools/list":
                return CreateMcpResponse(id, new JObject
                {
                    ["tools"] = GetToolDefinitions()
                });

            case "tools/call":
                return await HandleToolCallAsync(request, id).ConfigureAwait(false);

            case "ping":
                return CreateMcpResponse(id, new JObject());

            default:
                return CreateMcpError(id, -32601, $"Method not found: {method}");
        }
    }

    private JArray GetToolDefinitions()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "list_plugins",
                ["description"] = "List all available .NET skill plugins. Returns plugin names like dotnet, dotnet-ai, dotnet-msbuild, dotnet-nuget, dotnet-test, dotnet-upgrade, dotnet-aspnetcore, dotnet-blazor, dotnet-maui, dotnet-diag, dotnet-data, dotnet-template-engine, dotnet11.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "list_skills",
                ["description"] = "List all skills within a specific plugin. Each skill is a directory containing a SKILL.md file with structured .NET development guidance.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["plugin"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Plugin name (e.g., dotnet-ai, dotnet-msbuild, dotnet-test)"
                        }
                    },
                    ["required"] = new JArray { "plugin" }
                }
            },
            new JObject
            {
                ["name"] = "get_skill",
                ["description"] = "Get the full content of a specific skill. Returns detailed .NET development guidance including workflows, code examples, validation checklists, and anti-patterns.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["plugin"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Plugin name (e.g., dotnet-ai, dotnet-msbuild)"
                        },
                        ["skill"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Skill name (e.g., technology-selection, binlog-failure-analysis)"
                        }
                    },
                    ["required"] = new JArray { "plugin", "skill" }
                }
            },
            new JObject
            {
                ["name"] = "search_skills",
                ["description"] = "Search across all .NET skills for guidance on a specific topic. Returns matching skill files with text fragments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search query (e.g., 'dependency injection', 'EF Core migration', 'MSBuild performance')"
                        }
                    },
                    ["required"] = new JArray { "query" }
                }
            },
            new JObject
            {
                ["name"] = "get_plugin_details",
                ["description"] = "Get the plugin.json manifest for a plugin including its description, version, available skills, agents, and MCP servers.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["plugin"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Plugin name (e.g., dotnet-ai, dotnet-msbuild)"
                        }
                    },
                    ["required"] = new JArray { "plugin" }
                }
            }
        };
    }

    private async Task<HttpResponseMessage> HandleToolCallAsync(JObject request, JToken id)
    {
        var toolName = request["params"]?["name"]?.ToString() ?? "";
        var args = request["params"]?["arguments"] as JObject ?? new JObject();

        try
        {
            string resultText;

            switch (toolName)
            {
                case "list_plugins":
                    resultText = await CallListPluginsAsync().ConfigureAwait(false);
                    break;
                case "list_skills":
                    var plugin = args["plugin"]?.ToString();
                    if (string.IsNullOrEmpty(plugin))
                        return CreateMcpError(id, -32602, "Missing required parameter: plugin");
                    resultText = await CallListSkillsAsync(plugin).ConfigureAwait(false);
                    break;
                case "get_skill":
                    var gPlugin = args["plugin"]?.ToString();
                    var gSkill = args["skill"]?.ToString();
                    if (string.IsNullOrEmpty(gPlugin) || string.IsNullOrEmpty(gSkill))
                        return CreateMcpError(id, -32602, "Missing required parameters: plugin and skill");
                    resultText = await CallGetSkillAsync(gPlugin, gSkill).ConfigureAwait(false);
                    break;
                case "search_skills":
                    var query = args["query"]?.ToString();
                    if (string.IsNullOrEmpty(query))
                        return CreateMcpError(id, -32602, "Missing required parameter: query");
                    resultText = await CallSearchSkillsAsync(query).ConfigureAwait(false);
                    break;
                case "get_plugin_details":
                    var pdPlugin = args["plugin"]?.ToString();
                    if (string.IsNullOrEmpty(pdPlugin))
                        return CreateMcpError(id, -32602, "Missing required parameter: plugin");
                    resultText = await CallGetPluginDetailsAsync(pdPlugin).ConfigureAwait(false);
                    break;
                default:
                    return CreateMcpError(id, -32602, $"Unknown tool: {toolName}");
            }

            LogTelemetry("ToolCall", new Dictionary<string, string>
            {
                { "tool", toolName },
                { "success", "true" }
            });

            return CreateMcpResponse(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = resultText
                    }
                }
            });
        }
        catch (Exception ex)
        {
            LogTelemetry("ToolCallError", new Dictionary<string, string>
            {
                { "tool", toolName },
                { "error", ex.Message }
            });

            return CreateMcpError(id, -32603, $"Tool execution failed: {ex.Message}");
        }
    }

    // ==================== MCP TOOL IMPLEMENTATIONS ====================

    private async Task<string> CallListPluginsAsync()
    {
        var url = $"{GITHUB_API}/repos/{REPO_OWNER}/{REPO_NAME}/contents/{PLUGINS_PATH}?ref={DEFAULT_BRANCH}";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var dirs = (result as JArray)?
            .Where(i => i["type"]?.ToString() == "dir")
            .Select(i => i["name"]?.ToString())
            .Where(n => n != null)
            .ToList() ?? new List<string>();

        return JsonConvert.SerializeObject(new { plugins = dirs, count = dirs.Count });
    }

    private async Task<string> CallListSkillsAsync(string plugin)
    {
        var url = $"{GITHUB_API}/repos/{REPO_OWNER}/{REPO_NAME}/contents/{PLUGINS_PATH}/{Uri.EscapeDataString(plugin)}/skills?ref={DEFAULT_BRANCH}";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var dirs = (result as JArray)?
            .Where(i => i["type"]?.ToString() == "dir")
            .Select(i => i["name"]?.ToString())
            .Where(n => n != null)
            .ToList() ?? new List<string>();

        return JsonConvert.SerializeObject(new { plugin = plugin, skills = dirs, count = dirs.Count });
    }

    private async Task<string> CallGetSkillAsync(string plugin, string skill)
    {
        var url = $"{GITHUB_API}/repos/{REPO_OWNER}/{REPO_NAME}/contents/{PLUGINS_PATH}/{Uri.EscapeDataString(plugin)}/skills/{Uri.EscapeDataString(skill)}/SKILL.md?ref={DEFAULT_BRANCH}";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var content = result["content"]?.ToString() ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
        return decoded;
    }

    private async Task<string> CallGetPluginDetailsAsync(string plugin)
    {
        var url = $"{GITHUB_API}/repos/{REPO_OWNER}/{REPO_NAME}/contents/{PLUGINS_PATH}/{Uri.EscapeDataString(plugin)}/plugin.json?ref={DEFAULT_BRANCH}";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var content = result["content"]?.ToString() ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
        return decoded;
    }

    private async Task<string> CallSearchSkillsAsync(string query)
    {
        var searchQuery = Uri.EscapeDataString($"{query} repo:{REPO_OWNER}/{REPO_NAME} path:{PLUGINS_PATH} filename:SKILL.md");
        var url = $"{GITHUB_API}/search/code?q={searchQuery}";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);

        var items = (result["items"] as JArray)?
            .Select(i => new
            {
                path = i["path"]?.ToString(),
                html_url = i["html_url"]?.ToString(),
                fragments = (i["text_matches"] as JArray)?
                    .Select(tm => tm["fragment"]?.ToString())
                    .Where(f => f != null)
                    .ToList() ?? new List<string>()
            })
            .ToList();

        return JsonConvert.SerializeObject(new
        {
            total_count = result["total_count"]?.Value<int>() ?? 0,
            results = items
        });
    }

    // ==================== TYPED OPERATION HANDLERS ====================

    private async Task<HttpResponseMessage> HandleListPluginsAsync()
    {
        // Forward to GitHub and filter to directories only
        var response = await ForwardToGitHub().ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var items = JArray.Parse(responseBody);

        var dirs = items.Where(i => i["type"]?.ToString() == "dir").ToList();
        var filtered = new JArray(dirs);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                filtered.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task<HttpResponseMessage> HandleGetPluginDetailsAsync()
    {
        // Forward to GitHub, decode base64 content into parsed JSON
        var response = await ForwardToGitHub().ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var file = JObject.Parse(responseBody);

        var content = file["content"]?.ToString() ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));

        // Replace raw base64 with decoded JSON
        file["content"] = decoded;
        file["encoding"] = "utf-8";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                file.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task<HttpResponseMessage> HandleGetSkillAsync()
    {
        // Forward to GitHub, decode base64 content into readable markdown
        var response = await ForwardToGitHub().ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var file = JObject.Parse(responseBody);

        var content = file["content"]?.ToString() ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));

        // Replace raw base64 with decoded markdown
        file["content"] = decoded;
        file["encoding"] = "utf-8";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                file.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task<HttpResponseMessage> HandleSearchSkillsAsync()
    {
        // Append repo scope to user's query
        var originalRequest = this.Context.Request;
        var uri = originalRequest.RequestUri;
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var userQuery = queryParams["q"] ?? "";

        // Scope to dotnet/skills SKILL.md files
        var scopedQuery = $"{userQuery} repo:{REPO_OWNER}/{REPO_NAME} path:{PLUGINS_PATH} filename:SKILL.md";
        queryParams["q"] = scopedQuery;

        var newUri = new UriBuilder(uri)
        {
            Query = queryParams.ToString()
        }.Uri;

        originalRequest.RequestUri = newUri;

        // Add Accept header for text-match fragments
        originalRequest.Headers.Remove("Accept");
        originalRequest.Headers.Add("Accept", "application/vnd.github.text-match+json");

        var response = await this.Context.SendAsync(originalRequest, this.CancellationToken).ConfigureAwait(false);
        return response;
    }

    // ==================== GITHUB API HELPERS ====================

    private async Task<HttpResponseMessage> ForwardToGitHub()
    {
        // Add required GitHub headers
        var request = this.Context.Request;
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.Add("User-Agent", "PowerPlatform-DotNetSkills/1.0");
        }
        if (!request.Headers.Contains("Accept"))
        {
            request.Headers.Add("Accept", "application/vnd.github+json");
        }
        if (!request.Headers.Contains("X-GitHub-Api-Version"))
        {
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JToken> SendGitHubRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("User-Agent", "PowerPlatform-DotNetSkills/1.0");
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        // Copy auth header from original request
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GitHub API returned {(int)response.StatusCode}: {responseBody}");
        }

        return JToken.Parse(responseBody);
    }

    // ==================== MCP RESPONSE HELPERS ====================

    private HttpResponseMessage CreateMcpResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateMcpError(JToken id, int code, string message)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    // ==================== TELEMETRY ====================

    private void LogTelemetry(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = properties != null
                            ? JObject.FromObject(properties)
                            : new JObject()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            request.Content = new StringContent(
                telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");

            // Fire and forget
            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
