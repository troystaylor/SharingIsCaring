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
    // ========================================
    // CONFIGURATION
    // ========================================

    /// <summary>
    /// Application Insights connection string (optional)
    /// Get from: Azure Portal → Application Insights → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>MCP Server name</summary>
    private const string ServerName = "foundry-optimind";
    
    /// <summary>MCP Server version</summary>
    private const string ServerVersion = "1.0.0";
    
    /// <summary>MCP Protocol version supported</summary>
    private const string ProtocolVersion = "2025-11-25";

    /// <summary>
    /// OptiMind system prompt (recommended by the OptiMind team)
    /// </summary>
    private const string OPTIMIND_SYSTEM_PROMPT = @"You are an expert in optimization and mixed integer programming. You are given an optimization problem and you need to solve it using gurobipy.
Reason step by step before generating the gurobipy code.
When you respond, first think carefully.
After thinking, output the math modeling of the problem.
Finally output a ```python ...``` code block that solves the problem.
The code must include:
import gurobipy as gp
from gurobipy import GRB";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        this.Context.Logger.LogInformation($"[{correlationId}] Request received: {this.Context.OperationId}");

        try
        {
            _ = LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                Path = this.Context.Request.RequestUri.AbsolutePath,
                Method = this.Context.Request.Method.Method
            });

            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;

                case "FormulateOptimization":
                    response = await HandleFormulateOptimizationAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ChatCompletion":
                    response = await HandleChatCompletionAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ParseFormulation":
                    response = HandleParseFormulation(correlationId);
                    break;

                case "RefineOptimization":
                    response = await HandleRefineOptimizationAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ExplainOptimization":
                    response = await HandleExplainOptimizationAsync(correlationId).ConfigureAwait(false);
                    break;

                default:
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Error: {ex.Message}");
            _ = LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // OPERATION HANDLERS
    // ========================================

    /// <summary>
    /// Handles FormulateOptimization requests.
    /// Injects the OptiMind system prompt and restructures the request/response.
    /// </summary>
    private async Task<HttpResponseMessage> HandleFormulateOptimizationAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var problemDescription = body.Value<string>("problem_description");
        var additionalContext = body.Value<string>("additional_context");
        var temperature = body["temperature"]?.Value<float>() ?? 0.9f;
        var maxTokens = body["max_tokens"]?.Value<int>() ?? 4096;

        if (string.IsNullOrWhiteSpace(problemDescription))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"problem_description is required\"}")
            };
        }

        _ = LogToAppInsights("FormulateOptimizationRequest", new
        {
            CorrelationId = correlationId,
            ProblemLength = problemDescription.Length,
            HasAdditionalContext = !string.IsNullOrWhiteSpace(additionalContext)
        });

        // Build the user message, optionally appending additional context
        var userMessage = problemDescription;
        if (!string.IsNullOrWhiteSpace(additionalContext))
        {
            userMessage += "\n\nAdditional context:\n" + additionalContext;
        }

        // Build the chat completion request with the OptiMind system prompt
        var chatRequest = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = OPTIMIND_SYSTEM_PROMPT },
                new JObject { ["role"] = "user", ["content"] = userMessage }
            },
            ["temperature"] = temperature,
            ["top_p"] = 1.0,
            ["max_tokens"] = maxTokens
        };

        // Rewrite the request to the AI Services chat completions endpoint
        var optimindUri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = optimindUri;
        this.Context.Request.Content = new StringContent(
            chatRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("FormulateOptimizationError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Extract the formulation from the response
        var content = result["choices"]?[0]?["message"]?["content"]?.ToString() ?? "No formulation generated";

        var formattedResponse = new JObject
        {
            ["formulation"] = content,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };

        _ = LogToAppInsights("FormulateOptimizationResult", new
        {
            CorrelationId = correlationId,
            FormulationLength = content.Length,
            PromptTokens = result["usage"]?["prompt_tokens"]?.Value<int>() ?? 0,
            CompletionTokens = result["usage"]?["completion_tokens"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(formattedResponse.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Handles Chat Completion requests. Ensures content is returned as a string.
    /// Borrowed from the parent Foundry connector for explaining optimization results.
    /// </summary>
    private async Task<HttpResponseMessage> HandleChatCompletionAsync(string correlationId)
    {
        _ = LogToAppInsights("ChatCompletionRequest", new
        {
            CorrelationId = correlationId,
            RequestUrl = this.Context.Request.RequestUri.ToString()
        });

        // Rewrite the URL to /models/chat/completions (the AI Services endpoint)
        var originalUri = this.Context.Request.RequestUri;
        var newUri = new UriBuilder(originalUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = newUri;

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ChatCompletionError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Ensure content values in choices are strings
        var choices = result["choices"] as JArray;
        if (choices != null)
        {
            foreach (var choice in choices)
            {
                var message = choice["message"];
                if (message != null)
                {
                    var content = message["content"];
                    if (content != null && content.Type != JTokenType.String)
                    {
                        message["content"] = content.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }
        }

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Pass-through handler - forwards the request and returns the response unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    // ========================================
    // PARSE / REFINE / EXPLAIN HANDLERS
    // ========================================

    /// <summary>
    /// Parses a formulation response into reasoning, mathematical model, and Python code.
    /// Runs entirely in the connector — no API call.
    /// </summary>
    private HttpResponseMessage HandleParseFormulation(string correlationId)
    {
        var bodyString = this.Context.Request.Content.ReadAsStringAsync().Result;
        var body = JObject.Parse(bodyString);
        var formulation = body.Value<string>("formulation") ?? "";

        _ = LogToAppInsights("ParseFormulationRequest", new
        {
            CorrelationId = correlationId,
            FormulationLength = formulation.Length
        });

        var parsed = ParseFormulationText(formulation);

        _ = LogToAppInsights("ParseFormulationResult", new
        {
            CorrelationId = correlationId,
            HasCode = parsed["has_code"].Value<bool>(),
            ReasoningLength = (parsed["reasoning"]?.ToString() ?? "").Length,
            ModelLength = (parsed["mathematical_model"]?.ToString() ?? "").Length,
            CodeLength = (parsed["python_code"]?.ToString() ?? "").Length
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(parsed.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    /// <summary>
    /// Parses raw formulation text into structured components.
    /// Used by both the REST operation and MCP tool.
    /// </summary>
    private JObject ParseFormulationText(string formulation)
    {
        var reasoning = "";
        var mathematicalModel = "";
        var pythonCode = "";
        var hasCode = false;

        // Extract Python code blocks (```python ... ```)
        var codeMatch = System.Text.RegularExpressions.Regex.Match(
            formulation,
            @"```python\s*\n(.*?)```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (codeMatch.Success)
        {
            pythonCode = codeMatch.Groups[1].Value.Trim();
            hasCode = true;
        }

        // Split on common section markers to extract reasoning and math model
        // OptiMind typically outputs: thinking/reasoning → math formulation → code
        var textWithoutCode = formulation;
        if (hasCode)
        {
            textWithoutCode = formulation.Substring(0, codeMatch.Index).Trim();
        }

        // Look for math model markers (common patterns from OptiMind)
        var mathMarkers = new[]
        {
            @"(?i)(?:mathematical\s+(?:model|formulation|programming))",
            @"(?i)(?:optimization\s+(?:model|formulation))",
            @"(?i)(?:MILP\s+(?:model|formulation))",
            @"(?i)(?:linear\s+programming)",
            @"(?i)(?:objective\s+function)"
        };

        var mathPattern = string.Join("|", mathMarkers);
        var mathMatch = System.Text.RegularExpressions.Regex.Match(
            textWithoutCode, mathPattern);

        if (mathMatch.Success)
        {
            // Find the start of the line containing the math marker
            var lineStart = textWithoutCode.LastIndexOf('\n', mathMatch.Index);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            reasoning = textWithoutCode.Substring(0, lineStart).Trim();
            mathematicalModel = textWithoutCode.Substring(lineStart).Trim();
        }
        else
        {
            // No clear separation — put everything in reasoning
            reasoning = textWithoutCode.Trim();
        }

        return new JObject
        {
            ["reasoning"] = reasoning,
            ["mathematical_model"] = mathematicalModel,
            ["python_code"] = pythonCode,
            ["has_code"] = hasCode
        };
    }

    /// <summary>
    /// Refines a previous optimization formulation based on user feedback.
    /// </summary>
    private async Task<HttpResponseMessage> HandleRefineOptimizationAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var originalFormulation = body.Value<string>("original_formulation");
        var feedback = body.Value<string>("feedback");
        var temperature = body["temperature"]?.Value<float>() ?? 0.9f;
        var maxTokens = body["max_tokens"]?.Value<int>() ?? 4096;

        if (string.IsNullOrWhiteSpace(originalFormulation))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"original_formulation is required\"}")
            };
        }

        if (string.IsNullOrWhiteSpace(feedback))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"feedback is required\"}")
            };
        }

        _ = LogToAppInsights("RefineOptimizationRequest", new
        {
            CorrelationId = correlationId,
            OriginalLength = originalFormulation.Length,
            FeedbackLength = feedback.Length
        });

        var refinementPrompt = $@"Here is a previous optimization formulation:

---
{originalFormulation}
---

The user wants the following changes:
{feedback}

Modify the formulation based on the feedback while preserving all other constraints and structure. Output the updated mathematical model and updated gurobipy code.";

        var chatRequest = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = OPTIMIND_SYSTEM_PROMPT },
                new JObject { ["role"] = "user", ["content"] = refinementPrompt }
            },
            ["temperature"] = temperature,
            ["top_p"] = 1.0,
            ["max_tokens"] = maxTokens
        };

        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            chatRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("RefineOptimizationError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);
        var content = result["choices"]?[0]?["message"]?["content"]?.ToString() ?? "No refined formulation generated";

        var formattedResponse = new JObject
        {
            ["formulation"] = content,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };

        _ = LogToAppInsights("RefineOptimizationResult", new
        {
            CorrelationId = correlationId,
            FormulationLength = content.Length,
            PromptTokens = result["usage"]?["prompt_tokens"]?.Value<int>() ?? 0,
            CompletionTokens = result["usage"]?["completion_tokens"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(formattedResponse.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Returns a plain-English explanation of an optimization formulation.
    /// </summary>
    private async Task<HttpResponseMessage> HandleExplainOptimizationAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var formulation = body.Value<string>("formulation");
        var audience = body.Value<string>("audience") ?? "business stakeholder";
        var maxTokens = body["max_tokens"]?.Value<int>() ?? 2048;

        if (string.IsNullOrWhiteSpace(formulation))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"formulation is required\"}")
            };
        }

        _ = LogToAppInsights("ExplainOptimizationRequest", new
        {
            CorrelationId = correlationId,
            FormulationLength = formulation.Length,
            Audience = audience
        });

        var explainPrompt = $@"Explain the following optimization formulation in plain English for a {audience}. Cover:
1. What decisions are being made
2. What is being optimized (minimized or maximized)
3. What constraints must be satisfied
4. Any important assumptions

Do not include any code or mathematical notation. Use clear, non-technical language appropriate for the audience.

Formulation:
{formulation}";

        var chatRequest = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = "You are an expert at explaining optimization models in simple, clear language. Tailor your explanation to the specified audience." },
                new JObject { ["role"] = "user", ["content"] = explainPrompt }
            },
            ["temperature"] = 0.7,
            ["top_p"] = 1.0,
            ["max_tokens"] = maxTokens
        };

        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            chatRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ExplainOptimizationError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);
        var content = result["choices"]?[0]?["message"]?["content"]?.ToString() ?? "No explanation generated";

        var formattedResponse = new JObject
        {
            ["explanation"] = content,
            ["usage"] = result["usage"]
        };

        _ = LogToAppInsights("ExplainOptimizationResult", new
        {
            CorrelationId = correlationId,
            ExplanationLength = content.Length,
            Audience = audience
        });

        response.Content = CreateJsonContent(formattedResponse.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method
            });

            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleMcpToolsList(requestId);

                case "tools/call":
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject 
                    { 
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } 
                    });

                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Microsoft Foundry OptiMind",
                ["description"] = "Translate business optimization problems into mathematical formulations and GurobiPy code"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
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
                ["name"] = "formulate_optimization",
                ["description"] = "Translate a business optimization problem described in natural language into a mathematical formulation (MILP) and executable GurobiPy Python code. Supports scheduling, routing, supply chain, resource allocation, and network design problems.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["problem_description"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The optimization problem in natural language. Include decisions, constraints, and the objective (minimize cost, maximize profit, etc.)"
                        },
                        ["additional_context"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional additional data, parameters, or constraints"
                        }
                    },
                    ["required"] = new JArray { "problem_description" }
                }
            },
            new JObject
            {
                ["name"] = "chat_completion",
                ["description"] = "Send a general chat message to the OptiMind model. Use to explain optimization results, clarify constraints, or discuss optimization concepts in plain language.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The user's message or question"
                        },
                        ["system_prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional system instructions for the model"
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            },
            new JObject
            {
                ["name"] = "parse_formulation",
                ["description"] = "Parse an optimization formulation into separate components: reasoning, mathematical model, and Python code. No API call — runs instantly.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["formulation"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The raw formulation text to parse into components"
                        }
                    },
                    ["required"] = new JArray { "formulation" }
                }
            },
            new JObject
            {
                ["name"] = "refine_optimization",
                ["description"] = "Refine a previous optimization formulation based on feedback. Pass the original output and describe changes — added constraints, different objective, modified parameters.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["original_formulation"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The previous formulation output to refine"
                        },
                        ["feedback"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "What to change — e.g. 'add constraint that no driver works more than 8 hours'"
                        }
                    },
                    ["required"] = new JArray { "original_formulation", "feedback" }
                }
            },
            new JObject
            {
                ["name"] = "explain_optimization",
                ["description"] = "Explain an optimization formulation in plain English. Returns a non-technical summary covering decisions, objectives, constraints, and assumptions.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["formulation"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The optimization formulation to explain"
                        },
                        ["audience"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Target audience: 'business stakeholder', 'technical manager', or 'data scientist'. Default: 'business stakeholder'"
                        }
                    },
                    ["required"] = new JArray { "formulation" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ========================================
    // MCP TOOLS/CALL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        _ = LogToAppInsights("McpToolCall", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            JObject toolResult;
            
            switch (toolName.ToLowerInvariant())
            {
                case "formulate_optimization":
                    toolResult = await ExecuteFormulateOptimizationToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "chat_completion":
                    toolResult = await ExecuteChatCompletionToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "parse_formulation":
                    toolResult = ExecuteParseFormulationTool(arguments);
                    break;

                case "refine_optimization":
                    toolResult = await ExecuteRefineOptimizationToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "explain_optimization":
                    toolResult = await ExecuteExplainOptimizationToolAsync(arguments).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
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

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteFormulateOptimizationToolAsync(JObject arguments)
    {
        var problemDescription = arguments.Value<string>("problem_description");
        var additionalContext = arguments.Value<string>("additional_context");

        if (string.IsNullOrWhiteSpace(problemDescription))
        {
            throw new ArgumentException("'problem_description' is required");
        }

        var userMessage = problemDescription;
        if (!string.IsNullOrWhiteSpace(additionalContext))
        {
            userMessage += "\n\nAdditional context:\n" + additionalContext;
        }

        var requestBody = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = OPTIMIND_SYSTEM_PROMPT },
                new JObject { ["role"] = "user", ["content"] = userMessage }
            },
            ["temperature"] = 0.9,
            ["top_p"] = 1.0,
            ["max_tokens"] = 4096
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["formulation"] = content ?? "No formulation generated",
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };
    }

    private async Task<JObject> ExecuteChatCompletionToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        var systemPrompt = arguments.Value<string>("system_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });

        var requestBody = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = messages,
            ["temperature"] = 0.9,
            ["top_p"] = 1.0,
            ["max_tokens"] = 4096
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["response"] = content ?? "No response generated",
            ["usage"] = result["usage"]
        };
    }

    private JObject ExecuteParseFormulationTool(JObject arguments)
    {
        var formulation = arguments.Value<string>("formulation");
        if (string.IsNullOrWhiteSpace(formulation))
        {
            throw new ArgumentException("'formulation' is required");
        }

        return ParseFormulationText(formulation);
    }

    private async Task<JObject> ExecuteRefineOptimizationToolAsync(JObject arguments)
    {
        var originalFormulation = arguments.Value<string>("original_formulation");
        var feedback = arguments.Value<string>("feedback");

        if (string.IsNullOrWhiteSpace(originalFormulation))
        {
            throw new ArgumentException("'original_formulation' is required");
        }
        if (string.IsNullOrWhiteSpace(feedback))
        {
            throw new ArgumentException("'feedback' is required");
        }

        var refinementPrompt = $@"Here is a previous optimization formulation:

---
{originalFormulation}
---

The user wants the following changes:
{feedback}

Modify the formulation based on the feedback while preserving all other constraints and structure. Output the updated mathematical model and updated gurobipy code.";

        var requestBody = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = OPTIMIND_SYSTEM_PROMPT },
                new JObject { ["role"] = "user", ["content"] = refinementPrompt }
            },
            ["temperature"] = 0.9,
            ["top_p"] = 1.0,
            ["max_tokens"] = 4096
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["formulation"] = content ?? "No refined formulation generated",
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };
    }

    private async Task<JObject> ExecuteExplainOptimizationToolAsync(JObject arguments)
    {
        var formulation = arguments.Value<string>("formulation");
        var audience = arguments.Value<string>("audience") ?? "business stakeholder";

        if (string.IsNullOrWhiteSpace(formulation))
        {
            throw new ArgumentException("'formulation' is required");
        }

        var explainPrompt = $@"Explain the following optimization formulation in plain English for a {audience}. Cover:
1. What decisions are being made
2. What is being optimized (minimized or maximized)
3. What constraints must be satisfied
4. Any important assumptions

Do not include any code or mathematical notation. Use clear, non-technical language appropriate for the audience.

Formulation:
{formulation}";

        var requestBody = new JObject
        {
            ["model"] = "microsoft/OptiMind-SFT",
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = "You are an expert at explaining optimization models in simple, clear language. Tailor your explanation to the specified audience." },
                new JObject { ["role"] = "user", ["content"] = explainPrompt }
            },
            ["temperature"] = 0.7,
            ["top_p"] = 1.0,
            ["max_tokens"] = 2048
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["explanation"] = content ?? "No explanation generated",
            ["usage"] = result["usage"]
        };
    }

    // ========================================
    // API HELPERS
    // ========================================

    private string BuildApiUrl(string path)
    {
        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        return $"https://{host}{path}";
    }

    private async Task<JObject> SendApiRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        
        // Forward authorization
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        // Forward API key
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
        }

        return JObject.Parse(content);
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

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
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return;
            }

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = ServerName,
                ["ServerVersion"] = ServerVersion
            };

            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("InstrumentationKey=".Length);
            }
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("IngestionEndpoint=".Length);
            }
        }
        return "https://dc.services.visualstudio.com/";
    }
}
