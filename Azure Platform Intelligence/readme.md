# Azure Platform Intelligence

Azure platform engineering tools for Copilot Studio — cost estimation, security analysis, drift detection, template validation, resource visualization, policy compliance, and infrastructure export — delivered as MCP tools over a Power Platform custom connector.

Inspired by the [Git-Ape](https://azure.github.io/git-ape/) platform engineering framework and the [Platform Engineering for the Agentic AI Era](https://devblogs.microsoft.com/all-things-azure/platform-engineering-for-the-agentic-ai-era/) thesis by Microsoft.

## Architecture

```
Copilot Studio Agent
        ↓  (MCP / JSON-RPC 2.0)
Power Platform Custom Connector
        ↓  (OAuth delegated)
Azure Management APIs + Azure Retail Prices API
```

All logic runs in the connector's `script.csx`. No container app or external backend required. The connector forwards the user's OAuth token to Azure Management APIs, so RBAC determines what each user can access.

## Tools

| Tool | Description | Azure API | Required Role |
|------|-------------|-----------|---------------|
| `list_subscriptions` | List Azure subscriptions the user has access to | ARM Subscriptions API | Reader |
| `list_resource_groups` | List resource groups in a subscription | ARM Resource Groups API | Reader |
| `estimate_cost` | Estimate monthly cost for Azure resources by type/SKU or from an ARM template | Azure Retail Prices API (public, no auth) | None |
| `analyze_security` | Security posture analysis via Microsoft Defender for Cloud assessments | Microsoft.Security/assessments | Security Reader |
| `detect_drift` | Compare desired ARM template state against live Azure resources | Microsoft.Resources/deployments/whatIf | Contributor |
| `validate_template` | Validate an ARM template without deploying (schema, naming, permissions) | Microsoft.Resources/deployments/validate | Contributor |
| `visualize_resources` | Generate a Mermaid architecture diagram from a resource group | Microsoft.Resources (list resources) | Reader |
| `check_policy` | Check Azure Policy compliance summary for a subscription or resource group | Microsoft.PolicyInsights/policyStates | Reader |
| `import_resources` | Export a resource group as an ARM template (reverse-engineer IaC) | Microsoft.Resources/exportTemplate | Reader |

## Prerequisites

- Azure subscription
- Microsoft Entra ID app registration with `https://management.azure.com/user_impersonation` API permission
- Power Platform environment with custom connector support
- [PAC CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) for deployment

## App Registration Setup

1. Go to [Entra ID App Registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Create a new registration (or use an existing one)
3. Under **API permissions**, add:
   - `Azure Service Management` → `user_impersonation` (delegated)
4. Under **Authentication**, add the Power Platform redirect URI:
   - `https://global.consent.azure-apim.net/redirect`
5. Under **Certificates & secrets**, create a client secret
6. Note the **Application (client) ID** and **Directory (tenant) ID**

## Deploy

```bash
cd "Azure Platform Intelligence"
pac connector create \
  --settings-file apiProperties.json \
  --api-definition apiDefinition.swagger.json \
  --script script.csx \
  -e c4f149b0-9f42-e8c4-97d8-bc69b59f971c
```

When creating a connection, enter:
- **Client ID**: Your app registration's Application ID
- **Client Secret**: Your app registration's client secret
- **Tenant ID**: Your Entra ID tenant ID

## Application Insights (Optional)

To enable telemetry, edit `script.csx` and replace the placeholder:

```csharp
private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
```

With your Application Insights instrumentation key:

```csharp
private const string APP_INSIGHTS_KEY = "your-actual-key-here";
```

Telemetry is logged for every MCP request (method, duration, tool name, success/failure).

## Tool Details

### estimate_cost

Queries the [Azure Retail Prices API](https://learn.microsoft.com/rest/api/cost-management/retail-prices/azure-retail-prices) — a public API requiring no authentication.

**Single resource:**
```
"Estimate the monthly cost for a Standard_D2s_v3 Virtual Machine in East US"
```

**ARM template:**
```
"Estimate cost for this ARM template: {paste template JSON}"
```

### analyze_security

Queries Microsoft Defender for Cloud security assessments. Returns findings sorted by severity (High → Medium → Low) with remediation guidance.

```
"Analyze the security posture of subscription abc-123"
```

### detect_drift

Runs an ARM What-If operation comparing a desired template against live state. Identifies resources that would be created, modified, deleted, or remain unchanged.

```
"Check if this ARM template matches what's actually deployed in resource group my-rg"
```

### validate_template

Validates an ARM template without deploying — catches schema errors, invalid resource types, naming conflicts, and permission issues. Also runs a What-If preview showing what would happen if deployed.

```
"Validate this ARM template before I deploy it to my-rg"
```

### visualize_resources

Lists all resources in a resource group and generates a [Mermaid](https://mermaid.js.org/) architecture diagram showing resource types, groupings, and cross-references.

```
"Show me an architecture diagram of resource group my-rg"
```

### check_policy

Summarizes Azure Policy compliance — how many resources are compliant vs. non-compliant, which policy assignments have violations, and which specific policy definitions are failing.

```
"Check policy compliance for subscription abc-123"
```

### import_resources

Exports a resource group as an ARM template, including parameter default values and comments. Useful for bringing click-deployed or legacy infrastructure under IaC management.

```
"Export resource group my-rg as an ARM template"
```

## RBAC Summary

For full functionality, the connecting user needs these Azure RBAC roles:

| Minimum Role | Tools Enabled |
|-------------|---------------|
| None (public API) | `estimate_cost` |
| Reader | `list_subscriptions`, `list_resource_groups`, `visualize_resources`, `check_policy`, `import_resources` |
| Security Reader | `analyze_security` |
| Contributor | `detect_drift`, `validate_template` |

## Files

- `apiDefinition.swagger.json` — Swagger 2.0 definition with MCP endpoint
- `apiProperties.json` — Connector properties and OAuth configuration
- `script.csx` — MCP server with 9 tool implementations
- `readme.md` — This file
