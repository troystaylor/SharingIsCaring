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
    private const string ServerName = "agent-governance-toolkit";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpRequestAsync().ConfigureAwait(false);

            default:
                // All REST operations pass through to Container App
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                    .ConfigureAwait(false);
        }
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(requestBody);
        }
        catch (Exception)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: invalid JSON");
        }

        var method = request.Value<string>("method");
        var requestId = request["id"];

        // Notifications (no id) — return 202 Accepted
        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        switch (method)
        {
            case "initialize":
                return HandleMcpInitialize(requestId);

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleMcpToolsList(requestId);

            case "tools/call":
                return await HandleMcpToolsCallAsync(request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            case "completion/complete":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["completion"] = new JObject { ["values"] = new JArray() }
                });

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JToken requestId)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        });
    }

    // ========================================
    // MCP TOOLS LIST
    // ========================================

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "evaluate_action",
                ["description"] = "Evaluate a proposed tool call against governance policies before execution. Call this BEFORE executing any action that modifies data, sends messages, or accesses sensitive resources. Returns allow/deny with the reason.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["tool_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The tool or action to evaluate (e.g., file_write, send_email, delete_record)"
                        },
                        ["agent_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier for the agent requesting the action"
                        },
                        ["args"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "JSON string of arguments for the tool call"
                        }
                    },
                    ["required"] = new JArray { "tool_name" }
                }
            },
            new JObject
            {
                ["name"] = "check_compliance",
                ["description"] = "Grade a proposed action against regulatory frameworks (EU AI Act, HIPAA, SOC2, OWASP Agentic AI Top 10). Call this before handling regulated data or performing high-risk operations.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["tool_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The tool or action to check"
                        },
                        ["agent_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier for the agent"
                        },
                        ["framework"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Regulatory framework: OWASP-Agentic-2026, EU-AI-Act, HIPAA, or SOC2"
                        }
                    },
                    ["required"] = new JArray { "tool_name" }
                }
            },
            new JObject
            {
                ["name"] = "score_trust",
                ["description"] = "Get or update the dynamic trust score for an agent. Scores range 0-1000 across five tiers (Untrusted/Restricted/Standard/Trusted/Critical) mapping to execution rings (Ring0-Ring3). Use to decide privilege levels or when to escalate to a human.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["agent_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier for the agent to score"
                        },
                        ["action"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Trust action: positive (boost score), negative (penalize), set (absolute value). Omit to read current score."
                        },
                        ["amount"] = new JObject
                        {
                            ["type"] = "number",
                            ["description"] = "Amount for the trust action"
                        }
                    },
                    ["required"] = new JArray { "agent_id" }
                }
            },
            new JObject
            {
                ["name"] = "detect_injection",
                ["description"] = "Scan text for prompt injection attacks. Detects 7 types: DirectOverride, DelimiterAttack, RolePlay, ContextManipulation, SqlInjection, CanaryLeak, Custom. Call this before processing any user-provided input.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to scan for injection attacks"
                        }
                    },
                    ["required"] = new JArray { "text" }
                }
            },
            new JObject
            {
                ["name"] = "log_audit",
                ["description"] = "Record a completed action to the governance audit trail. Call this after every significant action for compliance logging and forensic analysis.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["agent_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier for the agent that performed the action"
                        },
                        ["action"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The action that was performed"
                        },
                        ["tool_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The tool that was used"
                        },
                        ["result"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The outcome of the action"
                        }
                    },
                    ["required"] = new JArray { "action" }
                }
            },
            new JObject
            {
                ["name"] = "check_circuit_breaker",
                ["description"] = "Check if a downstream service's circuit breaker is open (rejecting calls), closed (normal), or half-open (testing recovery). Call this before making calls to services that may be experiencing failures.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["service_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier for the downstream service to check"
                        }
                    },
                    ["required"] = new JArray { "service_id" }
                }
            },
            new JObject
            {
                ["name"] = "scan_mcp_tool",
                ["description"] = "Scan an MCP tool definition for security risks: tool poisoning, typosquatting, hidden instructions, insecure transport, and data exfiltration. Call this before connecting to unknown or untrusted MCP servers.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["tool_definition"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The MCP tool definition (JSON string) to scan"
                        }
                    },
                    ["required"] = new JArray { "tool_definition" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ========================================
    // MCP TOOLS/CALL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        try
        {
            // Map MCP tool names to Container App endpoints
            string apiPath;
            JObject apiBody;

            switch (toolName.ToLowerInvariant())
            {
                case "evaluate_action":
                    apiPath = "/api/evaluate";
                    apiBody = new JObject
                    {
                        ["toolName"] = arguments.Value<string>("tool_name"),
                        ["agentId"] = arguments.Value<string>("agent_id"),
                        ["args"] = ParseArgsString(arguments.Value<string>("args"))
                    };
                    break;

                case "check_compliance":
                    apiPath = "/api/compliance";
                    apiBody = new JObject
                    {
                        ["toolName"] = arguments.Value<string>("tool_name"),
                        ["agentId"] = arguments.Value<string>("agent_id"),
                        ["framework"] = arguments.Value<string>("framework")
                    };
                    break;

                case "score_trust":
                    apiPath = "/api/trust/score";
                    apiBody = new JObject
                    {
                        ["agentId"] = arguments.Value<string>("agent_id"),
                        ["action"] = arguments.Value<string>("action"),
                        ["amount"] = arguments.Value<double?>("amount")
                    };
                    break;

                case "detect_injection":
                    apiPath = "/api/injection/detect";
                    apiBody = new JObject
                    {
                        ["text"] = arguments.Value<string>("text")
                    };
                    break;

                case "log_audit":
                    apiPath = "/api/audit";
                    apiBody = new JObject
                    {
                        ["agentId"] = arguments.Value<string>("agent_id"),
                        ["action"] = arguments.Value<string>("action"),
                        ["toolName"] = arguments.Value<string>("tool_name"),
                        ["result"] = arguments.Value<string>("result")
                    };
                    break;

                case "check_circuit_breaker":
                    apiPath = "/api/circuit-breaker";
                    apiBody = new JObject
                    {
                        ["serviceId"] = arguments.Value<string>("service_id")
                    };
                    break;

                case "scan_mcp_tool":
                    apiPath = "/api/mcp/scan";
                    apiBody = new JObject
                    {
                        ["toolDefinition"] = arguments.Value<string>("tool_definition")
                    };
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            var result = await CallContainerAppAsync(apiPath, apiBody).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
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
                        ["text"] = $"Error: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // HELPERS
    // ========================================

    private async Task<JObject> CallContainerAppAsync(string path, JObject body)
    {
        var baseUri = this.Context.Request.RequestUri;
        var url = $"https://{baseUri.Host}{path}";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        // Forward API key header
        if (this.Context.Request.Headers.Contains("X-API-Key"))
        {
            request.Headers.Add("X-API-Key", this.Context.Request.Headers.GetValues("X-API-Key"));
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Governance API error ({(int)response.StatusCode}): {content}");
        }

        return JObject.Parse(content);
    }

    private JObject ParseArgsString(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            return new JObject();
        }

        try
        {
            return JObject.Parse(argsJson);
        }
        catch
        {
            return new JObject { ["raw"] = argsJson };
        }
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(responseObj.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        var responseObj = new JObject
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
            Content = CreateJsonContent(responseObj.ToString(Newtonsoft.Json.Formatting.None))
        };
    }
}
