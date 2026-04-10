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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: MCP FRAMEWORK                                                  ║
// ║                                                                             ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power        ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section    ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                             ║
// ║  Spec coverage: MCP 2025-11-25                                              ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,                ║
// ║           completion/complete, logging/setLevel, all notifications          ║
// ║                                                                             ║
// ║  Stateless limitations (Power Platform cannot send async notifications):    ║
// ║   - Tasks (experimental, requires persistent state between requests)        ║
// ║   - Server→client requests (sampling, elicitation, roots/list)              ║
// ║   - Server→client notifications (progress, logging/message, list_changed)   ║
// ║                                                                             ║
// ║  Do not modify unless extending the framework itself.                       ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchema = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annot = null;
        if (annotations != null)
        {
            annot = new JObject();
            annotations(annot);
        }

        JObject outSchema = null;
        if (outputSchema != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchema(outBuilder);
            outSchema = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outSchema,
            Annotations = annot,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ToolResult(JArray content, bool isError = false)
    {
        return new JObject { ["content"] = content, ["isError"] = isError };
    }

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: CONNECTOR ENTRY POINT                                          ║
// ║                                                                             ║
// ║  Copilot Cowork Skills — Manage custom skills in OneDrive                   ║
// ║  /Documents/Cowork/Skills/ folder via Microsoft Graph.                      ║
// ║                                                                             ║
// ║  Tools: list_skills, get_skill, create_skill, update_skill, delete_skill,   ║
// ║         validate_skill                                                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "copilot-cowork-skills",
            Version = "1.0.0",
            Title = "Copilot Cowork Skills",
            Description = "Manage Copilot Cowork custom skills stored in the user's OneDrive /Documents/Cowork/Skills/ folder."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = false,
            Prompts = false,
            Logging = true,
            Completions = false
        },
        Instructions = "Manages Copilot Cowork custom skills stored as SKILL.md files in OneDrive. "
            + "Each skill is a subfolder under /Documents/Cowork/Skills/ containing a SKILL.md file "
            + "with YAML frontmatter (name, description) and Markdown instructions. "
            + "Maximum 20 custom skills, each up to 1 MB."
    };

    private const string GRAPH_BASE = "https://graph.microsoft.com/v1.0";
    private const string SKILLS_PATH = "/Documents/Cowork/Skills";
    private const int MAX_SKILLS = 20;
    private const int MAX_SKILL_SIZE_BYTES = 1048576; // 1 MB

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var handler = new McpRequestHandler(Options);
        RegisterTools(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"{eventName}");
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ── list_skills ──────────────────────────────────────────────────
        handler.AddTool("list_skills",
            "List all Copilot Cowork custom skills in the user's OneDrive /Documents/Cowork/Skills/ folder. Returns skill folder names.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                var url = $"{GRAPH_BASE}/me/drive/root:{SKILLS_PATH}:/children?$filter=folder ne null&$select=name,id,lastModifiedDateTime,size";
                try
                {
                    var graphResult = await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
                    var items = graphResult["value"] as JArray ?? new JArray();
                    var skills = new JArray();
                    foreach (var item in items)
                    {
                        skills.Add(new JObject
                        {
                            ["name"] = item["name"],
                            ["id"] = item["id"],
                            ["lastModified"] = item["lastModifiedDateTime"]
                        });
                    }
                    return new JObject
                    {
                        ["skillCount"] = skills.Count,
                        ["maxSkills"] = MAX_SKILLS,
                        ["remainingSlots"] = MAX_SKILLS - skills.Count,
                        ["skills"] = skills
                    };
                }
                catch (Exception ex) when (ex.Message.Contains("404"))
                {
                    return new JObject
                    {
                        ["skillCount"] = 0,
                        ["maxSkills"] = MAX_SKILLS,
                        ["remainingSlots"] = MAX_SKILLS,
                        ["skills"] = new JArray(),
                        ["note"] = "The /Documents/Cowork/Skills/ folder does not exist yet. Create a skill to initialize it."
                    };
                }
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── get_skill ────────────────────────────────────────────────────
        handler.AddTool("get_skill",
            "Read the SKILL.md content of a specific Copilot Cowork custom skill.",
            schema: s => s
                .String("skillName", "The skill folder name (e.g., 'weekly-report')", required: true),
            handler: async (args, ct) =>
            {
                var skillName = RequireArgument(args, "skillName");
                ValidateSkillName(skillName);

                var url = $"{GRAPH_BASE}/me/drive/root:{SKILLS_PATH}/{Uri.EscapeDataString(skillName)}/SKILL.md:/content";
                var content = await DownloadFileContentAsync(url).ConfigureAwait(false);

                var parsed = ParseSkillMd(content);
                return new JObject
                {
                    ["skillName"] = skillName,
                    ["name"] = parsed["name"],
                    ["description"] = parsed["description"],
                    ["instructions"] = parsed["instructions"],
                    ["rawContent"] = content
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── create_skill ─────────────────────────────────────────────────
        handler.AddTool("create_skill",
            "Create a new Copilot Cowork custom skill. Creates the skill folder and SKILL.md file in OneDrive /Documents/Cowork/Skills/. Maximum 20 custom skills allowed.",
            schema: s => s
                .String("skillName", "The skill folder name using lowercase-with-dashes (e.g., 'weekly-report'). This becomes the subfolder name.", required: true)
                .String("name", "Display name for the skill shown in Cowork (e.g., 'Weekly Report')", required: true)
                .String("description", "One-line description of what the skill does. Used by Cowork to decide when to load the skill.", required: true)
                .String("instructions", "Markdown instructions that tell Cowork how to execute the skill. Be specific about data sources, formatting, and output expectations.", required: true),
            handler: async (args, ct) =>
            {
                var skillName = RequireArgument(args, "skillName");
                var name = RequireArgument(args, "name");
                var description = RequireArgument(args, "description");
                var instructions = RequireArgument(args, "instructions");

                ValidateSkillName(skillName);

                var skillMd = BuildSkillMd(name, description, instructions);
                if (Encoding.UTF8.GetByteCount(skillMd) > MAX_SKILL_SIZE_BYTES)
                    throw new ArgumentException($"SKILL.md exceeds the 1 MB size limit");

                // Ensure the Skills folder exists by creating the skill subfolder
                // Graph auto-creates parent folders when uploading via path
                var url = $"{GRAPH_BASE}/me/drive/root:{SKILLS_PATH}/{Uri.EscapeDataString(skillName)}/SKILL.md:/content";
                var uploaded = await UploadFileContentAsync(url, skillMd).ConfigureAwait(false);

                return new JObject
                {
                    ["success"] = true,
                    ["skillName"] = skillName,
                    ["name"] = name,
                    ["description"] = description,
                    ["fileId"] = uploaded["id"],
                    ["webUrl"] = uploaded["webUrl"],
                    ["message"] = "Skill created. Cowork will discover it automatically at the start of your next conversation."
                };
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        // ── update_skill ─────────────────────────────────────────────────
        handler.AddTool("update_skill",
            "Update an existing Copilot Cowork custom skill's SKILL.md file. Overwrites the entire file content.",
            schema: s => s
                .String("skillName", "The skill folder name (e.g., 'weekly-report')", required: true)
                .String("name", "Updated display name for the skill", required: true)
                .String("description", "Updated one-line description", required: true)
                .String("instructions", "Updated Markdown instructions", required: true),
            handler: async (args, ct) =>
            {
                var skillName = RequireArgument(args, "skillName");
                var name = RequireArgument(args, "name");
                var description = RequireArgument(args, "description");
                var instructions = RequireArgument(args, "instructions");

                ValidateSkillName(skillName);

                var skillMd = BuildSkillMd(name, description, instructions);
                if (Encoding.UTF8.GetByteCount(skillMd) > MAX_SKILL_SIZE_BYTES)
                    throw new ArgumentException($"SKILL.md exceeds the 1 MB size limit");

                var url = $"{GRAPH_BASE}/me/drive/root:{SKILLS_PATH}/{Uri.EscapeDataString(skillName)}/SKILL.md:/content";
                var uploaded = await UploadFileContentAsync(url, skillMd).ConfigureAwait(false);

                return new JObject
                {
                    ["success"] = true,
                    ["skillName"] = skillName,
                    ["name"] = name,
                    ["description"] = description,
                    ["fileId"] = uploaded["id"],
                    ["message"] = "Skill updated. Changes take effect at the start of your next Cowork conversation."
                };
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        // ── delete_skill ─────────────────────────────────────────────────
        handler.AddTool("delete_skill",
            "Delete a Copilot Cowork custom skill by removing its folder from OneDrive /Documents/Cowork/Skills/.",
            schema: s => s
                .String("skillName", "The skill folder name to delete (e.g., 'weekly-report')", required: true),
            handler: async (args, ct) =>
            {
                var skillName = RequireArgument(args, "skillName");
                ValidateSkillName(skillName);

                var url = $"{GRAPH_BASE}/me/drive/root:{SKILLS_PATH}/{Uri.EscapeDataString(skillName)}";
                await DeleteGraphItemAsync(url).ConfigureAwait(false);

                return new JObject
                {
                    ["success"] = true,
                    ["skillName"] = skillName,
                    ["message"] = "Skill deleted. It will no longer appear in new Cowork conversations."
                };
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── validate_skill ───────────────────────────────────────────────
        handler.AddTool("validate_skill",
            "Validate SKILL.md content without saving. Checks YAML frontmatter format, required fields (name, description), and size limits.",
            schema: s => s
                .String("content", "The full SKILL.md file content to validate, including YAML frontmatter and instructions.", required: true),
            handler: async (args, ct) =>
            {
                var content = RequireArgument(args, "content");
                var issues = new JArray();
                var parsed = new JObject();

                // Size check
                var sizeBytes = Encoding.UTF8.GetByteCount(content);
                if (sizeBytes > MAX_SKILL_SIZE_BYTES)
                    issues.Add($"File size ({sizeBytes} bytes) exceeds the 1 MB limit ({MAX_SKILL_SIZE_BYTES} bytes).");

                // Frontmatter check
                if (!content.TrimStart().StartsWith("---"))
                {
                    issues.Add("Missing YAML frontmatter. File must start with '---'.");
                }
                else
                {
                    var endIndex = content.IndexOf("---", content.IndexOf("---") + 3);
                    if (endIndex < 0)
                    {
                        issues.Add("YAML frontmatter is not closed. Missing closing '---' delimiter.");
                    }
                    else
                    {
                        var frontmatter = content.Substring(content.IndexOf("---") + 3, endIndex - content.IndexOf("---") - 3).Trim();
                        parsed = ParseYamlFrontmatter(frontmatter);

                        if (string.IsNullOrWhiteSpace(parsed.Value<string>("name")))
                            issues.Add("Missing required 'name' field in YAML frontmatter.");
                        if (string.IsNullOrWhiteSpace(parsed.Value<string>("description")))
                            issues.Add("Missing required 'description' field in YAML frontmatter.");

                        var instructions = content.Substring(endIndex + 3).Trim();
                        if (string.IsNullOrWhiteSpace(instructions))
                            issues.Add("No instructions found after the YAML frontmatter. Add Markdown instructions for your skill.");

                        parsed["instructions"] = instructions;
                    }
                }

                return await Task.FromResult(new JObject
                {
                    ["valid"] = issues.Count == 0,
                    ["issueCount"] = issues.Count,
                    ["issues"] = issues,
                    ["parsed"] = parsed,
                    ["sizeBytes"] = sizeBytes,
                    ["maxSizeBytes"] = MAX_SKILL_SIZE_BYTES
                }).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── Graph API Helpers ────────────────────────────────────────────────

    private async Task<JObject> SendGraphRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Graph request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private async Task<string> DownloadFileContentAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to download file ({(int)response.StatusCode}): {content}");

        return content;
    }

    private async Task<JObject> UploadFileContentAsync(string url, string fileContent)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Content = new StringContent(fileContent, Encoding.UTF8, "text/plain");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to upload file ({(int)response.StatusCode}): {responseContent}");

        try { return JObject.Parse(responseContent); }
        catch { return new JObject { ["success"] = true }; }
    }

    private async Task DeleteGraphItemAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new Exception($"Failed to delete item ({(int)response.StatusCode}): {content}");
        }
    }

    // ── SKILL.md Helpers ─────────────────────────────────────────────────

    private static string BuildSkillMd(string name, string description, string instructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {EscapeYamlValue(name)}");
        sb.AppendLine($"description: {EscapeYamlValue(description)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(instructions);
        return sb.ToString();
    }

    private static JObject ParseSkillMd(string content)
    {
        var result = new JObject();
        if (string.IsNullOrWhiteSpace(content)) return result;

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith("---")) return new JObject { ["instructions"] = content };

        var startIdx = trimmed.IndexOf("---");
        var endIdx = trimmed.IndexOf("---", startIdx + 3);
        if (endIdx < 0) return new JObject { ["instructions"] = content };

        var frontmatter = trimmed.Substring(startIdx + 3, endIdx - startIdx - 3).Trim();
        result = ParseYamlFrontmatter(frontmatter);
        result["instructions"] = trimmed.Substring(endIdx + 3).Trim();

        return result;
    }

    private static JObject ParseYamlFrontmatter(string yaml)
    {
        var result = new JObject();
        if (string.IsNullOrWhiteSpace(yaml)) return result;

        foreach (var line in yaml.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = line.Substring(0, colonIdx).Trim();
            var value = line.Substring(colonIdx + 1).Trim();

            // Remove surrounding quotes if present
            if (value.Length >= 2
                && ((value.StartsWith("\"") && value.EndsWith("\""))
                 || (value.StartsWith("'") && value.EndsWith("'"))))
            {
                value = value.Substring(1, value.Length - 2);
            }

            result[key] = value;
        }

        return result;
    }

    private static string EscapeYamlValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        // Quote values that contain YAML special characters
        if (value.Contains(":") || value.Contains("#") || value.Contains("'")
            || value.Contains("\"") || value.Contains("{") || value.Contains("}")
            || value.Contains("[") || value.Contains("]") || value.Contains(",")
            || value.Contains("&") || value.Contains("*") || value.Contains("?")
            || value.Contains("|") || value.Contains("-") || value.Contains("<")
            || value.Contains(">") || value.Contains("=") || value.Contains("!")
            || value.Contains("%") || value.Contains("@") || value.Contains("`")
            || value.TrimStart().StartsWith("-"))
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        return value;
    }

    // ── Validation Helpers ───────────────────────────────────────────────

    private static void ValidateSkillName(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new ArgumentException("'skillName' is required");

        if (skillName.Length > 100)
            throw new ArgumentException("Skill name must be 100 characters or fewer");

        // Only allow safe folder name characters
        foreach (var c in skillName)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != ' ')
                throw new ArgumentException($"Skill name contains invalid character '{c}'. Use letters, digits, dashes, underscores, or spaces.");
        }

        // Prevent path traversal
        if (skillName.Contains("..") || skillName.Contains("/") || skillName.Contains("\\"))
            throw new ArgumentException("Skill name cannot contain path separators or '..'");
    }

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
