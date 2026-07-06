using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // ─── Configuration ────────────────────────────────────────────────────────────
    private const string ACA_SUBSCRIPTION_ID = "[[YOUR_SUBSCRIPTION_ID]]";
    private const string ACA_RESOURCE_GROUP = "[[YOUR_RESOURCE_GROUP]]";
    private const string ACA_SANDBOX_GROUP = "[[YOUR_SANDBOX_GROUP]]";
    private const string ACA_REGION = "[[YOUR_REGION]]";
    private const string ACA_DATA_PLANE_BASE = "https://management.[[YOUR_REGION]].azuredevcompute.io";
    private const string ACA_DISK_IMAGE = "[[YOUR_DISK_IMAGE_ID]]";
    private const bool ACA_DISK_IS_PUBLIC = false;
    private const int MAX_OUTPUT_BYTES = 4096;
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const string CONNECTOR_LABEL = "aca-code-interpreter";

    // ─── App Insights ─────────────────────────────────────────────────────────────
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ─── Language → Command Mapping ───────────────────────────────────────────────
    private static readonly Dictionary<string, string> LANGUAGE_COMMANDS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = "python3 -c",
        ["javascript"] = "node -e",
        ["typescript"] = "npx tsx -e",
        ["powerfx"] = "pfx eval",
        ["bash"] = "bash -c",
        ["powershell"] = "pwsh -c",
        ["ruby"] = "ruby -e",
        ["perl"] = "perl -e",
        ["php"] = "php -r",
        ["sql"] = "sqlite3 :memory:",
        ["adaptivecard"] = "node /opt/tools/validate-card.js",
        ["fetchxml"] = "python3 /opt/tools/fetchxml_eval.py",
        ["openapi-lint"] = "node /opt/tools/lint-openapi.js",
        ["prompt"] = "python3 /opt/tools/render_prompt.py",
        ["expression"] = "dotnet /opt/tools/expr-eval.dll",
        ["regex"] = "python3 /opt/tools/regex_test.py"
    };

    // ─── MCP Tool Definitions ─────────────────────────────────────────────────────
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "execute_code",
            ["description"] = "Execute code in an isolated ACA Sandbox microVM. Supports 16 languages/modes: python (default), javascript, typescript, powerfx, bash, powershell, ruby, perl, php, sql, adaptivecard, fetchxml, openapi-lint, prompt, expression, regex. Returns stdout, stderr, and exit code. Creates a session automatically if session_id is not provided.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["code"] = new JObject { ["type"] = "string", ["description"] = "The code or input to execute." },
                    ["language"] = new JObject { ["type"] = "string", ["description"] = "Language or mode. Default: python.", ["enum"] = new JArray { "python", "javascript", "typescript", "powerfx", "bash", "powershell", "ruby", "perl", "php", "sql", "adaptivecard", "fetchxml", "openapi-lint", "prompt", "expression", "regex" } },
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID. If omitted, a new session is created automatically." },
                    ["timeout"] = new JObject { ["type"] = "integer", ["description"] = "Timeout in seconds. Default 30." },
                    ["variables"] = new JObject { ["type"] = "object", ["description"] = "Variables for prompt/regex modes." }
                },
                ["required"] = new JArray { "code" }
            }
        },
        new JObject
        {
            ["name"] = "upload_file",
            ["description"] = "Upload a file into the sandbox workspace. Use for staging data files (CSV, JSON, etc.) before code execution.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["file_name"] = new JObject { ["type"] = "string", ["description"] = "File name (e.g., data.csv)." },
                    ["content"] = new JObject { ["type"] = "string", ["description"] = "File content as base64." },
                    ["path"] = new JObject { ["type"] = "string", ["description"] = "Target directory. Default: /workspace" }
                },
                ["required"] = new JArray { "session_id", "file_name", "content" }
            }
        },
        new JObject
        {
            ["name"] = "download_file",
            ["description"] = "Download a file from the sandbox workspace. Use for retrieving generated artifacts like charts, reports, or transformed data.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["file_path"] = new JObject { ["type"] = "string", ["description"] = "Full path in sandbox (e.g., /workspace/chart.png)." }
                },
                ["required"] = new JArray { "session_id", "file_path" }
            }
        },
        new JObject
        {
            ["name"] = "list_files",
            ["description"] = "List files in the sandbox /workspace directory.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["path"] = new JObject { ["type"] = "string", ["description"] = "Directory to list. Default: /workspace" }
                },
                ["required"] = new JArray { "session_id" }
            }
        },
        new JObject
        {
            ["name"] = "create_session",
            ["description"] = "Create a new code interpreter session explicitly. Use when you need custom CPU/memory or egress rules. Otherwise, execute_code creates sessions automatically.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["cpu"] = new JObject { ["type"] = "string", ["description"] = "CPU in millicores: 500m, 1000m, 2000m, 4000m. Default: 1000m." },
                    ["memory"] = new JObject { ["type"] = "string", ["description"] = "Memory: 1024Mi, 2048Mi, 4096Mi, 8192Mi. Default: 2048Mi." },
                    ["egress_allow_hosts"] = new JObject { ["type"] = "array", ["description"] = "Additional hosts to allow through egress firewall.", ["items"] = new JObject { ["type"] = "string" } }
                },
                ["required"] = new JArray {}
            }
        },
        new JObject
        {
            ["name"] = "destroy_session",
            ["description"] = "Destroy a sandbox session and free all resources. Irreversible.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID to destroy." }
                },
                ["required"] = new JArray { "session_id" }
            }
        }
    };

    // ─── Main Router ──────────────────────────────────────────────────────────────
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        try
        {
            switch (operationId)
            {
                case "InvokeMCP":
                    return await HandleMcpRequestAsync().ConfigureAwait(false);
                case "CreateSession":
                    return await HandleCreateSessionAsync().ConfigureAwait(false);
                case "ExecuteCode":
                    return await HandleExecuteCodeAsync().ConfigureAwait(false);
                case "UploadFile":
                    return await HandleUploadFileAsync().ConfigureAwait(false);
                case "DownloadFile":
                    return await HandleDownloadFileAsync().ConfigureAwait(false);
                case "ListFiles":
                    return await HandleListFilesAsync().ConfigureAwait(false);
                case "GetSession":
                    return await HandleGetSessionAsync().ConfigureAwait(false);
                case "DestroySession":
                    return await HandleDestroySessionAsync().ConfigureAwait(false);
                default:
                    return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = $"Unknown operation: {operationId}" });
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("Error", new Dictionary<string, string>
            {
                ["operation"] = operationId,
                ["error"] = ex.Message,
                ["stackTrace"] = ex.StackTrace
            });
            return CreateJsonResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message,
                ["operation"] = operationId
            });
        }
    }

    // ─── Typed Operation Handlers ─────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleCreateSessionAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var cpu = body.Value<string>("cpu") ?? "1000m";
        var memory = body.Value<string>("memory") ?? "2048Mi";
        var autoSuspend = body.Value<int?>("autoSuspendMinutes") ?? 5;
        var egressHosts = body["egressAllowHosts"]?.ToObject<List<string>>() ?? new List<string>();

        var sandbox = await CreateSandboxAsync("", cpu, memory, autoSuspend, egressHosts).ConfigureAwait(false);

        // The API returns the sandbox ID in the response
        var sessionId = sandbox.Value<string>("id") ?? sandbox.Value<string>("name") ?? Guid.NewGuid().ToString();

        LogToAppInsights("SessionCreated", new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["cpu"] = cpu,
            ["memory"] = memory
        });

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["sessionId"] = sessionId,
            ["state"] = "Running",
            ["cpu"] = cpu,
            ["memory"] = memory,
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
            ["autoSuspendMinutes"] = autoSuspend
        });
    }

    private async Task<HttpResponseMessage> HandleExecuteCodeAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var code = body.Value<string>("code");
        var language = body.Value<string>("language") ?? "python";
        var timeout = body.Value<int?>("timeout") ?? DEFAULT_TIMEOUT_SECONDS;
        var variables = body["variables"] as JObject;

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });
        if (string.IsNullOrWhiteSpace(code))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "code is required." });

        var command = BuildExecCommand(language, code, variables);
        var startTime = DateTime.UtcNow;
        var result = await ExecInSandboxAsync(sessionId, command, timeout).ConfigureAwait(false);
        var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        var stdout = result.Value<string>("stdout") ?? "";
        var stderr = result.Value<string>("stderr") ?? "";
        var exitCode = result.Value<int?>("exitCode") ?? -1;
        var truncated = false;

        if (stdout.Length > MAX_OUTPUT_BYTES)
        {
            stdout = stdout.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated at 4KB]";
            truncated = true;
        }
        if (stderr.Length > MAX_OUTPUT_BYTES)
        {
            stderr = stderr.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated at 4KB]";
            truncated = true;
        }

        LogToAppInsights("CodeExecuted", new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["language"] = language,
            ["exitCode"] = exitCode.ToString(),
            ["executionTimeMs"] = elapsed.ToString()
        });

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exitCode"] = exitCode,
            ["executionTimeMs"] = elapsed,
            ["language"] = language,
            ["truncated"] = truncated,
            ["sessionId"] = sessionId
        });
    }

    private async Task<HttpResponseMessage> HandleUploadFileAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var fileName = body.Value<string>("fileName");
        var content = body.Value<string>("content");
        var path = body.Value<string>("path") ?? "/workspace";

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(content))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId, fileName, and content are required." });

        var fullPath = $"{path.TrimEnd('/')}/{fileName}";
        await WriteFileToSandboxAsync(sessionId, fullPath, content).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["success"] = true,
            ["filePath"] = fullPath,
            ["sizeBytes"] = Convert.FromBase64String(content).Length
        });
    }

    private async Task<HttpResponseMessage> HandleDownloadFileAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var filePath = body.Value<string>("filePath");

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(filePath))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and filePath are required." });

        var result = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, result);
    }

    private async Task<HttpResponseMessage> HandleListFilesAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var path = body.Value<string>("path") ?? "/workspace";

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        var result = await ListFilesInSandboxAsync(sessionId, path).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["path"] = path,
            ["files"] = result
        });
    }

    private async Task<HttpResponseMessage> HandleGetSessionAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        var result = await GetSandboxStatusAsync(sessionId).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, result);
    }

    private async Task<HttpResponseMessage> HandleDestroySessionAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        await DeleteSandboxAsync(sessionId).ConfigureAwait(false);

        LogToAppInsights("SessionDestroyed", new Dictionary<string, string> { ["sessionId"] = sessionId });

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["success"] = true,
            ["sessionId"] = sessionId
        });
    }

    // ─── MCP Handler ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = JObject.Parse(body);
        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        switch (method)
        {
            case "initialize":
                return McpSuccess(id, new JObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JObject { ["name"] = "aca-code-interpreter", ["version"] = "1.0.0" }
                });

            case "notifications/initialized":
                return McpSuccess(id, new JObject());

            case "tools/list":
                return McpSuccess(id, new JObject { ["tools"] = AVAILABLE_TOOLS });

            case "tools/call":
                return await HandleMcpToolCallAsync(id, request["params"] as JObject).ConfigureAwait(false);

            default:
                return McpError(id, -32601, "Method not found", method);
        }
    }

    private async Task<HttpResponseMessage> HandleMcpToolCallAsync(JToken id, JObject paramsObj)
    {
        var toolName = paramsObj?.Value<string>("name");
        var args = paramsObj?["arguments"] as JObject ?? new JObject();

        try
        {
            JToken result;
            switch (toolName)
            {
                case "execute_code":
                    result = await McpExecuteCodeAsync(args).ConfigureAwait(false);
                    break;
                case "upload_file":
                    result = await McpUploadFileAsync(args).ConfigureAwait(false);
                    break;
                case "download_file":
                    result = await McpDownloadFileAsync(args).ConfigureAwait(false);
                    break;
                case "list_files":
                    result = await McpListFilesAsync(args).ConfigureAwait(false);
                    break;
                case "create_session":
                    result = await McpCreateSessionAsync(args).ConfigureAwait(false);
                    break;
                case "destroy_session":
                    result = await McpDestroySessionAsync(args).ConfigureAwait(false);
                    break;
                default:
                    return McpError(id, -32602, $"Unknown tool: {toolName}");
            }

            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.None) } },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ─── MCP Tool Implementations ─────────────────────────────────────────────────

    private async Task<JObject> McpExecuteCodeAsync(JObject args)
    {
        var code = args.Value<string>("code");
        var language = args.Value<string>("language") ?? "python";
        var sessionId = args.Value<string>("session_id");
        var timeout = args.Value<int?>("timeout") ?? DEFAULT_TIMEOUT_SECONDS;
        var variables = args["variables"] as JObject;

        // Auto-create session if not provided
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            await CreateSandboxAsync(sessionId, "1000m", "2048Mi", 5, new List<string>()).ConfigureAwait(false);
        }

        var command = BuildExecCommand(language, code, variables);
        var startTime = DateTime.UtcNow;
        var result = await ExecInSandboxAsync(sessionId, command, timeout).ConfigureAwait(false);
        var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        var stdout = result.Value<string>("stdout") ?? "";
        var stderr = result.Value<string>("stderr") ?? "";
        if (stdout.Length > MAX_OUTPUT_BYTES) stdout = stdout.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated at 4KB]";
        if (stderr.Length > MAX_OUTPUT_BYTES) stderr = stderr.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated at 4KB]";

        return new JObject
        {
            ["session_id"] = sessionId,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exit_code"] = result.Value<int?>("exitCode") ?? -1,
            ["execution_time_ms"] = elapsed,
            ["language"] = language
        };
    }

    private async Task<JObject> McpUploadFileAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var fileName = args.Value<string>("file_name");
        var content = args.Value<string>("content");
        var path = args.Value<string>("path") ?? "/workspace";

        var fullPath = $"{path.TrimEnd('/')}/{fileName}";
        await WriteFileToSandboxAsync(sessionId, fullPath, content).ConfigureAwait(false);

        return new JObject { ["success"] = true, ["file_path"] = fullPath };
    }

    private async Task<JObject> McpDownloadFileAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var filePath = args.Value<string>("file_path");

        return await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);
    }

    private async Task<JObject> McpListFilesAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var path = args.Value<string>("path") ?? "/workspace";

        var files = await ListFilesInSandboxAsync(sessionId, path).ConfigureAwait(false);
        return new JObject { ["path"] = path, ["files"] = files };
    }

    private async Task<JObject> McpCreateSessionAsync(JObject args)
    {
        var cpu = args.Value<string>("cpu") ?? "1000m";
        var memory = args.Value<string>("memory") ?? "2048Mi";
        var egressHosts = args["egress_allow_hosts"]?.ToObject<List<string>>() ?? new List<string>();

        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
        await CreateSandboxAsync(sessionId, cpu, memory, 5, egressHosts).ConfigureAwait(false);

        return new JObject
        {
            ["session_id"] = sessionId,
            ["state"] = "Running",
            ["cpu"] = cpu,
            ["memory"] = memory
        };
    }

    private async Task<JObject> McpDestroySessionAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        await DeleteSandboxAsync(sessionId).ConfigureAwait(false);
        return new JObject { ["success"] = true, ["session_id"] = sessionId };
    }

    // ─── ACA Data Plane Client ────────────────────────────────────────────────────

    private string GetDataPlaneUrl()
    {
        return ACA_DATA_PLANE_BASE;
    }

    private string GetSandboxBasePath(string sessionId)
    {
        return $"/subscriptions/{ACA_SUBSCRIPTION_ID}/resourceGroups/{ACA_RESOURCE_GROUP}/sandboxGroups/{ACA_SANDBOX_GROUP}/sandboxes/{sessionId}";
    }

    private string GetSandboxGroupPath()
    {
        return $"/subscriptions/{ACA_SUBSCRIPTION_ID}/resourceGroups/{ACA_RESOURCE_GROUP}/sandboxGroups/{ACA_SANDBOX_GROUP}";
    }

    private async Task<JObject> CreateSandboxAsync(string sessionId, string cpu, string memory, int autoSuspendMinutes, List<string> egressAllowHosts)
    {
        var requestBody = new JObject
        {
            ["sourcesRef"] = new JObject
            {
                ["diskImage"] = ACA_DISK_IS_PUBLIC
                    ? new JObject { ["name"] = ACA_DISK_IMAGE, ["isPublic"] = true }
                    : new JObject { ["id"] = ACA_DISK_IMAGE, ["isPublic"] = false }
            },
            ["vmmType"] = "CloudHypervisor",
            ["resources"] = new JObject
            {
                ["cpu"] = cpu,
                ["memory"] = memory == "2048Mi" ? "2Gi" : memory == "4096Mi" ? "4Gi" : memory == "8192Mi" ? "8Gi" : "2Gi",
                ["disk"] = "20Gi"
            },
            ["lifecycle"] = new JObject
            {
                ["autoSuspendPolicy"] = new JObject
                {
                    ["enabled"] = true,
                    ["interval"] = autoSuspendMinutes * 60,
                    ["mode"] = "Memory"
                },
                ["autoDeletePolicy"] = new JObject
                {
                    ["enabled"] = false,
                    ["deleteIntervalInSeconds"] = 3600
                }
            }
        };

        var url = $"{GetDataPlaneUrl()}{GetSandboxGroupPath()}/sandboxes?includeDebug=true";
        var response = await SendAcaRequestAsync(HttpMethod.Put, url, requestBody).ConfigureAwait(false);

        // Extract the sandbox ID from the response
        var sandboxId = response.Value<string>("id") ?? response.Value<string>("name") ?? sessionId;
        return response;
    }

    private async Task<JObject> ExecInSandboxAsync(string sessionId, string command, int timeoutSeconds)
    {
        var requestBody = new JObject
        {
            ["command"] = command
        };

        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}/executeShellCommand";
        var response = await SendAcaRequestAsync(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
        return response;
    }

    private async Task WriteFileToSandboxAsync(string sessionId, string filePath, string base64Content)
    {
        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}/files{filePath}";
        var bytes = Convert.FromBase64String(base64Content);

        var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JObject> ReadFileFromSandboxAsync(string sessionId, string filePath)
    {
        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}/files{filePath}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = filePath.Split('/').Last();

        return new JObject
        {
            ["fileName"] = fileName,
            ["content"] = Convert.ToBase64String(bytes),
            ["sizeBytes"] = bytes.Length,
            ["mimeType"] = contentType
        };
    }

    private async Task<JArray> ListFilesInSandboxAsync(string sessionId, string path)
    {
        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}/files{path}?list=true";
        var response = await SendAcaRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);

        var files = response["entries"] as JArray ?? new JArray();
        var result = new JArray();
        foreach (var file in files)
        {
            result.Add(new JObject
            {
                ["name"] = file.Value<string>("name"),
                ["size"] = file.Value<long?>("size") ?? 0,
                ["modified"] = file.Value<string>("lastModified") ?? "",
                ["isDirectory"] = file.Value<bool?>("isDirectory") ?? false
            });
        }
        return result;
    }

    private async Task<JObject> GetSandboxStatusAsync(string sessionId)
    {
        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}";
        var response = await SendAcaRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);

        var props = response["properties"] as JObject ?? new JObject();
        return new JObject
        {
            ["sessionId"] = sessionId,
            ["state"] = props.Value<string>("state") ?? "Unknown",
            ["cpu"] = props.Value<string>("cpu") ?? "1000m",
            ["memory"] = props.Value<string>("memory") ?? "2048Mi",
            ["createdAt"] = props.Value<string>("createdAt") ?? "",
            ["autoSuspendMinutes"] = (props.Value<int?>("autoSuspendSeconds") ?? 300) / 60
        };
    }

    private async Task DeleteSandboxAsync(string sessionId)
    {
        var url = $"{GetDataPlaneUrl()}{GetSandboxBasePath(sessionId)}";
        await SendAcaRequestAsync(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> SendAcaRequestAsync(HttpMethod method, string url, JObject body)
    {
        var request = new HttpRequestMessage(method, url);

        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        // ACA Sandboxes data plane requires this header

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"ACA API error ({(int)response.StatusCode}): {responseBody}");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
            return new JObject();

        return JObject.Parse(responseBody);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private string BuildExecCommand(string language, string code, JObject variables)
    {
        if (!LANGUAGE_COMMANDS.TryGetValue(language, out var baseCommand))
        {
            baseCommand = LANGUAGE_COMMANDS["python"];
        }

        // For multi-line code or code with special chars, use base64 file approach
        if (code.Contains("\n") || code.Contains("'") || code.Contains("\"") || code.Contains("$"))
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
            var ext = GetFileExtension(language);
            var filePath = $"/tmp/_code{ext}";

            // For modes that need variables
            if (variables != null && (language == "prompt" || language == "regex"))
            {
                var varsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(variables.ToString(Newtonsoft.Json.Formatting.None)));
                return $"echo '{varsB64}' | base64 -d > /tmp/_vars.json && echo '{base64}' | base64 -d > {filePath} && {GetFileExecCommand(language, filePath)} --vars /tmp/_vars.json";
            }

            return $"echo '{base64}' | base64 -d > {filePath} && {GetFileExecCommand(language, filePath)}";
        }

        // Simple single-line code: inline execution
        if (language == "sql")
        {
            return $"{baseCommand} \"{EscapeForShell(code)}\"";
        }

        return $"{baseCommand} \"{EscapeForShell(code)}\"";
    }

    private string GetFileExtension(string language)
    {
        return language switch
        {
            "python" => ".py",
            "javascript" => ".js",
            "typescript" => ".ts",
            "powerfx" => ".pfx",
            "bash" => ".sh",
            "powershell" => ".ps1",
            "ruby" => ".rb",
            "perl" => ".pl",
            "php" => ".php",
            "sql" => ".sql",
            _ => ".txt"
        };
    }

    private string GetFileExecCommand(string language, string filePath)
    {
        return language switch
        {
            "python" => $"python3 {filePath}",
            "javascript" => $"node {filePath}",
            "typescript" => $"npx tsx {filePath}",
            "powerfx" => $"pfx eval \"$(cat {filePath})\"",
            "bash" => $"bash {filePath}",
            "powershell" => $"pwsh -File {filePath}",
            "ruby" => $"ruby {filePath}",
            "perl" => $"perl {filePath}",
            "php" => $"php {filePath}",
            "sql" => $"sqlite3 :memory: < {filePath}",
            "adaptivecard" => $"node /opt/tools/validate-card.js < {filePath}",
            "fetchxml" => $"python3 /opt/tools/fetchxml_eval.py < {filePath}",
            "openapi-lint" => $"node /opt/tools/lint-openapi.js < {filePath}",
            "prompt" => $"python3 /opt/tools/render_prompt.py < {filePath}",
            "expression" => $"dotnet /opt/tools/expr-eval.dll < {filePath}",
            "regex" => $"python3 /opt/tools/regex_test.py < {filePath}",
            _ => $"python3 {filePath}"
        };
    }

    private string EscapeForShell(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    private async Task<JObject> ReadBodyAsync()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, JObject body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ─── MCP Response Helpers ─────────────────────────────────────────────────────

    private HttpResponseMessage McpSuccess(JToken id, JObject result)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage McpError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ─── App Insights ─────────────────────────────────────────────────────────────

    private void LogToAppInsights(string eventName, IDictionary<string, string> properties = null)
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
                        ["name"] = $"ACACodeInterpreter.{eventName}",
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
                "application/json"
            );

            // Fire and forget — don't await telemetry
            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
