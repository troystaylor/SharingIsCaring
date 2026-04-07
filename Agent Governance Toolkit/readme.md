# Agent Governance Toolkit

## Overview

Runtime security governance for AI agents. This connector wraps the [Microsoft Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit) (.NET SDK) deployed as an Azure Container App, providing policy enforcement, compliance checking, prompt injection detection, trust scoring, circuit breakers, and MCP tool scanning for Copilot Studio agents and Power Automate flows.

Covers all 10 OWASP Agentic AI Top 10 risks with deterministic, sub-millisecond policy enforcement.

## Architecture

```
Copilot Studio / Power Automate
        ↓
  Custom Connector (this)
        ↓
  Azure Container App (.NET 8 minimal API)
        ↓
  Microsoft.AgentGovernance NuGet SDK
```

## Prerequisites

- Azure subscription
- Azure CLI
- Docker (optional — Azure Container Registry can build images in the cloud)

## Deployment

### 1. Create Azure Resources

```bash
# Create resource group
az group create --name rg-agent-governance --location westus2

# Deploy infrastructure (Container Registry + Container App)
az deployment group create \
  --resource-group rg-agent-governance \
  --template-file "Agent Governance Toolkit/deploy/main.bicep" \
  --parameters apiKey="your-secure-api-key-here"
```

### 2. Build and Push the Container Image

**Option A: Build in the cloud (no Docker required)**
```bash
az acr build --registry agentgovacr \
  --image agentgov:latest \
  --file "Agent Governance Toolkit/container-app/Dockerfile" \
  "Agent Governance Toolkit/container-app"
```

**Option B: Build locally with Docker**
```bash
cd "Agent Governance Toolkit/container-app"

# Default (pinned SDK version)
docker build -t agent-governance .

# Or override with latest SDK version
docker build --build-arg AGT_VERSION=3.* -t agent-governance .

# Push to ACR
az acr login --name agentgovacr
docker tag agent-governance agentgovacr.azurecr.io/agentgov:latest
docker push agentgovacr.azurecr.io/agentgov:latest
```

### 3. Force Container App Update

If the initial deployment failed because the image didn't exist yet, redeploy:
```bash
az containerapp update --name agentgov-api \
  --resource-group rg-agent-governance \
  --image agentgovacr.azurecr.io/agentgov:latest \
  --revision-suffix v1
```

### 4. Configure the Connector

1. Import the connector into Power Platform using `pac connector create`
2. Create a connection with:
   - **Host Name**: Your Container App FQDN (e.g., `agentgov-api.yourenv.westus2.azurecontainerapps.io`)
   - **API Key**: The API key you set during deployment

## Operations

### Evaluate Action

Evaluate a proposed tool call against governance policies before execution. Returns allow/deny with the matching policy rule.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Tool Name | Yes | The tool to evaluate (e.g., file_write, shell_exec) |
| Agent ID | No | Identifier for the requesting agent |
| Arguments | No | Key-value arguments for the tool call |

### Check Compliance

Grade a proposed action against regulatory frameworks.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Tool Name | Yes | The tool to check |
| Agent ID | No | Agent identifier |
| Framework | No | OWASP-Agentic-2026, EU-AI-Act, HIPAA, SOC2 |

### Score Trust

Get or update the dynamic trust score for an agent (0-1000 scale).

| Parameter | Required | Description |
|-----------|----------|-------------|
| Agent ID | Yes | Agent to score |
| Action | No | positive (boost), negative (penalize), set (absolute) |
| Amount | No | Amount for the trust action |

**Trust Tiers:**

| Score | Tier | Ring | Capabilities |
|-------|------|------|-------------|
| ≥ 950 | Critical | Ring0 | Full system access |
| ≥ 800 | Trusted | Ring1 | Write access, 1000 calls/min |
| ≥ 600 | Standard | Ring2 | Read + limited write, 100 calls/min |
| < 600 | Restricted/Untrusted | Ring3 | Read-only, 10 calls/min |

### Detect Prompt Injection

Scan text for 7 types of prompt injection attacks.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Text | Yes | Text to scan |

**Detected Attack Types:** DirectOverride, DelimiterAttack, RolePlay, ContextManipulation, SqlInjection, CanaryLeak, Custom

### Log Audit Event

Record an action to the governance audit trail.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Agent ID | No | Agent that acted |
| Action | No | What was done |
| Tool Name | No | Tool used |
| Result | No | Outcome |

### Check Circuit Breaker

Check downstream service reliability status.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Service ID | No | Service to check |

### Scan MCP Tool

Scan MCP tool definitions for security risks.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Tool Definition | Yes | MCP tool JSON to scan |

### Check Version

Check the running SDK version against the latest NuGet release.

## MCP Tools

| Tool | When to Call |
|------|-------------|
| `evaluate_action` | Before any tool call in any connector |
| `check_compliance` | Before handling regulated data |
| `score_trust` | When deciding privilege level or human escalation |
| `detect_injection` | Before processing any user input |
| `log_audit` | After every significant action |
| `check_circuit_breaker` | Before calling flaky external services |
| `scan_mcp_tool` | Before connecting to unknown MCP servers |

## OWASP Agentic AI Top 10 Coverage

| Risk | Category | How This Connector Addresses It |
|------|----------|--------------------------------|
| Goal Hijacking | ASI-01 | `detect_injection` + `evaluate_action` policy checks |
| Excessive Capabilities | ASI-02 | `evaluate_action` allow/deny lists |
| Identity & Privilege Abuse | ASI-03 | `score_trust` with execution rings |
| Uncontrolled Code Execution | ASI-04 | `evaluate_action` blocks shell_exec, etc. |
| Insecure Output Handling | ASI-05 | `check_compliance` output validation |
| Memory Poisoning | ASI-06 | Stateless evaluation (no shared context) |
| Unsafe Inter-Agent Communication | ASI-07 | `scan_mcp_tool` validates tool definitions |
| Cascading Failures | ASI-08 | `check_circuit_breaker` + SLO enforcement |
| Human-Agent Trust Deficit | ASI-09 | `log_audit` full audit trails |
| Rogue Agents | ASI-10 | `score_trust` decay + ring isolation |

## Policy Customization

Edit `container-app/policies/default.yaml` before building, or mount a custom policy file at runtime via Azure Files:

```yaml
version: "1.0"
name: custom-policy
description: Custom governance rules

rules:
  - name: allow-safe-reads
    condition: "tool_name == 'web_search'"
    action: allow
    priority: 10
  - name: block-destructive
    condition: "tool_name == 'shell_exec'"
    action: deny
    priority: 100

defaults:
  action: deny
```

## Keeping Up to Date

- **Dependabot**: If you fork this repo, Dependabot auto-creates PRs when new SDK versions are published
- **Version check**: Use the `CheckVersion` operation in a scheduled Power Automate flow to get alerted when updates are available
- **GitHub Watch**: Watch [microsoft/agent-governance-toolkit](https://github.com/microsoft/agent-governance-toolkit) releases for announcements
- **Build arg override**: Build with `--build-arg AGT_VERSION=3.*` to get the latest within the major version

## Cost Estimate

| Component | Monthly Cost |
|-----------|-------------|
| Container App (consumption, low traffic) | ~$0-5 |
| Container App (10K evaluations/day) | ~$5-15 |
| Container Registry (Basic) | ~$5 |
| Log Analytics (30-day retention) | ~$2-5 |
| **Total** | **~$7-25/mo** |
