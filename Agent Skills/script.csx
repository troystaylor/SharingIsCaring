using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Agent Skills connector: universal agentskills.io bridge for Copilot Studio.
/// Implements progressive disclosure: discover repos → list skills (metadata) → load full content.
/// MCP tools: discover_repositories, list_skills, get_skill, search_skills, get_curated_registry
/// </summary>
public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string GITHUB_API = "https://api.github.com";

    // Curated registry focused on skills useful in Power Automate and Copilot Studio.
    // Criteria: guidance/knowledge-based (no terminal/file system required),
    // relevant to low-code agent building, API design, data transformation, or communication.
    private static readonly Dictionary<string, RegistryEntry> CURATED_REGISTRY = new Dictionary<string, RegistryEntry>
    {
        ["dotnet/skills"] = new RegistryEntry
        {
            Description = "Microsoft .NET AI/ML technology selection, architecture patterns, and best practices. The dotnet-ai plugin is especially useful — covers LLM integration, RAG pipelines, agentic workflows, and technology decision frameworks without requiring code execution.",
            SkillsPaths = new[] { "plugins/dotnet-ai/skills", "plugins/dotnet-aspnetcore/skills", "plugins/dotnet-data/skills" },
            Topics = new[] { "agent-skills", "dotnet", "ai", "architecture" },
            Stars = 4300,
            Category = "Architecture & AI Patterns"
        },
        ["softaworks/agent-toolkit"] = new RegistryEntry
        {
            Description = "Professional skills for communication, requirements gathering, database design, API schemas, and documentation — all guidance-based, no code execution needed. Directly applicable to Copilot Studio agent design and Power Automate planning.",
            SkillsPaths = new[] { "skills" },
            Topics = new[] { "agent-skills", "professional", "communication", "requirements" },
            Stars = 2156,
            Category = "Business & Communication",
            RecommendedSkills = new[] { "professional-communication", "requirements-clarity", "database-schema-designer", "humanizer", "writing-clearly-and-concisely", "difficult-workplace-conversations", "feedback-mastery" }
        },
        ["mcp-use/mcp-use"] = new RegistryEntry
        {
            Description = "OpenAPI-to-MCP conversion skill — guidance for turning REST API specs into MCP tool definitions. Directly relevant to building Copilot Studio connectors and MCP servers.",
            SkillsPaths = new[] { "skills" },
            Topics = new[] { "agent-skills", "mcp", "openapi", "connectors" },
            Stars = 8500,
            Category = "Connector & API Design"
        },
        ["Forward-Future/loopy"] = new RegistryEntry
        {
            Description = "Library of practical AI agent loop patterns — repeatable workflow designs for multi-step reasoning, iteration, and orchestration. Useful for designing Power Automate agent loops and Copilot Studio topic flows.",
            SkillsPaths = new[] { "skills" },
            Topics = new[] { "agent-skills", "workflows", "orchestration", "patterns" },
            Stars = 2532,
            Category = "Workflow Patterns"
        },
        ["elastic/kibana"] = new RegistryEntry
        {
            Description = "OpenAPI specification improvement skill — guidance for writing clear, complete API specs. Applicable to Power Platform custom connector OpenAPI definitions.",
            SkillsPaths = new[] { ".agents/skills" },
            Topics = new[] { "agent-skills", "openapi", "documentation" },
            Stars = 20000,
            Category = "Connector & API Design",
            RecommendedSkills = new[] { "improve-oas" }
        },
        ["anthropics/skills"] = new RegistryEntry
        {
            Description = "Anthropic's reference skills — templates and examples for the agentskills.io standard. Useful for understanding skill structure and best practices.",
            SkillsPaths = new[] { "skills" },
            Topics = new[] { "agent-skills", "reference", "templates" },
            Stars = 159000,
            Category = "Reference & Templates"
        }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        switch (operationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync().ConfigureAwait(false);
            case "DiscoverRepositories":
                return await HandleDiscoverRepositoriesAsync().ConfigureAwait(false);
            case "ListSkills":
                return await HandleListSkillsAsync().ConfigureAwait(false);
            case "GetSkill":
                return await HandleGetSkillAsync().ConfigureAwait(false);
            case "SearchSkills":
                return await HandleSearchSkillsAsync().ConfigureAwait(false);
            case "GetRepoTopics":
                return await ForwardToGitHub().ConfigureAwait(false);
            default:
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
                        ["name"] = "Agent Skills MCP Server",
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
                ["name"] = "get_curated_registry",
                ["description"] = "Get the curated registry of known agentskills.io-compliant repositories. Returns repository names, descriptions, available skill paths, and topics. Use this first to discover what skill sources are available.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "discover_repositories",
                ["description"] = "Search GitHub for repositories containing Agent Skills. Use to find skill repositories beyond the curated registry. Searches by topic 'agent-skills' or custom keywords.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search query (e.g., 'agent-skills', 'python testing skills', 'kubernetes deployment')"
                        }
                    },
                    ["required"] = new JArray { "query" }
                }
            },
            new JObject
            {
                ["name"] = "list_skills",
                ["description"] = "List all skills in a repository at a given path. Returns skill names (metadata-level discovery per agentskills.io progressive disclosure).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["owner"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository owner (e.g., dotnet, anthropics)"
                        },
                        ["repo"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository name (e.g., skills, boost)"
                        },
                        ["skills_path"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Path to skills directory (e.g., plugins/dotnet-ai/skills, .github/skills)"
                        }
                    },
                    ["required"] = new JArray { "owner", "repo", "skills_path" }
                }
            },
            new JObject
            {
                ["name"] = "get_skill",
                ["description"] = "Load the full SKILL.md content for a specific skill. This is the activation step in agentskills.io progressive disclosure — returns complete instructions, workflows, code examples, and validation checklists.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["owner"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository owner"
                        },
                        ["repo"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository name"
                        },
                        ["skill_path"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Full path to skill directory (e.g., plugins/dotnet-ai/skills/technology-selection)"
                        }
                    },
                    ["required"] = new JArray { "owner", "repo", "skill_path" }
                }
            },
            new JObject
            {
                ["name"] = "search_skills",
                ["description"] = "Search for Agent Skills across all of GitHub by keyword. Finds SKILL.md files matching your query from any repository following the agentskills.io standard.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search keywords (e.g., 'PDF processing', 'database migration', 'CI/CD deployment')"
                        }
                    },
                    ["required"] = new JArray { "query" }
                }
            },
            new JObject
            {
                ["name"] = "get_skill_metadata",
                ["description"] = "Get only the YAML frontmatter (name, description, license, compatibility) from a SKILL.md without loading the full body. Useful for quick triage before committing to full skill loading.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["owner"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository owner"
                        },
                        ["repo"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Repository name"
                        },
                        ["skill_path"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Full path to skill directory"
                        }
                    },
                    ["required"] = new JArray { "owner", "repo", "skill_path" }
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
                case "get_curated_registry":
                    resultText = GetCuratedRegistryJson();
                    break;

                case "discover_repositories":
                    var dQuery = args["query"]?.ToString();
                    if (string.IsNullOrEmpty(dQuery))
                        return CreateMcpError(id, -32602, "Missing required parameter: query");
                    resultText = await CallDiscoverRepositoriesAsync(dQuery).ConfigureAwait(false);
                    break;

                case "list_skills":
                    var lOwner = args["owner"]?.ToString();
                    var lRepo = args["repo"]?.ToString();
                    var lPath = args["skills_path"]?.ToString();
                    if (string.IsNullOrEmpty(lOwner) || string.IsNullOrEmpty(lRepo) || string.IsNullOrEmpty(lPath))
                        return CreateMcpError(id, -32602, "Missing required parameters: owner, repo, skills_path");
                    resultText = await CallListSkillsAsync(lOwner, lRepo, lPath).ConfigureAwait(false);
                    break;

                case "get_skill":
                    var gOwner = args["owner"]?.ToString();
                    var gRepo = args["repo"]?.ToString();
                    var gPath = args["skill_path"]?.ToString();
                    if (string.IsNullOrEmpty(gOwner) || string.IsNullOrEmpty(gRepo) || string.IsNullOrEmpty(gPath))
                        return CreateMcpError(id, -32602, "Missing required parameters: owner, repo, skill_path");
                    resultText = await CallGetSkillAsync(gOwner, gRepo, gPath).ConfigureAwait(false);
                    break;

                case "search_skills":
                    var sQuery = args["query"]?.ToString();
                    if (string.IsNullOrEmpty(sQuery))
                        return CreateMcpError(id, -32602, "Missing required parameter: query");
                    resultText = await CallSearchSkillsAsync(sQuery).ConfigureAwait(false);
                    break;

                case "get_skill_metadata":
                    var mOwner = args["owner"]?.ToString();
                    var mRepo = args["repo"]?.ToString();
                    var mPath = args["skill_path"]?.ToString();
                    if (string.IsNullOrEmpty(mOwner) || string.IsNullOrEmpty(mRepo) || string.IsNullOrEmpty(mPath))
                        return CreateMcpError(id, -32602, "Missing required parameters: owner, repo, skill_path");
                    resultText = await CallGetSkillMetadataAsync(mOwner, mRepo, mPath).ConfigureAwait(false);
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

    private string GetCuratedRegistryJson()
    {
        var entries = CURATED_REGISTRY.Select(kvp => new
        {
            repository = kvp.Key,
            description = kvp.Value.Description,
            category = kvp.Value.Category,
            skills_paths = kvp.Value.SkillsPaths,
            recommended_skills = kvp.Value.RecommendedSkills,
            topics = kvp.Value.Topics,
            stars = kvp.Value.Stars
        }).ToList();

        return JsonConvert.SerializeObject(new
        {
            standard = "agentskills.io",
            focus = "Skills useful in Power Automate and Copilot Studio — guidance-based content that does not require terminal, file system, or IDE access.",
            categories = new[] { "Architecture & AI Patterns", "Business & Communication", "Connector & API Design", "Workflow Patterns", "Reference & Templates" },
            note = "Use discover_repositories to find more. Use search_skills for cross-repo keyword search.",
            repositories = entries
        });
    }

    private async Task<string> CallDiscoverRepositoriesAsync(string query)
    {
        // Add topic:agent-skills if not already scoped
        var scopedQuery = query.Contains("topic:") ? query : $"topic:agent-skills {query}";
        var url = $"{GITHUB_API}/search/repositories?q={Uri.EscapeDataString(scopedQuery)}&sort=stars&per_page=15";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);

        var items = (result["items"] as JArray)?
            .Select(i => new
            {
                full_name = i["full_name"]?.ToString(),
                description = i["description"]?.ToString(),
                stars = i["stargazers_count"]?.Value<int>() ?? 0,
                updated = i["updated_at"]?.ToString(),
                html_url = i["html_url"]?.ToString()
            })
            .ToList();

        return JsonConvert.SerializeObject(new
        {
            total_count = result["total_count"]?.Value<int>() ?? 0,
            repositories = items
        });
    }

    private async Task<string> CallListSkillsAsync(string owner, string repo, string skillsPath)
    {
        var url = $"{GITHUB_API}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{skillsPath}?ref=main";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var dirs = (result as JArray)?
            .Where(i => i["type"]?.ToString() == "dir")
            .Select(i => new
            {
                name = i["name"]?.ToString(),
                path = i["path"]?.ToString()
            })
            .ToList() ?? new List<object>();

        return JsonConvert.SerializeObject(new
        {
            repository = $"{owner}/{repo}",
            skills_path = skillsPath,
            skills = dirs,
            count = dirs.Count
        });
    }

    private async Task<string> CallGetSkillAsync(string owner, string repo, string skillPath)
    {
        var url = $"{GITHUB_API}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{skillPath}/SKILL.md?ref=main";
        var result = await SendGitHubRequest(HttpMethod.Get, url).ConfigureAwait(false);
        var content = result["content"]?.ToString() ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
        return decoded;
    }

    private async Task<string> CallGetSkillMetadataAsync(string owner, string repo, string skillPath)
    {
        // Fetch full file then extract only frontmatter
        var fullContent = await CallGetSkillAsync(owner, repo, skillPath).ConfigureAwait(false);

        // Parse YAML frontmatter between --- markers
        var frontmatterMatch = Regex.Match(fullContent, @"^---\s*\n(.*?)\n---", RegexOptions.Singleline);
        if (!frontmatterMatch.Success)
        {
            return JsonConvert.SerializeObject(new
            {
                error = "No YAML frontmatter found",
                repository = $"{owner}/{repo}",
                skill_path = skillPath
            });
        }

        var frontmatter = frontmatterMatch.Groups[1].Value;

        // Extract key fields from YAML (simple parsing without YAML library)
        var name = ExtractYamlField(frontmatter, "name");
        var description = ExtractYamlField(frontmatter, "description");
        var license = ExtractYamlField(frontmatter, "license");
        var compatibility = ExtractYamlField(frontmatter, "compatibility");

        return JsonConvert.SerializeObject(new
        {
            repository = $"{owner}/{repo}",
            skill_path = skillPath,
            name = name,
            description = description,
            license = license,
            compatibility = compatibility,
            body_lines = fullContent.Split('\n').Length - frontmatterMatch.Value.Split('\n').Length
        });
    }

    private async Task<string> CallSearchSkillsAsync(string query)
    {
        var searchQuery = Uri.EscapeDataString($"{query} filename:SKILL.md");
        var url = $"{GITHUB_API}/search/code?q={searchQuery}&per_page=15";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "PowerPlatform-AgentSkills/1.0");
        request.Headers.Add("Accept", "application/vnd.github.text-match+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseBody);

        var items = (result["items"] as JArray)?
            .Select(i => new
            {
                repository = i["repository"]?["full_name"]?.ToString(),
                path = i["path"]?.ToString(),
                html_url = i["html_url"]?.ToString(),
                fragments = (i["text_matches"] as JArray)?
                    .Select(tm => tm["fragment"]?.ToString())
                    .Where(f => f != null)
                    .Take(2)
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

    private async Task<HttpResponseMessage> HandleDiscoverRepositoriesAsync()
    {
        var uri = this.Context.Request.RequestUri;
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var userQuery = queryParams["q"] ?? "agent-skills";

        // Auto-scope to topic if not already
        if (!userQuery.Contains("topic:"))
        {
            userQuery = $"topic:agent-skills {userQuery}";
            queryParams["q"] = userQuery;
        }

        var newUri = new UriBuilder(uri) { Query = queryParams.ToString() }.Uri;
        this.Context.Request.RequestUri = newUri;

        return await ForwardToGitHub().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListSkillsAsync()
    {
        // Forward and filter to directories only
        var response = await ForwardToGitHub().ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        try
        {
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
        catch
        {
            return response;
        }
    }

    private async Task<HttpResponseMessage> HandleGetSkillAsync()
    {
        // Forward to GitHub, decode base64 content
        var response = await ForwardToGitHub().ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        try
        {
            var file = JObject.Parse(responseBody);
            var content = file["content"]?.ToString() ?? "";
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));

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
        catch
        {
            return response;
        }
    }

    private async Task<HttpResponseMessage> HandleSearchSkillsAsync()
    {
        var uri = this.Context.Request.RequestUri;
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var userQuery = queryParams["q"] ?? "";

        // Scope to SKILL.md files
        queryParams["q"] = $"{userQuery} filename:SKILL.md";

        var newUri = new UriBuilder(uri) { Query = queryParams.ToString() }.Uri;
        this.Context.Request.RequestUri = newUri;
        this.Context.Request.Headers.Remove("Accept");
        this.Context.Request.Headers.Add("Accept", "application/vnd.github.text-match+json");

        return await ForwardToGitHub().ConfigureAwait(false);
    }

    // ==================== GITHUB API HELPERS ====================

    private async Task<HttpResponseMessage> ForwardToGitHub()
    {
        var request = this.Context.Request;
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.Add("User-Agent", "PowerPlatform-AgentSkills/1.0");
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
        request.Headers.Add("User-Agent", "PowerPlatform-AgentSkills/1.0");
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

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

    private string ExtractYamlField(string yaml, string field)
    {
        var match = Regex.Match(yaml, $@"^{field}:\s*[""']?(.+?)[""']?\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
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

            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    // ==================== DATA TYPES ====================

    private class RegistryEntry
    {
        public string Description { get; set; }
        public string[] SkillsPaths { get; set; }
        public string[] Topics { get; set; }
        public int Stars { get; set; }
        public string Category { get; set; }
        public string[] RecommendedSkills { get; set; }
    }
}
