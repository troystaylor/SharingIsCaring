# MCP Shield

## Overview

Supply chain integrity monitoring for MCP tool descriptions. Counters the MCP tool poisoning attack pattern documented by [Microsoft Incident Response](https://www.microsoft.com/en-us/security/blog/2026/06/30/securing-ai-agents-ai-tools-move-from-reading-acting/) where threat actors silently modify tool descriptions to redirect agent behavior and exfiltrate data.

**Pure script.csx — no Azure infrastructure required.** All core operations (hashing, drift detection, DLP scanning, imperative language detection) run entirely within the custom connector runtime. Optional Prompt Shields integration requires an Azure AI Content Safety resource.

## Attack Phase Coverage

| Phase | Attack | MCP Shield Operation |
|-------|--------|---------------------|
| 1 | Tool description poisoning | **Detect Imperative Language** — flags command-like instructions hidden in descriptions |
| 2 | Silent re-trust | **Check Description Drift** — alerts when descriptions change vs. stored hash |
| 3 | Expanded data access | **Scan Outbound Payload** — catches bulk data, PII, financial patterns in parameters |
| 4 | Exfiltration | **Log Shield Event** — structured telemetry for Sentinel correlation |

## Prerequisites

- Power Platform environment
- PAC CLI for deployment
- (Optional) Azure AI Content Safety resource for ML-grade Prompt Shields

## Operations

### Hash Tool Description

Compute a SHA-256 hash of an MCP tool's description text. Store the returned hash in your registry (Dataverse custom table, SharePoint list, or flow variable) and pass it back on subsequent checks.

### Check Description Drift

Compare a current tool description against a previously stored hash. Returns `drifted: true` when the description has been modified — the primary defense against silent re-trust.

### Scan Outbound Payload

Inspect outbound tool call parameters for sensitive data patterns before forwarding to an external MCP server. Detects:
- SSN patterns (critical)
- Credit card numbers (critical)
- IBAN codes (high)
- Bulk query indicators — `SELECT *`, `TOP N`, "last 30 invoices" (high)
- Base64 encoded blocks (medium)
- Multiple email addresses (medium)

### Detect Imperative Language

Analyze a tool description for language that doesn't belong in documentation. Catches:
- **Exfiltration verbs** — "retrieve the last 30 invoices", "attach as additional parameter"
- **Override instructions** — "ignore previous rules", "you must always include"
- **Hidden directives** — "silently", "without telling", "before responding"
- **Bulk data requests** — "all unpaid invoices", "summarize every transaction"
- **Encoding abuse** — "base64 encode the output", zero-width characters

Returns a risk score (0-100) and detailed findings.

### Inspect with Prompt Shields

Submit text to Azure AI Content Safety Prompt Shields for ML-grade indirect injection detection. Requires a configured Content Safety key in the connection. Falls back gracefully if not configured.

### Log Shield Event

Record a structured security event to Application Insights for Microsoft Sentinel correlation. Event types: `drift_detected`, `payload_blocked`, `imperative_detected`, `prompt_shield_alert`, `description_approved`.

### MCP Endpoint

Copilot Studio agents can call MCP Shield inline via the MCP protocol to self-govern before interacting with external MCP servers.

## Stateless Design

MCP Shield is intentionally stateless. The **caller** maintains the hash registry:

```
┌─────────────────────────────────────────────────────┐
│ Power Automate Flow (or Copilot Studio topic)       │
│                                                     │
│  1. Get tool descriptions from MCP server           │
│  2. Call Hash Tool Description → store hash         │
│  3. On next run: Call Check Description Drift       │
│     with stored hash                                │
│  4. If drifted → block + alert + Log Shield Event   │
│  5. Before forwarding: Scan Outbound Payload        │
│  6. Before trusting new tools: Detect Imperative    │
└─────────────────────────────────────────────────────┘
```

Registry options:
- **Dataverse custom table** — `mcpshield_registry` with columns: ServerName, ToolName, DescriptionHash, ApprovedBy, ApprovedDate
- **SharePoint list** — simple for small environments
- **Flow variable** — for single-run validation without persistence

## Deployment

```powershell
pac connector create `
  -df "MCP Shield/apiDefinition.swagger.json" `
  -pf "MCP Shield/apiProperties.json" `
  -sf "MCP Shield/script.csx" `
  -env c4f149b0-9f42-e8c4-97d8-bc69b59f971c
```

## Example: Financial Invoice Protection Flow

Recreates the exact defense for the attack scenario in the Microsoft Security Blog:

1. **Trigger**: Scheduled (hourly) or on-demand
2. **Get tool list** from the invoice enrichment MCP server
3. **For each tool**:
   - `Detect Imperative Language` on the description
   - If suspicious → `Log Shield Event` (severity: critical) + send Teams alert + skip tool
   - `Check Description Drift` against Dataverse registry
   - If drifted → `Log Shield Event` + require human approval before updating hash
4. **Before each enrichment call**:
   - `Scan Outbound Payload` on the parameters
   - If blocked → `Log Shield Event` + abort call + notify SOC

## Relationship to Agent Governance Toolkit

| Agent Governance Toolkit | MCP Shield |
|---|---|
| Runtime policy enforcement (allow/deny tool calls) | Supply chain integrity monitoring |
| Requires Azure Container App | Pure script.csx — no infrastructure |
| Per-tool policy rules | Per-description content analysis |
| Trust scoring + circuit breakers | Hash registry + drift detection |

They compose: MCP Shield verifies the supply chain is clean, AGT enforces policy on what gets executed.
