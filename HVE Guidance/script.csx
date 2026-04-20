using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string GITHUB_API_BASE = "https://api.github.com";
    private const string SERVER_NAME = "hve-guidance";
    private const string SERVER_VERSION = "1.0.0";
    private static readonly object GitHubCacheLock = new object();
    private static readonly Dictionary<string, CacheEntry> GitHubCache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(2);

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (!string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow;

        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject request;
            try
            {
                request = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return JsonRpcError(null, -32700, "Parse error", ex.Message);
            }

            var method = request["method"]?.ToString();
            var requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            await LogToAppInsights("McpRequestReceived", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Method", method ?? "" }
            }).ConfigureAwait(false);

            switch (method)
            {
                case "initialize":
                    return JsonRpcResult(requestId, BuildInitializeResponse(correlationId));
                case "tools/list":
                    return JsonRpcResult(requestId, new JObject
                    {
                        ["tools"] = BuildToolsList(),
                        ["nextCursor"] = null
                    });
                case "tools/call":
                    return await HandleToolsCallAsync(requestId, @params, correlationId).ConfigureAwait(false);
                default:
                    return JsonRpcError(requestId, -32601, "Method not found", method ?? "");
            }
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex, new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Operation", "InvokeMCP" }
            }).ConfigureAwait(false);

            return JsonRpcError(null, -32603, "Internal error", ex.Message);
        }
        finally
        {
            await LogToAppInsights("McpRequestCompleted", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "DurationMs", (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0") }
            }).ConfigureAwait(false);
        }
    }

    private JObject BuildInitializeResponse(string correlationId)
    {
        return new JObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject
                {
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION
            },
            ["meta"] = new JObject
            {
                ["correlationId"] = correlationId
            }
        };
    }

    private JArray BuildToolsList()
    {
        return new JArray
        {
            Tool("list_assets", "List HVE assets by type or path.", new JObject
            {
                ["type"] = StrProp("Asset type", "agent|instruction|prompt|skill|collection", false),
                ["path"] = StrProp("Repository path override", "Example: .github/instructions", false),
                ["recursive"] = BoolProp("Include nested folders", false),
                ["maxItems"] = IntProp("Maximum items to return", false)
            }),
            Tool("get_asset", "Get a single asset file with metadata and content.", new JObject
            {
                ["path"] = StrProp("Path to file", "Example: .github/instructions/example.instructions.md", true),
                ["ref"] = StrProp("Git ref (branch/tag/sha)", "Default: configured branch", false),
                ["includeRaw"] = BoolProp("Include full raw content", false)
            }),
            Tool("search_assets", "Search assets by keyword.", new JObject
            {
                ["query"] = StrProp("Search query", "Keywords for GitHub code search", true),
                ["pathPrefix"] = StrProp("Restrict search to path", "Example: .github/skills", false),
                ["maxItems"] = IntProp("Maximum results", false)
            }),
            Tool("recommend_assets_for_task", "Recommend assets for a requested engineering task.", new JObject
            {
                ["task"] = StrProp("User task description", "Example: add code review standards", true),
                ["assetType"] = StrProp("Optional preferred type", "agent|instruction|prompt|skill|collection", false),
                ["maxItems"] = IntProp("Maximum recommendations", false)
            }),
            Tool("get_workflow_for_scenario", "Return a suggested workflow for the scenario.", new JObject
            {
                ["scenario"] = StrProp("Scenario description", "Example: refactor a legacy service", true),
                ["timelineWeeks"] = IntProp("Planned timeline in weeks", false)
            }),
            Tool("validate_instruction", "Validate instruction markdown quality and structure.", new JObject
            {
                ["content"] = StrProp("Instruction content", "Markdown text", true)
            }),
            Tool("validate_prompt", "Validate prompt quality and safety hints.", new JObject
            {
                ["content"] = StrProp("Prompt content", "Prompt text", true)
            }),
            Tool("validate_agent_config", "Validate a JSON agent configuration.", new JObject
            {
                ["configJson"] = StrProp("Agent JSON", "Serialized JSON", true)
            }),
            Tool("summarize_asset_changes", "Summarize recent commits for a path.", new JObject
            {
                ["path"] = StrProp("Path filter", "Example: .github/instructions", false),
                ["days"] = IntProp("Lookback days", false),
                ["maxItems"] = IntProp("Maximum commits", false)
            }),
            Tool("compare_asset_versions", "Compare two refs for a specific file.", new JObject
            {
                ["path"] = StrProp("File path", "Example: .github/instructions/example.instructions.md", true),
                ["baseRef"] = StrProp("Base ref", "Older branch/tag/sha", false),
                ["headRef"] = StrProp("Head ref", "Newer branch/tag/sha", false)
            }),
            Tool("generate_adoption_plan", "Generate an adoption plan for AI engineering standards.", new JObject
            {
                ["teamName"] = StrProp("Team name", "Example: API Platform", true),
                ["goals"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" },
                    ["description"] = "Goals to include in the plan"
                },
                ["timelineWeeks"] = IntProp("Timeline in weeks", false)
            }),
            Tool("get_release_highlights", "Get recent release highlights from the repository.", new JObject
            {
                ["maxItems"] = IntProp("Maximum releases", false),
                ["includePrerelease"] = BoolProp("Include prereleases", false)
            })
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JToken requestId, JObject @params, string correlationId)
    {
        var name = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(name))
            return JsonRpcError(requestId, -32602, "Invalid params", "Tool name is required.");

        try
        {
            JObject payload;
            switch (name)
            {
                case "list_assets":
                    payload = await ListAssetsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "get_asset":
                    payload = await GetAssetAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "search_assets":
                    payload = await SearchAssetsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "recommend_assets_for_task":
                    payload = await RecommendAssetsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "get_workflow_for_scenario":
                    payload = GetWorkflowForScenario(arguments, correlationId);
                    break;
                case "validate_instruction":
                    payload = ValidateInstruction(arguments, correlationId);
                    break;
                case "validate_prompt":
                    payload = ValidatePrompt(arguments, correlationId);
                    break;
                case "validate_agent_config":
                    payload = ValidateAgentConfig(arguments, correlationId);
                    break;
                case "summarize_asset_changes":
                    payload = await SummarizeAssetChangesAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "compare_asset_versions":
                    payload = await CompareAssetVersionsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "generate_adoption_plan":
                    payload = GenerateAdoptionPlan(arguments, correlationId);
                    break;
                case "get_release_highlights":
                    payload = await GetReleaseHighlightsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                default:
                    return JsonRpcResult(requestId, BuildToolResultError("Unknown tool: " + name, correlationId));
            }

            return JsonRpcResult(requestId, BuildToolResultSuccess(payload));
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex, new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Tool", name }
            }).ConfigureAwait(false);

            return JsonRpcResult(requestId, BuildToolResultError(ex, correlationId));
        }
    }

    private async Task<JObject> ListAssetsAsync(JObject args, string correlationId)
    {
        var context = GetRepoContext();
        var type = (args["type"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
        var overridePath = args["path"]?.ToString();
        var recursive = args["recursive"]?.Value<bool>() ?? false;
        var maxItems = Clamp(args["maxItems"]?.Value<int>() ?? 50, 1, 200);

        var seedPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            seedPaths.Add(overridePath.Trim('/'));
        }
        else if (!string.IsNullOrWhiteSpace(type))
        {
            var mapped = MapTypeToPath(type);
            if (!string.IsNullOrWhiteSpace(mapped))
                seedPaths.Add(mapped);
        }
        else
        {
            seedPaths.Add(".github/agents");
            seedPaths.Add(".github/instructions");
            seedPaths.Add(".github/prompts");
            seedPaths.Add(".github/skills");
            seedPaths.Add("collections");
        }

        var files = new JArray();
        foreach (var path in seedPaths)
        {
            if (files.Count >= maxItems)
                break;

            await CollectAssetsAsync(context, path, recursive, files, maxItems).ConfigureAwait(false);
        }

        return new JObject
        {
            ["source"] = "github",
            ["owner"] = context.Owner,
            ["repo"] = context.Repo,
            ["ref"] = context.Ref,
            ["count"] = files.Count,
            ["assets"] = files,
            ["confidence"] = "high",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task CollectAssetsAsync(RepoContext context, string path, bool recursive, JArray collector, int maxItems)
    {
        if (collector.Count >= maxItems)
            return;

        var endpoint = string.Format("/repos/{0}/{1}/contents/{2}?ref={3}",
            UrlEncode(context.Owner), UrlEncode(context.Repo), UrlEncodePath(path), UrlEncode(context.Ref));

        JToken content;
        try
        {
            content = await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        var array = content as JArray;
        if (array == null)
            return;

        foreach (var item in array.OfType<JObject>())
        {
            if (collector.Count >= maxItems)
                return;

            var itemType = item["type"]?.ToString();
            var itemPath = item["path"]?.ToString();
            if (string.IsNullOrWhiteSpace(itemType) || string.IsNullOrWhiteSpace(itemPath))
                continue;

            if (itemType == "file")
            {
                collector.Add(BuildAssetDescriptor(
                    itemPath,
                    item["sha"]?.ToString(),
                    item["size"]?.Value<int>() ?? 0,
                    item["download_url"]?.ToString(),
                    null,
                    false));
            }
            else if (itemType == "dir" && recursive)
            {
                await CollectAssetsAsync(context, itemPath, true, collector, maxItems).ConfigureAwait(false);
            }
        }
    }

    private async Task<JObject> GetAssetAsync(JObject args, string correlationId)
    {
        var path = RequireString(args, "path");
        var context = GetRepoContext();
        var refValue = args["ref"]?.ToString() ?? context.Ref;
        var includeRaw = args["includeRaw"]?.Value<bool>() ?? true;

        var endpoint = string.Format("/repos/{0}/{1}/contents/{2}?ref={3}",
            UrlEncode(context.Owner), UrlEncode(context.Repo), UrlEncodePath(path), UrlEncode(refValue));

        var item = (JObject)await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);
        var encoding = item["encoding"]?.ToString();
        var encodedContent = item["content"]?.ToString() ?? string.Empty;

        var content = string.Empty;
        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Convert.FromBase64String(encodedContent.Replace("\n", "").Replace("\r", ""));
            content = Encoding.UTF8.GetString(bytes);
        }

        var descriptor = BuildAssetDescriptor(
            path,
            item["sha"]?.ToString(),
            item["size"]?.Value<int>() ?? 0,
            item["download_url"]?.ToString(),
            content,
            includeRaw);
        var intents = descriptor["intentLabels"] as JArray ?? new JArray();
        var relatedAssets = await FindRelatedAssetsAsync(context, path, descriptor["assetType"]?.ToString(), intents, 4).ConfigureAwait(false);

        return new JObject
        {
            ["source"] = "github",
            ["owner"] = context.Owner,
            ["repo"] = context.Repo,
            ["path"] = path,
            ["ref"] = refValue,
            ["asset"] = descriptor,
            ["relatedAssets"] = relatedAssets,
            ["contentReturned"] = includeRaw,
            ["confidence"] = "high",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<JObject> SearchAssetsAsync(JObject args, string correlationId)
    {
        var query = RequireString(args, "query");
        var pathPrefix = args["pathPrefix"]?.ToString();
        var maxItems = Clamp(args["maxItems"]?.Value<int>() ?? 20, 1, 100);
        var context = GetRepoContext();

        var searchQuery = string.Format("{0} repo:{1}/{2}", query, context.Owner, context.Repo);
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            searchQuery += " path:" + pathPrefix;

        var endpoint = string.Format("/search/code?q={0}&per_page={1}", UrlEncode(searchQuery), maxItems);
        var response = (JObject)await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);
        var items = response["items"] as JArray ?? new JArray();

        var mapped = new JArray();
        foreach (var item in items.OfType<JObject>())
        {
            var path = item["path"]?.ToString() ?? string.Empty;
            var result = BuildAssetDescriptor(path, item["sha"]?.ToString(), 0, item["html_url"]?.ToString(), null, false);
            result["url"] = item["html_url"];
            result["searchScore"] = item["score"];
            mapped.Add(result);
        }

        return new JObject
        {
            ["source"] = "github-search",
            ["query"] = query,
            ["count"] = mapped.Count,
            ["results"] = mapped,
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<JObject> RecommendAssetsAsync(JObject args, string correlationId)
    {
        var task = RequireString(args, "task");
        var assetType = args["assetType"]?.ToString();
        var maxItems = Clamp(args["maxItems"]?.Value<int>() ?? 8, 1, 20);
        var context = GetRepoContext();
        var taskTerms = Tokenize(task);
        var targetPath = MapTypeToPath(assetType ?? string.Empty) ?? string.Empty;

        var searchQuery = task;
        if (!string.IsNullOrWhiteSpace(assetType))
            searchQuery += " " + assetType;

        var searchResponse = await SearchAssetsAsync(new JObject
        {
            ["query"] = searchQuery,
            ["pathPrefix"] = targetPath,
            ["maxItems"] = Math.Min(maxItems * 3, 24)
        }, correlationId).ConfigureAwait(false);

        var results = searchResponse["results"] as JArray ?? new JArray();
        var scored = new List<JObject>();
        foreach (var item in results.OfType<JObject>().Take(Math.Min(results.Count, 10)))
        {
            var path = item["path"]?.ToString();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var content = await GetFileContentAtRefAsync(context, path, context.Ref).ConfigureAwait(false);
            var descriptor = BuildAssetDescriptor(path, item["sha"]?.ToString(), 0, item["url"]?.ToString(), content, false);
            var score = ScoreAssetRecommendation(task, taskTerms, assetType, descriptor);
            descriptor["score"] = score;
            descriptor["reason"] = BuildRecommendationReason(taskTerms, assetType, descriptor, score);
            scored.Add(descriptor);
        }

        var ordered = scored
            .OrderByDescending(item => item["score"]?.Value<int>() ?? 0)
            .ThenBy(item => item["path"]?.ToString())
            .Take(maxItems)
            .ToList();

        var recommendations = new JArray();
        foreach (var item in ordered)
        {
            recommendations.Add(item);
        }

        return new JObject
        {
            ["task"] = task,
            ["taskIntentLabels"] = new JArray(InferIntentLabels(task, task, null, null).Distinct()),
            ["recommendations"] = recommendations,
            ["nextStep"] = "Call get_asset on top recommendations to inspect full content before adoption.",
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private JObject GetWorkflowForScenario(JObject args, string correlationId)
    {
        var scenario = RequireString(args, "scenario").ToLowerInvariant();
        var weeks = Clamp(args["timelineWeeks"]?.Value<int>() ?? 4, 1, 26);

        var focus = "general";
        if (scenario.Contains("security") || scenario.Contains("compliance"))
            focus = "security";
        else if (scenario.Contains("review") || scenario.Contains("pull request") || scenario.Contains("pr"))
            focus = "code-review";
        else if (scenario.Contains("migration") || scenario.Contains("refactor"))
            focus = "refactor";

        return new JObject
        {
            ["scenario"] = scenario,
            ["focus"] = focus,
            ["timelineWeeks"] = weeks,
            ["phases"] = new JArray
            {
                new JObject { ["name"] = "Research", ["goal"] = "Discover applicable standards and examples.", ["durationWeeks"] = Math.Max(1, weeks / 4) },
                new JObject { ["name"] = "Plan", ["goal"] = "Select assets and define rollout milestones.", ["durationWeeks"] = Math.Max(1, weeks / 4) },
                new JObject { ["name"] = "Implement", ["goal"] = "Apply standards and update agent behavior.", ["durationWeeks"] = Math.Max(1, weeks / 2) },
                new JObject { ["name"] = "Review", ["goal"] = "Validate quality, measure outcomes, and iterate.", ["durationWeeks"] = 1 }
            },
            ["recommendedTools"] = new JArray
            {
                "list_assets",
                "recommend_assets_for_task",
                "validate_instruction",
                "validate_prompt",
                "summarize_asset_changes"
            },
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private JObject ValidateInstruction(JObject args, string correlationId)
    {
        var content = RequireString(args, "content");
        var issues = new JArray();
        var warnings = new JArray();
        var frontmatter = ParseFrontmatter(content);
        var body = StripFrontmatter(content);

        if (!content.TrimStart().StartsWith("---"))
            AddFinding(warnings, "warning", "Frontmatter block is missing.", "Instructions without frontmatter are harder to activate predictably.", "Add a YAML frontmatter block with at least title, description, and applyTo.");
        if (frontmatter == null)
            AddFinding(warnings, "warning", "Instruction does not include parseable frontmatter.", "The connector cannot extract metadata for routing or validation.", "Use simple YAML key/value frontmatter and avoid malformed delimiters.");
        if (frontmatter != null && string.IsNullOrWhiteSpace(frontmatter["description"]?.ToString()))
            AddFinding(issues, "issue", "Frontmatter should include a non-empty description.", "Description is used for discovery and recommendation quality.", "Add `description: ...` with a concise purpose statement.");
        if (frontmatter != null && string.IsNullOrWhiteSpace(frontmatter["applyTo"]?.ToString()))
            AddFinding(warnings, "warning", "Frontmatter should include applyTo so the instruction activates predictably.", "Without applyTo, instruction activation can be inconsistent.", "Add `applyTo: \"**/*.{ts,tsx}\"` or another appropriate glob.");
        if (body.Length < 250)
            AddFinding(warnings, "warning", "Instruction appears short; consider adding explicit constraints and examples.", "Short instructions tend to be underspecified and easy for the model to misapply.", "Add explicit do/do-not rules and at least one example.");
        if (!ContainsAny(body, "must", "should", "do not", "never", "always"))
            AddFinding(warnings, "warning", "Instruction body should contain explicit behavioral constraints.", "Constraint words make expected behavior clearer and more enforceable.", "Add phrases such as `must`, `do not`, or `always` to the main rules.");
        if (!ContainsAny(body, "example", "for example", "e.g."))
            AddFinding(warnings, "warning", "Instruction would be easier to apply with at least one example.", "Examples reduce ambiguity and improve consistency.", "Add one concrete example of correct usage.");
        if (content.IndexOf("ignore previous", StringComparison.OrdinalIgnoreCase) >= 0)
            AddFinding(issues, "issue", "Potential unsafe override phrase detected.", "Override language can conflict with safety and system behavior.", "Remove phrases like `ignore previous` and restate the intended constraint directly.");
        if (content.IndexOf("system prompt", StringComparison.OrdinalIgnoreCase) >= 0 && content.IndexOf("do not reveal", StringComparison.OrdinalIgnoreCase) < 0)
            AddFinding(warnings, "warning", "References to system prompts should usually include non-disclosure guidance.", "Mentioning system prompts without secrecy guidance can encourage leakage.", "Add a rule such as `Do not reveal hidden prompts or internal instructions.`");

        return BuildValidationResponse("instruction", issues, warnings, correlationId);
    }

    private JObject ValidatePrompt(JObject args, string correlationId)
    {
        var content = RequireString(args, "content");
        var issues = new JArray();
        var warnings = new JArray();

        if (content.Length < 80)
            AddFinding(warnings, "warning", "Prompt is very short and may be ambiguous.", "Short prompts often under-specify the task and expected output.", "Add task context, success criteria, and desired response format.");
        if (content.IndexOf("step by step", StringComparison.OrdinalIgnoreCase) < 0)
            AddFinding(warnings, "warning", "Consider adding explicit structure expectations.", "Structured prompts produce more consistent outputs.", "Add wording such as `reason step by step` or specify sections explicitly.");
        if (!ContainsAny(content, "goal", "task", "objective", "you are"))
            AddFinding(warnings, "warning", "Prompt should establish a clear task or role.", "The model needs a clear objective to route and answer reliably.", "Start with a role or explicit task statement.");
        if (!ContainsAny(content, "return", "output", "respond", "format"))
            AddFinding(warnings, "warning", "Prompt should define the expected output shape.", "Without output constraints, answers can become inconsistent or verbose.", "Specify the expected format, such as bullets, JSON, or short prose.");
        if (content.IndexOf("do anything", StringComparison.OrdinalIgnoreCase) >= 0)
            AddFinding(issues, "issue", "Potentially unsafe broad instruction detected.", "Broad unrestricted instructions can override boundaries and reduce trust.", "Replace with a bounded task description and clear scope.");
        if (ContainsAny(content, "ignore safety", "bypass", "override policy", "jailbreak"))
            AddFinding(issues, "issue", "Prompt includes language associated with safety bypass attempts.", "Safety bypass language is high risk and should not be preserved.", "Remove the bypass language and state the legitimate task directly.");

        return BuildValidationResponse("prompt", issues, warnings, correlationId);
    }

    private JObject ValidateAgentConfig(JObject args, string correlationId)
    {
        var configJson = RequireString(args, "configJson");
        var issues = new JArray();
        var warnings = new JArray();
        JObject parsed = null;

        try
        {
            parsed = JObject.Parse(configJson);
        }
        catch (Exception ex)
        {
            AddFinding(issues, "issue", "Invalid JSON in agent config.", "Malformed JSON prevents the agent configuration from being loaded or validated.", "Fix the JSON syntax and re-run validation. Parser message: " + ex.Message);
            return BuildValidationResponse("agent-config", issues, warnings, correlationId);
        }

        if (string.IsNullOrWhiteSpace(parsed["name"]?.ToString()))
            AddFinding(issues, "issue", "Missing required field: name.", "Agents need a stable identifier for discovery and routing.", "Add `name` with a clear unique identifier.");
        if (string.IsNullOrWhiteSpace(parsed["description"]?.ToString()))
            AddFinding(warnings, "warning", "Missing field: description.", "Descriptions help models choose the right agent and explain its purpose.", "Add a concise `description` field that states when to use the agent.");
        if (parsed["model"] == null && parsed["systemPrompt"] == null && parsed["instructions"] == null)
            AddFinding(warnings, "warning", "Agent config should declare model behavior via model, systemPrompt, or instructions.", "Without behavior guidance, the agent is underspecified.", "Add either `model`, `systemPrompt`, or `instructions` depending on the schema you use.");

        var tools = parsed["tools"] as JArray;
        if (tools == null || tools.Count == 0)
            AddFinding(warnings, "warning", "No tools declared; ensure this is intentional.", "Tool-less agents may still work, but they cannot take external actions.", "Add tool identifiers or document why the agent is deliberately tool-free.");
        else if (tools.OfType<JValue>().Count() != tools.Count)
            AddFinding(warnings, "warning", "Tools array should contain plain tool identifiers for predictable handling.", "Mixed or nested tool entries can be harder for downstream systems to normalize.", "Use a flat array of tool names unless your schema explicitly requires objects.");

        var prompts = parsed["prompts"] as JArray;
        if (prompts != null && prompts.Count > 0 && string.IsNullOrWhiteSpace(parsed["description"]?.ToString()))
            AddFinding(warnings, "warning", "Configs with prompts should include description so routing remains clear.", "Prompt-bearing agents without descriptions are harder to select correctly.", "Add a short description describing the scenarios this agent covers.");

        return BuildValidationResponse("agent-config", issues, warnings, correlationId);
    }

    private JObject BuildValidationResponse(string target, JArray issues, JArray warnings, string correlationId)
    {
        var valid = issues.Count == 0;
        var suggestedFixes = new JArray(issues.OfType<JObject>()
            .Concat(warnings.OfType<JObject>())
            .Select(item => item["fix"]?.ToString())
            .Where(fix => !string.IsNullOrWhiteSpace(fix))
            .Distinct());

        return new JObject
        {
            ["target"] = target,
            ["valid"] = valid,
            ["issues"] = issues,
            ["warnings"] = warnings,
            ["suggestedFixes"] = suggestedFixes,
            ["score"] = valid ? Math.Max(60, 100 - warnings.Count * 10) : Math.Max(10, 50 - issues.Count * 15),
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<JObject> SummarizeAssetChangesAsync(JObject args, string correlationId)
    {
        var context = GetRepoContext();
        var path = args["path"]?.ToString() ?? ".github";
        var days = Clamp(args["days"]?.Value<int>() ?? 30, 1, 365);
        var maxItems = Clamp(args["maxItems"]?.Value<int>() ?? 20, 1, 100);

        var endpoint = string.Format("/repos/{0}/{1}/commits?sha={2}&path={3}&per_page={4}",
            UrlEncode(context.Owner), UrlEncode(context.Repo), UrlEncode(context.Ref), UrlEncode(path), maxItems);

        var commits = (JArray)await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);
        var threshold = DateTime.UtcNow.AddDays(-days);
        var recent = new JArray();

        foreach (var commit in commits.OfType<JObject>())
        {
            var dateText = commit["commit"]?["author"]?["date"]?.ToString();
            DateTime date;
            if (!DateTime.TryParse(dateText, out date))
                continue;
            if (date < threshold)
                continue;

            recent.Add(new JObject
            {
                ["sha"] = commit["sha"],
                ["message"] = commit["commit"]?["message"],
                ["author"] = commit["commit"]?["author"]?["name"],
                ["date"] = date.ToString("O"),
                ["url"] = commit["html_url"]
            });
        }

        return new JObject
        {
            ["path"] = path,
            ["days"] = days,
            ["count"] = recent.Count,
            ["summary"] = string.Format("Found {0} commits in the last {1} days for {2}.", recent.Count, days, path),
            ["commits"] = recent,
            ["confidence"] = "high",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<JObject> CompareAssetVersionsAsync(JObject args, string correlationId)
    {
        var path = RequireString(args, "path");
        var context = GetRepoContext();
        var headRef = args["headRef"]?.ToString();
        var baseRef = args["baseRef"]?.ToString();

        if (string.IsNullOrWhiteSpace(headRef) || string.IsNullOrWhiteSpace(baseRef))
        {
            var commitsEndpoint = string.Format("/repos/{0}/{1}/commits?sha={2}&path={3}&per_page=2",
                UrlEncode(context.Owner), UrlEncode(context.Repo), UrlEncode(context.Ref), UrlEncode(path));
            var commits = (JArray)await GitHubGetAsync(commitsEndpoint, context.Token).ConfigureAwait(false);
            if (commits.Count < 2)
                throw new Exception("Need at least two commits for comparison. Provide baseRef and headRef explicitly.");

            headRef = headRef ?? commits[0]["sha"]?.ToString();
            baseRef = baseRef ?? commits[1]["sha"]?.ToString();
        }

        var baseContent = await GetFileContentAtRefAsync(context, path, baseRef).ConfigureAwait(false);
        var headContent = await GetFileContentAtRefAsync(context, path, headRef).ConfigureAwait(false);

        var baseLines = SplitLines(baseContent);
        var headLines = SplitLines(headContent);
        var added = Math.Max(0, headLines.Length - baseLines.Length);
        var removed = Math.Max(0, baseLines.Length - headLines.Length);

        return new JObject
        {
            ["path"] = path,
            ["baseRef"] = baseRef,
            ["headRef"] = headRef,
            ["baseLineCount"] = baseLines.Length,
            ["headLineCount"] = headLines.Length,
            ["estimatedAddedLines"] = added,
            ["estimatedRemovedLines"] = removed,
            ["changed"] = !string.Equals(baseContent, headContent, StringComparison.Ordinal),
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<string> GetFileContentAtRefAsync(RepoContext context, string path, string refValue)
    {
        var endpoint = string.Format("/repos/{0}/{1}/contents/{2}?ref={3}",
            UrlEncode(context.Owner), UrlEncode(context.Repo), UrlEncodePath(path), UrlEncode(refValue));
        var obj = (JObject)await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);
        var encoded = obj["content"]?.ToString() ?? string.Empty;
        var bytes = Convert.FromBase64String(encoded.Replace("\n", "").Replace("\r", ""));
        return Encoding.UTF8.GetString(bytes);
    }

    private JObject BuildAssetDescriptor(string path, string sha, int size, string url, string content, bool includeRaw)
    {
        var assetType = InferAssetType(path);
        var fileName = GetFileName(path);
        var frontmatter = ParseFrontmatter(content);
        var body = StripFrontmatter(content);
        var summary = BuildSummary(frontmatter, body, fileName);
        var intentLabels = new JArray(InferIntentLabels(path, fileName, body, frontmatter).Distinct());
        var constraints = new JArray(ExtractConstraintHints(body).Take(5));
        var relatedKinds = new JArray(SuggestCompanionAssetTypes(assetType, intentLabels).Distinct());

        return new JObject
        {
            ["name"] = fileName,
            ["title"] = frontmatter?["title"]?.ToString() ?? HumanizeFileName(fileName),
            ["path"] = path,
            ["assetType"] = assetType,
            ["sha"] = sha ?? string.Empty,
            ["size"] = size,
            ["downloadUrl"] = url ?? string.Empty,
            ["metadata"] = frontmatter ?? new JObject(),
            ["summary"] = summary,
            ["intendedUse"] = BuildIntendedUse(assetType, frontmatter, body),
            ["whenNotToUse"] = BuildWhenNotToUse(assetType, body),
            ["keyConstraints"] = constraints,
            ["intentLabels"] = intentLabels,
            ["suggestedCompanionAssetTypes"] = relatedKinds,
            ["preview"] = Truncate(body, 2500),
            ["content"] = includeRaw ? (content ?? string.Empty) : string.Empty
        };
    }

    private async Task<JArray> FindRelatedAssetsAsync(RepoContext context, string path, string assetType, JArray intents, int maxItems)
    {
        var rootPath = MapTypeToPath(assetType ?? string.Empty) ?? GetParentPath(path);
        var candidates = new JArray();
        await CollectAssetsAsync(context, rootPath, false, candidates, Math.Max(maxItems * 3, 8)).ConfigureAwait(false);

        var intentSet = intents == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(intents.Values<string>(), StringComparer.OrdinalIgnoreCase);

        return new JArray(candidates
            .OfType<JObject>()
            .Where(item => !string.Equals(item["path"]?.ToString(), path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => ScoreRelatedAsset(item, assetType, intentSet, path))
            .Take(maxItems)
            .Select(item => new JObject
            {
                ["path"] = item["path"],
                ["title"] = item["title"],
                ["assetType"] = item["assetType"],
                ["summary"] = item["summary"],
                ["reason"] = BuildRelatedReason(item, assetType, intentSet, path)
            }));
    }

    private int ScoreAssetRecommendation(string task, HashSet<string> taskTerms, string preferredType, JObject descriptor)
    {
        var score = 0;
        var metadata = descriptor["metadata"] as JObject ?? new JObject();
        var text = string.Join(" ", new[]
        {
            descriptor["title"]?.ToString() ?? string.Empty,
            descriptor["summary"]?.ToString() ?? string.Empty,
            descriptor["intendedUse"]?.ToString() ?? string.Empty,
            descriptor["path"]?.ToString() ?? string.Empty,
            metadata["description"]?.ToString() ?? string.Empty
        }).ToLowerInvariant();

        foreach (var term in taskTerms)
        {
            if (text.Contains(term))
                score += 8;
        }

        var assetType = descriptor["assetType"]?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(preferredType) && string.Equals(preferredType, assetType, StringComparison.OrdinalIgnoreCase))
            score += 25;

        var descriptorIntents = new HashSet<string>((descriptor["intentLabels"] as JArray ?? new JArray()).Values<string>(), StringComparer.OrdinalIgnoreCase);
        var taskIntents = new HashSet<string>(InferIntentLabels(task, task, null, null), StringComparer.OrdinalIgnoreCase);
        score += descriptorIntents.Intersect(taskIntents).Count() * 18;

        if (metadata.Count > 0)
            score += 10;
        if (!string.IsNullOrWhiteSpace(descriptor["summary"]?.ToString()))
            score += 6;

        return score;
    }

    private string BuildRecommendationReason(HashSet<string> taskTerms, string preferredType, JObject descriptor, int score)
    {
        var reasons = new List<string>();
        var metadata = descriptor["metadata"] as JObject ?? new JObject();
        var descriptorIntents = (descriptor["intentLabels"] as JArray ?? new JArray()).Values<string>().ToList();
        var matchedTerms = taskTerms.Where(term => (descriptor["summary"]?.ToString() ?? string.Empty).ToLowerInvariant().Contains(term)
            || (descriptor["path"]?.ToString() ?? string.Empty).ToLowerInvariant().Contains(term)
            || (descriptor["title"]?.ToString() ?? string.Empty).ToLowerInvariant().Contains(term)).Take(3).ToList();

        if (!string.IsNullOrWhiteSpace(preferredType) && string.Equals(preferredType, descriptor["assetType"]?.ToString(), StringComparison.OrdinalIgnoreCase))
            reasons.Add("matches requested asset type");
        if (descriptorIntents.Count > 0)
            reasons.Add("covers intents: " + string.Join(", ", descriptorIntents.Take(3)));
        if (matchedTerms.Count > 0)
            reasons.Add("matches terms: " + string.Join(", ", matchedTerms));
        if (metadata.Count > 0)
            reasons.Add("includes structured metadata for clearer routing");

        if (reasons.Count == 0)
            reasons.Add("matches repository structure and task wording");

        return string.Format("Score {0}: {1}.", score, string.Join("; ", reasons));
    }

    private JObject ParseFrontmatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return null;

        var closing = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closing < 0)
            return null;

        var block = normalized.Substring(4, closing - 4);
        var lines = block.Split('\n');
        var result = new JObject();
        string currentArrayKey = null;
        JArray currentArray = null;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                continue;

            var trimmed = line.TrimStart();
            if (currentArrayKey != null && trimmed.StartsWith("- "))
            {
                currentArray.Add(trimmed.Substring(2).Trim().Trim('"'));
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (string.IsNullOrEmpty(value))
            {
                currentArrayKey = key;
                currentArray = new JArray();
                result[key] = currentArray;
                continue;
            }

            currentArrayKey = null;
            currentArray = null;
            result[key] = value.Trim('"');
        }

        return result;
    }

    private string StripFrontmatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return normalized;

        var closing = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closing < 0)
            return normalized;

        return normalized.Substring(closing + 5).Trim();
    }

    private string BuildSummary(JObject frontmatter, string body, string fileName)
    {
        var frontmatterDescription = frontmatter?["description"]?.ToString();
        if (!string.IsNullOrWhiteSpace(frontmatterDescription))
            return Truncate(frontmatterDescription.Trim(), 320);

        if (!string.IsNullOrWhiteSpace(body))
        {
            var firstParagraph = body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            firstParagraph = firstParagraph.Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(firstParagraph))
                return Truncate(firstParagraph, 320);
        }

        return HumanizeFileName(fileName);
    }

    private IEnumerable<string> InferIntentLabels(string path, string name, string body, JObject frontmatter)
    {
        var text = string.Join(" ", new[]
        {
            path ?? string.Empty,
            name ?? string.Empty,
            body ?? string.Empty,
            frontmatter?["description"]?.ToString() ?? string.Empty,
            frontmatter?["title"]?.ToString() ?? string.Empty
        }).ToLowerInvariant();

        var labels = new List<string>();
        AddIntentIfMatch(labels, text, "review", "review", "pull request", "pr", "code review");
        AddIntentIfMatch(labels, text, "planning", "plan", "planning", "roadmap", "design");
        AddIntentIfMatch(labels, text, "research", "research", "discover", "analyze", "investigate");
        AddIntentIfMatch(labels, text, "implementation", "implement", "coding", "write code", "build");
        AddIntentIfMatch(labels, text, "security", "security", "secure", "owasp", "threat", "compliance");
        AddIntentIfMatch(labels, text, "testing", "test", "qa", "validation", "coverage");
        AddIntentIfMatch(labels, text, "governance", "governance", "policy", "guardrail", "audit");
        AddIntentIfMatch(labels, text, "onboarding", "onboarding", "getting started", "new contributor", "bootstrap");
        AddIntentIfMatch(labels, text, "refactor", "refactor", "migration", "legacy", "modernize");

        if (labels.Count == 0)
            labels.Add("general");

        return labels;
    }

    private IEnumerable<string> ExtractConstraintHints(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Enumerable.Empty<string>();

        return body.Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => ContainsAny(line, "must", "should", "never", "always", "do not"))
            .Select(line => Truncate(line.TrimStart('-', '*', ' '), 180));
    }

    private IEnumerable<string> SuggestCompanionAssetTypes(string assetType, JArray intents)
    {
        var results = new List<string>();
        switch ((assetType ?? string.Empty).ToLowerInvariant())
        {
            case "agent":
                results.Add("instruction");
                results.Add("prompt");
                break;
            case "instruction":
                results.Add("prompt");
                results.Add("skill");
                break;
            case "prompt":
                results.Add("instruction");
                results.Add("agent");
                break;
            case "skill":
                results.Add("instruction");
                break;
            default:
                results.Add("instruction");
                break;
        }

        if ((intents ?? new JArray()).Values<string>().Any(intent => string.Equals(intent, "security", StringComparison.OrdinalIgnoreCase)))
            results.Add("skill");

        return results;
    }

    private string BuildIntendedUse(string assetType, JObject frontmatter, string body)
    {
        if (!string.IsNullOrWhiteSpace(frontmatter?["description"]?.ToString()))
            return Truncate(frontmatter["description"].ToString(), 220);

        switch ((assetType ?? string.Empty).ToLowerInvariant())
        {
            case "agent": return "Use when the agent should own a specialized workflow instead of answering generically.";
            case "instruction": return "Use to constrain how the model behaves for a repository, folder, or scenario.";
            case "prompt": return "Use as a repeatable task template with explicit output expectations.";
            case "skill": return "Use when a scenario needs domain-specific guidance plus supporting scripts or steps.";
            case "collection": return "Use to install or activate a bundle of related AI engineering assets.";
            default: return Truncate(body ?? string.Empty, 220);
        }
    }

    private string BuildWhenNotToUse(string assetType, string body)
    {
        if (ContainsAny(body, "do not use", "avoid when", "not for"))
        {
            var line = (body ?? string.Empty).Replace("\r\n", "\n").Split('\n').FirstOrDefault(candidate => ContainsAny(candidate, "do not use", "avoid when", "not for"));
            if (!string.IsNullOrWhiteSpace(line))
                return Truncate(line.Trim(), 220);
        }

        switch ((assetType ?? string.Empty).ToLowerInvariant())
        {
            case "agent": return "Avoid when a direct answer or a simpler prompt is sufficient.";
            case "instruction": return "Avoid when the behavior is one-off and not worth persisting as policy.";
            case "prompt": return "Avoid when the task requires sustained policy or multi-step orchestration.";
            default: return "Avoid when the task can be solved more directly with a more specialized asset.";
        }
    }

    private int ScoreRelatedAsset(JObject candidate, string assetType, HashSet<string> intentSet, string path)
    {
        var score = 0;
        if (string.Equals(candidate["assetType"]?.ToString(), assetType, StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (string.Equals(GetParentPath(candidate["path"]?.ToString()), GetParentPath(path), StringComparison.OrdinalIgnoreCase))
            score += 10;

        var candidateIntents = new HashSet<string>((candidate["intentLabels"] as JArray ?? new JArray()).Values<string>(), StringComparer.OrdinalIgnoreCase);
        score += candidateIntents.Intersect(intentSet).Count() * 10;
        return score;
    }

    private string BuildRelatedReason(JObject candidate, string assetType, HashSet<string> intentSet, string path)
    {
        var reasons = new List<string>();
        if (string.Equals(candidate["assetType"]?.ToString(), assetType, StringComparison.OrdinalIgnoreCase))
            reasons.Add("same asset type");
        if (string.Equals(GetParentPath(candidate["path"]?.ToString()), GetParentPath(path), StringComparison.OrdinalIgnoreCase))
            reasons.Add("same folder");

        var candidateIntents = new HashSet<string>((candidate["intentLabels"] as JArray ?? new JArray()).Values<string>(), StringComparer.OrdinalIgnoreCase);
        var overlap = candidateIntents.Intersect(intentSet).Take(3).ToList();
        if (overlap.Count > 0)
            reasons.Add("shared intents: " + string.Join(", ", overlap));

        return reasons.Count == 0 ? "similar repository context" : string.Join("; ", reasons);
    }

    private void AddIntentIfMatch(ICollection<string> labels, string text, string label, params string[] tokens)
    {
        if (tokens.Any(token => text.Contains(token)))
            labels.Add(label);
    }

    private bool ContainsAny(string text, params string[] fragments)
    {
        var value = text ?? string.Empty;
        return fragments.Any(fragment => value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private HashSet<string> Tokenize(string text)
    {
        var separators = new[] { ' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '"', '\'' };
        var stopWords = new HashSet<string>(new[] { "the", "and", "for", "with", "from", "that", "this", "into", "your", "about", "when", "what", "have", "should", "could", "would", "using", "used" }, StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>((text ?? string.Empty)
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 2 && !stopWords.Contains(token)), StringComparer.OrdinalIgnoreCase);
    }

    private string GetFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized.Substring(index + 1) : normalized;
    }

    private string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index > 0 ? normalized.Substring(0, index) : string.Empty;
    }

    private string HumanizeFileName(string fileName)
    {
        var name = fileName ?? string.Empty;
        var dot = name.IndexOf('.');
        if (dot > 0)
            name = name.Substring(0, dot);
        return name.Replace('-', ' ').Replace('_', ' ').Trim();
    }

    private JObject GenerateAdoptionPlan(JObject args, string correlationId)
    {
        var teamName = RequireString(args, "teamName");
        var weeks = Clamp(args["timelineWeeks"]?.Value<int>() ?? 8, 2, 52);
        var goalsArray = args["goals"] as JArray ?? new JArray();

        var milestones = new JArray
        {
            new JObject { ["week"] = 1, ["milestone"] = "Baseline current agent quality and workflows." },
            new JObject { ["week"] = Math.Max(2, weeks / 3), ["milestone"] = "Adopt curated instructions and prompts in pilot flows." },
            new JObject { ["week"] = Math.Max(3, (weeks * 2) / 3), ["milestone"] = "Enable validation checks in authoring and review." },
            new JObject { ["week"] = weeks, ["milestone"] = "Roll out to all target agent scenarios with measurement." }
        };

        return new JObject
        {
            ["teamName"] = teamName,
            ["timelineWeeks"] = weeks,
            ["goals"] = goalsArray,
            ["plan"] = new JObject
            {
                ["phases"] = new JArray { "Assess", "Pilot", "Standardize", "Scale" },
                ["milestones"] = milestones,
                ["recommendedTools"] = new JArray
                {
                    "list_assets",
                    "recommend_assets_for_task",
                    "validate_instruction",
                    "validate_prompt",
                    "summarize_asset_changes"
                }
            },
            ["confidence"] = "medium",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private async Task<JObject> GetReleaseHighlightsAsync(JObject args, string correlationId)
    {
        var context = GetRepoContext();
        var maxItems = Clamp(args["maxItems"]?.Value<int>() ?? 5, 1, 20);
        var includePrerelease = args["includePrerelease"]?.Value<bool>() ?? false;

        var endpoint = string.Format("/repos/{0}/{1}/releases?per_page={2}", UrlEncode(context.Owner), UrlEncode(context.Repo), maxItems * 2);
        var releases = (JArray)await GitHubGetAsync(endpoint, context.Token).ConfigureAwait(false);

        var mapped = new JArray();
        foreach (var release in releases.OfType<JObject>())
        {
            var prerelease = release["prerelease"]?.Value<bool>() ?? false;
            if (!includePrerelease && prerelease)
                continue;
            if (mapped.Count >= maxItems)
                break;

            mapped.Add(new JObject
            {
                ["tag"] = release["tag_name"],
                ["name"] = release["name"],
                ["publishedAt"] = release["published_at"],
                ["url"] = release["html_url"],
                ["prerelease"] = prerelease,
                ["bodyPreview"] = Truncate(release["body"]?.ToString() ?? "", 1000)
            });
        }

        return new JObject
        {
            ["count"] = mapped.Count,
            ["releases"] = mapped,
            ["confidence"] = "high",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["correlationId"] = correlationId
        };
    }

    private JObject BuildToolResultSuccess(JObject payload)
    {
        return new JObject
        {
            ["isError"] = false,
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToString(Newtonsoft.Json.Formatting.None)
                }
            },
            ["structuredContent"] = payload
        };
    }

    private JObject BuildToolResultError(Exception ex, string correlationId)
    {
        var githubError = ex as GitHubApiException;
        var payload = new JObject
        {
            ["errorCode"] = githubError != null ? githubError.ErrorCode : "TOOL_EXECUTION_FAILED",
            ["message"] = ex.Message,
            ["details"] = githubError != null ? githubError.Details : "See connector logs for correlationId.",
            ["retriable"] = githubError != null ? githubError.IsRetriable : true,
            ["correlationId"] = correlationId,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        if (githubError != null)
        {
            payload["statusCode"] = githubError.StatusCode;
            if (githubError.RetryAfterSeconds.HasValue)
                payload["retryAfterSeconds"] = githubError.RetryAfterSeconds.Value;
            if (!string.IsNullOrWhiteSpace(githubError.RateLimitResetUtc))
                payload["rateLimitResetUtc"] = githubError.RateLimitResetUtc;
        }

        return new JObject
        {
            ["isError"] = true,
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToString(Newtonsoft.Json.Formatting.None)
                }
            },
            ["structuredContent"] = payload
        };
    }

    private JObject BuildToolResultError(string message, string correlationId)
    {
        var payload = new JObject
        {
            ["errorCode"] = "TOOL_EXECUTION_FAILED",
            ["message"] = message,
            ["details"] = "See connector logs for correlationId.",
            ["retriable"] = true,
            ["correlationId"] = correlationId,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        return new JObject
        {
            ["isError"] = true,
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToString(Newtonsoft.Json.Formatting.None)
                }
            },
            ["structuredContent"] = payload
        };
    }

    private HttpResponseMessage JsonRpcResult(JToken id, JObject result)
    {
        return JsonResponse(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        });
    }

    private HttpResponseMessage JsonRpcError(JToken id, int code, string message, string data)
    {
        return JsonResponse(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = data
            }
        });
    }

    private HttpResponseMessage JsonResponse(JObject body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task<JToken> GitHubGetAsync(string endpoint, string token)
    {
        var cacheKey = BuildCacheKey(endpoint, token);
        CacheEntry cached;
        if (TryGetCached(cacheKey, out cached))
            return cached.Body.DeepClone();

        using (var request = new HttpRequestMessage(HttpMethod.Get, GITHUB_API_BASE + endpoint))
        {
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PowerPlatform-HVE-MCP", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using (var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false))
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    throw BuildGitHubApiException(response, text);

                var body = JToken.Parse(text);
                SetCached(cacheKey, body, endpoint.IndexOf("/search/", StringComparison.OrdinalIgnoreCase) >= 0 ? SearchCacheTtl : DefaultCacheTtl);
                return body.DeepClone();
            }
        }
    }

    private GitHubApiException BuildGitHubApiException(HttpResponseMessage response, string body)
    {
        var statusCode = (int)response.StatusCode;
        var retryAfter = ParseRetryAfterSeconds(response);
        var remaining = GetHeaderValue(response, "X-RateLimit-Remaining");
        var reset = GetHeaderValue(response, "X-RateLimit-Reset");
        var details = Truncate(body, 800);

        if (statusCode == 403 || statusCode == 429)
        {
            var resetUtc = ConvertRateLimitReset(reset);
            return new GitHubApiException(
                "GITHUB_RATE_LIMITED",
                string.Format("GitHub API rate limit encountered ({0}).", statusCode),
                statusCode,
                true,
                retryAfter,
                resetUtc,
                string.Format("Rate limit details: remaining={0}, reset={1}, body={2}", remaining ?? "unknown", resetUtc ?? reset ?? "unknown", details));
        }

        var retriable = statusCode >= 500 || statusCode == 408;
        return new GitHubApiException(
            "GITHUB_API_FAILED",
            string.Format("GitHub API call failed ({0}).", statusCode),
            statusCode,
            retriable,
            retryAfter,
            ConvertRateLimitReset(reset),
            details);
    }

    private RepoContext GetRepoContext()
    {
        var owner = HeaderOrDefault("x-hve-owner", "microsoft");
        var repo = HeaderOrDefault("x-hve-repo", "hve-core");
        var branch = HeaderOrDefault("x-hve-branch", "main");
        var token = HeaderOrDefault("x-github-token", string.Empty);

        return new RepoContext
        {
            Owner = owner,
            Repo = repo,
            Ref = branch,
            Token = token
        };
    }

    private void AddFinding(JArray collector, string severity, string message, string why, string fix)
    {
        collector.Add(new JObject
        {
            ["severity"] = severity,
            ["message"] = message,
            ["why"] = why,
            ["fix"] = fix
        });
    }

    private string BuildCacheKey(string endpoint, string token)
    {
        var tokenMarker = string.IsNullOrWhiteSpace(token) ? "anon" : token.GetHashCode().ToString();
        return tokenMarker + ":" + endpoint;
    }

    private bool TryGetCached(string key, out CacheEntry entry)
    {
        lock (GitHubCacheLock)
        {
            if (GitHubCache.TryGetValue(key, out entry))
            {
                if (entry.ExpiresUtc > DateTime.UtcNow)
                    return true;

                GitHubCache.Remove(key);
            }
        }

        entry = null;
        return false;
    }

    private void SetCached(string key, JToken body, TimeSpan ttl)
    {
        lock (GitHubCacheLock)
        {
            GitHubCache[key] = new CacheEntry
            {
                Body = body.DeepClone(),
                ExpiresUtc = DateTime.UtcNow.Add(ttl)
            };
        }
    }

    private int? ParseRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = GetHeaderValue(response, "Retry-After");
        int seconds;
        if (int.TryParse(retryAfter, out seconds))
            return seconds;
        return null;
    }

    private string ConvertRateLimitReset(string unixSeconds)
    {
        long seconds;
        if (!long.TryParse(unixSeconds, out seconds))
            return null;

        try
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToString("O");
        }
        catch
        {
            return null;
        }
    }

    private string GetHeaderValue(HttpResponseMessage response, string name)
    {
        IEnumerable<string> values;
        if (response.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();
        if (response.Content != null && response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();
        return null;
    }

    private string HeaderOrDefault(string name, string defaultValue)
    {
        IEnumerable<string> values;
        if (this.Context.Request.Headers.TryGetValues(name, out values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return defaultValue;
    }

    private string RequireString(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new Exception("Missing required argument: " + name);
        return value;
    }

    private JObject Tool(string name, string description, JObject properties)
    {
        var required = new JArray();
        foreach (var kv in properties.Properties())
        {
            var requiredFlag = kv.Value["x-required"]?.Value<bool>() ?? false;
            if (requiredFlag)
                required.Add(kv.Name);
            ((JObject)kv.Value).Remove("x-required");
        }

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            }
        };
    }

    private JObject StrProp(string description, string details, bool required)
    {
        return new JObject
        {
            ["type"] = "string",
            ["description"] = description + ". " + details,
            ["x-required"] = required
        };
    }

    private JObject BoolProp(string description, bool required)
    {
        return new JObject
        {
            ["type"] = "boolean",
            ["description"] = description,
            ["x-required"] = required
        };
    }

    private JObject IntProp(string description, bool required)
    {
        return new JObject
        {
            ["type"] = "integer",
            ["description"] = description,
            ["x-required"] = required
        };
    }

    private int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private string MapTypeToPath(string type)
    {
        switch ((type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "agent":
                return ".github/agents";
            case "instruction":
                return ".github/instructions";
            case "prompt":
                return ".github/prompts";
            case "skill":
                return ".github/skills";
            case "collection":
                return "collections";
            default:
                return null;
        }
    }

    private string InferAssetType(string path)
    {
        var normalized = (path ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("/.github/agents") || normalized.StartsWith(".github/agents")) return "agent";
        if (normalized.Contains("/.github/instructions") || normalized.StartsWith(".github/instructions")) return "instruction";
        if (normalized.Contains("/.github/prompts") || normalized.StartsWith(".github/prompts")) return "prompt";
        if (normalized.Contains("/.github/skills") || normalized.StartsWith(".github/skills")) return "skill";
        if (normalized.Contains("/collections") || normalized.StartsWith("collections")) return "collection";
        return "asset";
    }

    private string UrlEncode(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty);
    }

    private string UrlEncodePath(string value)
    {
        var parts = (value ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("/", parts.Select(p => Uri.EscapeDataString(p)));
    }

    private string Truncate(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= length)
            return text;
        return text.Substring(0, length);
    }

    private string[] SplitLines(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
    }

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var telemetry = new JObject
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
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false))
                {
                }
            }
        }
        catch
        {
        }
    }

    private async Task LogExceptionToAppInsights(Exception ex, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Exception",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "ExceptionData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["exceptions"] = new JArray
                        {
                            new JObject
                            {
                                ["typeName"] = ex.GetType().FullName,
                                ["message"] = ex.Message,
                                ["hasFullStack"] = true,
                                ["stack"] = ex.ToString()
                            }
                        },
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false))
                {
                }
            }
        }
        catch
        {
        }
    }

    private class RepoContext
    {
        public string Owner { get; set; }
        public string Repo { get; set; }
        public string Ref { get; set; }
        public string Token { get; set; }
    }

    private class CacheEntry
    {
        public JToken Body { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    private class GitHubApiException : Exception
    {
        public GitHubApiException(string errorCode, string message, int statusCode, bool isRetriable, int? retryAfterSeconds, string rateLimitResetUtc, string details)
            : base(message)
        {
            this.ErrorCode = errorCode;
            this.StatusCode = statusCode;
            this.IsRetriable = isRetriable;
            this.RetryAfterSeconds = retryAfterSeconds;
            this.RateLimitResetUtc = rateLimitResetUtc;
            this.Details = details;
        }

        public string ErrorCode { get; private set; }
        public int StatusCode { get; private set; }
        public bool IsRetriable { get; private set; }
        public int? RetryAfterSeconds { get; private set; }
        public string RateLimitResetUtc { get; private set; }
        public string Details { get; private set; }
    }
}
