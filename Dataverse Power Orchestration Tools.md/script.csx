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
/// Dataverse Power Orchestration Tools: Power MCP tool server for Copilot Studio
/// Dynamic tools loaded from tst_agentinstructions table with learned pattern discovery.
/// Orchestration tools: search_tools, call_tool, execute_workflow, get_patterns
/// </summary>
public class Script : ScriptBase
{
    // Application Insights telemetry (optional - leave empty to disable)
    // Format: InstrumentationKey=xxx;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // Meta-tool names
    private const string TOOL_DISCOVER_FUNCTIONS = "discover_functions";
    private const string TOOL_INVOKE_TOOL = "invoke_tool";
    private const string TOOL_ORCHESTRATE_PLAN = "orchestrate_plan";
    private const string TOOL_LEARN_PATTERNS = "learn_patterns";
    
    // Cache for dynamic content (per request lifecycle)
    private string _cachedAgentMd = null;
    private string _cachedInstructionsRecordId = null;  // For learned patterns updates
    private JArray _cachedTools = null;           // MCP format (name, description, inputSchema)
    private JArray _cachedFullTools = null;       // Full format (includes category, keywords)
    
    // Tool handler registry - dictionary dispatch for O(1) lookup
    private Dictionary<string, Func<JObject, Task<JObject>>> _toolHandlers;
    
    private async Task<string> GetAgentMdAsync()
    {
        // Return cached if available
        if (_cachedAgentMd != null)
            return _cachedAgentMd;
            
        try
        {
            // Query tst_agentinstructions table for active instructions
            var filter = "tst_name eq 'dataverse-tools-agent' and tst_enabled eq true";
            var select = "tst_agentinstructionsid,tst_agentmd,tst_learnedpatterns,tst_version,tst_updatecount";
            var url = BuildDataverseUrl($"tst_agentinstructionses?$filter={Uri.EscapeDataString(filter)}&$select={Uri.EscapeDataString(select)}&$top=1");
            
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            var records = result["value"] as JArray;
            
            if (records == null || records.Count == 0)
            {
                _cachedAgentMd = string.Empty;
                return _cachedAgentMd;
            }
            
            var record = records[0] as JObject;
            _cachedInstructionsRecordId = record?["tst_agentinstructionsid"]?.ToString();
            var agentMd = record?["tst_agentmd"]?.ToString() ?? string.Empty;
            var learnedPatterns = record?["tst_learnedpatterns"]?.ToString();
            
            // Append learned patterns if present
            if (!string.IsNullOrWhiteSpace(learnedPatterns))
            {
                agentMd += "\n\n## LEARNED PATTERNS\n\n" + learnedPatterns;
            }
            
            _cachedAgentMd = agentMd;
            return _cachedAgentMd;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to load agents.md: {ex.Message}");
            _cachedAgentMd = string.Empty;
            return _cachedAgentMd;
        }
    }
    
    // Parse tools from agents.md JSON block
    private async Task<JArray> GetDynamicToolsAsync()
    {
        if (_cachedTools != null)
            return _cachedTools;
            
        var agentMd = await GetAgentMdAsync().ConfigureAwait(false);
        _cachedTools = ParseToolsFromAgentMd(agentMd);
        return _cachedTools;
    }
    
    // Get full tools with category/keywords for search
    private async Task<JArray> GetFullToolsAsync()
    {
        if (_cachedFullTools != null)
            return _cachedFullTools;
            
        var agentMd = await GetAgentMdAsync().ConfigureAwait(false);
        _cachedFullTools = ParseFullToolsFromAgentMd(agentMd);
        return _cachedFullTools;
    }
    
    private JArray ParseFullToolsFromAgentMd(string agentMd)
    {
        if (string.IsNullOrWhiteSpace(agentMd))
            return new JArray();
            
        try
        {
            var toolsMarker = "## TOOLS";
            var toolsIndex = agentMd.IndexOf(toolsMarker, StringComparison.OrdinalIgnoreCase);
            if (toolsIndex < 0) return new JArray();
            
            var afterMarker = agentMd.Substring(toolsIndex + toolsMarker.Length);
            var jsonStart = afterMarker.IndexOf('[');
            if (jsonStart < 0) return new JArray();
            
            var jsonEnd = FindMatchingBracket(afterMarker, jsonStart);
            if (jsonEnd < 0) return new JArray();
            
            var jsonStr = afterMarker.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JArray.Parse(jsonStr);
        }
        catch
        {
            return new JArray();
        }
    }
    
    // Discover functions by intent - matches against name, description, category, keywords
    private async Task<JObject> ExecuteDiscoverFunctions(JObject args)
    {
        var intent = args["intent"]?.ToString()?.ToLowerInvariant() ?? "";
        var category = args["category"]?.ToString()?.ToLowerInvariant();
        var maxResults = args["maxResults"]?.Value<int?>() ?? 10;
        
        if (string.IsNullOrWhiteSpace(intent) && string.IsNullOrWhiteSpace(category))
        {
            return new JObject
            {
                ["error"] = "Either 'intent' or 'category' is required",
                ["tools"] = new JArray()
            };
        }
        
        var fullTools = await GetFullToolsAsync().ConfigureAwait(false);
        var matches = new List<(JObject tool, int score)>();
        var intentWords = intent?.Split(new[] { ' ', ',', '-', '_' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
        
        foreach (var tool in fullTools)
        {
            var toolObj = tool as JObject;
            if (toolObj == null) continue;
            
            var name = toolObj["name"]?.ToString()?.ToLowerInvariant() ?? "";
            var desc = toolObj["description"]?.ToString()?.ToLowerInvariant() ?? "";
            var toolCategory = toolObj["category"]?.ToString()?.ToLowerInvariant() ?? "";
            var keywords = toolObj["keywords"] as JArray;
            var keywordList = keywords?.Select(k => k.ToString().ToLowerInvariant()).ToList() ?? new List<string>();
            
            // Category filter (exact match)
            if (!string.IsNullOrWhiteSpace(category) && toolCategory != category)
                continue;
            
            // Score based on matches
            var score = 0;
            
            foreach (var word in intentWords)
            {
                if (word.Length < 2) continue;
                
                // Exact keyword match = highest score
                if (keywordList.Contains(word)) score += 10;
                // Name contains word
                else if (name.Contains(word)) score += 8;
                // Description contains word
                else if (desc.Contains(word)) score += 3;
                // Partial keyword match
                else if (keywordList.Any(k => k.Contains(word) || word.Contains(k))) score += 5;
            }
            
            // Category match bonus (when filtering by category)
            if (!string.IsNullOrWhiteSpace(category) && toolCategory == category)
                score += 5;
            
            if (score > 0 || !string.IsNullOrWhiteSpace(category))
            {
                matches.Add((toolObj, score));
            }
        }
        
        // Sort by score descending, take top results
        var results = matches
            .OrderByDescending(m => m.score)
            .Take(maxResults)
            .Select(m => new JObject
            {
                ["name"] = m.tool["name"],
                ["description"] = m.tool["description"],
                ["category"] = m.tool["category"],
                ["score"] = m.score
            })
            .ToList();
        
        return new JObject
        {
            ["intent"] = intent,
            ["category"] = category,
            ["matchCount"] = results.Count,
            ["tools"] = new JArray(results)
        };
    }
    
    // Invoke a tool dynamically by name
    private async Task<JObject> ExecuteInvokeTool(JObject args)
    {
        var toolName = args["toolName"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new JObject
            {
                ["error"] = "'toolName' is required",
                ["success"] = false
            };
        }
        
        // Validate tool exists in dynamic tools
        var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
        var toolDef = tools.FirstOrDefault(t => t["name"]?.ToString() == toolName);
        if (toolDef == null)
        {
            return new JObject
            {
                ["error"] = $"Tool '{toolName}' not found. Use discover_functions to find available tools.",
                ["success"] = false,
                ["suggestion"] = "Call discover_functions with your intent to discover relevant tools"
            };
        }
        
        // Get tool arguments
        var toolArgs = args["args"] as JObject ?? new JObject();
        
        try
        {
            this.Context.Logger.LogInformation($"invoke_tool executing: {toolName}");
            var result = await ExecuteToolByName(toolName, toolArgs).ConfigureAwait(false);
            
            return new JObject
            {
                ["success"] = true,
                ["toolName"] = toolName,
                ["result"] = result
            };
        }
        catch (ArgumentException ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["toolName"] = toolName,
                ["error"] = $"Invalid arguments: {ex.Message}",
                ["inputSchema"] = toolDef["inputSchema"]
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["toolName"] = toolName,
                ["error"] = $"Execution failed: {ex.Message}"
            };
        }
    }
    
    // Orchestrate a plan of multiple tool calls in sequence
    private async Task<JObject> ExecuteOrchestratePlan(JObject args)
    {
        var stepsToken = args["steps"];
        if (stepsToken == null || stepsToken.Type != JTokenType.Array)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "'steps' array is required"
            };
        }
        
        var steps = stepsToken as JArray;
        if (steps.Count == 0)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "'steps' array cannot be empty"
            };
        }
        
        var stopOnError = args["stopOnError"]?.Value<bool?>() ?? true;
        var results = new JArray();
        var context = new JObject(); // Shared context for variable substitution
        var allSuccess = true;
        
        this.Context.Logger.LogInformation($"orchestrate_plan starting with {steps.Count} steps");
        
        _ = LogToAppInsights("PlanStarted", new Dictionary<string, string>
        {
            ["stepCount"] = steps.Count.ToString(),
            ["stopOnError"] = stopOnError.ToString()
        });
        
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i] as JObject;
            if (step == null)
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["success"] = false,
                    ["error"] = "Invalid step format"
                });
                allSuccess = false;
                if (stopOnError) break;
                continue;
            }
            
            var toolName = step["tool"]?.ToString();
            var stepArgs = step["args"] as JObject ?? new JObject();
            var outputAs = step["outputAs"]?.ToString(); // Variable name to store result
            
            if (string.IsNullOrWhiteSpace(toolName))
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["success"] = false,
                    ["error"] = "'tool' is required in each step"
                });
                allSuccess = false;
                if (stopOnError) break;
                continue;
            }
            
            // Substitute variables from context into args
            var resolvedArgs = ResolveWorkflowVariables(stepArgs, context);
            
            try
            {
                this.Context.Logger.LogInformation($"Plan step {i + 1}/{steps.Count}: {toolName}");
                var result = await ExecuteToolByName(toolName, resolvedArgs).ConfigureAwait(false);
                
                // Store result in context if outputAs specified
                if (!string.IsNullOrWhiteSpace(outputAs))
                {
                    context[outputAs] = result;
                }
                
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["tool"] = toolName,
                    ["success"] = true,
                    ["result"] = result,
                    ["outputAs"] = outputAs
                });
            }
            catch (Exception ex)
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["tool"] = toolName,
                    ["success"] = false,
                    ["error"] = ex.Message
                });
                allSuccess = false;
                if (stopOnError) break;
            }
        }
        
        // Log successful plan pattern for learning
        if (allSuccess && steps.Count >= 2)
        {
            _ = LogLearnedPatternAsync("plan", steps, results);
        }
        
        return new JObject
        {
            ["success"] = allSuccess,
            ["stepsExecuted"] = results.Count,
            ["totalSteps"] = steps.Count,
            ["results"] = results,
            ["context"] = context
        };
    }
    
    /// <summary>
    /// Log a successful pattern to tst_learnedpatterns for self-improvement
    /// Appends pattern with timestamp to existing patterns, increments update count
    /// </summary>
    private async Task LogLearnedPatternAsync(string patternType, JArray steps, JArray results)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                // Ensure we have the record ID
                await GetAgentMdAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
                {
                    this.Context.Logger.LogWarning("Cannot log learned pattern: no instructions record found");
                    return;
                }
            }
            
            // Build pattern summary
            var toolSequence = new JArray();
            foreach (var step in steps)
            {
                var stepObj = step as JObject;
                toolSequence.Add(stepObj?["tool"]?.ToString() ?? "unknown");
            }
            
            var pattern = new JObject
            {
                ["type"] = patternType,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["tools"] = toolSequence,
                ["stepCount"] = steps.Count
            };
            
            // Get current learned patterns
            var getUrl = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})?$select=tst_learnedpatterns,tst_updatecount");
            var current = await SendDataverseRequest(HttpMethod.Get, getUrl, null).ConfigureAwait(false);
            
            var existingPatterns = current["tst_learnedpatterns"]?.ToString() ?? "";
            var updateCount = current["tst_updatecount"]?.Value<int?>() ?? 0;
            
            // Append new pattern (limit to last 50 patterns to prevent bloat)
            var patternLine = $"- [{DateTime.UtcNow:yyyy-MM-dd HH:mm}] {patternType}: {string.Join(" → ", toolSequence.Select(t => t.ToString()))}";
            var lines = existingPatterns.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            lines.Add(patternLine);
            if (lines.Count > 50) lines = lines.Skip(lines.Count - 50).ToList();
            
            var updatedPatterns = string.Join("\n", lines);
            
            // Update record
            var updateUrl = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})");
            var updateBody = new JObject
            {
                ["tst_learnedpatterns"] = updatedPatterns,
                ["tst_updatecount"] = updateCount + 1,
                ["tst_lastupdated"] = DateTime.UtcNow.ToString("o")
            };
            
            await SendDataverseRequest(new HttpMethod("PATCH"), updateUrl, updateBody).ConfigureAwait(false);
            
            this.Context.Logger.LogInformation($"Logged learned pattern: {patternLine}");
            
            _ = LogToAppInsights("LearnedPatternLogged", new Dictionary<string, string>
            {
                ["patternType"] = patternType,
                ["toolCount"] = toolSequence.Count.ToString(),
                ["tools"] = string.Join(",", toolSequence.Select(t => t.ToString()))
            });
        }
        catch (Exception ex)
        {
            // Don't fail the request if pattern logging fails
            this.Context.Logger.LogWarning($"Failed to log learned pattern: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Learn patterns from tst_learnedpatterns - exposes organizational learning to Copilot
    /// </summary>
    private async Task<JObject> ExecuteLearnPatterns(JObject args)
    {
        var toolNameFilter = args["toolName"]?.ToString()?.ToLowerInvariant();
        var limit = Math.Min(Math.Max(args["limit"]?.Value<int?>() ?? 10, 1), 50);
        
        try
        {
            // Ensure we have the record ID
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                await GetAgentMdAsync().ConfigureAwait(false);
            }
            
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                return new JObject
                {
                    ["patterns"] = new JArray(),
                    ["totalCount"] = 0,
                    ["message"] = "No instructions record configured. Create tst_agentinstructions with tst_name='dataverse-tools-agent'"
                };
            }
            
            // Get current learned patterns
            var url = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})?$select=tst_learnedpatterns,tst_updatecount,tst_lastupdated");
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            var patternsRaw = result["tst_learnedpatterns"]?.ToString() ?? "";
            var updateCount = result["tst_updatecount"]?.Value<int?>() ?? 0;
            var lastUpdated = result["tst_lastupdated"]?.ToString();
            
            if (string.IsNullOrWhiteSpace(patternsRaw))
            {
                return new JObject
                {
                    ["patterns"] = new JArray(),
                    ["totalCount"] = 0,
                    ["updateCount"] = updateCount,
                    ["message"] = "No patterns learned yet. Orchestrate plans to start learning."
                };
            }
            
            // Parse patterns (format: "- [timestamp] type: tool1 → tool2 → tool3")
            var lines = patternsRaw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var patterns = new List<JObject>();
            
            foreach (var line in lines.Reverse()) // Most recent first
            {
                if (!line.TrimStart().StartsWith("-")) continue;
                
                var pattern = ParsePatternLine(line);
                if (pattern == null) continue;
                
                // Apply tool filter if specified
                if (!string.IsNullOrWhiteSpace(toolNameFilter))
                {
                    var tools = pattern["tools"] as JArray;
                    if (tools == null || !tools.Any(t => t.ToString().ToLowerInvariant().Contains(toolNameFilter)))
                        continue;
                }
                
                patterns.Add(pattern);
                if (patterns.Count >= limit) break;
            }
            
            // Extract common sequences for suggestions
            var toolFrequency = new Dictionary<string, int>();
            var sequenceFrequency = new Dictionary<string, int>();
            
            foreach (var line in lines)
            {
                var pattern = ParsePatternLine(line);
                if (pattern == null) continue;
                
                var tools = pattern["tools"] as JArray;
                if (tools == null) continue;
                
                foreach (var tool in tools)
                {
                    var toolName = tool.ToString();
                    toolFrequency[toolName] = toolFrequency.GetValueOrDefault(toolName, 0) + 1;
                }
                
                // Track 2-tool sequences
                for (var i = 0; i < tools.Count - 1; i++)
                {
                    var seq = $"{tools[i]} → {tools[i + 1]}";
                    sequenceFrequency[seq] = sequenceFrequency.GetValueOrDefault(seq, 0) + 1;
                }
            }
            
            var topTools = toolFrequency.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new JObject { ["tool"] = kv.Key, ["count"] = kv.Value }).ToList();
            var topSequences = sequenceFrequency.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new JObject { ["sequence"] = kv.Key, ["count"] = kv.Value }).ToList();
            
            return new JObject
            {
                ["patterns"] = new JArray(patterns),
                ["totalCount"] = lines.Length,
                ["returnedCount"] = patterns.Count,
                ["updateCount"] = updateCount,
                ["lastUpdated"] = lastUpdated,
                ["insights"] = new JObject
                {
                    ["mostUsedTools"] = new JArray(topTools),
                    ["commonSequences"] = new JArray(topSequences)
                },
                ["filter"] = toolNameFilter
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["patterns"] = new JArray(),
                ["error"] = $"Failed to retrieve patterns: {ex.Message}"
            };
        }
    }
    
    private JObject ParsePatternLine(string line)
    {
        try
        {
            // Format: "- [2026-01-10 14:23] workflow: tool1 → tool2 → tool3"
            var trimmed = line.TrimStart('-', ' ');
            
            var timestampEnd = trimmed.IndexOf(']');
            if (timestampEnd < 0) return null;
            
            var timestamp = trimmed.Substring(1, timestampEnd - 1); // Skip opening [
            var rest = trimmed.Substring(timestampEnd + 1).TrimStart();
            
            var colonPos = rest.IndexOf(':');
            if (colonPos < 0) return null;
            
            var patternType = rest.Substring(0, colonPos).Trim();
            var toolsStr = rest.Substring(colonPos + 1).Trim();
            
            var tools = toolsStr.Split(new[] { " → ", "→" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            
            return new JObject
            {
                ["timestamp"] = timestamp,
                ["type"] = patternType,
                ["tools"] = new JArray(tools),
                ["toolCount"] = tools.Count
            };
        }
        catch
        {
            return null;
        }
    }
    
    // Resolve {{variable}} placeholders in workflow args from context
    private JObject ResolveWorkflowVariables(JObject args, JObject context)
    {
        var resolved = new JObject();
        
        foreach (var prop in args.Properties())
        {
            resolved[prop.Name] = ResolveValue(prop.Value, context);
        }
        
        return resolved;
    }
    
    private JToken ResolveValue(JToken value, JObject context)
    {
        if (value.Type == JTokenType.String)
        {
            var str = value.ToString();
            // Check for {{varName}} or {{varName.property}} pattern
            if (str.StartsWith("{{") && str.EndsWith("}}"))
            {
                var varPath = str.Substring(2, str.Length - 4).Trim();
                return ResolveVariablePath(varPath, context);
            }
            return value;
        }
        else if (value.Type == JTokenType.Object)
        {
            return ResolveWorkflowVariables(value as JObject, context);
        }
        else if (value.Type == JTokenType.Array)
        {
            var arr = new JArray();
            foreach (var item in value as JArray)
            {
                arr.Add(ResolveValue(item, context));
            }
            return arr;
        }
        return value;
    }
    
    private JToken ResolveVariablePath(string path, JObject context)
    {
        var parts = path.Split('.');
        JToken current = context;
        
        foreach (var part in parts)
        {
            if (current == null) return JValue.CreateNull();
            
            if (current.Type == JTokenType.Object)
            {
                current = (current as JObject)?[part];
            }
            else if (current.Type == JTokenType.Array && int.TryParse(part, out var index))
            {
                var arr = current as JArray;
                current = (index >= 0 && index < arr.Count) ? arr[index] : null;
            }
            else
            {
                return JValue.CreateNull();
            }
        }
        
        return current ?? JValue.CreateNull();
    }
    
    private JArray ParseToolsFromAgentMd(string agentMd)
    {
        if (string.IsNullOrWhiteSpace(agentMd))
            return GetFallbackTools();
            
        try
        {
            // Find JSON block after ## TOOLS marker
            var toolsMarker = "## TOOLS";
            var toolsIndex = agentMd.IndexOf(toolsMarker, StringComparison.OrdinalIgnoreCase);
            if (toolsIndex < 0)
            {
                this.Context.Logger.LogWarning("No ## TOOLS section found in agents.md");
                return GetFallbackTools();
            }
            
            var afterMarker = agentMd.Substring(toolsIndex + toolsMarker.Length);
            
            // Find JSON array - look for ```json block or raw [ ]
            var jsonStart = afterMarker.IndexOf('[');
            if (jsonStart < 0)
            {
                this.Context.Logger.LogWarning("No JSON array found in TOOLS section");
                return GetFallbackTools();
            }
            
            // Find matching closing bracket
            var jsonEnd = FindMatchingBracket(afterMarker, jsonStart);
            if (jsonEnd < 0)
            {
                this.Context.Logger.LogWarning("No closing bracket found for tools JSON");
                return GetFallbackTools();
            }
            
            var jsonStr = afterMarker.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var toolsArray = JArray.Parse(jsonStr);
            
            // Convert to MCP tool format (remove category/keywords, keep name/description/inputSchema)
            var mcpTools = new JArray();
            
            // Always inject orchestration tools first
            mcpTools.Add(GetDiscoverFunctionsDefinition());
            mcpTools.Add(GetInvokeToolDefinition());
            mcpTools.Add(GetOrchestratePlanDefinition());
            mcpTools.Add(GetLearnPatternsDefinition());
            
            foreach (var tool in toolsArray)
            {
                mcpTools.Add(new JObject
                {
                    ["name"] = tool["name"],
                    ["description"] = tool["description"],
                    ["inputSchema"] = tool["inputSchema"]
                });
            }
            
            this.Context.Logger.LogInformation($"Loaded {mcpTools.Count} tools from agents.md (includes orchestration tools)");
            return mcpTools;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to parse tools from agents.md: {ex.Message}");
            return GetFallbackTools();
        }
    }
    
    private int FindMatchingBracket(string text, int openPos)
    {
        var depth = 0;
        var inString = false;
        var escapeNext = false;
        
        for (var i = openPos; i < text.Length; i++)
        {
            var c = text[i];
            
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }
            
            if (c == '\\' && inString)
            {
                escapeNext = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString) continue;
            
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        
        return -1;
    }
    
    // Orchestration tools definition (always available)
    private JObject GetDiscoverFunctionsDefinition() => new JObject
    {
        ["name"] = "discover_functions",
        ["description"] = "Discover available functions by intent/keywords or category. Use this to find relevant tools before invoking them.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["intent"] = new JObject { ["type"] = "string", ["description"] = "Natural language description of what you want to do (e.g., 'create account', 'update contact', 'query metadata')" },
                ["category"] = new JObject { ["type"] = "string", ["description"] = "Filter by category: READ, WRITE, BULK, RELATIONSHIPS, METADATA, SECURITY, RECORD_MGMT, ATTACHMENTS, CHANGE_TRACKING, ASYNC, ADVANCED" },
                ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Maximum tools to return (default 10)" }
            },
            ["required"] = new JArray()
        }
    };
    
    private JObject GetInvokeToolDefinition() => new JObject
    {
        ["name"] = "invoke_tool",
        ["description"] = "Invoke a tool dynamically by name. Use discover_functions first to find the right tool, then invoke_tool to run it.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["toolName"] = new JObject { ["type"] = "string", ["description"] = "Name of the tool to execute (e.g., 'dataverse_create_row', 'dataverse_list_rows')" },
                ["args"] = new JObject { ["type"] = "object", ["description"] = "Arguments to pass to the tool as a JSON object" }
            },
            ["required"] = new JArray { "toolName" }
        }
    };
    
    private JObject GetOrchestratePlanDefinition() => new JObject
    {
        ["name"] = "orchestrate_plan",
        ["description"] = "Orchestrate multiple tools in sequence as a plan. Supports variable substitution between steps using {{varName}} syntax. Use for multi-step operations like 'create account then add contact'.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["steps"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "Array of plan steps to orchestrate in order",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["tool"] = new JObject { ["type"] = "string", ["description"] = "Tool name to execute" },
                            ["args"] = new JObject { ["type"] = "object", ["description"] = "Arguments for the tool. Use {{varName}} to reference previous step outputs." },
                            ["outputAs"] = new JObject { ["type"] = "string", ["description"] = "Variable name to store this step's result for use in later steps" }
                        },
                        ["required"] = new JArray { "tool" }
                    }
                },
                ["stopOnError"] = new JObject { ["type"] = "boolean", ["description"] = "Stop plan on first error (default true)" }
            },
            ["required"] = new JArray { "steps" }
        }
    };
    
    private JObject GetLearnPatternsDefinition() => new JObject
    {
        ["name"] = "learn_patterns",
        ["description"] = "Learn from successful patterns captured in previous executions. Use to suggest next steps or discover common tool sequences used in this organization.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["toolName"] = new JObject { ["type"] = "string", ["description"] = "Filter patterns involving this specific tool (optional)" },
                ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Maximum patterns to return (default 10, max 50)" }
            },
            ["required"] = new JArray()
        }
    };
    
    // Minimal fallback tools when agents.md not configured
    private JArray GetFallbackTools() => new JArray
    {
        GetDiscoverFunctionsDefinition(),
        GetInvokeToolDefinition(),
        GetOrchestratePlanDefinition(),
        GetLearnPatternsDefinition(),
        new JObject
        {
            ["name"] = "dataverse_list_rows",
            ["description"] = "List Dataverse rows from a table with optional $select, $filter, $orderby, $top",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name (e.g., accounts)" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData $filter expression" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows (default 5)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = "dataverse_get_row",
            ["description"] = "Get a single Dataverse row by ID",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Row GUID" }
                },
                ["required"] = new JArray { "table", "id" }
            }
        }
    };

    // Server metadata
    private JObject GetServerInfo() => new JObject
    {
        ["name"] = "dataverse-power-mcp-tools-md",
        ["version"] = "2.0.0",
        ["title"] = "Dataverse Power Orchestration Tools",
        ["description"] = "Power MCP tool server for Dataverse with dynamic tools from tools.md and learned pattern discovery"
    };

    private JObject GetServerCapabilities() => new JObject
    {
        ["tools"] = new JObject { ["listChanged"] = false },
        ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
        ["prompts"] = new JObject { ["listChanged"] = false },
        ["logging"] = new JObject(),
        ["completions"] = new JObject()
    };

    // Tools are now loaded dynamically from agents.md via GetDynamicToolsAsync()
    // See ParseToolsFromAgentMd() for JSON parsing and GetFallbackTools() for minimal defaults

    private JArray GetDefinedResources() => new JArray();
    private JArray GetDefinedResourceTemplates() => new JArray();
    private JArray GetDefinedPrompts() => new JArray();

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation("Dataverse MCP Agent request received");
        
        _ = LogToAppInsights("RequestReceived", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["path"] = this.Context.Request.RequestUri.AbsolutePath,
            ["method"] = this.Context.Request.Method.Method
        });
        
        try
        {
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            // Route metadata/query operations
            if (requestPath.EndsWith("/metadata/tables", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTables metadata operation");
                return await HandleGetTables().ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/metadata/schema", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTableSchema metadata operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                var table = query["table"];
                return await HandleGetTableSchema(table).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/list", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to ListRecords query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleListRecords(
                    query["table"],
                    query["select"],
                    query["filter"],
                    string.IsNullOrEmpty(query["top"]) ? 10 : int.Parse(query["top"])
                ).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/get", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetRecord query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleGetRecord(
                    query["table"],
                    query["id"],
                    query["select"]
                ).ConfigureAwait(false);
            }

            // MCP Protocol mode - JSON-RPC 2.0
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            this.Context.Logger.LogDebug($"Request body length: {body?.Length ?? 0} characters");
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning("Empty request body received");
                return CreateErrorResponse(-32600, "Empty request body", null);
            }

            JObject payload;
            try 
            { 
                payload = JObject.Parse(body); 
            }
            catch (JsonException ex) 
            { 
                return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", null);
            }

            // Route to MCP protocol handler
            this.Context.Logger.LogInformation("Routing to MCP protocol handler");
            return await HandleMCPRequest(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["error"] = ex.Message,
                ["errorType"] = ex.GetType().Name
            });
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", null);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["durationMs"] = duration.TotalMilliseconds.ToString("F0")
            });
        }
    }

    // ---------- MCP Mode ----------
    private async Task<HttpResponseMessage> HandleMCPRequest(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];
        this.Context.Logger.LogInformation($"MCP method: {method}");

        try
        {
            switch (method)
            {
                case "initialize":
                    return CreateSuccessResponse(new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = GetServerCapabilities(),
                        ["serverInfo"] = GetServerInfo()
                    }, id);
                case "initialized":
                case "ping":
                case "notifications/cancelled":
                    return CreateSuccessResponse(new JObject(), id);
                case "tools/list":
                    var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
                    return CreateSuccessResponse(new JObject { ["tools"] = tools }, id);
                case "tools/call":
                    return await HandleToolsCall(request["params"] as JObject, id).ConfigureAwait(false);
                case "resources/list":
                    return CreateSuccessResponse(new JObject { ["resources"] = GetDefinedResources() }, id);
                case "resources/templates/list":
                    return CreateSuccessResponse(new JObject { ["resourceTemplates"] = GetDefinedResourceTemplates() }, id);
                case "resources/read":
                    return CreateErrorResponse(-32601, "resources/read not implemented", id);
                case "prompts/list":
                    return CreateSuccessResponse(new JObject { ["prompts"] = GetDefinedPrompts() }, id);
                case "prompts/get":
                    return CreateErrorResponse(-32000, "prompts not implemented", id);
                case "completion/complete":
                    return CreateSuccessResponse(new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } }, id);
                case "logging/setLevel":
                    return CreateSuccessResponse(new JObject(), id);
                default:
                    return CreateErrorResponse(-32601, $"Method not found: {method}", id);
            }
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", id);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject parms, JToken id)
    {
        if (parms == null) return CreateErrorResponse(-32602, "params object required", id);
        var toolName = parms["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName)) return CreateErrorResponse(-32602, "Tool name required", id);

        var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
        if (!tools.Any(t => t["name"]?.ToString() == toolName)) return CreateErrorResponse(-32601, $"Unknown tool: {toolName}", id);

        var arguments = parms["arguments"] as JObject ?? new JObject();
        try
        {
            var result = await ExecuteToolByName(toolName, arguments).ConfigureAwait(false);
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString() } },
                ["isError"] = false
            }, id);
        }
        catch (ArgumentException ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    // ---------- Tool Execution ----------
    private void InitializeToolHandlers()
    {
        _toolHandlers = new Dictionary<string, Func<JObject, Task<JObject>>>
        {
            // Orchestration tools (use constants)
            [TOOL_DISCOVER_FUNCTIONS] = ExecuteDiscoverFunctions,
            [TOOL_INVOKE_TOOL] = ExecuteInvokeTool,
            [TOOL_ORCHESTRATE_PLAN] = ExecuteOrchestratePlan,
            [TOOL_LEARN_PATTERNS] = ExecuteLearnPatterns,
            
            // Dataverse tools (inline strings - definitions in agents.md)
            ["dataverse_list_rows"] = ExecuteListRows,
            ["dataverse_get_row"] = ExecuteGetRow,
            ["dataverse_create_row"] = ExecuteCreateRow,
            ["dataverse_update_row"] = ExecuteUpdateRow,
            ["dataverse_delete_row"] = ExecuteDeleteRow,
            ["dataverse_fetchxml"] = ExecuteFetchXml,
            ["dataverse_execute_action"] = ExecuteAction,
            ["dataverse_associate"] = ExecuteAssociate,
            ["dataverse_disassociate"] = ExecuteDisassociate,
            ["dataverse_upsert"] = ExecuteUpsert,
            ["dataverse_create_multiple"] = ExecuteCreateMultiple,
            ["dataverse_update_multiple"] = ExecuteUpdateMultiple,
            ["dataverse_upsert_multiple"] = ExecuteUpsertMultiple,
            ["dataverse_batch"] = ExecuteBatch,
            ["dataverse_execute_function"] = ExecuteFunction,
            ["dataverse_query_expand"] = ExecuteQueryExpand,
            ["dataverse_get_entity_metadata"] = ExecuteGetEntityMetadata,
            ["dataverse_get_attribute_metadata"] = ExecuteGetAttributeMetadata,
            ["dataverse_get_relationships"] = ExecuteGetRelationships,
            ["dataverse_count_rows"] = ExecuteCountRows,
            ["dataverse_aggregate"] = ExecuteAggregate,
            ["dataverse_execute_saved_query"] = ExecuteSavedQuery,
            ["dataverse_upload_attachment"] = ExecuteUploadAttachment,
            ["dataverse_download_attachment"] = ExecuteDownloadAttachment,
            ["dataverse_track_changes"] = ExecuteTrackChanges,
            ["dataverse_get_global_optionsets"] = ExecuteGetGlobalOptionSets,
            ["dataverse_get_business_rules"] = ExecuteGetBusinessRules,
            ["dataverse_get_security_roles"] = ExecuteGetSecurityRoles,
            ["dataverse_get_async_operation"] = ExecuteGetAsyncOperation,
            ["dataverse_list_async_operations"] = ExecuteListAsyncOperations,
            ["dataverse_detect_duplicates"] = ExecuteDetectDuplicates,
            ["dataverse_get_audit_history"] = ExecuteGetAuditHistory,
            ["dataverse_get_plugin_traces"] = ExecuteGetPluginTraces,
            ["dataverse_whoami"] = ExecuteWhoAmI,
            ["dataverse_set_state"] = ExecuteSetState,
            ["dataverse_assign"] = ExecuteAssign,
            ["dataverse_merge"] = ExecuteMerge,
            ["dataverse_share"] = ExecuteShare,
            ["dataverse_unshare"] = ExecuteUnshare,
            ["dataverse_modify_access"] = ExecuteModifyAccess,
            ["dataverse_add_team_members"] = ExecuteAddTeamMembers,
            ["dataverse_remove_team_members"] = ExecuteRemoveTeamMembers,
            ["dataverse_retrieve_principal_access"] = ExecuteRetrievePrincipalAccess,
            ["dataverse_initialize_from"] = ExecuteInitializeFrom,
            ["dataverse_calculate_rollup"] = ExecuteCalculateRollup
        };
    }

    private async Task<JObject> ExecuteToolByName(string toolName, JObject args)
    {
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"Executing tool: {toolName}");
        this.Context.Logger.LogDebug($"Tool arguments: {args?.ToString(Newtonsoft.Json.Formatting.None)}");
        
        if (_toolHandlers == null) InitializeToolHandlers();
        
        try
        {
            if (_toolHandlers.TryGetValue(toolName, out var handler))
            {
                var result = await handler(args).ConfigureAwait(false);
                
                _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
                {
                    ["toolName"] = toolName,
                    ["success"] = "true",
                    ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
                });
                
                return result;
            }
            
            throw new Exception($"Unknown tool: {toolName}");
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
            {
                ["toolName"] = toolName,
                ["success"] = "false",
                ["error"] = ex.Message,
                ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            });
            throw;
        }
    }

    private async Task<JObject> ExecuteListRows(JObject args)
    {
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtPage = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtPage, impersonateUserId).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFormatted = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFormatted, impersonateUserId).ConfigureAwait(false);
        return resp;
    }

    private async Task<JObject> ExecuteGetRow(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var select = args["select"]?.ToString();
        var includeFmtGetRow = args["includeFormatted"]?.Value<bool?>() ?? false;
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var qs = string.IsNullOrWhiteSpace(select) ? string.Empty : $"?$select={Uri.EscapeDataString(select)}";
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)}){qs}");
        return await SendDataverseRequest(HttpMethod.Get, url, null, includeFmtGetRow, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var url = BuildDataverseUrl(table);
        return await SendDataverseRequest(HttpMethod.Post, url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteRow(JObject args)
    {
        var table = Require(args, "table");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(HttpMethod.Delete, url, null, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteFetchXml(JObject args)
    {
        var table = Require(args, "table");
        var fetchXml = Require(args, "fetchXml");
        var url = BuildDataverseUrl($"{table}?fetchXml={Uri.EscapeDataString(fetchXml)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAction(JObject args)
    {
        var action = Require(args, "action");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound action: POST /table(id)/Microsoft.Dynamics.CRM.action
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{action}");
        }
        else
        {
            // Unbound action: POST /action
            url = BuildDataverseUrl(action);
        }

        return await SendDataverseRequest(HttpMethod.Post, url, parameters).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedTable = Require(args, "relatedTable");
        var relatedId = Require(args, "relatedId");

        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        var relatedUri = BuildDataverseUrl($"{relatedTable}({SanitizeGuid(relatedId)})");
        
        // Need to build full URI for @odata.id
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var body = new JObject
        {
            ["@odata.id"] = $"{baseUrl}{relatedUri}"
        };

        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDisassociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedId = args["relatedId"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(relatedId))
        {
            // Collection-valued navigation property: DELETE /table(id)/navprop(relatedId)/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}({SanitizeGuid(relatedId)})/$ref");
        }
        else
        {
            // Single-valued navigation property: DELETE /table(id)/navprop/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        }

        return await SendDataverseRequest(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsert(JObject args)
    {
        var table = Require(args, "table");
        var keys = RequireObject(args, "keys");
        var record = RequireObject(args, "record");

        // Build alternate key selector: table(key1=value1,key2=value2)
        var keyPairs = new List<string>();
        foreach (var prop in keys.Properties())
        {
            var val = prop.Value.ToString();
            // Quote string values
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            keyPairs.Add($"{prop.Name}={quotedVal}");
        }
        var keySelector = string.Join(",", keyPairs);
        var url = BuildDataverseUrl($"{table}({keySelector})");

        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("CreateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpdateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsertMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpsertMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteBatch(JObject args)
    {
        var requestsToken = args["requests"];
        if (requestsToken == null || requestsToken.Type != JTokenType.Array)
            throw new ArgumentException("requests must be an array");

        var requests = requestsToken as JArray;
        var batchId = Guid.NewGuid().ToString();
        var batchBoundary = $"batch_{batchId}";
        var changesetId = Guid.NewGuid().ToString();
        var changesetBoundary = $"changeset_{changesetId}";

        var batchContent = new StringBuilder();
        batchContent.AppendLine($"--{batchBoundary}");
        batchContent.AppendLine($"Content-Type: multipart/mixed;boundary={changesetBoundary}");
        batchContent.AppendLine();

        int contentId = 1;
        foreach (var req in requests)
        {
            var reqObj = req as JObject;
            if (reqObj == null) continue;

            var method = reqObj["method"]?.ToString()?.ToUpper() ?? "GET";
            var url = reqObj["url"]?.ToString() ?? "";
            var bodyObj = reqObj["body"] as JObject;

            batchContent.AppendLine($"--{changesetBoundary}");
            batchContent.AppendLine("Content-Type: application/http");
            batchContent.AppendLine("Content-Transfer-Encoding: binary");
            batchContent.AppendLine($"Content-ID: {contentId++}");
            batchContent.AppendLine();
            batchContent.AppendLine($"{method} {url} HTTP/1.1");
            batchContent.AppendLine("Content-Type: application/json");
            batchContent.AppendLine();
            if (bodyObj != null)
            {
                batchContent.AppendLine(bodyObj.ToString(Newtonsoft.Json.Formatting.None));
            }
            batchContent.AppendLine();
        }

        batchContent.AppendLine($"--{changesetBoundary}--");
        batchContent.AppendLine($"--{batchBoundary}--");

        var batchReq = new HttpRequestMessage(HttpMethod.Post, BuildDataverseUrl("$batch"));
        batchReq.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        batchReq.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        batchReq.Content = new StringContent(batchContent.ToString(), Encoding.UTF8, $"multipart/mixed;boundary={batchBoundary}");

        var response = await this.Context.SendAsync(batchReq, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase,
                ["body"] = content
            };
        }

        // Parse multipart response (simplified - return raw for now)
        return new JObject
        {
            ["status"] = (int)response.StatusCode,
            ["batchResponse"] = content
        };
    }

    private async Task<JObject> ExecuteFunction(JObject args)
    {
        var function = Require(args, "function");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        // Build query string from parameters
        var queryParts = new List<string>();
        foreach (var prop in parameters.Properties())
        {
            var val = prop.Value.ToString();
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            queryParts.Add($"{prop.Name}={quotedVal}");
        }
        var queryString = queryParts.Any() ? "?" + string.Join("&", queryParts) : "";

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound function: GET /table(id)/Microsoft.Dynamics.CRM.function(params)
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{function}{queryString}");
        }
        else
        {
            // Unbound function: GET /function(params)
            url = BuildDataverseUrl($"{function}{queryString}");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteQueryExpand(JObject args)
    {
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtNext = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtNext).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var expand = Require(args, "expand");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFmt = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        query.Add("$expand=" + Uri.EscapeDataString(expand));
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFmt).ConfigureAwait(false);
        return resp;
    }

    private async Task<JObject> ExecuteGetEntityMetadata(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,SchemaName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute,EntitySetName");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAttributeMetadata(JObject args)
    {
        var table = Require(args, "table");
        var attribute = args["attribute"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{attribute}')?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }
        else
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetRelationships(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/ManyToOneRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var manyToOne = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/OneToManyRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var oneToMany = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        return new JObject
        {
            ["manyToOne"] = manyToOne["value"] ?? new JArray(),
            ["oneToMany"] = oneToMany["value"] ?? new JArray()
        };
    }

    private async Task<JObject> ExecuteCountRows(JObject args)
    {
        var table = Require(args, "table");
        var filter = args["filter"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query.Add("$filter=" + Uri.EscapeDataString(filter));
        }
        query.Add("$count=true");
        query.Add("$top=0"); // Don't return any rows, just the count
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
        
        return new JObject
        {
            ["count"] = resp["@odata.count"] ?? 0
        };
    }

    private async Task<JObject> ExecuteAggregate(JObject args)
    {
        var table = Require(args, "table");
        var aggregateAttribute = Require(args, "aggregateAttribute");
        var aggregateFunction = Require(args, "aggregateFunction");
        var groupBy = args["groupBy"]?.ToString();
        var filter = args["filter"]?.ToString();
        var filterOperator = args["filterOperator"]?.ToString();
        var filterValue = args["filterValue"]?.ToString();
        
        // Build FetchXML for aggregation
        var fetchXml = $"<fetch aggregate='true'>";
        fetchXml += $"<entity name='{table}'>";
        fetchXml += $"<attribute name='{aggregateAttribute}' alias='result' aggregate='{aggregateFunction}' />";
        
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            fetchXml += $"<attribute name='{groupBy}' alias='groupby' groupby='true' />";
        }
        
        if (!string.IsNullOrWhiteSpace(filter) && !string.IsNullOrWhiteSpace(filterOperator) && !string.IsNullOrWhiteSpace(filterValue))
        {
            fetchXml += $"<filter><condition attribute='{filter}' operator='{filterOperator}' value='{System.Security.SecurityElement.Escape(filterValue)}' /></filter>";
        }
        
        fetchXml += "</entity></fetch>";
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSavedQuery(JObject args)
    {
        var table = Require(args, "table");
        var viewName = Require(args, "viewName");
        var top = args["top"]?.Value<int?>() ?? 5;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Lookup the saved query by name
        var queryFilter = $"returnedtypecode eq '{table}' and name eq '{viewName.Replace("'", "''")}'";
        var queryUrl = BuildDataverseUrl($"savedqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
        var queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
        
        var savedQueries = queryResult["value"] as JArray;
        if (savedQueries == null || savedQueries.Count == 0)
        {
            // Try user query
            queryUrl = BuildDataverseUrl($"userqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
            queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
            savedQueries = queryResult["value"] as JArray;
            
            if (savedQueries == null || savedQueries.Count == 0)
            {
                return new JObject
                {
                    ["error"] = $"Saved query '{viewName}' not found for table '{table}'"
                };
            }
        }
        
        var fetchXml = savedQueries[0]["fetchxml"]?.ToString();
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return new JObject { ["error"] = "FetchXML is empty" };
        }
        
        // Modify FetchXML to apply top limit
        fetchXml = System.Text.RegularExpressions.Regex.Replace(
            fetchXml, 
            "<fetch", 
            $"<fetch top='{top}'", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUploadAttachment(JObject args)
    {
        var regarding = Require(args, "regarding");
        var regardingType = Require(args, "regardingType");
        var fileName = Require(args, "fileName");
        var mimeType = Require(args, "mimeType");
        var content = Require(args, "content");
        var subject = args["subject"]?.ToString() ?? fileName;
        
        var annotation = new JObject
        {
            ["subject"] = subject,
            ["filename"] = fileName,
            ["mimetype"] = mimeType,
            ["documentbody"] = content,
            ["objectid_" + regardingType + "@odata.bind"] = $"/{regardingType}s({SanitizeGuid(regarding)})"
        };
        
        var url = BuildDataverseUrl("annotations");
        return await SendDataverseRequest(HttpMethod.Post, url, annotation).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDownloadAttachment(JObject args)
    {
        var annotationId = Require(args, "annotationId");
        var url = BuildDataverseUrl($"annotations({SanitizeGuid(annotationId)})?$select=filename,mimetype,documentbody,filesize");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteTrackChanges(JObject args)
    {
        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var deltaToken = args["deltaToken"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        
        if (!string.IsNullOrWhiteSpace(deltaToken))
        {
            query.Add("$deltatoken=" + Uri.EscapeDataString(deltaToken));
        }
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        req.Headers.TryAddWithoutValidation("Prefer", "odata.track-changes");
        
        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["error"] = "Change tracking may not be enabled for this table",
                ["body"] = TryParseJson(content)
            };
        }
        
        return TryParseJson(content);
    }

    private async Task<JObject> ExecuteGetGlobalOptionSets(JObject args)
    {
        var optionSetName = args["optionSetName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(optionSetName))
        {
            url = BuildDataverseUrl($"GlobalOptionSetDefinitions(Name='{optionSetName}')?$select=Name,DisplayName,Options");
        }
        else
        {
            url = BuildDataverseUrl("GlobalOptionSetDefinitions?$select=Name,DisplayName,Options");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBusinessRules(JObject args)
    {
        var table = Require(args, "table");
        
        // Query workflows where category = 2 (business rule) and primary entity matches
        var filter = $"category eq 2 and primaryentity eq '{table}'";
        var url = BuildDataverseUrl($"workflows?$select=name,description,statecode,statuscode,xaml&$filter={Uri.EscapeDataString(filter)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetSecurityRoles(JObject args)
    {
        var roleName = args["roleName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var filter = $"name eq '{roleName.Replace("'", "''")}'";
            url = BuildDataverseUrl($"roles?$select=name,roleid,businessunitid&$filter={Uri.EscapeDataString(filter)}");
        }
        else
        {
            url = BuildDataverseUrl("roles?$select=name,roleid,businessunitid&$top=50");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAsyncOperation(JObject args)
    {
        var asyncOperationId = Require(args, "asyncOperationId");
        var url = BuildDataverseUrl($"asyncoperations({SanitizeGuid(asyncOperationId)})?$select=name,statuscode,statecode,message,friendlymessage,errorcode,createdon,completedon");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListAsyncOperations(JObject args)
    {
        var status = args["status"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=name,statuscode,statecode,message,createdon,completedon");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Map friendly status to statuscode values
            var statusCode = status.ToLower() switch
            {
                "inprogress" => "20",
                "succeeded" => "30",
                "failed" => "31",
                "canceled" => "32",
                _ => null
            };
            
            if (statusCode != null)
            {
                query.Add($"$filter=statuscode eq {statusCode}");
            }
        }
        
        var url = BuildDataverseUrl($"asyncoperations?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectDuplicates(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        
        // Use RetrieveDuplicates action
        var requestBody = new JObject
        {
            ["BusinessEntity"] = record,
            ["MatchingEntityName"] = table,
            ["PagingInfo"] = new JObject
            {
                ["PageNumber"] = 1,
                ["Count"] = 50
            }
        };
        
        var url = BuildDataverseUrl("RetrieveDuplicates");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAuditHistory(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Query audit table for the specific record
        var filter = $"objectid/Id eq {SanitizeGuid(recordId)} and objecttypecode eq '{table}'";
        var url = BuildDataverseUrl($"audits?$select=createdon,action,userid,attributemask,changedata&$filter={Uri.EscapeDataString(filter)}&$orderby=createdon desc&$top={top}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetPluginTraces(JObject args)
    {
        var correlationId = args["correlationId"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=typename,messageblock,exceptiondetails,createdon,correlationid");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query.Add($"$filter=correlationid eq {SanitizeGuid(correlationId)}");
        }
        
        var url = BuildDataverseUrl($"plugintracelog?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteWhoAmI(JObject args)
    {
        var url = BuildDataverseUrl("WhoAmI");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSetState(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var state = args["state"]?.Value<int?>() ?? throw new Exception("'state' is required");
        var status = args["status"]?.Value<int?>() ?? throw new Exception("'status' is required");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["State"] = state,
            ["Status"] = status
        };
        
        var url = BuildDataverseUrl("SetState");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssign(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var assigneeId = Require(args, "assigneeId");
        var assigneeType = Require(args, "assigneeType");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Assignee"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{assigneeType}",
                [assigneeType + "id"] = SanitizeGuid(assigneeId)
            }
        };
        
        var url = BuildDataverseUrl("Assign");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteMerge(JObject args)
    {
        var table = Require(args, "table");
        var targetId = Require(args, "targetId");
        var subordinateId = Require(args, "subordinateId");
        var updateContent = args["updateContent"] as JObject;
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(targetId)
            },
            ["SubordinateId"] = SanitizeGuid(subordinateId),
            ["PerformParentingChecks"] = false
        };
        
        if (updateContent != null)
        {
            requestBody["UpdateContent"] = updateContent;
        }
        
        var url = BuildDataverseUrl("Merge");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteShare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var principalType = Require(args, "principalType");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = $"Microsoft.Dynamics.CRM.{principalType}",
                    [principalType + "id"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("GrantAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUnshare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Revokee"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RevokeAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteModifyAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                    ["systemuserid"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("ModifyAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("AddMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRemoveTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("RemoveMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRetrievePrincipalAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Principal"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RetrievePrincipalAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteInitializeFrom(JObject args)
    {
        var sourceTable = Require(args, "sourceTable");
        var sourceId = Require(args, "sourceId");
        var targetTable = Require(args, "targetTable");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{sourceTable}",
                [sourceTable + "id"] = SanitizeGuid(sourceId)
            },
            ["TargetEntityName"] = targetTable,
            ["TargetFieldType"] = 0
        };
        
        var url = BuildDataverseUrl("InitializeFrom");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCalculateRollup(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var fieldName = Require(args, "fieldName");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["FieldName"] = fieldName
        };
        
        var url = BuildDataverseUrl("CalculateRollupField");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private string BuildDataverseUrl(string relativePath)
    {
        var clean = relativePath.TrimStart('/');
        return $"/api/data/v9.2/{clean}";
    }

    private async Task<JObject> SendDataverseRequest(HttpMethod method, string url, JObject body, bool includeFormatted = false, string impersonateUserId = null, string correlationId = null)
    {
        // Ensure absolute URL for Dataverse requests
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
            url = $"{baseUrl}{url}";
            this.Context.Logger.LogDebug($"Constructed absolute URL: {url}");
        }
        
        var req = new HttpRequestMessage(method, url);
        
        // Copy OAuth token from incoming request to Dataverse request
        if (this.Context.Request.Headers.Authorization != null)
        {
            req.Headers.Authorization = this.Context.Request.Headers.Authorization;
            this.Context.Logger.LogDebug("OAuth token forwarded to Dataverse request");
        }
        
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");

        // Impersonation header
        if (!string.IsNullOrWhiteSpace(impersonateUserId))
        {
            req.Headers.TryAddWithoutValidation("MSCRMCallerID", SanitizeGuid(impersonateUserId));
        }

        // Telemetry/correlation header for request tracking
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            req.Headers.TryAddWithoutValidation("x-ms-correlation-request-id", correlationId);
        }

        // Include formatted values for lookups/optionsets/money fields
        if (includeFormatted)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");
        }

        // Ask Dataverse to return representations on writes
        if (method == HttpMethod.Post || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            var preferValue = includeFormatted ? "return=representation,odata.include-annotations=\"*\"" : "return=representation";
            req.Headers.Remove("Prefer");
            req.Headers.TryAddWithoutValidation("Prefer", preferValue);
        }

        // Use wildcard ETag to allow overwrite when no specific ETag is supplied
        if (method == HttpMethod.Delete || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.TryAddWithoutValidation("If-Match", "*");
        }

        if (body != null)
        {
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Dataverse API error: {response.StatusCode} - {url}");
            // Enhanced error parsing for Dataverse errors
            var errorObj = new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase
            };

            try
            {
                var errorBody = JObject.Parse(content);
                var error = errorBody["error"];
                if (error != null)
                {
                    errorObj["errorCode"] = error["code"];
                    errorObj["message"] = error["message"];
                    errorObj["details"] = error["innererror"]?["message"] ?? error["message"];
                }
                else
                {
                    errorObj["body"] = errorBody;
                }
            }
            catch
            {
                errorObj["body"] = content;
            }

            return errorObj;
        }

        return TryParseJson(content);
    }

    private JObject TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JObject();
        try { return JObject.Parse(text); }
        catch { return new JObject { ["text"] = text }; }
    }

    private string Require(JObject obj, string name)
    {
        var val = obj?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{name} is required");
        return val;
    }

    private JObject RequireObject(JObject obj, string name)
    {
        var token = obj?[name] as JObject;
        if (token == null) throw new ArgumentException($"{name} must be an object");
        return token;
    }

    private string SanitizeGuid(string id)
    {
        var trimmed = id.Trim();
        if (Guid.TryParse(trimmed, out var g)) return g.ToString();
        throw new ArgumentException("id must be a GUID");
    }

    // ---------- Query/Metadata Handlers ----------
    private async Task<HttpResponseMessage> HandleGetTables()
    {
        try
        {
            // Query EntityDefinitions for common tables
            var url = "/api/data/v9.2/EntityDefinitions?$select=LogicalName,DisplayName&$filter=IsValidForAdvancedFind eq true and IsCustomizable/Value eq true";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve tables",
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var entities = data["value"] as JArray;
            var tables = new JArray();
            
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    var logicalName = entity["LogicalName"]?.ToString();
                    var displayName = entity["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        tables.Add(new JObject
                        {
                            ["name"] = logicalName,
                            ["displayName"] = displayName ?? logicalName
                        });
                    }
                }
            }

            return CreateHttpResponse(HttpStatusCode.OK, tables);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetTableSchema(string table)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Query EntityDefinition for attributes
            var url = $"/api/data/v9.2/EntityDefinitions(LogicalName='{table}')?$select=LogicalName&$expand=Attributes($select=LogicalName,DisplayName,AttributeType)";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve table schema",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var attributes = data["Attributes"] as JArray;
            var properties = new JObject();
            
            if (attributes != null)
            {
                foreach (var attr in attributes)
                {
                    var logicalName = attr["LogicalName"]?.ToString();
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        properties[logicalName] = new JObject
                        {
                            ["type"] = "string",  // Simplified - all properties as string
                            ["description"] = attr["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString() ?? logicalName
                        };
                    }
                }
            }

            var schema = new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = properties
                }
            };

            return CreateHttpResponse(HttpStatusCode.OK, new JObject
            {
                ["schema"] = schema
            });
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleListRecords(string table, string select, string filter, int top)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}";
            var queryParams = new List<string>();
            
            if (!string.IsNullOrEmpty(select))
                queryParams.Add($"$select={select}");
            if (!string.IsNullOrEmpty(filter))
                queryParams.Add($"$filter={filter}");
            if (top > 0 && top <= 50)
                queryParams.Add($"$top={top}");

            if (queryParams.Any())
                url += "?" + string.Join("&", queryParams);

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve records",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data["value"] ?? new JArray());
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetRecord(string table, string id, string select)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }
            if (string.IsNullOrEmpty(id))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "ID parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}({id})";
            if (!string.IsNullOrEmpty(select))
                url += $"?$select={select}";

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve record",
                    ["table"] = table,
                    ["id"] = id,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    // ---------- Helpers ----------
    // Connection parameters are not used (OAuth handles Dataverse token; AI key is constant)

    private HttpResponseMessage CreateHttpResponse(HttpStatusCode statusCode, JToken content)
    {
        var resp = new HttpResponseMessage(statusCode);
        resp.Content = CreateJsonContent(content.ToString());
        return resp;
    }

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    private HttpResponseMessage CreateErrorResponse(int code, string message, JToken id)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject { ["code"] = code, ["message"] = message },
            ["id"] = id
        };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    // ---------- Application Insights Telemetry ----------
    
    /// <summary>
    /// Send custom event to Application Insights (fire-and-forget)
    /// </summary>
    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint") 
                ?? "https://dc.services.visualstudio.com/";
            
            if (string.IsNullOrEmpty(instrumentationKey))
                return; // Telemetry disabled
            
            var telemetryData = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = instrumentationKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties)
                    }
                }
            };
            
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var request = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(telemetryData.ToString(), Encoding.UTF8, "application/json")
            };
            
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors - don't fail the main request
        }
    }
    
    private string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}



