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
/// Humanizer MCP Server - Detects and removes AI writing patterns to make text sound more natural
/// 
/// Based on Wikipedia's "Signs of AI writing" guide and blader/humanizer skill.
/// Detects 24 AI writing patterns across categories:
/// - Content patterns (significance inflation, promotional language, vague attributions)
/// - Language patterns (AI vocabulary, copula avoidance, rule of three)
/// - Style patterns (em dash overuse, boldface, emojis)
/// - Communication patterns (chatbot artifacts, sycophantic tone)
/// - Filler and hedging (filler phrases, excessive hedging)
/// 
/// MCP specification compliance (2025-11-25) - Copilot Studio compatible
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // SERVER CONFIGURATION
    // ========================================
    private const string ServerName = "humanizer-mcp";
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "Humanizer MCP";
    private const string ServerDescription = "Detects and removes AI writing patterns to make text sound more natural and human";
    private const string ProtocolVersion = "2025-11-25";
    private const string ServerInstructions = @"Use the humanize tool to improve AI-generated text by removing common patterns that make writing sound artificial. 
The tool detects 24 different AI writing patterns and provides suggestions for making text more natural.
You can analyze text for patterns, get a full rewrite, or request specific pattern detection.";

    // ========================================
    // APPLICATION INSIGHTS (optional)
    // ========================================
    private const string APP_INSIGHTS_CONNECTION_STRING = ""; // Set connection string to enable telemetry

    // ========================================
    // TOOL NAMES
    // ========================================
    private const string TOOL_HUMANIZE = "humanize";
    private const string TOOL_DETECT_PATTERNS = "detect_patterns";
    private const string TOOL_GET_PATTERNS = "get_patterns";

    // ========================================
    // AI WRITING PATTERNS
    // ========================================
    private static readonly Dictionary<string, PatternDefinition> AIPatterns = new Dictionary<string, PatternDefinition>
    {
        // CONTENT PATTERNS
        ["significance_inflation"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Significance Inflation",
            Description = "Puffs up importance with statements about how aspects represent broader topics",
            WordsToWatch = new[] { "stands as", "serves as", "is a testament", "pivotal moment", "crucial role", "vital role", "underscores", "highlights its importance", "reflects broader", "symbolizing its ongoing", "enduring", "lasting", "contributing to the", "setting the stage", "marking", "shaping the", "key turning point", "evolving landscape", "focal point", "indelible mark", "deeply rooted" },
            ExampleBefore = "marking a pivotal moment in the evolution of...",
            ExampleAfter = "was established in 1989 to collect regional statistics"
        },
        ["notability_namedropping"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Notability Name-dropping",
            Description = "Lists sources without context to claim notability",
            WordsToWatch = new[] { "independent coverage", "local media", "regional media", "national media", "leading expert", "active social media presence", "cited in", "featured in", "covered by" },
            ExampleBefore = "cited in NYT, BBC, FT, and The Hindu",
            ExampleAfter = "In a 2024 NYT interview, she argued..."
        },
        ["superficial_ing_analyses"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Superficial -ing Analyses",
            Description = "Tacks present participle phrases onto sentences to add fake depth",
            WordsToWatch = new[] { "highlighting", "underscoring", "emphasizing", "ensuring", "reflecting", "symbolizing", "contributing to", "cultivating", "fostering", "encompassing", "showcasing" },
            ExampleBefore = "symbolizing... reflecting... showcasing...",
            ExampleAfter = "Remove or expand with actual sources"
        },
        ["promotional_language"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Promotional Language",
            Description = "Advertisement-like tone, especially for cultural topics",
            WordsToWatch = new[] { "boasts a", "vibrant", "rich cultural", "profound", "enhancing its", "showcasing", "exemplifies", "commitment to", "natural beauty", "nestled", "in the heart of", "groundbreaking", "renowned", "breathtaking", "must-visit", "stunning" },
            ExampleBefore = "nestled within the breathtaking region",
            ExampleAfter = "is a town in the Gonder region"
        },
        ["vague_attributions"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Vague Attributions",
            Description = "Attributes opinions to vague authorities without specific sources",
            WordsToWatch = new[] { "industry reports", "observers have cited", "experts argue", "some critics argue", "several sources", "several publications", "experts believe", "studies show", "research indicates" },
            ExampleBefore = "Experts believe it plays a crucial role",
            ExampleAfter = "according to a 2019 survey by..."
        },
        ["formulaic_challenges"] = new PatternDefinition
        {
            Category = "Content",
            Name = "Formulaic Challenges",
            Description = "Generic 'Challenges and Future Prospects' sections",
            WordsToWatch = new[] { "despite its", "faces several challenges", "despite these challenges", "challenges and legacy", "future outlook", "continues to thrive" },
            ExampleBefore = "Despite challenges... continues to thrive",
            ExampleAfter = "Specific facts about actual challenges"
        },

        // LANGUAGE PATTERNS
        ["ai_vocabulary"] = new PatternDefinition
        {
            Category = "Language",
            Name = "AI Vocabulary Words",
            Description = "High-frequency words that appear far more often in post-2023 AI text",
            WordsToWatch = new[] { "additionally", "align with", "crucial", "delve", "emphasizing", "enduring", "enhance", "fostering", "garner", "highlight", "interplay", "intricate", "intricacies", "landscape", "pivotal", "showcase", "tapestry", "testament", "underscore", "valuable", "vibrant" },
            ExampleBefore = "Additionally... testament... landscape... showcasing",
            ExampleAfter = "also... remain common"
        },
        ["copula_avoidance"] = new PatternDefinition
        {
            Category = "Language",
            Name = "Copula Avoidance",
            Description = "Substitutes elaborate constructions for simple 'is/are'",
            WordsToWatch = new[] { "serves as", "stands as", "marks", "represents a", "boasts", "features", "offers a" },
            ExampleBefore = "serves as... features... boasts",
            ExampleAfter = "is... has"
        },
        ["negative_parallelisms"] = new PatternDefinition
        {
            Category = "Language",
            Name = "Negative Parallelisms",
            Description = "Overuse of 'Not only...but...' or 'It's not just about...'",
            WordsToWatch = new[] { "not only", "but also", "it's not just about", "it's not merely", "it's about" },
            ExampleBefore = "It's not just X, it's Y",
            ExampleAfter = "State the point directly"
        },
        ["rule_of_three"] = new PatternDefinition
        {
            Category = "Language",
            Name = "Rule of Three Overuse",
            Description = "Forces ideas into groups of three to appear comprehensive",
            WordsToWatch = new[] { "innovation, inspiration, and", "seamless, intuitive, and", "streamlining processes, enhancing collaboration, and" },
            ExampleBefore = "innovation, inspiration, and insights",
            ExampleAfter = "Use natural number of items"
        },
        ["synonym_cycling"] = new PatternDefinition
        {
            Category = "Language",
            Name = "Synonym Cycling",
            Description = "Excessive synonym substitution due to repetition-penalty",
            WordsToWatch = new[] { "the protagonist", "the main character", "the central figure", "the hero" },
            ExampleBefore = "protagonist... main character... central figure... hero",
            ExampleAfter = "protagonist (repeat when clearest)"
        },
        ["false_ranges"] = new PatternDefinition
        {
            Category = "Language",
            Name = "False Ranges",
            Description = "Uses 'from X to Y' where X and Y aren't on a meaningful scale",
            WordsToWatch = new[] { "from hobbyist experiments to enterprise-wide", "from solo developers to cross-functional teams", "from the singularity to" },
            ExampleBefore = "from the Big Bang to dark matter",
            ExampleAfter = "List topics directly"
        },

        // STYLE PATTERNS
        ["em_dash_overuse"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Em Dash Overuse",
            Description = "Uses em dashes more than humans, mimicking 'punchy' sales writing",
            WordsToWatch = new[] { "\u2014" },
            ExampleBefore = "institutions\u2014not the people\u2014yet this continues\u2014",
            ExampleAfter = "Use commas or periods"
        },
        ["boldface_overuse"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Boldface Overuse",
            Description = "Emphasizes phrases in boldface mechanically",
            WordsToWatch = new[] { "**" },
            ExampleBefore = "**OKRs**, **KPIs**, **BMC**",
            ExampleAfter = "OKRs, KPIs, BMC"
        },
        ["inline_header_lists"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Inline-Header Lists",
            Description = "Lists where items start with bolded headers followed by colons",
            WordsToWatch = new[] { "**User Experience:**", "**Performance:**", "**Security:**" },
            ExampleBefore = "**Performance:** Performance improved",
            ExampleAfter = "Convert to prose"
        },
        ["title_case_headings"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Title Case Headings",
            Description = "Capitalizes all main words in headings unnecessarily",
            WordsToWatch = new string[] { },
            ExampleBefore = "Strategic Negotiations And Partnerships",
            ExampleAfter = "Strategic negotiations and partnerships"
        },
        ["emojis"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Emoji Decoration",
            Description = "Decorates headings or bullet points with emojis",
            WordsToWatch = new[] { "\ud83d\ude80", "\ud83d\udca1", "\u2705", "\ud83c\udfaf", "\u2b50", "\ud83d\udd25", "\ud83d\udcc8", "\ud83d\udcaa", "\ud83c\udf1f", "\u2728" },
            ExampleBefore = "\ud83d\ude80 Launch Phase: \ud83d\udca1 Key Insight:",
            ExampleAfter = "Remove emojis"
        },
        ["curly_quotes"] = new PatternDefinition
        {
            Category = "Style",
            Name = "Curly Quotation Marks",
            Description = "Uses curly quotes instead of straight quotes",
            WordsToWatch = new[] { "\u201c", "\u201d", "\u2018", "\u2019" },
            ExampleBefore = "said \u201cthe project\u201d",
            ExampleAfter = "said \"the project\""
        },

        // COMMUNICATION PATTERNS
        ["chatbot_artifacts"] = new PatternDefinition
        {
            Category = "Communication",
            Name = "Chatbot Artifacts",
            Description = "Text meant as chatbot correspondence gets pasted as content",
            WordsToWatch = new[] { "i hope this helps", "of course!", "certainly!", "you're absolutely right", "would you like", "let me know", "here is a", "great question", "happy to help" },
            ExampleBefore = "I hope this helps! Let me know if...",
            ExampleAfter = "Remove entirely"
        },
        ["cutoff_disclaimers"] = new PatternDefinition
        {
            Category = "Communication",
            Name = "Knowledge-Cutoff Disclaimers",
            Description = "AI disclaimers about incomplete information left in text",
            WordsToWatch = new[] { "as of", "up to my last training", "while specific details are limited", "based on available information", "while details are scarce" },
            ExampleBefore = "While details are limited in available sources...",
            ExampleAfter = "Find sources or remove"
        },
        ["sycophantic_tone"] = new PatternDefinition
        {
            Category = "Communication",
            Name = "Sycophantic Tone",
            Description = "Overly positive, people-pleasing language",
            WordsToWatch = new[] { "great question", "excellent point", "you're absolutely right", "that's a fantastic", "wonderful insight" },
            ExampleBefore = "Great question! You're absolutely right!",
            ExampleAfter = "Respond directly"
        },

        // FILLER AND HEDGING
        ["filler_phrases"] = new PatternDefinition
        {
            Category = "Filler",
            Name = "Filler Phrases",
            Description = "Unnecessary wordy constructions",
            WordsToWatch = new[] { "in order to", "due to the fact that", "at this point in time", "in the event that", "has the ability to", "it is important to note that", "at its core", "the value proposition is" },
            ExampleBefore = "In order to, Due to the fact that",
            ExampleAfter = "To, Because"
        },
        ["excessive_hedging"] = new PatternDefinition
        {
            Category = "Filler",
            Name = "Excessive Hedging",
            Description = "Over-qualifying statements",
            WordsToWatch = new[] { "could potentially", "might possibly", "it could be argued", "some might say", "perhaps maybe" },
            ExampleBefore = "could potentially possibly",
            ExampleAfter = "may"
        },
        ["generic_conclusions"] = new PatternDefinition
        {
            Category = "Filler",
            Name = "Generic Positive Conclusions",
            Description = "Vague upbeat endings",
            WordsToWatch = new[] { "the future looks bright", "exciting times lie ahead", "journey toward excellence", "step in the right direction", "continue to thrive" },
            ExampleBefore = "The future looks bright",
            ExampleAfter = "Specific plans or facts"
        }
    };

    private class PatternDefinition
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] WordsToWatch { get; set; }
        public string ExampleBefore { get; set; }
        public string ExampleAfter { get; set; }
    }

    // ========================================
    // MAIN ENTRY
    // ========================================
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"[{correlationId}] MCP request received");

        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            try
            {
                body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                _ = LogToAppInsights("ParseError", new { CorrelationId = correlationId, Error = "Unable to read request body" });
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning($"[{correlationId}] Empty request body received");
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                _ = LogToAppInsights("ParseError", new { CorrelationId = correlationId, Error = "Invalid JSON" });
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method,
                HasId = requestId != null
            });

            this.Context.Logger.LogInformation($"[{correlationId}] Processing MCP method: {method}");

            HttpResponseMessage response;
            switch (method)
            {
                case "initialize":
                    response = HandleInitialize(correlationId, request, requestId);
                    break;
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;
                case "ping":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;
                case "tools/list":
                    response = HandleToolsList(correlationId, request, requestId);
                    break;
                case "tools/call":
                    response = await HandleToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;
                case "resources/list":
                    response = HandleResourcesList(correlationId, requestId);
                    break;
                case "resources/templates/list":
                    response = HandleResourceTemplatesList(correlationId, requestId);
                    break;
                case "resources/read":
                    response = HandleResourcesRead(correlationId, request, requestId);
                    break;
                case "prompts/list":
                    response = HandlePromptsList(correlationId, requestId);
                    break;
                case "prompts/get":
                    response = HandlePromptsGet(correlationId, request, requestId);
                    break;
                case "completion/complete":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });
                    break;
                case "logging/setLevel":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;
                default:
                    _ = LogToAppInsights("McpMethodNotFound", new { CorrelationId = correlationId, Method = method });
                    response = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Internal error: {ex.Message}, StackTrace: {ex.StackTrace}");
            _ = LogToAppInsights("McpError", new
            {
                CorrelationId = correlationId,
                Method = method,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"[{correlationId}] Request completed in {duration.TotalMilliseconds}ms");
        }
    }

    // ========================================
    // MCP HANDLERS
    // ========================================
    private HttpResponseMessage HandleInitialize(string correlationId, JObject request, JToken requestId)
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
                ["logging"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = ServerTitle,
                ["description"] = ServerDescription
            },
            ["instructions"] = ServerInstructions
        };

        _ = LogToAppInsights("McpInitialized", new
        {
            CorrelationId = correlationId,
            ServerName = ServerName,
            ServerVersion = ServerVersion
        });

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(string correlationId, JObject request, JToken requestId)
    {
        var tools = BuildToolsList();

        _ = LogToAppInsights("McpToolsListed", new
        {
            CorrelationId = correlationId,
            ToolCount = tools.Count
        });

        var result = new JObject { ["tools"] = tools };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleResourcesList(string correlationId, JToken requestId)
    {
        var resources = new JArray
        {
            new JObject
            {
                ["uri"] = "patterns://all",
                ["name"] = "All AI Writing Patterns",
                ["description"] = "Complete list of 24 AI writing patterns with descriptions and examples",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "patterns://content",
                ["name"] = "Content Patterns",
                ["description"] = "AI patterns related to content: significance inflation, promotional language, vague attributions",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "patterns://language",
                ["name"] = "Language Patterns",
                ["description"] = "AI patterns related to language: AI vocabulary, copula avoidance, rule of three",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "patterns://style",
                ["name"] = "Style Patterns",
                ["description"] = "AI patterns related to style: em dashes, boldface, emojis, formatting",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "patterns://communication",
                ["name"] = "Communication Patterns",
                ["description"] = "AI patterns in communication: chatbot artifacts, sycophantic tone",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "patterns://filler",
                ["name"] = "Filler Patterns",
                ["description"] = "AI filler patterns: filler phrases, excessive hedging, generic conclusions",
                ["mimeType"] = "text/plain"
            },
            new JObject
            {
                ["uri"] = "examples://before-after",
                ["name"] = "Before/After Examples",
                ["description"] = "Sample AI text transformations showing humanization in action",
                ["mimeType"] = "text/plain"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = resources });
    }

    private HttpResponseMessage HandleResourceTemplatesList(string correlationId, JToken requestId)
    {
        var templates = new JArray
        {
            new JObject
            {
                ["uriTemplate"] = "patterns://{category}",
                ["name"] = "Pattern Category",
                ["description"] = "Get AI patterns for a specific category (all, content, language, style, communication, filler)"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = templates });
    }

    private HttpResponseMessage HandleResourcesRead(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri") ?? "";

        string content = null;
        string mimeType = "text/plain";

        if (uri == "patterns://all" || uri == "patterns://")
        {
            content = GeneratePatternsResourceContent(null);
        }
        else if (uri == "patterns://content")
        {
            content = GeneratePatternsResourceContent("Content");
        }
        else if (uri == "patterns://language")
        {
            content = GeneratePatternsResourceContent("Language");
        }
        else if (uri == "patterns://style")
        {
            content = GeneratePatternsResourceContent("Style");
        }
        else if (uri == "patterns://communication")
        {
            content = GeneratePatternsResourceContent("Communication");
        }
        else if (uri == "patterns://filler")
        {
            content = GeneratePatternsResourceContent("Filler");
        }
        else if (uri == "examples://before-after")
        {
            content = GenerateExamplesResourceContent();
        }
        else
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", $"Unknown resource URI: {uri}");
        }

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = mimeType,
                    ["text"] = content
                }
            }
        });
    }

    private string GeneratePatternsResourceContent(string categoryFilter)
    {
        var sb = new StringBuilder();
        
        if (string.IsNullOrEmpty(categoryFilter))
        {
            sb.AppendLine("# All AI Writing Patterns (24 Total)");
            sb.AppendLine();
            sb.AppendLine("Based on Wikipedia's 'Signs of AI writing' guide.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"# {categoryFilter} Patterns");
            sb.AppendLine();
        }

        var patterns = AIPatterns
            .Where(p => string.IsNullOrEmpty(categoryFilter) || p.Value.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Value.Category)
            .OrderBy(g => g.Key);

        foreach (var group in patterns)
        {
            if (string.IsNullOrEmpty(categoryFilter))
            {
                sb.AppendLine($"## {group.Key} Patterns");
                sb.AppendLine();
            }

            foreach (var pattern in group)
            {
                sb.AppendLine($"### {pattern.Value.Name}");
                sb.AppendLine($"**ID:** {pattern.Key}");
                sb.AppendLine($"**Description:** {pattern.Value.Description}");
                sb.AppendLine();
                
                if (pattern.Value.WordsToWatch.Length > 0)
                {
                    sb.AppendLine("**Words to watch:**");
                    sb.AppendLine(string.Join(", ", pattern.Value.WordsToWatch.Take(10)));
                    sb.AppendLine();
                }

                sb.AppendLine($"**Before:** {pattern.Value.ExampleBefore}");
                sb.AppendLine($"**After:** {pattern.Value.ExampleAfter}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private string GenerateExamplesResourceContent()
    {
        return @"# Before/After Examples

## Example 1: Promotional Blog Post

**Before (AI-generated):**
> Great question! This serves as a pivotal moment in the evolution of AI. The landscape is vibrant and continues to thrive despite challenges. Additionally, experts believe it plays a crucial role in fostering innovation, highlighting the intricate interplay between automation and human judgment. I hope this helps!

**After (Humanized):**
> AI tools are getting better at specific tasks. A 2024 Stanford study found that code assistants helped developers complete routine functions 40% faster, though they showed no improvement on debugging. The technology works well for repetitive tasks but still needs human oversight.

**Changes made:**
- Removed chatbot artifacts ('Great question!', 'I hope this helps!')
- Removed significance inflation ('pivotal moment', 'evolution')
- Replaced vague 'experts believe' with specific study
- Removed AI vocabulary ('landscape', 'fostering', 'interplay')
- Added specific facts and numbers

---

## Example 2: Company Description

**Before (AI-generated):**
> Nestled in the heart of Silicon Valley, TechCorp stands as a testament to innovation. The company boasts a vibrant culture and showcases its commitment to excellence. It's not just about technology; it's about making a difference. The future looks bright!

**After (Humanized):**
> TechCorp is a software company in Palo Alto, founded in 2018. They make project management tools used by about 500 companies. Last year they raised $20M in Series B funding and hired 50 engineers.

**Changes made:**
- Removed promotional language ('nestled', 'vibrant', 'testament')
- Replaced 'stands as' with 'is'
- Removed generic statements ('commitment to excellence')
- Removed negative parallelism ('not just...it's about')
- Removed generic conclusion ('future looks bright')
- Added specific facts: location, founding year, product, customers, funding

---

## Example 3: Technical Explanation

**Before (AI-generated):**
> In order to delve into this topic, it is important to note that machine learning serves as a powerful tool. The technology encompasses various methodologies, from hobbyist experiments to enterprise-wide implementations. \ud83d\ude80 **Key insight:** It could potentially revolutionize the industry.

**After (Humanized):**
> Machine learning uses statistical models to find patterns in data. Companies use it for things like spam filtering, product recommendations, and fraud detection. The main challenge is getting enough clean training data.

**Changes made:**
- Removed filler ('In order to', 'it is important to note')
- Replaced AI vocabulary ('delve', 'encompasses')
- Replaced 'serves as' with direct statement
- Removed false range ('from hobbyist to enterprise-wide')
- Removed emoji and bold formatting
- Removed hedging ('could potentially')
- Added concrete examples
";
    }

    private HttpResponseMessage HandlePromptsList(string correlationId, JToken requestId)
    {
        var prompts = new JArray
        {
            new JObject
            {
                ["name"] = "humanize_text",
                ["description"] = "Full humanization: analyze text for AI patterns and provide detailed rewrite guidelines",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "text",
                        ["description"] = "The text to humanize",
                        ["required"] = true
                    }
                }
            },
            new JObject
            {
                ["name"] = "quick_check",
                ["description"] = "Fast AI detection: get AI score and top issues without full guidelines",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "text",
                        ["description"] = "The text to check",
                        ["required"] = true
                    }
                }
            },
            new JObject
            {
                ["name"] = "rewrite_as_human",
                ["description"] = "Aggressive rewriting prompt: transform AI text into natural human writing",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "text",
                        ["description"] = "The text to rewrite",
                        ["required"] = true
                    }
                }
            },
            new JObject
            {
                ["name"] = "match_voice",
                ["description"] = "Humanize while preserving a specific voice or tone (formal, casual, technical, friendly)",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "text",
                        ["description"] = "The text to humanize",
                        ["required"] = true
                    },
                    new JObject
                    {
                        ["name"] = "voice",
                        ["description"] = "Target voice: formal, casual, technical, or friendly",
                        ["required"] = true
                    }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = prompts });
    }

    private HttpResponseMessage HandlePromptsGet(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        string promptContent = null;
        string description = null;

        switch (promptName)
        {
            case "humanize_text":
                var text1 = arguments["text"]?.ToString() ?? "";
                description = "Full humanization with detailed guidelines";
                promptContent = $@"You are a writing editor that removes signs of AI-generated text.

Analyze and rewrite the following text to sound more natural and human:

{text1}

Guidelines:
1. Remove significance inflation ('pivotal moment', 'testament to')
2. Replace vague attributions with specific sources
3. Remove promotional language ('nestled', 'vibrant', 'breathtaking')
4. Replace AI vocabulary words ('delve', 'landscape', 'foster')
5. Use simple 'is/are' instead of 'serves as', 'stands as'
6. Remove chatbot artifacts ('I hope this helps', 'Great question!')
7. Cut filler phrases ('in order to' \u2192 'to')
8. Remove excessive hedging
9. Replace generic conclusions with specific facts
10. Add personality and voice - opinions, varied rhythm, specific details

Provide the rewritten text followed by a brief summary of changes made.";
                break;

            case "quick_check":
                var text2 = arguments["text"]?.ToString() ?? "";
                description = "Fast AI detection check";
                promptContent = $@"Quickly analyze this text for AI writing patterns. Return:
1. AI Score (0-100, lower = more human)
2. Top 3 issues found
3. One-sentence verdict

Text to analyze:
{text2}

Be concise - no lengthy explanations needed.";
                break;

            case "rewrite_as_human":
                var text3 = arguments["text"]?.ToString() ?? "";
                description = "Aggressive humanization rewrite";
                promptContent = $@"Completely rewrite this AI-generated text to sound like a human wrote it. Be aggressive about removing AI patterns:

{text3}

Rules:
- NO 'pivotal', 'crucial', 'testament', 'landscape', 'delve', 'foster', 'enhance'
- NO 'serves as', 'stands as' - use 'is'
- NO 'I hope this helps', 'Great question!', 'Let me know'
- NO 'in order to', 'due to the fact that' - use 'to', 'because'
- NO em dashes for emphasis
- NO emoji decorations
- NO vague 'experts say' - use specific sources or remove
- NO 'the future looks bright' endings

ADD:
- Specific facts, dates, names
- Varied sentence length
- First-person perspective where natural
- Opinions and reactions
- Acknowledgment of uncertainty when appropriate

Return ONLY the rewritten text, nothing else.";
                break;

            case "match_voice":
                var text4 = arguments["text"]?.ToString() ?? "";
                var voice = arguments["voice"]?.ToString()?.ToLowerInvariant() ?? "casual";
                description = $"Humanize while maintaining {voice} voice";
                
                var voiceGuidance = voice switch
                {
                    "formal" => "Maintain professional tone. Use complete sentences. Avoid contractions. Keep structure clear but remove AI patterns.",
                    "technical" => "Keep technical accuracy. Use precise terminology. Remove fluff but preserve necessary detail. Be direct.",
                    "friendly" => "Use warm, approachable language. Contractions are fine. Add conversational elements. Be personable but not sycophantic.",
                    _ => "Use relaxed, conversational language. Contractions encouraged. Short sentences mixed with longer ones. Be natural."
                };

                promptContent = $@"Rewrite this text to sound human while maintaining a {voice} voice:

{text4}

Voice guidance: {voiceGuidance}

Still remove these AI patterns:
- Significance inflation ('pivotal moment', 'testament')
- AI vocabulary ('delve', 'landscape', 'foster')
- Chatbot artifacts ('I hope this helps')
- Vague attributions ('experts say')
- Filler phrases ('in order to')

Return the rewritten text followed by a note about how you preserved the {voice} tone.";
                break;

            default:
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", $"Unknown prompt: {promptName}");
        }

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["description"] = description,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JObject
                    {
                        ["type"] = "text",
                        ["text"] = promptContent
                    }
                }
            }
        });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string correlationId, JObject request, JToken requestId)
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

        this.Context.Logger.LogInformation($"[{correlationId}] Executing tool: {toolName}");
        _ = LogToAppInsights("McpToolCallStarted", new { CorrelationId = correlationId, ToolName = toolName });

        try
        {
            JObject toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            _ = LogToAppInsights("McpToolCallCompleted", new { CorrelationId = correlationId, ToolName = toolName, IsError = false });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) }
                },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("McpToolCallError", new { CorrelationId = correlationId, ToolName = toolName, ErrorMessage = ex.Message });
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // TOOL ROUTER
    // ========================================
    private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
    {
        switch (toolName.ToLowerInvariant())
        {
            case TOOL_HUMANIZE:
                return ExecuteHumanize(arguments);
            case TOOL_DETECT_PATTERNS:
                return ExecuteDetectPatterns(arguments);
            case TOOL_GET_PATTERNS:
                return ExecuteGetPatterns(arguments);
            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    // ========================================
    // HUMANIZE IMPLEMENTATION
    // ========================================
    private JObject ExecuteHumanize(JObject args)
    {
        var text = RequireArgument(args, "text");
        var mode = args["mode"]?.ToString()?.ToLowerInvariant() ?? "full";
        var preserveTone = args["preserveTone"]?.ToObject<bool?>() ?? true;

        // Detect patterns in the text
        var detectedPatterns = DetectPatternsInText(text);
        
        // Generate suggestions based on detected patterns
        var suggestions = GenerateSuggestions(text, detectedPatterns);
        
        // Calculate AI score (0-100, lower is more human)
        var aiScore = CalculateAIScore(text, detectedPatterns);

        var result = new JObject
        {
            ["success"] = true,
            ["originalText"] = text,
            ["aiScore"] = aiScore,
            ["aiScoreDescription"] = GetAIScoreDescription(aiScore),
            ["patternsDetected"] = detectedPatterns.Count,
            ["detectedPatterns"] = new JArray(detectedPatterns.Select(p => new JObject
            {
                ["patternId"] = p.PatternId,
                ["patternName"] = p.PatternName,
                ["category"] = p.Category,
                ["matchedText"] = p.MatchedText,
                ["suggestion"] = p.Suggestion
            })),
            ["suggestions"] = new JArray(suggestions)
        };

        if (mode == "full" || mode == "rewrite")
        {
            result["rewriteGuidelines"] = GenerateRewriteGuidelines(detectedPatterns);
        }

        return result;
    }

    private JObject ExecuteDetectPatterns(JObject args)
    {
        var text = RequireArgument(args, "text");
        var category = args["category"]?.ToString();

        var detectedPatterns = DetectPatternsInText(text, category);
        var aiScore = CalculateAIScore(text, detectedPatterns);

        var patternsByCategory = detectedPatterns
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new JObject
        {
            ["success"] = true,
            ["textLength"] = text.Length,
            ["aiScore"] = aiScore,
            ["aiScoreDescription"] = GetAIScoreDescription(aiScore),
            ["totalPatterns"] = detectedPatterns.Count,
            ["patternsByCategory"] = new JObject(
                patternsByCategory.Select(kvp => new JProperty(kvp.Key, new JArray(
                    kvp.Value.Select(p => new JObject
                    {
                        ["patternId"] = p.PatternId,
                        ["patternName"] = p.PatternName,
                        ["matchedText"] = p.MatchedText,
                        ["position"] = p.Position,
                        ["suggestion"] = p.Suggestion
                    })
                )))
            ),
            ["summary"] = GeneratePatternSummary(detectedPatterns)
        };
    }

    private JObject ExecuteGetPatterns(JObject args)
    {
        var category = args["category"]?.ToString();
        var includeExamples = args["includeExamples"]?.ToObject<bool?>() ?? true;

        var patterns = AIPatterns
            .Where(p => string.IsNullOrEmpty(category) || p.Value.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => new JObject
            {
                ["id"] = kvp.Key,
                ["name"] = kvp.Value.Name,
                ["category"] = kvp.Value.Category,
                ["description"] = kvp.Value.Description,
                ["wordsToWatch"] = new JArray(kvp.Value.WordsToWatch.Take(10)),
                ["exampleBefore"] = includeExamples ? kvp.Value.ExampleBefore : null,
                ["exampleAfter"] = includeExamples ? kvp.Value.ExampleAfter : null
            })
            .ToList();

        var categories = AIPatterns.Values
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        return new JObject
        {
            ["success"] = true,
            ["totalPatterns"] = patterns.Count,
            ["categories"] = new JArray(categories),
            ["patterns"] = new JArray(patterns)
        };
    }

    // ========================================
    // PATTERN DETECTION
    // ========================================
    private class DetectedPattern
    {
        public string PatternId { get; set; }
        public string PatternName { get; set; }
        public string Category { get; set; }
        public string MatchedText { get; set; }
        public int Position { get; set; }
        public string Suggestion { get; set; }
    }

    private List<DetectedPattern> DetectPatternsInText(string text, string categoryFilter = null)
    {
        var detected = new List<DetectedPattern>();
        var lowerText = text.ToLowerInvariant();

        foreach (var pattern in AIPatterns)
        {
            if (!string.IsNullOrEmpty(categoryFilter) && 
                !pattern.Value.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var watchWord in pattern.Value.WordsToWatch)
            {
                if (string.IsNullOrEmpty(watchWord)) continue;

                var lowerWatch = watchWord.ToLowerInvariant();
                var index = lowerText.IndexOf(lowerWatch);
                
                if (index >= 0)
                {
                    // Extract surrounding context
                    var contextStart = Math.Max(0, index - 20);
                    var contextEnd = Math.Min(text.Length, index + watchWord.Length + 20);
                    var matchedText = text.Substring(contextStart, contextEnd - contextStart);

                    detected.Add(new DetectedPattern
                    {
                        PatternId = pattern.Key,
                        PatternName = pattern.Value.Name,
                        Category = pattern.Value.Category,
                        MatchedText = matchedText.Trim(),
                        Position = index,
                        Suggestion = GetSuggestionForPattern(pattern.Key, watchWord)
                    });
                }
            }
        }

        // Deduplicate by pattern (keep first occurrence)
        return detected
            .GroupBy(p => p.PatternId)
            .Select(g => g.First())
            .OrderBy(p => p.Position)
            .ToList();
    }

    private string GetSuggestionForPattern(string patternId, string matchedWord)
    {
        var suggestions = new Dictionary<string, string>
        {
            ["significance_inflation"] = "Remove inflated language. State facts directly.",
            ["notability_namedropping"] = "Replace name-dropping with specific claims from specific sources.",
            ["superficial_ing_analyses"] = "Remove -ing phrases or expand with actual sources.",
            ["promotional_language"] = "Use neutral, factual language instead.",
            ["vague_attributions"] = "Replace with specific sources (who said what, when).",
            ["formulaic_challenges"] = "Replace with specific facts about actual challenges.",
            ["ai_vocabulary"] = $"Replace '{matchedWord}' with simpler alternatives.",
            ["copula_avoidance"] = "Use simple 'is' or 'are' instead.",
            ["negative_parallelisms"] = "State the point directly without 'not only...but'.",
            ["rule_of_three"] = "Use a natural number of items, not forced threes.",
            ["synonym_cycling"] = "Repeat the clearest word instead of cycling synonyms.",
            ["false_ranges"] = "List topics directly instead of 'from X to Y'.",
            ["em_dash_overuse"] = "Use commas or periods instead of em dashes.",
            ["boldface_overuse"] = "Remove unnecessary bold formatting.",
            ["inline_header_lists"] = "Convert to prose paragraphs.",
            ["title_case_headings"] = "Use sentence case for headings.",
            ["emojis"] = "Remove decorative emojis.",
            ["curly_quotes"] = "Use straight quotes instead.",
            ["chatbot_artifacts"] = "Remove chatbot phrases entirely.",
            ["cutoff_disclaimers"] = "Find actual sources or remove the disclaimer.",
            ["sycophantic_tone"] = "Respond directly without excessive praise.",
            ["filler_phrases"] = "Simplify: 'in order to' \u2192 'to', 'due to the fact that' \u2192 'because'.",
            ["excessive_hedging"] = "Reduce hedging: 'could potentially' \u2192 'may'.",
            ["generic_conclusions"] = "Replace with specific plans or facts."
        };

        return suggestions.TryGetValue(patternId, out var suggestion) ? suggestion : "Consider revising for clarity.";
    }

    private List<string> GenerateSuggestions(string text, List<DetectedPattern> patterns)
    {
        var suggestions = new List<string>();

        // Group by category and provide category-level suggestions
        var byCategory = patterns.GroupBy(p => p.Category).ToList();

        foreach (var category in byCategory)
        {
            switch (category.Key)
            {
                case "Content":
                    suggestions.Add($"Content issues ({category.Count()}): Replace vague claims with specific facts and sources.");
                    break;
                case "Language":
                    suggestions.Add($"Language patterns ({category.Count()}): Simplify word choices and sentence structure.");
                    break;
                case "Style":
                    suggestions.Add($"Style issues ({category.Count()}): Clean up formatting and punctuation.");
                    break;
                case "Communication":
                    suggestions.Add($"Communication artifacts ({category.Count()}): Remove chatbot-style language.");
                    break;
                case "Filler":
                    suggestions.Add($"Filler detected ({category.Count()}): Cut unnecessary words and hedging.");
                    break;
            }
        }

        // Add voice suggestion if text is very neutral
        if (patterns.Count > 3)
        {
            suggestions.Add("Add personality: Use first-person when appropriate, vary sentence rhythm, acknowledge uncertainty.");
        }

        return suggestions;
    }

    private int CalculateAIScore(string text, List<DetectedPattern> patterns)
    {
        // Base score starts at 0 (fully human)
        var score = 0;

        // Add points for each pattern detected (weighted by severity)
        var weights = new Dictionary<string, int>
        {
            ["Content"] = 5,
            ["Language"] = 4,
            ["Communication"] = 6,
            ["Style"] = 2,
            ["Filler"] = 3
        };

        foreach (var pattern in patterns)
        {
            score += weights.TryGetValue(pattern.Category, out var weight) ? weight : 3;
        }

        // Normalize to 0-100 scale
        var wordCount = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var densityFactor = Math.Min(1.0, wordCount / 100.0); // Adjust for text length
        
        score = (int)(score * densityFactor);
        return Math.Min(100, score);
    }

    private string GetAIScoreDescription(int score)
    {
        if (score <= 10) return "Excellent - Reads very human";
        if (score <= 25) return "Good - Minor AI patterns detected";
        if (score <= 50) return "Moderate - Several AI patterns present";
        if (score <= 75) return "High - Significant AI characteristics";
        return "Very High - Strongly AI-generated";
    }

    private string GeneratePatternSummary(List<DetectedPattern> patterns)
    {
        if (patterns.Count == 0)
            return "No significant AI writing patterns detected. The text appears natural.";

        var topPatterns = patterns
            .GroupBy(p => p.PatternName)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        return $"Primary issues: {string.Join(", ", topPatterns)}. Consider revising for a more natural tone.";
    }

    private string GenerateRewriteGuidelines(List<DetectedPattern> patterns)
    {
        var guidelines = new List<string>
        {
            "## Rewrite Guidelines",
            "",
            "Based on detected patterns, focus on:"
        };

        var categories = patterns.Select(p => p.Category).Distinct().ToList();

        if (categories.Contains("Content"))
        {
            guidelines.Add("- **Replace vague claims** with specific facts, dates, and named sources");
            guidelines.Add("- **Remove significance inflation** - let facts speak for themselves");
        }

        if (categories.Contains("Language"))
        {
            guidelines.Add("- **Simplify vocabulary** - 'delve' \u2192 'explore', 'landscape' \u2192 'field/area'");
            guidelines.Add("- **Use 'is/are'** instead of 'serves as', 'stands as', 'represents'");
        }

        if (categories.Contains("Communication"))
        {
            guidelines.Add("- **Remove chatbot artifacts** - no 'I hope this helps' or 'Great question!'");
            guidelines.Add("- **Respond directly** without sycophantic praise");
        }

        if (categories.Contains("Style"))
        {
            guidelines.Add("- **Clean up formatting** - reduce em dashes, remove emojis, use sentence case");
        }

        if (categories.Contains("Filler"))
        {
            guidelines.Add("- **Cut filler** - 'in order to' \u2192 'to', 'due to the fact' \u2192 'because'");
            guidelines.Add("- **Reduce hedging** - state things directly");
        }

        guidelines.Add("");
        guidelines.Add("**Add personality:** Vary sentence length, include opinions when appropriate, acknowledge complexity.");

        return string.Join("\n", guidelines);
    }

    // ========================================
    // TOOL DEFINITIONS
    // ========================================
    private JArray BuildToolsList()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = TOOL_HUMANIZE,
                ["description"] = "Analyze text for AI writing patterns and provide humanization suggestions. Returns detected patterns, AI score, and rewrite guidelines.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject 
                        { 
                            ["type"] = "string", 
                            ["description"] = "The text to analyze and humanize" 
                        },
                        ["mode"] = new JObject 
                        { 
                            ["type"] = "string", 
                            ["enum"] = new JArray { "detect", "full", "rewrite" },
                            ["description"] = "Analysis mode: 'detect' for patterns only, 'full' for patterns + guidelines, 'rewrite' for transformation suggestions",
                            ["default"] = "full"
                        },
                        ["preserveTone"] = new JObject 
                        { 
                            ["type"] = "boolean", 
                            ["description"] = "Whether to preserve the original tone (formal, casual, technical)",
                            ["default"] = true
                        }
                    },
                    ["required"] = new JArray { "text" }
                },
                ["annotations"] = new JObject 
                { 
                    ["readOnlyHint"] = true, 
                    ["idempotentHint"] = true,
                    ["title"] = "Humanize Text",
                    ["destructiveHint"] = false
                }
            },
            new JObject
            {
                ["name"] = TOOL_DETECT_PATTERNS,
                ["description"] = "Detect specific AI writing patterns in text without rewrite suggestions. Returns detailed pattern analysis by category.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject 
                        { 
                            ["type"] = "string", 
                            ["description"] = "The text to analyze for AI patterns" 
                        },
                        ["category"] = new JObject 
                        { 
                            ["type"] = "string", 
                            ["enum"] = new JArray { "Content", "Language", "Style", "Communication", "Filler" },
                            ["description"] = "Filter by pattern category (optional)"
                        }
                    },
                    ["required"] = new JArray { "text" }
                },
                ["annotations"] = new JObject 
                { 
                    ["readOnlyHint"] = true, 
                    ["idempotentHint"] = true 
                }
            },
            new JObject
            {
                ["name"] = TOOL_GET_PATTERNS,
                ["description"] = "Get the list of all 24 AI writing patterns with descriptions and examples. Use to understand what patterns the humanizer detects.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["category"] = new JObject 
                        { 
                            ["type"] = "string", 
                            ["enum"] = new JArray { "Content", "Language", "Style", "Communication", "Filler" },
                            ["description"] = "Filter patterns by category (optional)"
                        },
                        ["includeExamples"] = new JObject 
                        { 
                            ["type"] = "boolean", 
                            ["description"] = "Include before/after examples",
                            ["default"] = true
                        }
                    },
                    ["required"] = new JArray()
                },
                ["annotations"] = new JObject 
                { 
                    ["readOnlyHint"] = true, 
                    ["idempotentHint"] = true 
                }
            }
        };
    }

    // ========================================
    // HELPER METHODS
    // ========================================
    private string RequireArgument(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required");
        }
        return value;
    }

    private string GetConnectionParameter(string name)
    {
        // Try to get from connection parameters via headers or query
        return null;
    }

    // ========================================
    // JSON-RPC RESPONSE HELPERS
    // ========================================
    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // TELEMETRY (OPTIONAL)
    // ========================================
    private async Task LogToAppInsights(string eventName, object properties)
    {
        if (string.IsNullOrWhiteSpace(APP_INSIGHTS_CONNECTION_STRING))
        {
            return;
        }

        try
        {
            // Parse connection string for ingestion endpoint
            var parts = APP_INSIGHTS_CONNECTION_STRING.Split(';')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            if (!parts.TryGetValue("InstrumentationKey", out var instrumentationKey))
            {
                return;
            }

            var endpoint = parts.TryGetValue("IngestionEndpoint", out var ing) 
                ? ing.TrimEnd('/') + "/v2/track" 
                : "https://dc.services.visualstudio.com/v2/track";

            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
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

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silently fail telemetry
        }
    }
}
