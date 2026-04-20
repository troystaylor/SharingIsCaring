using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ═══════════════════════════════════════════════════════════════════════════════
// Power Rubber Duck - Enterprise Multi-Perspective Analysis Engine
// Model Context Protocol connector for Copilot Studio
// ═══════════════════════════════════════════════════════════════════════════════

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokePowerRubberDuckMcp":
                return await ExecuteMcpAsync().ConfigureAwait(false);
            case "GetSecondOpinion":
            case "AnalyzeRisk":
            case "IdentifyCognitiveBiases":
            case "ComparativeAnalysis":
            case "ListResources":
            case "ReadResource":
                return await HandleDirectOperationAsync(this.Context.OperationId).ConfigureAwait(false);
            default:
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> ExecuteMcpAsync()
    {
        try
        {
            var requestBody = await this.Context.Request.Content.ReadAsStringAsync();
            var mcpRequest = JsonConvert.DeserializeObject<McpRequest>(requestBody);

            _ = LogToAppInsightsAsync("MCP_Request", new Dictionary<string, string>
            {
                { "method", mcpRequest?.Method ?? "unknown" },
                { "requestId", mcpRequest?.Id ?? "unknown" }
            });

            if (mcpRequest == null)
            {
                return this.ErrorResponseMessage("Invalid request", -32700);
            }

            McpResponse response = null;

            switch (mcpRequest.Method)
            {
                case "tools/list":
                    response = HandleToolsList(mcpRequest);
                    break;

                case "tools/call":
                    response = await HandleToolCall(mcpRequest);
                    break;

                case "resources/list":
                    response = HandleResourcesList(mcpRequest);
                    break;

                case "resources/read":
                    response = await HandleResourceRead(mcpRequest);
                    break;

                default:
                    response = new McpResponse
                    {
                        Id = mcpRequest.Id,
                        Error = JObject.FromObject(new { code = -32601, message = "Method not found" })
                    };
                    break;
            }

            var responseContent = JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.None);
            return CreateJsonResponse(System.Net.HttpStatusCode.OK, responseContent);
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("MCP_Handler_Error", ex);
            return this.ErrorResponseMessage(ex.Message, -32603);
        }
    }

    private async Task<HttpResponseMessage> HandleDirectOperationAsync(string operationId)
    {
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var args = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);

            object payloadData;
            switch (operationId)
            {
                case "GetSecondOpinion":
                    payloadData = new { result = await CallGetSecondOpinion(args).ConfigureAwait(false) };
                    break;
                case "AnalyzeRisk":
                    payloadData = new { result = await CallAnalyzeRisk(args).ConfigureAwait(false) };
                    break;
                case "IdentifyCognitiveBiases":
                    payloadData = new { result = await CallIdentifyCognitiveBiases(args).ConfigureAwait(false) };
                    break;
                case "ComparativeAnalysis":
                    payloadData = new { result = await CallComparativeAnalysis(args).ConfigureAwait(false) };
                    break;
                case "ListResources":
                    payloadData = HandleResourcesList(new McpRequest { Id = "direct" }).Result;
                    break;
                case "ReadResource":
                    var uri = args?["uri"]?.ToString();
                    if (string.IsNullOrWhiteSpace(uri))
                    {
                        return this.ErrorResponseMessage("uri is required", -32602);
                    }

                    var readResponse = await HandleResourceRead(new McpRequest
                    {
                        Id = "direct",
                        Params = new JObject { ["uri"] = uri }
                    }).ConfigureAwait(false);

                    if (readResponse.Error != null)
                    {
                        return this.ErrorResponseMessage(readResponse.Error["message"]?.ToString() ?? "Failed to read resource", -32603);
                    }

                    payloadData = readResponse.Result;
                    break;
                default:
                    return this.ErrorResponseMessage("Unsupported operation", -32601);
            }

            var payload = JsonConvert.SerializeObject(payloadData);
            return CreateJsonResponse(System.Net.HttpStatusCode.OK, payload);
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("DirectOperationError", ex);
            return this.ErrorResponseMessage(ex.Message, -32603);
        }
    }

    private HttpResponseMessage CreateJsonResponse(System.Net.HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage ErrorResponseMessage(string message, int code)
    {
        var errorResponse = new McpResponse
        {
            Error = JObject.FromObject(new { code = code, message = message })
        };
        var content = JsonConvert.SerializeObject(errorResponse);
        return CreateJsonResponse(System.Net.HttpStatusCode.InternalServerError, content);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP PROTOCOL CLASSES
    // ═══════════════════════════════════════════════════════════════════════════════

    public class McpRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JObject Params { get; set; }
    }

    public class McpResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public JObject Error { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // TOOLS HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════════

    private McpResponse HandleToolsList(McpRequest request)
    {
        var tools = new object[]
        {
            new
            {
                name = "get_second_opinion",
                description = "Alternative perspective from Foundry model on a decision or analysis. Useful for complex decisions, risk assessment, or creative exploration.",
                inputSchema = new
                {
                    type = "object",
                    required = new[] { "prompt" },
                    properties = new
                    {
                        prompt = new { type = "string", description = "Question or analysis to examine from alternative perspective" },
                        analysis_depth = new { type = "string", @enum = new[] { "quick", "balanced", "deep" }, description = "Level of analysis detail" },
                        focus_area = new { type = "string", description = "Optional: Specific aspect to prioritize in analysis" }
                    }
                }
            },
            new
            {
                name = "analyze_risk",
                description = "Structured risk assessment identifying downsides, probability, impact, mitigation strategies, and leading indicators.",
                inputSchema = new
                {
                    type = "object",
                    required = new[] { "scenario" },
                    properties = new
                    {
                        scenario = new { type = "string", description = "Decision scenario or plan to analyze for risks" },
                        risk_categories = new { type = "array", items = new { type = "string" }, description = "Optional: Risk categories to focus on" },
                        context = new { type = "string", description = "Optional: Additional context about the scenario" }
                    }
                }
            },
            new
            {
                name = "identify_cognitive_biases",
                description = "Detect cognitive biases in reasoning including anchoring, confirmation bias, availability bias, sunk cost fallacy, and overconfidence.",
                inputSchema = new
                {
                    type = "object",
                    required = new[] { "analysis" },
                    properties = new
                    {
                        analysis = new { type = "string", description = "Decision rationale or reasoning to examine for biases" },
                        focus_biases = new { type = "array", items = new { type = "string" }, description = "Optional: Specific biases to check for" }
                    }
                }
            },
            new
            {
                name = "comparative_analysis",
                description = "Compare multiple approaches side-by-side evaluating against criteria, highlighting tradeoffs, and identifying key dimensions.",
                inputSchema = new
                {
                    type = "object",
                    required = new[] { "context", "options" },
                    properties = new
                    {
                        context = new { type = "string", description = "Decision context or problem statement" },
                        options = new { type = "array", items = new { type = "string" }, description = "2-4 options or approaches to compare" },
                        criteria = new { type = "array", items = new { type = "string" }, description = "Optional: Evaluation criteria to use" }
                    }
                }
            }
        };

        return new McpResponse
        {
            Id = request.Id,
            Result = new { tools = tools }
        };
    }

    private async Task<McpResponse> HandleToolCall(McpRequest request)
    {
        try
        {
            var toolName = request.Params["name"]?.ToString();
            var arguments = request.Params["arguments"] as JObject;

            _ = LogToAppInsightsAsync("Tool_Call", new Dictionary<string, string>
            {
                { "tool", toolName ?? "unknown" }
            });

            string result = null;

            switch (toolName)
            {
                case "get_second_opinion":
                    result = await CallGetSecondOpinion(arguments);
                    break;

                case "analyze_risk":
                    result = await CallAnalyzeRisk(arguments);
                    break;

                case "identify_cognitive_biases":
                    result = await CallIdentifyCognitiveBiases(arguments);
                    break;

                case "comparative_analysis":
                    result = await CallComparativeAnalysis(arguments);
                    break;

                default:
                    return new McpResponse
                    {
                        Id = request.Id,
                        Error = JObject.FromObject(new { code = -32601, message = $"Tool not found: {toolName}" })
                    };
            }

            return new McpResponse
            {
                Id = request.Id,
                Result = new { content = new[] { new { type = "text", text = result } } }
            };
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("Tool_Call_Error", ex);
            return new McpResponse
            {
                Id = request.Id,
                Error = JObject.FromObject(new { code = -32603, message = "Tool execution error", data = ex.Message })
            };
        }
    }

    private async Task<string> CallGetSecondOpinion(JObject args)
    {
        var prompt = args?["prompt"]?.ToString() ?? "";
        var analysisDepth = args?["analysis_depth"]?.ToString() ?? "balanced";
        var focusArea = args?["focus_area"]?.ToString();

        var systemPrompt = BuildSecondOpinionSystemPrompt(analysisDepth, focusArea);

        try
        {
            var foundryResponse = await CallFoundryModel(new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prompt }
            });

            _ = LogToAppInsightsAsync("SecondOpinionGenerated", new Dictionary<string, string>
            {
                { "depth", analysisDepth }
            });

            return $"## Alternative Perspective\n\n{foundryResponse}\n\n---\n*Generated using secondary analysis.*";
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("SecondOpinionError", ex);
            throw;
        }
    }

    private async Task<string> CallAnalyzeRisk(JObject args)
    {
        var scenario = args?["scenario"]?.ToString() ?? "";
        var context = args?["context"]?.ToString();

        var systemPrompt = @"Risk analysis expert. For the scenario, identify:
1. Key risks (market, technical, organizational, financial, reputational)
2. Probability and Impact (High/Medium/Low)
3. Mitigation strategies
4. Leading indicators to monitor

Be specific and structured.";

        var userPrompt = $"Analyze risks for:\n\n{scenario}";
        if (!string.IsNullOrEmpty(context))
            userPrompt += $"\n\nContext: {context}";

        try
        {
            var foundryResponse = await CallFoundryModel(new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            });

            _ = LogToAppInsightsAsync("RiskAnalysisCompleted", new Dictionary<string, string> { { "success", "true" } });
            return foundryResponse;
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("RiskAnalysisError", ex);
            throw;
        }
    }

    private async Task<string> CallIdentifyCognitiveBiases(JObject args)
    {
        var analysis = args?["analysis"]?.ToString() ?? "";

        var systemPrompt = @"Cognitive bias expert. Examine the reasoning and identify:
1. Potential cognitive biases (anchoring, confirmation, availability, sunk cost, overconfidence, groupthink)
2. How each distorts judgment
3. Impact on decision quality
4. How to correct

Be specific with evidence.";

        var userPrompt = $"Analyze for cognitive biases:\n\n{analysis}";

        try
        {
            var foundryResponse = await CallFoundryModel(new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            });

            _ = LogToAppInsightsAsync("BiasAnalysisCompleted", new Dictionary<string, string> { { "success", "true" } });
            return foundryResponse;
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("BiasAnalysisError", ex);
            throw;
        }
    }

    private async Task<string> CallComparativeAnalysis(JObject args)
    {
        var context = args?["context"]?.ToString() ?? "";
        var options = args?["options"]?.ToObject<List<string>>() ?? new List<string>();
        var criteria = args?["criteria"]?.ToObject<List<string>>() ?? new List<string>();

        var systemPrompt = @"Decision analysis expert. For the options, provide:
1. Side-by-side comparison matrix
2. Score each option on each criterion (1-5)
3. Key tradeoffs
4. Recommendation with rationale

Be specific.";

        var userPrompt = $"Compare these options:\n{string.Join("\n", options)}\n\nContext: {context}";
        if (criteria.Any())
            userPrompt += $"\n\nCriteria: {string.Join(", ", criteria)}";

        try
        {
            var foundryResponse = await CallFoundryModel(new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            });

            _ = LogToAppInsightsAsync("ComparativeAnalysisCompleted", new Dictionary<string, string> { { "options", options.Count.ToString() } });
            return foundryResponse;
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("ComparativeAnalysisError", ex);
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // RESOURCES HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════════

    private McpResponse HandleResourcesList(McpRequest request)
    {
        var resources = new[]
        {
            new { uri = "resource://decision-frameworks/investment", name = "Investment Assessment Framework", description = "Evaluate investment decisions", mimeType = "text/markdown" },
            new { uri = "resource://decision-frameworks/operational", name = "Operational Change Framework", description = "Assess organizational changes", mimeType = "text/markdown" },
            new { uri = "resource://decision-frameworks/strategic", name = "Strategic Planning Framework", description = "Strategic decisions framework", mimeType = "text/markdown" },
            new { uri = "resource://knowledge-base/benchmarks", name = "Industry Benchmarks", description = "Market and financial benchmarks", mimeType = "text/markdown" },
            new { uri = "resource://knowledge-base/case-studies", name = "Case Studies", description = "Real-world decision examples", mimeType = "application/json" },
            new { uri = "resource://reasoning/biases", name = "Cognitive Bias Checklist", description = "Bias identification and mitigation", mimeType = "text/markdown" },
            new { uri = "resource://reasoning/process", name = "Decision Process", description = "Structured 5-phase process", mimeType = "text/markdown" },
            new { uri = "resource://reasoning/questions", name = "Critical Questions", description = "30 essential pre-decision questions", mimeType = "text/markdown" },
            new { uri = "resource://best-practices/implementation", name = "Implementation Best Practices", description = "Execution patterns and strategies", mimeType = "text/markdown" },
            new { uri = "resource://best-practices/org", name = "Organizational Decision-Making", description = "Building decision-making capability", mimeType = "text/markdown" }
        };

        return new McpResponse
        {
            Id = request.Id,
            Result = new { resources = resources }
        };
    }

    private async Task<McpResponse> HandleResourceRead(McpRequest request)
    {
        try
        {
            var uri = request.Params?["uri"]?.ToString() ?? "";
            var content = GetResourceContent(uri);

            _ = LogToAppInsightsAsync("ResourceRead", new Dictionary<string, string> { { "uri", uri } });

            return new McpResponse
            {
                Id = request.Id,
                Result = new
                {
                    contents = new[]
                    {
                        new { uri = uri, mimeType = "text/plain", text = content }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("ResourceReadError", ex);
            return new McpResponse
            {
                Id = request.Id,
                Error = JObject.FromObject(new { code = -32603, message = "Failed to read resource", data = ex.Message })
            };
        }
    }

    private string GetResourceContent(string uri)
    {
        return uri switch
        {
            "resource://decision-frameworks/investment" => GetDecisionFrameworkInvestment(),
            "resource://decision-frameworks/operational" => GetDecisionFrameworkOperational(),
            "resource://decision-frameworks/strategic" => GetDecisionFrameworkStrategic(),
            "resource://knowledge-base/benchmarks" => GetIndustryBenchmarks(),
            "resource://knowledge-base/case-studies" => GetCaseStudies(),
            "resource://reasoning/biases" => GetCognitiveBiasesChecklist(),
            "resource://reasoning/process" => GetDecisionProcess(),
            "resource://reasoning/questions" => GetCriticalQuestions(),
            "resource://best-practices/implementation" => GetImplementationBestPractices(),
            "resource://best-practices/org" => GetOrganizationalBestPractices(),
            _ => throw new ArgumentException($"Unknown resource: {uri}")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // RESOURCE CONTENT (Abbreviated; see readme.md for full versions)
    // ═══════════════════════════════════════════════════════════════════════════════

    private string GetDecisionFrameworkInvestment() => @"# Investment Assessment Framework

## Assessment Dimensions
1. **Opportunity**: Market size, growth, competition
2. **Solution**: Problem clarity, solution maturity
3. **Financial**: Revenue model, unit economics, capital efficiency
4. **Team**: Domain expertise, execution track record
5. **Risk**: Market, technology, team, competitive risks

## Decision Criteria
Weight: Market (30-40%), Solution (20-30%), Team (20-30%), Financials (10-20%), Risk (0-20%)

## Risk-Reward Matrix
- High-Risk, High-Reward: Early-stage platforms
- Medium-Risk, Medium-Reward: Growth stage
- Low-Risk, Low-Reward: Mature, stable
- High-Risk, Low-Reward: Avoid";

    private string GetDecisionFrameworkOperational() => @"# Operational Change Framework

## Assessment Dimensions
1. **Scope**: Breadth, depth, velocity, reversibility
2. **Impact**: Direct, indirect, political, engagement
3. **Readiness**: Capacity, skills, technology, culture
4. **Implementation**: Technical, process, dependencies, timeline
5. **Risk**: Disruption, adoption, quality, knowledge loss

## Success Metrics
- Adoption rate
- Efficiency gains
- Quality metrics
- Capability metrics

## Best Practices
- Pilot with early adopters
- Have rollback plan
- Communicate continuously
- Celebrate early wins";

    private string GetDecisionFrameworkStrategic() => @"# Strategic Planning Framework

## Assessment Dimensions
1. **Market Analysis**: Dynamics, needs, competition, trends
2. **Capability**: Strengths, weaknesses, opportunities, threats
3. **Positioning**: Lead, Fast Follow, Niche, Cost, Retreat
4. **Strategy**: Market, value, advantage, operations
5. **Scenarios**: Base, upside, downside, inflection

## Capability Gaps
- What you need
- What you have
- Gaps to close
- Build vs. Buy vs. Partner";

    private string GetIndustryBenchmarks() => @"# Industry Benchmarks

## Metrics
1. **Financial**: Gross margins, Rule of 40, CAC/LTV, payback
2. **Operational**: Engineering productivity, sales efficiency
3. **Market**: Growth rates, competitive concentration

## Sources
- Gartner, IDC, McKinsey reports
- Public company presentations
- CB Insights database
- Analyst calls

## Usage
1. Gather benchmarks for your segment
2. Compare assumptions
3. Determine if realistic
4. Document differences";

    private string GetCaseStudies() => @"{
  ""studies"": [
    {
      ""title"": ""Market Entry Timing"",
      ""outcome"": ""Fast followers captured 70% with 40% lower investment"",
      ""lessons"": [""Being first < being best"", ""Execution speed critical""]
    },
    {
      ""title"": ""Change Pace"",
      ""outcome"": ""Incremental succeeded; big bang = 40% productivity loss"",
      ""lessons"": [""Pace matters"", ""Pilot-first reduces risk""]
    }
  ]
}";

    private string GetCognitiveBiasesChecklist() => @"# Cognitive Biases

## Key Biases

**Anchoring**: First number becomes reference
- Mitigation: Generate estimates independently

**Confirmation Bias**: Seek confirming info
- Mitigation: Explicitly list counter-arguments

**Availability Bias**: Recent examples seem more common
- Mitigation: Use actual data

**Sunk Cost Fallacy**: Continue bad choice due to past investment
- Mitigation: Forward-looking decision

**Overconfidence**: Overestimate capability
- Mitigation: Use reference class, build buffers

**Groupthink**: Harmony over critical evaluation
- Mitigation: Devil's advocate, anonymous input

## Quality Checklist
- Sought disconfirming evidence?
- Estimates realistic vs. reference class?
- Conflict of interest?
- Emotional state defensive?
- Diverse perspectives heard?";

    private string GetDecisionProcess() => @"# Structured Decision Process

## 5 Phases

**Phase 1: Define**
- What are we deciding?
- What triggers this now?
- What would ""right"" look like?

**Phase 2: Criteria**
- Mandatory criteria
- Weight by importance

**Phase 3: Generate**
- Develop 3+ options
- Evaluate against criteria
- Score 1-5 scale

**Phase 4: Sensitivity**
- Key assumptions
- Risk analysis
- Mitigating actions

**Phase 5: Decide**
- Clear recommendation
- Document assumptions
- Name implementation lead
- Define success metrics

## Review Cadence
- Week 1: Execution check
- Month: Track metrics
- Quarterly: Stay vs. pivot";

    private string GetCriticalQuestions() => @"# Critical Questions (30)

## Reality Check (1-5)
1. What would have to be true for this to fail?
2. What am I assuming that might not be true?
3. If this goes wrong, what's the damage?
4. Have we done this before?
5. Who succeeded? Who failed?

## Competition (6-10)
6. How would smart competitor respond?
7. Is there a defensible moat?
8. How long until advantage erodes?
9. What could make us obsolete?
10. Are we competing on something defensible?

## Team (11-15)
11. Domain expertise?
12. Done this complexity before?
13. Incentivized to succeed?
14. Needed resources?
15. Natural adoption vs. enforcement?

## Financial (16-20)
16. True ROI with all costs?
17. When do we break even?
18. If growth is half of plan?
19. Comparing apples-to-apples?
20. Payback vs. market standard?

## Implementation (21-25)
21. Success at 30/90/365 days?
22. Biggest risk?
23. Fallback plan?
24. Realistic timeline?
25. Accountability?

## Cultural (26-30)
26. Aligned with values?
27. Organization embrace or resist?
28. Strengthen or weaken culture?
29. Running from or toward?
30. Glad in 5 years?";

    private string GetImplementationBestPractices() => @"# Implementation Best Practices

## Pre-Launch
- Clear DRI (directly responsible individual)
- Resources allocated
- Milestone targets (30/60/90)
- Success metrics defined
- Communication plan
- Escalation path
- Risk contingencies

## Communication
1. Pre: Why and what's changing
2. Launch: Roadmap, roles, timeline
3. Ongoing: Weekly progress
4. Celebrate: Early wins
5. Retrospective: Lessons

## Pace & Rollout
- **Big Bang**: All at once (high-impact, high-risk)
- **Phased**: By team/function (moderate)
- **Pilot**: Small group first (lowest risk)

**Best Practice**: Pilot → iterate → scale

## Resistance
1. Understand concern
2. Acknowledge validity
3. Address with evidence
4. Decide and execute
5. Support through change

## Metrics
- Weekly: Need adjustment?
- Monthly: On track?
- Document: Knowledge base";

    private string GetOrganizationalBestPractices() => @"# Organizational Decision-Making

## Framework

**1. Governance**
- Decision types and authority
- Who decides, inputs, informed
- Timeline authority

**2. Process**
- Big decisions get more process
- Reversible decisions get less
- Include definition through analysis

**3. Collaboration**
- Psychological safety
- Diverse perspectives
- Devil's advocate
- External experts

**4. Assumptions**
- Write them down
- Test before deciding
- Document for learning

**5. Documentation**
- What was decided
- Why (rationale, assumptions)
- What was rejected
- Success metrics

**6. Review Cadence**
- Immediate: Execution
- Quarterly: Course correction
- Annual: Reflection

## Building Capability
- Learn from past outcomes
- Reduce groupthink
- Speed reversible decisions
- Slow irreversible decisions
- Safe to be wrong";

    private string BuildSecondOpinionSystemPrompt(string analysisDepth, string focusArea)
    {
        var depthText = analysisDepth switch
        {
            "quick" => "Brief alternative perspective (2-3 paragraphs).",
            "balanced" => "Balanced perspective (4-5 paragraphs) with context and counterarguments.",
            "deep" => "Comprehensive analysis (7-10 paragraphs) with edge cases and implications.",
            _ => "Thoughtful alternative perspective."
        };

        var focusText = string.IsNullOrEmpty(focusArea) ? "" : $"\n\nFocus on: {focusArea}";

        return $@"You provide a ""second opinion"" on decisions.

Your role:
1. Complement primary analysis
2. Identify gaps and assumptions
3. Offer credible counterpoints
4. Use same decision frameworks

{depthText}
{focusText}

Be specific, evidence-based, constructive.";
    }

    private async Task<string> CallFoundryModel(dynamic[] messages)
    {
        try
        {
            var foundryEndpoint = "http://localhost:60311/v1";
            var foundryModel = "phi-4";

            var requestBody = new
            {
                model = foundryModel,
                messages = messages,
                temperature = 0.7,
                max_tokens = 2000,
                top_p = 0.95
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, $"{foundryEndpoint}/chat/completions"))
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Foundry error: {response.StatusCode}");
                }

                var responseJson = JObject.Parse(responseText);
                var result = responseJson["choices"]?[0]?["message"]?["content"]?.ToString();

                _ = LogToAppInsightsAsync("FoundryModelCall", new Dictionary<string, string>
                {
                    { "model", foundryModel },
                    { "responseLength", result?.Length.ToString() ?? "0" }
                });

                return result ?? "No response from model.";
            }
        }
        catch (Exception ex)
        {
            _ = LogExceptionToAppInsightsAsync("FoundryModelCallError", ex);
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // APPLICATION INSIGHTS LOGGING
    // ═══════════════════════════════════════════════════════════════════════════════

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
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

                request.Content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json"
                );

                await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            }
        }
        catch { /* Silent fail */ }
    }

    private async Task LogExceptionToAppInsightsAsync(string exceptionName, Exception ex)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                var telemetryData = new
                {
                    name = "Microsoft.ApplicationInsights.Exception",
                    time = DateTime.UtcNow.ToString("O"),
                    iKey = APP_INSIGHTS_KEY,
                    data = new
                    {
                        baseType = "ExceptionData",
                        baseData = new
                        {
                            ver = 2,
                            exceptions = new[]
                            {
                                new
                                {
                                    typeName = ex.GetType().FullName,
                                    message = ex.Message,
                                    stack = ex.StackTrace
                                }
                            },
                            properties = new Dictionary<string, string>
                            {
                                { "operation", exceptionName }
                            }
                        }
                    }
                };

                request.Content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json"
                );

                await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            }
        }
        catch { /* Silent fail */ }
    }
}
