# ACA Code Interpreter

A dual-mode Power Platform custom connector that executes code in isolated [ACA Sandbox](https://sandboxes.azure.com) microVMs. Supports 16 languages and modes from both Power Automate (typed operations) and Copilot Studio (MCP tools).

## Supported Languages

| Language | Mode | Runtime |
|----------|------|---------|
| Python | `python` | Python 3.14+ with pandas, numpy, matplotlib, scikit-learn |
| JavaScript | `javascript` | Node.js 20+ |
| TypeScript | `typescript` | tsx (via npx) |
| PowerFx | `powerfx` | Custom CLI (Microsoft Power Fx engine) |
| Bash | `bash` | GNU Bash |
| PowerShell | `powershell` | pwsh 7.6+ |
| Ruby | `ruby` | Ruby 3.2+ |
| Perl | `perl` | Perl 5 |
| PHP | `php` | PHP CLI |
| SQL | `sql` | SQLite 3.45+ (in-memory) |
| Adaptive Card | `adaptivecard` | JSON validator |
| FetchXML | `fetchxml` | Parser + OData converter |
| OpenAPI Lint | `openapi-lint` | Spectral-based linter |
| Prompt | `prompt` | Jinja2 template renderer |
| Expression | `expression` | Power Automate expression evaluator |
| Regex | `regex` | Pattern tester with match output |

## Prerequisites

1. Azure subscription with access to [ACA Sandboxes](https://sandboxes.azure.com)
2. [ACA CLI](https://github.com/microsoft/azure-container-apps/releases) installed
3. Docker (for building the custom disk image)
4. .NET 9 SDK (for building pfx and expr-eval tools)
5. Power Platform environment with custom connector permissions

## Quick Start

### 1. Deploy Infrastructure

```powershell
az deployment sub create --name aca-code-interpreter `
    --location westus2 `
    --template-file infra/main.bicep `
    --parameters principalId=$(az ad signed-in-user show --query id -o tsv)
```

### 2. Build and Import Disk Image

```powershell
cd infra
.\build-image.ps1
# Or for lite (Python + Bash only):
.\build-image.ps1 -Lite
```

### 3. Configure the Connector

Update `script.csx` with your values:
- `ACA_SUBSCRIPTION_ID` — your Azure subscription
- `ACA_RESOURCE_GROUP` — resource group name
- `ACA_SANDBOX_GROUP` — sandbox group name
- `ACA_REGION` — Azure region (e.g., westus2)
- `ACA_DATA_PLANE_BASE` — `https://management.{region}.azuredevcompute.io`
- `ACA_DISK_IMAGE` — disk image ID from `aca sandboxgroup disk list`

### 4. Register Entra App

```powershell
az ad app create --display-name "ACA Code Interpreter Connector" `
    --sign-in-audience AzureADMultipleOrgs `
    --web-redirect-uris "https://global.consent.azure-apim.net/redirect"
```

Add the `Sessions.ReadWrite.All` permission from `Azure ContainerApps Sessions` (appId: `2c7dd73f-7a21-485b-b97d-a2508fa152c3`) and grant admin consent.

### 5. Deploy Connector

```powershell
pac connector create `
    -df apiDefinition.swagger.json `
    -sf script.csx `
    -env <environment-id>
```

Then configure OAuth in the portal Security tab:
- **Resource URL**: `https://dynamicsessions.io`
- **Scope**: `https://dynamicsessions.io/.default offline_access`
- **Client ID**: your Entra app client ID
- **Client Secret**: your Entra app secret

## Project Structure

```
ACA Code Interpreter/
├── apiDefinition.swagger.json    # OpenAPI 2.0 (8 operations + MCP)
├── apiProperties.json            # OAuth config, scriptOperations
├── script.csx                    # C# connector logic
├── infra/
│   ├── Dockerfile                # Full image (all 16 runtimes)
│   ├── Dockerfile.lite           # Lite image (Python + Bash)
│   ├── build-image.ps1           # Build + import script
│   ├── deploy.ps1                # End-to-end deployment
│   ├── main.bicep                # Subscription-scoped infra
│   ├── main.bicepparam           # Parameters
│   ├── modules/
│   │   └── sandbox-group.bicep   # Sandbox group + RBAC
│   └── tools/
│       ├── validate-card.js      # Adaptive Card validator
│       ├── fetchxml_eval.py      # FetchXML → OData
│       ├── lint-openapi.js       # OpenAPI linter
│       ├── render_prompt.py      # Prompt template renderer
│       ├── regex_test.py         # Regex pattern tester
│       ├── expr-eval/            # Power Automate expression evaluator (.NET)
│       └── pfx/                  # Power Fx CLI (.NET)
└── readme.md
```

## Key Architecture Details

- **Data Plane**: `https://management.{region}.azuredevcompute.io`
- **Token Audience**: `https://dynamicsessions.io/.default`
- **API Format**: PUT to create sandboxes, POST to `/executeShellCommand`
- **Disk Images**: Referenced by ID for private images, name for public
- **Multi-line Code**: Base64 encoded → file → execute (avoids shell escaping)

## Operations

| Operation | Description |
|-----------|-------------|
| CreateSession | Boot an isolated sandbox microVM |
| ExecuteCode | Run code in any of 16 languages |
| UploadFile | Stage data files in the sandbox |
| DownloadFile | Retrieve generated artifacts |
| ListFiles | Browse the sandbox filesystem |
| GetSession | Check sandbox state |
| DestroySession | Clean up resources |
| InvokeMCP | Copilot Studio MCP endpoint |

## Author

Troy Taylor — [troy@troystaylor.com](mailto:troy@troystaylor.com) — [GitHub](https://github.com/troystaylor)
