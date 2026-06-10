# SharePoint File Transfer — Copilot Cowork Plugin

Manage files across SharePoint from Copilot Cowork. Browse sites, document libraries, and folders. Upload files from public HTTPS URLs — small files upload inline, large files (250 MB+) stream in the background with resumable chunked uploads. Move and rename items within a library, create folders, set metadata columns, and generate sharing links.

Backed by a .NET MCP server deployed to Azure Container Apps with Microsoft Entra SSO and On-Behalf-Of (OBO) token exchange for Microsoft Graph.

### What it can do

- **Browse** any SharePoint site, library, and folder the signed-in user has access to
- **Upload** files from any publicly reachable HTTPS URL (e.g., GitHub releases, CDN links, public downloads) into any SharePoint library the user can write to
- **Copy** files and folders across sites and libraries — `copy_item` uses Graph's native async copy, so there's no file size limit and no download/re-upload
- **Move / rename** items within the same document library
- **Create folders**, **set metadata** columns, and **generate sharing links**
- **Resume** or **cancel** large in-flight uploads

### Current limitations

- **Move is same-drive only.** `move_item` moves items within a single document library. For cross-library or cross-site moves, use `copy_item` followed by deleting the original (not yet automated as a single tool).

## Architecture

```
Copilot Cowork ──SSO token──▶ Azure Container App (MCP server)
                                 │
                                 ├─ OBO exchange ──▶ Microsoft Graph (SharePoint)
                                 └─ Managed Identity ──▶ Azure Table Storage (session tracking)
```

## Prerequisites

- Azure subscription
- [Azure Developer CLI (`azd`)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) v1.11+
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for container builds)
- A Microsoft Entra app registration (multi-tenant) with delegated Graph permissions
- Access to upload Cowork plugins in M365 Admin Center

## MCP Tools

| Tool | Description |
|------|-------------|
| `list_sites` | Search SharePoint sites the signed-in user can access |
| `list_drives` | List document libraries (drives) inside a SharePoint site |
| `list_folder` | List items in a folder within a drive |
| `get_item` | Get a drive item's metadata |
| `upload_from_url` | Server-side ingest from a public HTTPS URL into SharePoint |
| `start_upload_session` | Create a Graph upload session and return the pre-signed upload URL |
| `resume_upload_from_url` | Resume a failed or partial upload |
| `get_upload_status` | Check status of an in-flight or completed upload |
| `cancel_upload` | Cancel an in-flight upload session |
| `create_folder` | Create a new folder inside a drive |
| `move_item` | Move and/or rename an item within the same drive |
| `set_metadata` | Set SharePoint list-item column values on a drive item |
| `copy_item` | Copy a file or folder to another drive/site (async, cross-site supported) |
| `check_copy_status` | Check progress of an async copy operation |
| `create_link` | Create a shareable link for a SharePoint item |

## Deployment

### 1. Create the Entra app registration

Register a **multi-tenant** app in Microsoft Entra ID:

1. Go to **Azure Portal > App registrations > New registration**
2. Set **Supported account types** to "Accounts in any organizational directory"
3. Add a **client secret** and note the value
4. Under **API permissions**, add these **delegated** Microsoft Graph permissions and grant admin consent:
   - `Sites.ReadWrite.All`
   - `Files.ReadWrite.All`
   - `Sites.Read.All`
   - `User.Read`
5. Under **Expose an API**, add a scope (e.g., `access_as_user`) and set the Application ID URI

Save these values — you'll need them after infrastructure deployment:
- Application (client) ID
- Client secret value

### 2. Provision Azure infrastructure

```bash
azd init
azd provision
```

This creates:
- Resource group
- Azure Container Registry
- Azure Container Apps environment + container app (with system-assigned managed identity)
- Azure Storage account + `uploadSessions` table
- Application Insights
- RBAC assignments (ACR pull, Table Data Contributor)

### 3. Deploy the server

```bash
azd deploy
```

### 4. Configure OBO credentials

After deployment, add the OBO client ID and secret to the container app. Store the secret as a Container App secret so it survives redeployments:

```bash
# Get your container app name from azd output
CA_NAME="<container-app-name>"
RG_NAME="<resource-group-name>"

# Add the client secret as a Container App secret
az containerapp secret set -n $CA_NAME -g $RG_NAME \
  --secrets obo-client-secret="<your-client-secret>"

# Set environment variables
az containerapp update -n $CA_NAME -g $RG_NAME \
  --set-env-vars \
    OBO_CLIENT_ID="<your-client-id>" \
    OBO_CLIENT_SECRET=secretref:obo-client-secret
```

> **Important:** Use `secretref:` for the client secret. Plain-text env vars get overwritten by `azd deploy`.

### 5. Verify public network access on storage

The Bicep templates set `publicNetworkAccess: Enabled` on the storage account. If you later lock this down, ensure the Container App can still reach Table Storage (via private endpoint or service endpoint).

### 6. Register the Cowork plugin

1. Update `manifest.json`:
   - Replace `{{GUID}}` with a new GUID (`[guid]::NewGuid()` in PowerShell)
   - Replace `<YOUR-CONTAINER-APP-FQDN>` with the container app's FQDN
   - Replace `<YOUR-OAUTH-REFERENCE-ID>` with the OAuth reference from the Enterprise Token Store
2. Package: `Compress-Archive -Path manifest.json, color.png, outline.png, skills -DestinationPath "SharePoint File Transfer.zip"`
3. Upload the `.zip` in **M365 Admin Center > Settings > Integrated apps > Upload custom apps**
4. Grant admin consent when prompted

## Post-deployment checklist

- [ ] OBO credentials configured on the container app (`OBO_CLIENT_ID`, `OBO_CLIENT_SECRET` as secretRef)
- [ ] Entra app has delegated Graph permissions granted (admin consent)
- [ ] Storage account Table Storage accessible from the container app
- [ ] Managed identity has `Storage Table Data Contributor` on the storage account
- [ ] `manifest.json` updated with FQDN and OAuth reference ID
- [ ] Cowork plugin uploaded and admin-consented in M365 Admin Center

## Project structure

```
├── azure.yaml              # azd service definition
├── manifest.json           # Cowork plugin manifest
├── color.png               # Plugin icon (color)
├── outline.png             # Plugin icon (outline)
├── infra/                  # Bicep infrastructure-as-code
│   ├── main.bicep
│   ├── main.parameters.json
│   └── modules/
├── server/                 # .NET MCP server
│   ├── Program.cs
│   ├── Dockerfile
│   ├── Auth/               # SSO + OBO token exchange
│   ├── Endpoints/          # MCP JSON-RPC endpoint
│   ├── Graph/              # Graph client, upload runner, session store
│   └── Tools/              # MCP tool implementations
└── skills/                 # Cowork agent skill definitions
```

## License

MIT
