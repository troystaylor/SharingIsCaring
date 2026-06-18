# Power Platform ALM

Dual-mode custom connector for Power Platform Application Lifecycle Management. Manages solutions and deployment pipelines across environments via the Dataverse Web API and Power Platform Admin API.

- **MCP endpoint** for Copilot Studio agents (natural language ALM operations)
- **Typed operations** with full schemas, dynamic dropdowns, and IntelliSense for Power Automate

## Prerequisites

- An Azure AD app registration with:
  - **API permissions**: `https://api.powerplatform.com` (delegated)
  - **Redirect URI**: `https://global.consent.azure-apim.net/redirect`
- Power Platform admin or environment maker role on target environments
- Dataverse provisioned in environments you want to manage

## Supported Operations

### Solution Management

| Operation | Description | API |
|-----------|-------------|-----|
| **List Solutions** | List all visible solutions in an environment with name, version, managed status, and publisher | `GET /api/data/v9.2/solutions` |
| **Get Solution** | Get details of a specific solution by unique name | `GET /api/data/v9.2/solutions?$filter=uniquename eq '{name}'` |
| **Export Solution** | Export a solution as managed or unmanaged zip (base64-encoded) | `POST /api/data/v9.2/ExportSolution` |
| **Import Solution** | Import a solution zip into a target environment (async). Supports holding solution for staged upgrades | `POST /api/data/v9.2/ImportSolution` |
| **Publish Customizations** | Publish all unpublished customizations in an environment | `POST /api/data/v9.2/PublishAllXml` |
| **Delete Solution** | Delete a solution from an environment | `DELETE /api/data/v9.2/solutions({id})` |
| **Set Solution Version** | Update an unmanaged solution's version number | `PATCH /api/data/v9.2/solutions({id})` |
| **Clone as Patch** | Create a patch solution for incremental changes to a parent solution | `POST /api/data/v9.2/CloneAsPatch` |
| **Clone as Solution** | Fork/branch a solution as a new independent solution | `POST /api/data/v9.2/CloneAsSolution` |
| **Delete and Promote** | Complete a staged upgrade by deleting the base and promoting the holding solution | `POST /api/data/v9.2/DeleteAndPromote` |

### Pipeline Management

| Operation | Description | Dataverse Entity |
|-----------|-------------|------------------|
| **List Pipelines** | List deployment pipelines in a host environment | `deploymentpipelines` |
| **List Pipeline Stages** | List stages for a pipeline with target environments | `deploymentstages` |
| **Deploy to Pipeline** | Trigger a deployment through a pipeline stage. Supports current/new version parameters | `deploymentstageruns` |
| **Get Deployment Status** | Check the status of a deployment run (Queued, Running, Succeeded, Failed) | `deploymentstageruns` |
| **List Deployment History** | List past deployment runs for a pipeline | `deploymentstageruns` |

### Dynamic Dropdowns (Internal)

| Operation | Purpose |
|-----------|---------|
| **GetEnvironmentDropdown** | Populates environment picker (filters to Dataverse-provisioned environments) |
| **GetSolutionDropdown** | Populates solution picker (filters to unmanaged solutions) |
| **GetPipelineDropdown** | Populates pipeline picker for selected host environment |

## Architecture

### Authentication

Uses OAuth 2.0 against `https://api.powerplatform.com` via the `aad` identity provider. The connector:

1. Calls the Power Platform Admin API to list environments and resolve Dataverse org URLs
2. Forwards the auth token to individual Dataverse Web APIs via `Context.SendAsync`

> **Note**: The `api.powerplatform.com` token is forwarded to Dataverse org URLs. If this results in 401 errors during testing, the auth resource may need to change to a per-environment Dataverse org URL.

### Dual-Mode Pattern

- `basePath: "/"` with MCP endpoint at `path: "/mcp"` (required for dual-mode connectors)
- `x-ms-agentic-protocol: "mcp-streamable-1.0"` on the MCP operation
- All operations listed in `scriptOperations` in `apiProperties.json` — script.csx handles all routing

### MCP Tools (12 total)

The MCP endpoint exposes these tools for Copilot Studio agents:

| Tool | Description |
|------|-------------|
| `alm_list_solutions` | List solutions in an environment |
| `alm_get_solution` | Get solution details by unique name |
| `alm_export_solution` | Export solution as managed/unmanaged zip |
| `alm_import_solution` | Import solution zip with holding solution support |
| `alm_publish_customizations` | Publish all customizations |
| `alm_delete_solution` | Delete a solution (destructive) |
| `alm_set_solution_version` | Update solution version number |
| `alm_clone_as_patch` | Create a patch solution for incremental changes |
| `alm_clone_as_solution` | Fork/branch a solution as a new independent solution |
| `alm_delete_and_promote` | Complete staged upgrade (destructive) |
| `alm_list_pipelines` | List deployment pipelines |
| `alm_list_pipeline_stages` | List pipeline stages with target environments |
| `alm_deploy_to_pipeline` | Trigger pipeline deployment (destructive) |
| `alm_get_deployment_status` | Check deployment run status |
| `alm_list_deployment_history` | List past deployment runs |

## Configuration

1. Create an Azure AD app registration (multi-tenant, `AzureADMultipleOrgs`)
2. Add delegated API permissions for `Power Platform API` (`8578e004-a5c6-46e7-913e-12f58912df43`):
   - `EnvironmentManagement.Environments.Read`
   - `EnvironmentManagement.Settings.Read`
   - `EnvironmentManagement.Settings.ReadWrite`
   - `AppManagement.ApplicationPackages.Read`
   - `AppManagement.ApplicationPackages.Install`
3. Create a service principal for the app: `az ad sp create --id <appId>`
4. Grant admin consent: `az ad app permission grant --id <appId> --api 8578e004-a5c6-46e7-913e-12f58912df43 --scope "EnvironmentManagement.Environments.Read EnvironmentManagement.Settings.Read EnvironmentManagement.Settings.ReadWrite AppManagement.ApplicationPackages.Read AppManagement.ApplicationPackages.Install"`
5. Add redirect URIs:
   - `https://global.consent.azure-apim.net/redirect`
   - The connector-specific redirect URI shown during connection creation
6. Create a client secret and note it for connection creation
7. Replace `[INSERT_YOUR_CLIENT_ID]` in `apiProperties.json` with your app's client ID
8. Optionally replace `[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]` in `script.csx` for telemetry
9. Deploy with PAC CLI:
   ```
   pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx
   ```
10. When creating a connection, enter the client secret from step 6

## Known Limitations

- **Solution export size**: Custom connectors have an approximate 100 MB request body limit. Most solutions are well under this.
- **Pipeline entity names**: `deploymentpipelines`, `deploymentstages`, and `deploymentstageruns` are the expected Dataverse entity logical names. Verify against your environment's metadata if pipelines are not returning results.

## API References

- [solution EntityType](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/solution)
- [ExportSolution Action](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/exportsolution)
- [ImportSolution Action](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/importsolution)
- [Pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines)
- [PAC CLI pipeline commands](https://learn.microsoft.com/power-platform/developer/cli/reference/pipeline)
- [Power Platform Admin API](https://learn.microsoft.com/power-platform/admin/programmability-and-extensibility/powerplatform-api-reference)
