using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // ─── MCP server identity ──────────────────────────────────────────────────────
    private const string MCP_SERVER_NAME = "azure-functions-hosted-skills";
    private const string MCP_SERVER_VERSION = "1.0.0";
    private const string MCP_PROTOCOL_VERSION = "2024-11-05";

    // Surfaced to the planner on initialize, so an agent does not guess slugs or
    // lose a conversation by dropping the session id between turns.
    private const string MCP_INSTRUCTIONS =
        "These tools call Azure Functions hosted skills: AI skills defined in .agent.md files, each identified by a slug. "
        + "Call list_skills first when you do not know which slug to use, and prefer a skill whose hasChat is true. "
        + "The agent argument is optional only when the function app hosts exactly one callable skill. "
        + "invoke_skill returns a session_id; pass it back on later calls to continue the same conversation, "
        + "and pass it to get_session_history to recall earlier turns. "
        + "A skill that starts a dynamic workflow returns before the work finishes, so poll get_workflow_status until runtime_status leaves Running.";

    // ─── Runtime surface ──────────────────────────────────────────────────────────
    // The hosted skills runtime registers its built-in endpoints under the Functions
    // route prefix. That prefix defaults to "api", but the official quickstart sets
    // it to "" in host.json, so both layouts are common. Calls are made as declared
    // and retried against the other layout on a 404.
    private const string ROUTE_PREFIX = "api";
    private const string FUNCTION_KEY_HEADER = "x-functions-key";
    private const string SESSION_HEADER = "x-ms-session-id";

    // The runtime registers no catalog endpoint, so skills are discovered from the
    // Functions host admin API, which lists every registered function and its route.
    private const string ADMIN_FUNCTIONS_PATH = "/admin/functions";
    private static readonly Regex BuiltinRoutePattern = new Regex(
        @"^agents/(?<slug>[A-Za-z0-9._-]+)(/(?<endpoint>chatstream|chat|history|workflows|workflow-status))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ─── Resilience ───────────────────────────────────────────────────────────────
    // Reads are retried when the app is throttling or still scaling out. An invoke is
    // never retried: a skill that sent mail or wrote a record must not run twice.
    private const int MAX_READ_ATTEMPTS = 3;
    private const int MAX_RETRY_DELAY_SECONDS = 8;
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new HashSet<HttpStatusCode>
    {
        (HttpStatusCode)429,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    // ─── Agent output budget ──────────────────────────────────────────────────────
    // A skill that scraped a dozen pages can return more than an agent's context can
    // hold, so MCP results are trimmed before they reach Copilot Studio.
    private const int MAX_MCP_RESULT_CHARS = 16000;
    private const int MAX_TOOL_DETAIL_CHARS = 600;

    // The runtime uses the session id as a blob path component and rejects anything
    // outside this set, so reject it here rather than paying for a round trip.
    private static readonly Regex SafeIdPattern = new Regex(@"^[A-Za-z0-9._-]{1,128}$", RegexOptions.Compiled);

    // ─── App Insights ─────────────────────────────────────────────────────────────
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        try
        {
            switch (operationId)
            {
                case "InvokeSkill":
                    return await HandleInvokeSkillAsync().ConfigureAwait(false);
                case "GetSessionHistory":
                    return await HandleGetSessionHistoryAsync().ConfigureAwait(false);
                case "ListSessionWorkflows":
                    return await HandleListSessionWorkflowsAsync().ConfigureAwait(false);
                case "GetWorkflowStatus":
                    return await HandleGetWorkflowStatusAsync().ConfigureAwait(false);
                case "ListSkills":
                    return await HandleListSkillsAsync().ConfigureAwait(false);
                case "InvokeMCP":
                    return await HandleMcpAsync().ConfigureAwait(false);
                default:
                    return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("OperationFailed", new Dictionary<string, string>
            {
                ["operationId"] = operationId ?? "unknown",
                ["error"] = ex.Message
            }).ConfigureAwait(false);

            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // REST operations
    // ══════════════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleInvokeSkillAsync()
    {
        var sessionId = GetHeader(SESSION_HEADER);
        if (!string.IsNullOrEmpty(sessionId) && !SafeIdPattern.IsMatch(sessionId))
        {
            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                "Session ID must be 1 to 128 characters of letters, digits, dots, hyphens or underscores. Leave it blank to start a new session.");
        }

        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var prompt = body["prompt"]?.ToString();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "Prompt is required.");
        }

        // Send only what the runtime reads, so extra fields cannot shift its parsing.
        var payloadJson = new JObject { ["prompt"] = prompt }.ToString(Newtonsoft.Json.Formatting.None);

        var response = await SendWithPrefixFallbackAsync(
            () =>
            {
                var request = CloneIncomingRequest();
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                return request;
            },
            retryTransientFailures: false).ConfigureAwait(false);

        var payload = await ReadResponseAsync(response).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(response.StatusCode, DescribeFailure(response.StatusCode, payload, response));
        }

        await LogToAppInsightsAsync("SkillInvoked", new Dictionary<string, string>
        {
            ["agent"] = GetAgentSlugFromPath() ?? "unknown",
            ["toolCallCount"] = (payload["tool_calls"] as JArray)?.Count.ToString() ?? "0"
        }).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, NormalizeSkillResponse(payload), response);
    }

    private async Task<HttpResponseMessage> HandleGetSessionHistoryAsync()
    {
        var sessionError = ValidateRequiredSession();
        if (sessionError != null)
        {
            return sessionError;
        }

        var response = await SendWithPrefixFallbackAsync(CloneIncomingRequest, retryTransientFailures: true).ConfigureAwait(false);
        var payload = await ReadResponseAsync(response).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(response.StatusCode, DescribeFailure(response.StatusCode, payload));
        }

        return CreateJsonResponse(HttpStatusCode.OK, payload, response);
    }

    private async Task<HttpResponseMessage> HandleListSessionWorkflowsAsync()
    {
        var sessionError = ValidateRequiredSession();
        if (sessionError != null)
        {
            return sessionError;
        }

        var response = await SendWithPrefixFallbackAsync(CloneIncomingRequest, retryTransientFailures: true).ConfigureAwait(false);
        var payload = await ReadResponseAsync(response).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(response.StatusCode, DescribeWorkflowFailure(response.StatusCode, payload));
        }

        if (payload["workflows"] is JArray workflows)
        {
            for (var i = 0; i < workflows.Count; i++)
            {
                if (workflows[i] is JObject workflow)
                {
                    workflows[i] = NormalizeWorkflow(workflow);
                }
            }
        }

        return CreateJsonResponse(HttpStatusCode.OK, payload, response);
    }

    private async Task<HttpResponseMessage> HandleGetWorkflowStatusAsync()
    {
        var sessionError = ValidateRequiredSession();
        if (sessionError != null)
        {
            return sessionError;
        }

        var response = await SendWithPrefixFallbackAsync(CloneIncomingRequest, retryTransientFailures: true).ConfigureAwait(false);
        var payload = await ReadResponseAsync(response).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(response.StatusCode, DescribeWorkflowFailure(response.StatusCode, payload));
        }

        return CreateJsonResponse(HttpStatusCode.OK, NormalizeWorkflow(payload), response);
    }

    private HttpResponseMessage ValidateRequiredSession()
    {
        var sessionId = GetHeader(SESSION_HEADER);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                "Session ID is required. Use the session ID returned by Invoke a hosted skill.");
        }

        if (!SafeIdPattern.IsMatch(sessionId))
        {
            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                "Session ID must be 1 to 128 characters of letters, digits, dots, hyphens or underscores.");
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Skill discovery
    // ══════════════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleListSkillsAsync()
    {
        return CreateJsonResponse(HttpStatusCode.OK, await BuildSkillCatalogAsync().ConfigureAwait(false));
    }

    private async Task<JObject> BuildSkillCatalogAsync()
    {
        // The admin API takes the master key. The connection's key is used as-is,
        // so discovery works when that key is the master key and degrades when it
        // is a narrower host or function key.
        var key = GetHeader(FUNCTION_KEY_HEADER);

        var authority = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        Func<HttpRequestMessage> factory = () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, authority + ADMIN_FUNCTIONS_PATH);
            if (!string.IsNullOrWhiteSpace(key))
            {
                request.Headers.TryAddWithoutValidation(FUNCTION_KEY_HEADER, key);
            }

            // An app behind App Service Authentication needs the caller's token too.
            if (this.Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            return request;
        };

        HttpResponseMessage response;
        try
        {
            response = await SendReadWithRetryAsync(factory).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return FallbackCatalog($"The function app's admin API could not be reached: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            return FallbackCatalog(DescribeAdminFailure(response.StatusCode));
        }

        var payload = await ReadResponseTokenAsync(response).ConfigureAwait(false);
        var functions = payload as JArray;
        if (functions == null)
        {
            return FallbackCatalog("The function app's admin API returned an unexpected response.");
        }

        var skills = ParseSkillsFromFunctions(functions);
        if (skills.Count == 0)
        {
            return FallbackCatalog("The function app has no hosted skills with built-in endpoints. Set builtin_endpoints.chat_api to true in an .agent.md file and redeploy.");
        }

        var value = new JArray();
        foreach (var slug in skills.Keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var skill = skills[slug];

            // A skill that serves only the debug UI cannot be invoked, so say so in
            // the picker rather than letting someone choose it and get a 404.
            if (!skill["hasChat"].Value<bool>())
            {
                skill["displayName"] = slug + " (no chat API)";
            }

            value.Add(skill);
        }

        return new JObject
        {
            ["value"] = value,
            ["source"] = "adminApi"
        };
    }

    private Dictionary<string, JObject> ParseSkillsFromFunctions(JArray functions)
    {
        var skills = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var function in functions.OfType<JObject>())
        {
            var route = GetBuiltinRoute(function);
            if (route == null)
            {
                continue;
            }

            var match = BuiltinRoutePattern.Match(route);
            if (!match.Success)
            {
                continue;
            }

            var slug = match.Groups["slug"].Value;
            JObject skill;
            if (!skills.TryGetValue(slug, out skill))
            {
                skill = new JObject
                {
                    ["slug"] = slug,
                    ["displayName"] = slug,
                    ["hasChat"] = false,
                    ["hasHistory"] = false,
                    ["hasWorkflows"] = false,
                    ["hasChatUi"] = false
                };
                skills[slug] = skill;
            }

            switch (match.Groups["endpoint"].Value.ToLowerInvariant())
            {
                case "chat":
                    skill["hasChat"] = true;
                    break;
                case "history":
                    skill["hasHistory"] = true;
                    break;
                case "workflows":
                case "workflow-status":
                    skill["hasWorkflows"] = true;
                    break;
                case "":
                    skill["hasChatUi"] = true;
                    break;
            }
        }

        return skills;
    }

    // Reads the HTTP route off a function's trigger binding, falling back to the
    // invoke URL for hosts that report routes only there.
    private static string GetBuiltinRoute(JObject function)
    {
        var bindings = function["config"]?["bindings"] as JArray;
        if (bindings != null)
        {
            foreach (var binding in bindings.OfType<JObject>())
            {
                var type = binding["type"]?.ToString();
                var route = binding["route"]?.ToString();
                if (!string.IsNullOrWhiteSpace(route)
                    && string.Equals(type, "httpTrigger", StringComparison.OrdinalIgnoreCase))
                {
                    return route.Trim('/');
                }
            }
        }

        var invokeUrl = function["invoke_url_template"]?.ToString();
        Uri parsed;
        if (!string.IsNullOrWhiteSpace(invokeUrl) && Uri.TryCreate(invokeUrl, UriKind.Absolute, out parsed))
        {
            var path = parsed.AbsolutePath.Trim('/');
            if (path.StartsWith(ROUTE_PREFIX + "/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(ROUTE_PREFIX.Length + 1);
            }

            return path;
        }

        return null;
    }

    // Discovery is a convenience. A failure returns an empty list and explains
    // itself, rather than putting an error where a picker expects a list.
    private JObject FallbackCatalog(string message)
    {
        return new JObject
        {
            ["value"] = new JArray(),
            ["source"] = "unavailable",
            ["message"] = message + " Enter the skill slug by hand: it is the .agent.md file name without its extension."
        };
    }

    private static string DescribeAdminFailure(HttpStatusCode statusCode)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return "Listing skills needs the function app's master key. Use that key as the connection's Function Key if you want the skill list.";

            case HttpStatusCode.NotFound:
                return "The function app's admin API is unavailable, which happens when admin isolation is enabled on the app.";

            default:
                return $"The function app's admin API returned {(int)statusCode} {statusCode}.";
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // MCP endpoint
    // ══════════════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var raw = await ReadRequestBodyTextAsync().ConfigureAwait(false);

        JObject request;
        try
        {
            request = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
        }

        var method = request["method"]?.ToString();
        var id = request["id"];
        var parameters = request["params"] as JObject ?? new JObject();

        switch (method)
        {
            case "initialize":
                return CreateJsonRpcSuccessResponse(id, new JObject
                {
                    ["protocolVersion"] = MCP_PROTOCOL_VERSION,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = MCP_SERVER_NAME,
                        ["version"] = MCP_SERVER_VERSION
                    },
                    ["instructions"] = MCP_INSTRUCTIONS
                });

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
            case "ping":
                return CreateJsonRpcSuccessResponse(id, new JObject());

            case "tools/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["tools"] = GetToolDefinitions() });

            case "tools/call":
                return await HandleToolsCallAsync(parameters, id).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["prompts"] = new JArray() });

            default:
                return CreateJsonRpcErrorResponse(id, -32601, "Method not found", method ?? string.Empty);
        }
    }

    private JArray GetToolDefinitions()
    {
        var agentProperty = new JObject
        {
            ["type"] = "string",
            ["description"] = "Slug of the hosted skill, which is its .agent.md file name without the extension. Optional when the function app hosts exactly one callable skill."
        };

        var sessionProperty = new JObject
        {
            ["type"] = "string",
            ["description"] = "Session to continue, as returned by a previous invoke_skill call."
        };

        return new JArray
        {
            new JObject
            {
                ["name"] = "list_skills",
                ["description"] = "List the hosted skills available in the function app, with the endpoints each one exposes. Call this first when you do not know which skill to use, or to check whether a skill supports dynamic workflows.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject()
                }
            },
            new JObject
            {
                ["name"] = "invoke_skill",
                ["description"] = "Send a prompt to an Azure Functions hosted skill and return its response. Use when a task needs the reasoning, tools or connectors that a hosted skill provides. Returns the session ID so a follow-up prompt can continue the same conversation.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Instruction or question for the hosted skill."
                        },
                        ["agent"] = agentProperty.DeepClone(),
                        ["session_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Session to continue. Omit to start a new session; the response reports the session that was created."
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            },
            new JObject
            {
                ["name"] = "get_session_history",
                ["description"] = "Return the user and assistant turns already recorded in a hosted skill session, oldest first. Use to recall what was said earlier in a conversation.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["session_id"] = sessionProperty.DeepClone(),
                        ["agent"] = agentProperty.DeepClone()
                    },
                    ["required"] = new JArray { "session_id" }
                }
            },
            new JObject
            {
                ["name"] = "list_session_workflows",
                ["description"] = "List the dynamic workflows a hosted skill started in a session, newest first. Use to check whether long-running work a skill kicked off has finished.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["session_id"] = sessionProperty.DeepClone(),
                        ["agent"] = agentProperty.DeepClone()
                    },
                    ["required"] = new JArray { "session_id" }
                }
            },
            new JObject
            {
                ["name"] = "get_workflow_status",
                ["description"] = "Return the runtime status and final output of one dynamic workflow run started by a hosted skill.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["session_id"] = sessionProperty.DeepClone(),
                        ["workflow_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Workflow run to inspect, as returned by list_session_workflows."
                        },
                        ["agent"] = agentProperty.DeepClone()
                    },
                    ["required"] = new JArray { "session_id", "workflow_id" }
                }
            }
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject parameters, JToken id)
    {
        var toolName = parameters["name"]?.ToString();
        var arguments = parameters["arguments"] as JObject ?? new JObject();

        try
        {
            JObject result;
            var agent = "n/a";

            if (toolName == "list_skills")
            {
                result = await BuildSkillCatalogAsync().ConfigureAwait(false);
            }
            else
            {
                agent = await ResolveAgentSlugAsync(arguments).ConfigureAwait(false);

                switch (toolName)
                {
                    case "invoke_skill":
                        result = await CallInvokeSkillToolAsync(agent, arguments).ConfigureAwait(false);
                        break;

                    case "get_session_history":
                        result = await SendRuntimeRequestAsync(
                            HttpMethod.Get,
                            $"/{ROUTE_PREFIX}/agents/{Uri.EscapeDataString(agent)}/history",
                            RequireArgument(arguments, "session_id"),
                            null).ConfigureAwait(false);
                        break;

                    case "list_session_workflows":
                        result = await SendRuntimeRequestAsync(
                            HttpMethod.Get,
                            $"/{ROUTE_PREFIX}/agents/{Uri.EscapeDataString(agent)}/workflows",
                            RequireArgument(arguments, "session_id"),
                            null).ConfigureAwait(false);
                        break;

                    case "get_workflow_status":
                        var workflowId = RequireArgument(arguments, "workflow_id");
                        result = await SendRuntimeRequestAsync(
                            HttpMethod.Get,
                            $"/{ROUTE_PREFIX}/agents/{Uri.EscapeDataString(agent)}/workflow-status?workflow_id={Uri.EscapeDataString(workflowId)}",
                            RequireArgument(arguments, "session_id"),
                            null).ConfigureAwait(false);
                        break;

                    default:
                        throw new ArgumentException($"Unknown tool: {toolName}");
                }
            }

            await LogToAppInsightsAsync("McpToolCalled", new Dictionary<string, string>
            {
                ["tool"] = toolName ?? "unknown",
                ["agent"] = agent
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = TrimForAgent(result)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("McpToolFailed", new Dictionary<string, string>
            {
                ["tool"] = toolName ?? "unknown",
                ["error"] = ex.Message
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(id, new JObject
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

    private async Task<JObject> CallInvokeSkillToolAsync(string agent, JObject arguments)
    {
        var prompt = RequireArgument(arguments, "prompt");
        var sessionId = arguments["session_id"]?.ToString();

        var payload = await SendRuntimeRequestAsync(
            HttpMethod.Post,
            $"/{ROUTE_PREFIX}/agents/{Uri.EscapeDataString(agent)}/chat",
            sessionId,
            new JObject { ["prompt"] = prompt }).ConfigureAwait(false);

        return NormalizeSkillResponse(payload);
    }

    private async Task<string> ResolveAgentSlugAsync(JObject arguments)
    {
        var agent = arguments["agent"]?.ToString();

        // Nothing named: an app that hosts exactly one callable skill has only one
        // sensible answer, so use it rather than making the agent guess.
        if (string.IsNullOrWhiteSpace(agent))
        {
            agent = await ResolveSoleChatSkillAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(agent))
        {
            throw new ArgumentException(
                "No skill was named, and this function app hosts more than one or none could be listed. Call list_skills to see what is available, then pass 'agent' with the slug you want.");
        }

        agent = agent.Trim();
        if (!SafeIdPattern.IsMatch(agent))
        {
            throw new ArgumentException($"'{agent}' is not a valid skill slug. Use letters, digits, dots, hyphens or underscores.");
        }

        return agent;
    }

    private async Task<string> ResolveSoleChatSkillAsync()
    {
        try
        {
            var catalog = await BuildSkillCatalogAsync().ConfigureAwait(false);
            var callable = (catalog["value"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Where(s => s["hasChat"]?.Value<bool>() == true)
                .ToList();

            return callable.Count == 1 ? callable[0]["slug"]?.ToString() : null;
        }
        catch
        {
            // Discovery is optional; fall through to asking the caller to name one.
            return null;
        }
    }

    private static string RequireArgument(JObject arguments, string name)
    {
        var value = arguments[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required.");
        }

        if (name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) && !SafeIdPattern.IsMatch(value))
        {
            throw new ArgumentException($"'{name}' must be 1 to 128 characters of letters, digits, dots, hyphens or underscores.");
        }

        return value;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Runtime calls
    // ══════════════════════════════════════════════════════════════════════════════

    private async Task<JObject> SendRuntimeRequestAsync(HttpMethod method, string pathAndQuery, string sessionId, JObject body)
    {
        var authority = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);

        Func<HttpRequestMessage> factory = () =>
        {
            var request = new HttpRequestMessage(method, authority + pathAndQuery);
            ForwardCallerCredentials(request);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                request.Headers.TryAddWithoutValidation(SESSION_HEADER, sessionId);
            }

            if (body != null)
            {
                request.Content = new StringContent(
                    body.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json");
            }

            return request;
        };

        // Only reads are replayed on throttling; an invoke runs once and reports it.
        var response = await SendWithPrefixFallbackAsync(
            factory,
            retryTransientFailures: method == HttpMethod.Get).ConfigureAwait(false);

        var payload = await ReadResponseAsync(response).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFailure(response.StatusCode, payload, response));
        }

        return payload;
    }

    // The connection's function key, or its Entra token, arrives on the inbound
    // request. Carry whichever is present onto calls this script makes.
    private void ForwardCallerCredentials(HttpRequestMessage request)
    {
        var functionKey = GetHeader(FUNCTION_KEY_HEADER);
        if (!string.IsNullOrEmpty(functionKey))
        {
            request.Headers.TryAddWithoutValidation(FUNCTION_KEY_HEADER, functionKey);
        }

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
    }

    private async Task<HttpResponseMessage> SendReadWithRetryAsync(Func<HttpRequestMessage> factory)
    {
        HttpResponseMessage response = null;

        for (var attempt = 1; attempt <= MAX_READ_ATTEMPTS; attempt++)
        {
            response = await this.Context.SendAsync(factory(), this.CancellationToken).ConfigureAwait(false);

            if (!RetryableStatusCodes.Contains(response.StatusCode) || attempt == MAX_READ_ATTEMPTS)
            {
                return response;
            }

            var delay = ResolveRetryDelay(response, attempt);
            await Task.Delay(delay, this.CancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    // Sends a request, and on a 404 tries the other route-prefix layout once. A 404
    // means nothing ran, so this is safe even for an operation that is not idempotent.
    private async Task<HttpResponseMessage> SendWithPrefixFallbackAsync(
        Func<HttpRequestMessage> factory,
        bool retryTransientFailures)
    {
        var response = retryTransientFailures
            ? await SendReadWithRetryAsync(factory).ConfigureAwait(false)
            : await this.Context.SendAsync(factory(), this.CancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            return response;
        }

        var alternate = factory();
        var flipped = FlipRoutePrefix(alternate.RequestUri);
        if (flipped == null)
        {
            return response;
        }

        alternate.RequestUri = flipped;

        var alternateResponse = retryTransientFailures
            ? await SendReadWithRetryAsync(() => CloneRequest(alternate)).ConfigureAwait(false)
            : await this.Context.SendAsync(alternate, this.CancellationToken).ConfigureAwait(false);

        // Keep the original 404 when the alternate layout is no better, so the error
        // describes the skill rather than the second guess.
        return alternateResponse.StatusCode == HttpStatusCode.NotFound ? response : alternateResponse;
    }

    // Adds the route prefix when it is absent, removes it when present. Returns null
    // when the path is not a built-in skill endpoint.
    private static Uri FlipRoutePrefix(Uri uri)
    {
        var path = uri.AbsolutePath;
        string flipped;

        if (path.StartsWith("/" + ROUTE_PREFIX + "/agents/", StringComparison.OrdinalIgnoreCase))
        {
            flipped = path.Substring(ROUTE_PREFIX.Length + 1);
        }
        else if (path.StartsWith("/agents/", StringComparison.OrdinalIgnoreCase))
        {
            flipped = "/" + ROUTE_PREFIX + path;
        }
        else
        {
            return null;
        }

        return new UriBuilder(uri) { Path = flipped }.Uri;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Content = source.Content;
        return clone;
    }

    // Honours Retry-After when the app sends one, and otherwise backs off gently.
    private static TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        double seconds = attempt;

        if (retryAfter != null)
        {
            if (retryAfter.Delta.HasValue)
            {
                seconds = retryAfter.Delta.Value.TotalSeconds;
            }
            else if (retryAfter.Date.HasValue)
            {
                seconds = (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }
        }

        if (seconds < 1)
        {
            seconds = 1;
        }

        return TimeSpan.FromSeconds(Math.Min(seconds, MAX_RETRY_DELAY_SECONDS));
    }

    private HttpRequestMessage CloneIncomingRequest()
    {
        var source = this.Context.Request;
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Response shaping
    // ══════════════════════════════════════════════════════════════════════════════

    private static JObject NormalizeSkillResponse(JObject payload)
    {
        if (payload["tool_calls"] is JArray toolCalls)
        {
            foreach (var toolCall in toolCalls.OfType<JObject>())
            {
                toolCall["arguments"] = AsText(toolCall["arguments"]);
                toolCall["result"] = AsText(toolCall["result"]);
            }
        }

        return payload;
    }

    private static JObject NormalizeWorkflow(JObject workflow)
    {
        workflow["custom_status"] = AsText(workflow["custom_status"]);
        workflow["output"] = AsText(workflow["output"]);

        // Give pickers something readable: an opaque instance id alone tells a maker
        // nothing about which run they are choosing.
        var id = workflow["workflow_id"]?.ToString();
        if (!string.IsNullOrWhiteSpace(id))
        {
            var status = workflow["runtime_status"]?.ToString();
            var started = workflow["created_time"]?.ToString();
            var label = id;

            if (!string.IsNullOrWhiteSpace(status))
            {
                label += " — " + status;
            }

            if (!string.IsNullOrWhiteSpace(started))
            {
                label += " (" + started + ")";
            }

            workflow["displayName"] = label;
        }

        return workflow;
    }

    // A tool's arguments, a tool's result and a workflow's output are whatever the
    // skill produced, so flatten anything structured into JSON text to match the
    // string types the Swagger definition promises.
    private static JToken AsText(JToken value)
    {
        if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
        {
            return JValue.CreateNull();
        }

        if (value.Type == JTokenType.String)
        {
            return value;
        }

        return new JValue(value.ToString(Newtonsoft.Json.Formatting.None));
    }

    // Keeps a verbose skill from crowding out an agent's context. Tool detail is
    // shortened first, because the skill's own answer is what the agent needs.
    private static string TrimForAgent(JObject result)
    {
        var text = result.ToString(Newtonsoft.Json.Formatting.Indented);
        if (text.Length <= MAX_MCP_RESULT_CHARS)
        {
            return text;
        }

        var trimmed = (JObject)result.DeepClone();
        if (trimmed["tool_calls"] is JArray toolCalls)
        {
            foreach (var toolCall in toolCalls.OfType<JObject>())
            {
                toolCall["arguments"] = Shorten(toolCall["arguments"], MAX_TOOL_DETAIL_CHARS);
                toolCall["result"] = Shorten(toolCall["result"], MAX_TOOL_DETAIL_CHARS);
            }
        }

        text = trimmed.ToString(Newtonsoft.Json.Formatting.Indented);
        if (text.Length <= MAX_MCP_RESULT_CHARS)
        {
            return text + $"\n\n[Tool call detail was shortened to fit. Use Invoke a hosted skill for the full payload.]";
        }

        return text.Substring(0, MAX_MCP_RESULT_CHARS)
            + $"\n\n[Truncated at {MAX_MCP_RESULT_CHARS} characters of {text.Length}. Use Invoke a hosted skill for the full payload.]";
    }

    private static JToken Shorten(JToken value, int maxChars)
    {
        var text = value?.Type == JTokenType.String ? value.ToString() : null;
        if (text == null || text.Length <= maxChars)
        {
            return value ?? JValue.CreateNull();
        }

        return new JValue(text.Substring(0, maxChars) + $"… [{text.Length - maxChars} more characters]");
    }

    private string DescribeFailure(HttpStatusCode statusCode, JObject payload, HttpResponseMessage response = null)
    {
        var detail = payload?["error"]?.ToString();
        var agent = GetAgentSlugFromPath();

        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return "The function key was rejected. Update the Function Key on the connection with a current host key, or a function key for this skill.";

            case HttpStatusCode.NotFound:
                return agent == null
                    ? "The hosted skill endpoint was not found. Confirm the skill exists and sets builtin_endpoints.chat_api to true."
                    : $"No hosted skill named '{agent}' exposes a chat API. Confirm the slug matches the .agent.md file name and that the file sets builtin_endpoints.chat_api to true.";

            case HttpStatusCode.RequestTimeout:
            case HttpStatusCode.GatewayTimeout:
                return "The hosted skill did not finish in time. Long-running work belongs in a dynamic workflow, which you can poll with Get dynamic workflow status.";

            case (HttpStatusCode)429:
                // An invoke is never replayed automatically, because the skill may
                // have already acted on the world before the throttle was applied.
                return "The function app is throttling requests." + DescribeRetryHint(response) + " This call was not retried, because rerunning a skill can repeat whatever it already did.";

            default:
                return string.IsNullOrWhiteSpace(detail)
                    ? $"The hosted skill returned {(int)statusCode} {statusCode}."
                    : detail;
        }
    }

    private static string DescribeRetryHint(HttpResponseMessage response)
    {
        var retryAfter = response?.Headers?.RetryAfter;
        if (retryAfter == null)
        {
            return string.Empty;
        }

        if (retryAfter.Delta.HasValue)
        {
            return $" Try again in {Math.Ceiling(retryAfter.Delta.Value.TotalSeconds)} seconds.";
        }

        if (retryAfter.Date.HasValue)
        {
            return $" Try again after {retryAfter.Date.Value.UtcDateTime:O}.";
        }

        return string.Empty;
    }

    private string DescribeWorkflowFailure(HttpStatusCode statusCode, JObject payload)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            return "This endpoint exists only for skills that enable dynamic workflows. Confirm the skill's .agent.md file turns workflows on, or that the workflow belongs to this session.";
        }

        return DescribeFailure(statusCode, payload);
    }
    private string GetAgentSlugFromPath()
    {
        var segments = this.Context.Request.RequestUri?.AbsolutePath?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments == null)
        {
            return null;
        }

        var index = Array.IndexOf(segments, "agents");
        return index >= 0 && index + 1 < segments.Length ? Uri.UnescapeDataString(segments[index + 1]) : null;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════════

    private string GetHeader(string name)
    {
        IEnumerable<string> values;
        if (this.Context.Request.Headers.TryGetValues(name, out values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private async Task<string> ReadRequestBodyTextAsync()
    {
        if (this.Context.Request.Content == null)
        {
            return string.Empty;
        }

        return await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<JObject> ReadRequestBodyAsync()
    {
        var raw = await ReadRequestBodyTextAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JObject();
        }

        try
        {
            return JObject.Parse(raw);
        }
        catch (JsonException)
        {
            return new JObject();
        }
    }

    private static async Task<JObject> ReadResponseAsync(HttpResponseMessage response)
    {
        var token = await ReadResponseTokenAsync(response).ConfigureAwait(false);
        return token as JObject ?? new JObject();
    }

    private static async Task<JToken> ReadResponseTokenAsync(HttpResponseMessage response)
    {
        if (response.Content == null)
        {
            return new JObject();
        }

        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JObject();
        }

        try
        {
            // Read timestamps as written. Left to itself, the parser turns them into
            // local DateTime values and renders them back in the host's culture.
            using (var reader = new JsonTextReader(new StringReader(raw)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JToken.ReadFrom(reader);
            }
        }
        catch (JsonException)
        {
            // A non-JSON body is almost always an HTML error page from the platform.
            return new JObject { ["error"] = raw.Length > 500 ? raw.Substring(0, 500) : raw };
        }
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, JObject payload, HttpResponseMessage source = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                payload.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };

        IEnumerable<string> sessionValues;
        if (source != null && source.Headers.TryGetValues(SESSION_HEADER, out sessionValues))
        {
            response.Headers.TryAddWithoutValidation(SESSION_HEADER, sessionValues.FirstOrDefault());
        }

        return response;
    }

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                new JObject { ["error"] = message }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (!string.IsNullOrEmpty(data))
        {
            error["data"] = data;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!APP_INSIGHTS_ENABLED
            || string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
        {
            return;
        }

        try
        {
            var telemetryData = new
            {
                name = "Microsoft.ApplicationInsights.Event",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = properties ?? new Dictionary<string, string>()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
