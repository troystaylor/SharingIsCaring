# Agent Governance Toolkit

## Overview

Runtime security governance for AI agents. This connector wraps the [Microsoft Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit) (.NET SDK) deployed as an Azure Container App, providing policy enforcement, compliance checking, prompt injection detection, trust scoring, circuit breakers, MCP tool scanning, and [Agent Control Specification (ACS)](https://github.com/microsoft/agent-governance-toolkit/tree/main/policy-engine) lifecycle policy evaluation for Copilot Studio agents and Power Automate flows.

Covers all 10 OWASP Agentic AI Top 10 risks with deterministic, sub-millisecond policy enforcement. ACS adds lifecycle-aware verdicts (`allow`/`deny`/`warn`/`escalate`/`transform`) across 8 intervention points spanning the full agent loop.

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
- PowerShell 7+ (for the ACS nupkg build script, if enabling ACS)

## Deployment

> **Validated end-to-end on Azure Container Apps (westus3).** All operations including the 3 ACS endpoints respond correctly from a live FQDN. If `eastus2` returns `AKSCapacityHeavyUsage`, retry in `westus3` or another region.

### 1. (Optional) Build the ACS nupkg

Skip this step if you only need the per-tool layer (`evaluate_action`, `detect_injection`, etc.). To enable Agent Control Specification lifecycle policies, build the SDK nupkg from upstream — it isn't on nuget.org yet.

```powershell
cd "Agent Governance Toolkit/container-app"
./scripts/build-acs-nupkg.ps1
```

Requires Docker Desktop in Linux container mode. Produces `local-packages/AgentControlSpecification.0.3.1-beta.0.nupkg` and writes the `local-packages/.acs-enabled` marker that flips the csproj into ACS-enabled mode automatically. See [container-app/manifests/README.md](container-app/manifests/README.md) for details.

### 2. Create Azure Resources

```powershell
# Generate a secure API key (40 chars). Re-use the same shell for the deploy
# step below — sub-shells may drop the env var.
$env:AGT_API_KEY = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 40 | ForEach-Object {[char]$_})

az group create --name rg-agent-governance --location westus3

az deployment group create `
  --resource-group rg-agent-governance `
  --template-file "Agent Governance Toolkit/deploy/main.bicep" `
  --parameters apiKey="$env:AGT_API_KEY" namePrefix=agentgov location=westus3
```

The first deploy provisions ACR + Log Analytics + the Container Apps environment, then attempts to create the Container App. **It is expected to fail with `MANIFEST_UNKNOWN`** because no image exists in ACR yet — proceed to step 3.

### 3. Build and Push the Container Image

```powershell
# Cloud build — no Docker required locally. Uploads the build context
# (including local-packages/) and builds inside ACR.
az acr build --registry agentgovacr `
  --image agentgov:latest `
  --platform linux/amd64 `
  --file "Agent Governance Toolkit/container-app/Dockerfile" `
  "Agent Governance Toolkit/container-app"
```

Alternative: build locally with Docker, then `az acr login` + `docker push`. Use `--build-arg AGT_VERSION=3.*` to pull the latest `Microsoft.AgentGovernance` package within the major version.

### 4. Re-run the Deploy

Now that the image exists, re-run the Bicep deploy in the **same shell** so `$env:AGT_API_KEY` is still set:

```powershell
az deployment group create `
  --resource-group rg-agent-governance `
  --template-file "Agent Governance Toolkit/deploy/main.bicep" `
  --parameters apiKey="$env:AGT_API_KEY" namePrefix=agentgov location=westus3 `
  --query "properties.outputs.containerAppUrl.value" -o tsv
```

The output is your Container App FQDN.

> **Lost the API key?** Pull it back from the Container App secret:
> ```powershell
> az containerapp secret show -g rg-agent-governance -n agentgov-api `
>   --secret-name api-key --query value -o tsv
> ```

### 5. Smoke-test the deployment

```powershell
$fqdn = "agentgov-api.<your-env>.westus3.azurecontainerapps.io"
$h = @{ 'X-API-Key' = $env:AGT_API_KEY; 'Content-Type' = 'application/json' }

# Per-tool policy (always available)
Invoke-RestMethod -Method POST "https://$fqdn/api/evaluate" -Headers $h `
  -Body '{"toolName":"shell_exec","agentId":"test"}'

# ACS lifecycle policy (only when nupkg was built in step 1)
Invoke-RestMethod -Method POST "https://$fqdn/api/acs/manifest/load" -Headers $h `
  -Body '{"path":"example.yaml","id":"example"}'
Invoke-RestMethod -Method POST "https://$fqdn/api/acs/evaluate" -Headers $h `
  -Body '{"manifestId":"example","interventionPoint":"input","snapshot":{"input":{"text":"hello"}}}'
```

If `Load ACS Manifest` returns `"sdkBound": true`, the live Rust core is loaded inside the container.

### 6. Configure the Connector

1. Import the connector into Power Platform using `pac connector create`
2. Create a connection with:
   - **Host Name**: Your Container App FQDN (no `https://` prefix)
   - **API Key**: The API key from `$env:AGT_API_KEY` (or recovered via `az containerapp secret show`)

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

### Load ACS Manifest

Register an Agent Control Specification (ACS) manifest by id. Call once at agent startup.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Manifest Path | Yes | Filename in `MANIFEST_DIR` or absolute path |
| Manifest ID | No | ID for later evaluate/transform calls (defaults to filename without extension) |

### Evaluate Intervention

Submit a snapshot at one of the 8 ACS intervention points and return the verdict.

| Parameter | Required | Description |
|-----------|----------|-------------|
| Manifest ID | Yes | ID of a manifest registered via Load ACS Manifest |
| Intervention Point | Yes | `agent_startup`, `input`, `pre_model_call`, `post_model_call`, `pre_tool_call`, `post_tool_call`, `output`, or `agent_shutdown` |
| Snapshot | Yes | JSON object the policy evaluates against |
| Tool Name | No | Required for `pre_tool_call` and `post_tool_call` |
| Mode | No | `enforce` (default) or `evaluate_only` |

**Verdict decisions:** `allow`, `deny`, `warn`, `escalate`, `transform`.

### Transform Payload

Same inputs as Evaluate Intervention, but surfaces the transformed body when the verdict is `transform` (e.g., redacted output). Returns the original payload for `allow`/`warn` and the verdict for `deny`/`escalate`.

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
| `load_manifest` | Once at agent startup, before any ACS evaluation |
| `evaluate_intervention` | At any ACS lifecycle point when you need warn/escalate/transform verdicts |
| `transform_payload` | When the policy may rewrite the body (e.g., output redaction) |

## Agent Control Specification (ACS)

ACS is the lifecycle-aware policy layer of the Agent Governance Toolkit. A single YAML manifest declares what to validate at each of 8 intervention points across `input -> model -> tool -> output`, with verdicts that go beyond allow/deny (`warn`, `escalate`, `transform`).

**Default state:** the three ACS endpoints (`/api/acs/manifest/load`, `/api/acs/evaluate`, `/api/acs/transform`) are scaffolded in the container. `LoadAcsManifest` works immediately (registers manifests and parses syntax). `EvaluateIntervention` and `TransformPayload` return **HTTP 501** with a setup pointer until the `AgentControlSpecification` .NET SDK is wired.

**To enable live ACS evaluation:** see [container-app/manifests/README.md](container-app/manifests/README.md). The wiring requires obtaining the SDK nupkg (currently artifact-only — not on nuget.org), adding a local NuGet source, and flipping `AcsRegistry.SdkAvailable` to `true` in `Program.cs`.

**When to use ACS over the per-tool layer:**

| Per-tool (`evaluate_action`) | ACS (`evaluate_intervention`) |
|---|---|
| Allow/deny on tool name | warn, escalate, transform verdicts |
| Simple YAML rules | Rego, Cedar, composable manifests |
| One snapshot point | Full agent loop coverage |
| Sub-millisecond | Sub-millisecond per intervention, stateless |

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
