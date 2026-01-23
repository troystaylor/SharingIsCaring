# Humanizer MCP

MCP-compliant custom connector that detects and removes AI writing patterns to make text sound more natural and human. Based on [Wikipedia's "Signs of AI writing"](https://en.wikipedia.org/wiki/Wikipedia:Signs_of_AI_writing) guide and the [blader/humanizer](https://github.com/blader/humanizer) Claude Code skill.

## Features

- **MCP compliant** (2025-11-25) and **Copilot Studio** compatible
- Detects **24 AI writing patterns** across 5 categories
- Returns **AI score** (0-100) indicating how AI-generated text appears
- Provides **rewrite guidelines** and **specific suggestions**
- Optional Application Insights telemetry

## Tools

### humanize

Analyze text for AI writing patterns and provide humanization suggestions.

**Parameters:**
- `text` *(string, required)*: The text to analyze
- `mode` *(string, optional)*: `detect` | `full` | `rewrite` (default: `full`)
- `preserveTone` *(boolean, optional)*: Maintain original tone (default: `true`)

**Returns:**
- `aiScore`: 0-100 score (lower = more human)
- `detectedPatterns`: Array of patterns found with suggestions
- `rewriteGuidelines`: Markdown guidelines for humanizing

### detect_patterns

Detailed pattern detection by category.

**Parameters:**
- `text` *(string, required)*: The text to analyze
- `category` *(string, optional)*: Filter by `Content` | `Language` | `Style` | `Communication` | `Filler`

### get_patterns

Get the list of all 24 AI writing patterns with descriptions and examples.

**Parameters:**
- `category` *(string, optional)*: Filter by category
- `includeExamples` *(boolean, optional)*: Include before/after examples (default: `true`)

## 24 Patterns Detected

### Content Patterns (1-6)
| # | Pattern | Words to Watch |
|---|---------|----------------|
| 1 | Significance Inflation | "pivotal moment", "testament to", "crucial role" |
| 2 | Notability Name-dropping | "cited in", "featured in", "covered by" |
| 3 | Superficial -ing Analyses | "highlighting", "showcasing", "reflecting" |
| 4 | Promotional Language | "nestled", "vibrant", "breathtaking", "stunning" |
| 5 | Vague Attributions | "experts believe", "studies show", "research indicates" |
| 6 | Formulaic Challenges | "despite challenges", "continues to thrive" |

### Language Patterns (7-12)
| # | Pattern | Words to Watch |
|---|---------|----------------|
| 7 | AI Vocabulary Words | "delve", "landscape", "foster", "tapestry" |
| 8 | Copula Avoidance | "serves as", "stands as", "represents a" |
| 9 | Negative Parallelisms | "not only...but also", "it's not just about" |
| 10 | Rule of Three Overuse | "innovation, inspiration, and insights" |
| 11 | Synonym Cycling | "protagonist...main character...central figure" |
| 12 | False Ranges | "from X to Y" where not meaningful |

### Style Patterns (13-18)
| # | Pattern | Issue |
|---|---------|-------|
| 13 | Em Dash Overuse | Too many — dashes |
| 14 | Boldface Overuse | **Excessive** emphasis |
| 15 | Inline-Header Lists | **Label:** Description format |
| 16 | Title Case Headings | Every Word Capitalized |
| 17 | Emoji Decoration | 🚀 💡 ✅ in content |
| 18 | Curly Quotes | "curly" vs "straight" |

### Communication Patterns (19-21)
| # | Pattern | Words to Watch |
|---|---------|----------------|
| 19 | Chatbot Artifacts | "I hope this helps!", "Let me know if..." |
| 20 | Knowledge-Cutoff Disclaimers | "as of my last training", "based on available information" |
| 21 | Sycophantic Tone | "Great question!", "You're absolutely right!" |

### Filler and Hedging (22-24)
| # | Pattern | Before → After |
|---|---------|----------------|
| 22 | Filler Phrases | "in order to" → "to" |
| 23 | Excessive Hedging | "could potentially possibly" → "may" |
| 24 | Generic Conclusions | "The future looks bright" → specific facts |

## AI Score Interpretation

| Score | Description |
|-------|-------------|
| 0-10 | Excellent - Reads very human |
| 11-25 | Good - Minor AI patterns detected |
| 26-50 | Moderate - Several AI patterns present |
| 51-75 | High - Significant AI characteristics |
| 76-100 | Very High - Strongly AI-generated |

## Swagger (Streamable HTTP)

```json
{
  "swagger": "2.0",
  "info": {
    "title": "Humanizer MCP",
    "description": "Detects and removes AI writing patterns to make text sound more natural",
    "version": "1.0.0"
  },
  "host": "your-api-host.com",
  "basePath": "/mcp",
  "schemes": ["https"],
  "consumes": ["application/json"],
  "produces": ["application/json"],
  "paths": {
    "/": {
      "post": {
        "summary": "MCP Server Streamable HTTP",
        "x-ms-agentic-protocol": "mcp-streamable-1.0",
        "operationId": "InvokeMCP",
        "responses": { "200": { "description": "Success" } }
      }
    }
  }
}
```

## apiProperties.json

```json
{
  "properties": {
    "connectionParameters": {},
    "policyTemplateInstances": [
      {
        "templateId": "routeRequestToCode",
        "title": "MCP Handler",
        "parameters": {
          "x-ms-apimTemplate-operationName": ["InvokeMCP"]
        }
      }
    ]
  }
}
```

## Example Usage

### Copilot Studio Agent Instruction

```
When generating text responses, use the humanize tool to check your draft before sending.
If the AI score is above 25, revise using the provided guidelines.

Focus on:
- Removing chatbot artifacts ("I hope this helps")
- Using specific facts instead of vague claims
- Simplifying vocabulary ("delve" → "explore")
- Adding personality and varied sentence structure
```

### Sample Tool Call

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 1,
  "params": {
    "name": "humanize",
    "arguments": {
      "text": "Great question! This serves as a pivotal moment in the evolution of AI. The landscape is vibrant and continues to thrive despite challenges. I hope this helps!",
      "mode": "full"
    }
  }
}
```

### Sample Response

```json
{
  "success": true,
  "aiScore": 68,
  "aiScoreDescription": "High - Significant AI characteristics",
  "patternsDetected": 5,
  "detectedPatterns": [
    {
      "patternId": "chatbot_artifacts",
      "patternName": "Chatbot Artifacts",
      "category": "Communication",
      "matchedText": "Great question!",
      "suggestion": "Remove chatbot phrases entirely."
    },
    {
      "patternId": "significance_inflation",
      "patternName": "Significance Inflation",
      "matchedText": "pivotal moment in the evolution",
      "suggestion": "Remove inflated language. State facts directly."
    }
  ],
  "rewriteGuidelines": "## Rewrite Guidelines\n\nBased on detected patterns..."
}
```

## Extending Patterns

To add new patterns as your voice evolves, modify the `AIPatterns` dictionary in script.csx:

```csharp
["new_pattern_id"] = new PatternDefinition
{
    Category = "Content", // or Language, Style, Communication, Filler
    Name = "New Pattern Name",
    Description = "What this pattern indicates",
    WordsToWatch = new[] { "word1", "phrase to watch" },
    ExampleBefore = "AI-sounding version",
    ExampleAfter = "Human-sounding version"
}
```

## References

- [Wikipedia: Signs of AI writing](https://en.wikipedia.org/wiki/Wikipedia:Signs_of_AI_writing)
- [WikiProject AI Cleanup](https://en.wikipedia.org/wiki/Wikipedia:WikiProject_AI_Cleanup)
- [blader/humanizer](https://github.com/blader/humanizer) - Original Claude Code skill

## License

MIT
