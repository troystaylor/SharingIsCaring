# ACA Agent Skills

A Copilot Cowork plugin that discovers [agentskills.io](https://agentskills.io/) skills from GitHub and executes them in isolated [Azure Container Apps Sandboxes](https://learn.microsoft.com/en-us/azure/container-apps/sandboxes-overview).

## What It Does

Ask M365 Copilot to find a skill, learn from it, or apply it — and it will search 8,800+ skill repositories, fetch the instructions, and run the steps in a fresh Linux sandbox with .NET, Node, or Python pre-installed.

**Example prompts:**
- "Search for agent skills about building REST APIs with ASP.NET Core, then apply the best one in a sandbox"
- "What .NET AI skills are available?"
- "Apply the mcp-csharp-create skill from dotnet/skills"

## Architecture

```
Copilot Cowork → MCP Server (Azure Container App) → GitHub API (discover/fetch)
                                                   → ACA Sandboxes (execute)
```

## Tools (10)

| Tool | Description |
|------|-------------|
| `get_curated_registry` | Curated list of known skill repositories |
| `search_skills` | Search GitHub for SKILL.md files by keyword |
| `list_skills` | List skills in a repository path |
| `get_skill` | Fetch full SKILL.md content |
| `apply_skill` | One-shot: fetch skill + extract commands + execute in sandbox |
| `execute_in_sandbox` | Run shell commands in an isolated ACA Sandbox |
| `read_sandbox_file` | Read a file from a sandbox |
| `list_sandbox_files` | List directory contents in a sandbox |
| `get_sandbox_status` | Check sandbox state and command history |
| `delete_sandbox` | Clean up a sandbox |

## Deployment

### Prerequisites

- Azure subscription
- M365 Admin Center access (for plugin upload)
- Node.js 22+ (for local dev)

### Deploy the MCP Server

```bash
# Create resources
az group create --name rg-agent-skills --location westus2
az acr create --name agentskillsacr --resource-group rg-agent-skills --sku Basic --admin-enabled true

# Build and deploy
cd server
az acr build --registry agentskillsacr --resource-group rg-agent-skills --image aca-agent-skills-mcp:latest .

az containerapp env create --name agent-skills-env --resource-group rg-agent-skills --location westus2

az containerapp create \
  --name aca-agent-skills-mcp \
  --resource-group rg-agent-skills \
  --environment agent-skills-env \
  --image agentskillsacr.azurecr.io/aca-agent-skills-mcp:latest \
  --registry-server agentskillsacr.azurecr.io \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1

# Enable managed identity + sandbox access
az containerapp identity assign --name aca-agent-skills-mcp --resource-group rg-agent-skills --system-assigned

az role assignment create \
  --assignee $(az containerapp show --name aca-agent-skills-mcp --resource-group rg-agent-skills --query identity.principalId -o tsv) \
  --role "Container Apps SandboxGroup Data Owner" \
  --scope /subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-agent-skills

# Set environment variables
az containerapp update --name aca-agent-skills-mcp --resource-group rg-agent-skills \
  --set-env-vars \
    "AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)" \
    "AZURE_RESOURCE_GROUP=rg-agent-skills" \
    "SANDBOX_GROUP_NAME=agent-skills-sandboxes" \
    "SANDBOX_REGION=westus2" \
    "GITHUB_TOKEN=<your-github-pat>"
```

### Create the Sandbox Group

```bash
# Via ARM REST API (2026-02-01-preview)
az rest --method PUT \
  --url "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-agent-skills/providers/Microsoft.App/sandboxGroups/agent-skills-sandboxes?api-version=2026-02-01-preview" \
  --body '{"location":"westus2","properties":{}}'
```

### Create a .NET SDK Disk Image (Optional)

Speeds up .NET skill execution by eliminating SDK install time:

```bash
token=$(az account get-access-token --resource "https://dynamicsessions.io" --query accessToken -o tsv)
curl -X PUT "https://management.westus2.azuredevcompute.io/subscriptions/<sub>/resourceGroups/rg-agent-skills/sandboxGroups/agent-skills-sandboxes/diskimages" \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"image":{"base":"mcr.microsoft.com/dotnet/sdk:10.0"},"labels":{"name":"dotnet-sdk-10"}}'
```

### Install the Cowork Plugin

1. Update `manifest.json` with your Container App's FQDN
2. Package: zip the root files (manifest.json, icons, agent-skills-tools.json, skills/)
3. Upload to **M365 Admin Center → Agents → All Agents → Add Agent**

## Local Development

```bash
cd server
npm install
node index.js  # Starts on :8080
```

Test with the included client:
```bash
node test-client.mjs
```

## Project Structure

```
├── manifest.json              # Cowork plugin manifest (v1.28)
├── agent-skills-tools.json    # MCP tool declarations for Cowork
├── color.png / outline.png    # Plugin icons
├── skills/                    # Cowork agent skills (SKILL.md files)
│   ├── discover-skills/       # Find and browse skills
│   ├── apply-skill/           # Execute skill steps in sandbox
│   └── learn-from-skill/      # Get guidance without execution
└── server/                    # Node.js MCP server
    ├── index.js               # Express + MCP SDK transport
    ├── tools/github.js        # GitHub API tools
    ├── tools/sandbox.js       # ACA Sandbox execution tools
    ├── public/                # MCP App widgets
    └── Dockerfile
```

## License

MIT
