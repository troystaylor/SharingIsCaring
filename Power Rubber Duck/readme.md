# Power Rubber Duck - Enterprise Multi-Perspective Analysis Engine

A Model Context Protocol (MCP) connector for Microsoft Copilot Studio that provides comprehensive decision support through alternative perspectives, risk analysis, cognitive bias detection, and comparative reasoning. Designed for organizations making high-stakes decisions.

## Overview

**Problem:** Even sophisticated models can miss important perspectives, overlook risks, or fall prey to reasoning biases. Important decisions need multiple analytical angles.

**Solution:** Power Rubber Duck exposes both analytical tools AND a comprehensive knowledge base as MCP resources. Copilot Studio's primary model can:
1. Call tools for alternative analysis
2. Access shared decision frameworks
3. Reference case studies and benchmarks
4. Check for cognitive biases in its own reasoning

**Architecture:**
```
User Question
    ↓
Copilot Studio (Primary Model)
    ├→ Reads: Decision Framework → Structures analysis
    ├→ Calls: get_second_opinion → Foundry model analyzes
    ├→ Calls: identify_cognitive_biases → Checks gaps
    ├→ Reads: Case Studies → Reference similar decisions
    └→ Synthesizes → Unified recommendation with perspectives
```

## Key Features

### Tools (4 Analytical Capabilities)

| Tool | Purpose | Use When |
|------|---------|----------|
| **get_second_opinion** | Alternative perspective from Foundry model | Complex decisions, risk assessment, creative exploration |
| **analyze_risk** | Structured risk/reward assessment | Major initiatives, changes with downside risk |
| **identify_cognitive_biases** | Detect reasoning blind spots | Making sense of disagreement, validating analysis |
| **comparative_analysis** | Side-by-side comparison of options | Evaluating multiple approaches, tradeoff analysis |

### Resources (10 Knowledge Assets)

**Decision Frameworks** (3)
- Investment Assessment Framework
- Operational Change Framework
- Strategic Planning Framework

**Knowledge Base** (2)
- Industry Benchmarks & Patterns
- Case Studies Repository

**Reasoning Guides** (3)
- Cognitive Bias Checklist
- Structured Decision Process
- Critical Questions Checklist

**Best Practices** (2)
- Implementation Best Practices
- Organizational Decision-Making Practices

## Installation & Configuration

### Prerequisites
- Power Platform CLI (`pac`) installed
- Access to Copilot Studio environment
- Foundry model endpoint (local or cloud):
  - **Local**: `http://localhost:60311/v1` (requires local Foundry running)
  - **Cloud**: Azure OpenAI or other OpenAI-compatible endpoint

### Deploy Connector

```powershell
# Validate connector
ppcv ".\Power Rubber Duck"

# Create connector in Power Platform
pac connector create --api-definition-file ".\Power Rubber Duck\apiDefinition.swagger.json" `
                     --api-properties-file ".\Power Rubber Duck\apiProperties.json" `
                     --script-file ".\Power Rubber Duck\script.csx"
```

### Configure for Copilot Studio

1. **In Copilot Studio**, create a new agent or edit existing
2. **Add MCP Plugin**: Copilot Studio → Plugins → Add MCP
3. **Paste MCP endpoint URL**: From your connector deployment
4. **Test the connection**:
   ```json
   {
     "jsonrpc": "2.0",
     "method": "tools/list",
     "id": "test-1"
   }
   ```
   Should return 4 tools in the result.

### Configure Foundry Model

Edit `script.csx` to configure Foundry endpoint and model:

```csharp
private string foundryEndpoint = "http://localhost:60311/v1"; // or cloud endpoint
private string foundryModel = "phi-4"; // or "mistral-7b", "llama-3", etc.
```

Or use connection parameters (see apiProperties.json):
- `foundry_endpoint`: Override the hardcoded endpoint
- `foundry_model`: Override the hardcoded model name

### Application Insights Setup

1. Replace placeholder in script.csx:
   ```csharp
   private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
   ```

2. Get your instrumentation key from Azure Application Insights resource

3. All telemetry includes:
   - Tool calls and methods invoked
   - Resource reads and usage
   - Foundry model calls and performance
   - Error tracking with stack traces

## Usage Examples

### Example 1: Should We Enter This Market?

**User (to Copilot Studio):**
> "We're considering entering the healthcare AI market. The TAM is $50B with 25% growth. We have 3 engineers and limited domain expertise. What should we consider?"

**Copilot Studio Flow:**
1. Reads `resource://decision-frameworks/investment-assessment`
2. Calls `tools/call → get_second_opinion` with depth="deep"
   - Secondary model: "Consider regulatory barriers, reputation risk with healthcare, and longer sales cycles..."
3. Calls `tools/call → analyze_risk` with focus_area="market timing"
   - Foundry response: "First-mover disadvantage risk: Large tech companies likely to enter soon..."
4. Calls `resources/read → resource://knowledge-base/case-studies`
   - Reviews similar market entries and outcomes
5. **Response to User:**
   > "The market is attractive (Investment Framework suggests promising), but you're competing against better-resourced incumbents (Second Opinion suggests first-mover disadvantage). Key risks: [from Risk Analysis]. Similar case: Company X entered healthcare AI successfully by focusing on workflow integration rather than being first. Recommendation: Acquire domain expertise first—recommend hiring a healthcare AI consultant before major investment."

### Example 2: Should We Implement This Org Change?

**User:**
> "We want to move to cross-functional product teams. I think this is obviously right. But we're getting pushback from engineering leadership. What am I missing?"

**Copilot Studio Flow:**
1. Calls `tools/call → identify_cognitive_biases` with focus="anchoring, confirmation bias"
   - Result: "You may be anchoring on what worked at your previous company. Check: What about this org is different?"
2. Calls `resources/read → resource://reasoning/decision-process`
   - Gets structured change assessment
3. Calls `tools/call → get_second_opinion` with analysis_depth="balanced"
   - Secondary: "Engineering concerns about context-switching and skill dilution are valid. Consider: Pilot first with one team, measure productivity impact..."
4. Calls `tools/call → analyze_risk` with scenario="org restructure" and focus_area="productivity loss"

**Response:**
> "Your instinct is sound, but there are legitimate concerns. Alternative view: The productivity loss during transition might be higher than your Framework assumes (6 months vs. 4 weeks). Recommendation: Pilot with one cross-functional team for 90 days, measure productivity and satisfaction, then decide on org-wide rollout. Key metrics: [from resources]"

### Example 3: Option Comparison

**User:**
> "Should we build, buy, or partner for our new data integration capability? Help me think through the tradeoffs."

**Copilot Studio Flow:**
1. Calls `tools/call → comparative_analysis` with options=["Build in-house", "Buy solution", "Partner with vendor"], criteria=["Time-to-market", "Cost", "Customization", "Risk", "Team capability"]
2. Calls `resources/read → resource://best-practices/implementation`
3. Calls `tools/call → analyze_risk` for each option
4. Calls `tools/call → get_second_opinion` with focus="hidden costs"

**Response:**
> "Comparison shows: [Matrix]. Build wins on customization, Buy wins on speed and cost, Partner splits the difference. Second opinion: Don't underestimate integration and change management costs in Buy scenario (often 3x the software cost). Recommendation: Buy + Partner hybrid—purchase solution but retain vendor for customization to your workflows."

## Resource Content Reference

### Decision Frameworks
Each framework provides:
- **Quick Evaluation**: Key factors to assess
- **Structured Steps**: Multi-phase process
- **Checklists**: What to verify before deciding
- **Risk Assessment**: Key risks by domain
- **Decision Criteria**: How to weight factors

**Files:**
- `resources/decision-frameworks-investment.md` - For investment/M&A decisions
- `resources/decision-frameworks-operational.md` - For org changes and process redesigns
- `resources/decision-frameworks-strategic.md` - For market entry and competitive moves

### Knowledge Base
**Industry Benchmarks:**
- Reference metrics by industry and stage
- Comparison sources (Gartner, IDC, public companies)
- How to calibrate assumptions against benchmarks
- When to trust benchmarks vs. your unique situation

**Case Studies:**
- JSON format with `decision_type`, `outcome`, and `lessons`
- Add new case studies to `resources/case-studies.json`
- Include both success and failure cases

### Reasoning Guides
**Cognitive Bias Checklist:**
- 8 major cognitive biases with detection patterns
- Why they matter in decision-making
- Specific mitigation strategies
- Quality checklist for your reasoning

**Decision Process:**
- 5-phase structured process (Define → Criteria → Generate → Analyze → Decide)
- How to weight and score options
- Sensitivity analysis and risk review
- Decision documentation requirements

**Critical Questions:**
- 30 essential pre-decision questions
- Organized by category (Reality, Competition, Team, Finance, Implementation, Culture)
- Prevents common blind spots

### Best Practices
**Implementation Best Practices:**
- Pre-launch checklist
- Communication strategy
- Pace and rollout approaches
- Resistance management
- Metrics and course correction

**Organizational Decision-Making:**
- Governance framework (who decides what)
- Process maturity
- Building decision-making muscle
- Anti-patterns to avoid

## Resource Versioning & Updates

### Embedded Resources
All resources are embedded in `script.csx`. For updates:

1. Edit the resource content in the appropriate `GetResourceName()` method
2. Redeploy the connector: `pac connector update ...`
3. Copilot Studio automatically picks up new resources

### External Knowledge Base (Optional)
To point to external resources (Azure Blob Storage, etc.):

1. Store resources in Azure Blob Storage or similar
2. Update connection parameters in apiProperties.json
3. Modify `GetResourceContent()` to load from external URL:
   ```csharp
   private async Task<string> GetResourceContent(string uri)
   {
       var externalUrl = $"{knowledgeBaseUrl}/resources/{uri}.md";
       using (var client = new HttpClient())
       {
           return await client.GetStringAsync(externalUrl);
       }
   }
   ```

4. This allows live updates without redeploying connector

## Monitoring & Diagnostics

### Application Insights
All events are logged to Application Insights if configured:

**Events:**
- `MCP_Request` - All incoming MCP requests with method
- `Tool_Call` - Which tool was invoked
- `SecondOpinionGenerated`, `RiskAnalysisCompleted`, etc. - Tool completions
- `FoundryModelCall` - Secondary model interactions
- `ResourceRead` - Which resources were accessed

**Exceptions:**
- `MCP_Handler_Error` - MCP protocol errors
- `Tool_Call_Error` - Tool execution failures
- `FoundryModelCallError` - Secondary model failures
- `ResourceReadError` - Resource access failures

**Queries:**
```kusto
// Errors in last 24 hours
customEvents
| where name startswith "error"
| summarize count() by name
| order by count_ desc

// Most used tools
customEvents
| where name == "Tool_Call"
| extend tool = tostring(customDimensions.tool)
| summarize count() by tool
| order by count_ desc

// Foundry model performance
customEvents
| where name == "FoundryModelCall"
| extend responseLength = toint(customDimensions.responseLength)
| summarize avg(responseLength), max(responseLength) by bin(timestamp, 1h)
```

## Troubleshooting

### Connector Won't Deploy
```
Error: "Ambiguous policy sections defined for policy template setheader"
```
**Fix:** No setheader policy is used in this connector. Check that your apiProperties.json doesn't have policy templates.

### MCP Requests Fail
```
Error: Method not found (-32601)
```
**Cause:** Client is calling unsupported method.
**Fix:** Ensure method is one of: `tools/list`, `tools/call`, `resources/list`, `resources/read`

### Foundry Model Not Responding
```
Error: Foundry model error: 500
```
**Cause:** 
- Foundry endpoint unreachable
- Model name not available
- Endpoint requires auth

**Fix:**
- Verify endpoint: `curl http://localhost:60311/v1/models`
- Check auth credentials if cloud endpoint
- Update foundryEndpoint and foundryModel in script.csx

### Slow Tool Responses
**Monitoring:**
- Check Application Insights for response times
- Foundry model latency typically 2-5 seconds
- If > 10s, check network/endpoint health

**Optimization:**
- Use depth="quick" for faster responses
- Cache frequently-used resources
- Consider model choice (phi-4 faster than larger models)

## Customization

### Adding New Tools
1. Add method definition to `HandleToolsList()`
2. Implement handler in `HandleToolCall()`
3. Create private method: `CallMyNewTool(JObject args)`
4. Update Swagger definition with new tool parameters

Example:
```csharp
private async Task<string> CallMyNewTool(JObject args)
{
    var input = args["input_param"]?.ToString();
    var foundryResponse = await CallFoundryModel(new[] {
        new { role = "system", content = "System prompt for my tool" },
        new { role = "user", content = input }
    });
    LogToAppInsights("MyToolInvoked", new Dictionary<string, string> {
        { "input_length", input?.Length.ToString() }
    });
    return foundryResponse;
}
```

### Adding New Resources
1. Add resource metadata in `HandleResourcesList()`
2. Implement `GetResourceContent()` case for your URI
3. Create content method: `GetResourceName()`

Example:
```csharp
new
{
    uri = "resource://domain/my-resource",
    name = "My Resource",
    description = "Description...",
    mimeType = "text/markdown"
}

// In GetResourceContent:
case "resource://domain/my-resource":
    return GetMyResource();

private string GetMyResource()
{
    return @"# My Resource
Content here...";
}
```

### Changing the Secondary Model
Edit script.csx:
```csharp
// Current
private string foundryModel = "phi-4";

// Change to
private string foundryModel = "mistral-7b";  // or "llama-3", "gpt-4o-mini", etc.
```

## Architecture Decisions

| Decision | Rationale |
|----------|-----------|
| **MCP Resources vs. Tools** | Resources enable both models to access shared knowledge; tools enable dynamic analysis. Together they prevent hallucination and ensure consistency. |
| **Foundry Model for Secondary** | Different from primary model ensures diverse perspective. Foundry allows flexibility and lower cost than enterprise models. |
| **Hardcoded App Insights** | Ensures observability is always present; no configuration burden on users. |
| **Embedded Resources** | Simpler deployment and versioning; can optionally externalize later. |
| **Multiple Tools** | Different analysis modes serve different decision types. |

## Performance Characteristics

| Operation | Typical Duration | Notes |
|-----------|------------------|-------|
| tools/list | <10ms | Simple metadata return |
| tools/call (quick) | 2-3s | Foundry model inference |
| tools/call (balanced) | 3-5s | More comprehensive analysis |
| tools/call (deep) | 5-10s | Detailed analysis with edge cases |
| resources/read | <100ms | Metadata or embedded content |
| Full decision flow | 15-30s | Multiple tools + synthesis |

## Security

### Data Privacy
- No data is logged to persistent storage by default
- Enable Application Insights only if you want telemetry
- Resources don't contain sensitive information
- Foundry model calls happen server-to-server

### Authentication
- Optional API key in connection parameters
- Can be used to gate resource access
- Secure connection required (HTTPS) for production

### Sensitive Decisions
For decisions containing confidential information:
1. Don't include specific names/amounts in prompts
2. Use anonymized examples
3. Disable Application Insights for those calls
4. Consider on-premise Foundry model instead of cloud

## Future Enhancements

- [ ] Integrate with organizational decision history (feedback loop)
- [ ] Add weighted scoring visualization
- [ ] Custom frameworks per industry/company
- [ ] Integration with project management tools (track decisions vs. outcomes)
- [ ] Collaborative decision-making (multiple stakeholders reviewing)
- [ ] Decision analytics (which decisions succeed/fail, pattern analysis)
- [ ] Custom bias-checking for your industry
- [ ] External benchmark integration (Gartner, etc.)

## Support

**Issues:**
- Check Application Insights for detailed error messages
- Verify Foundry model endpoint is responding
- Confirm connection parameters match your environment

**Questions:**
- GitHub: https://github.com/troystaylor/SharingIsCaring
- Email: troy@troystaylor.com

## License

Part of SharingIsCaring collection

---

**Version:** 1.0.0  
**Last Updated:** April 2026  
**Author:** Troy Taylor (@troystaylor)
