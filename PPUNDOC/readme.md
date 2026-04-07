# PPUNDOC

## Overview

Undocumented Power Platform APIs — 119 endpoints across 9 collections, mapped, tested, and catalogued by [ppundoc.com](https://ppundoc.com). Provides programmatic access to BAP administration, Flow management, Dataverse operations, Power Apps connections, licensing, DLP policies, admin analytics, environment management, and tenant configuration.

Includes MCP protocol support with 34 tools for Copilot Studio admin agents.

> **WARNING:** These are undocumented APIs discovered from browser network traffic. They may change or break without notice. Do not use in production-critical scenarios without fallback handling.

## Prerequisites

- Power Platform admin or Global admin role
- A bearer token obtained from browser developer tools or OAuth flow

## Connection Setup

| Parameter | Description | Example |
|-----------|-------------|---------|
| **Dataverse URL** | Your Dataverse environment hostname | `org123.crm.dynamics.com` |
| **Tenant ID** | Power Platform format tenant ID | `Default-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| **Environment ID** | Power Platform environment ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| **Environment ID (PP format)** | Environment ID without hyphens, with period | `Defaultxxxxxxxxxxxxxxxxxxxxxxxx.xx` |
| **Bearer Token** | Authorization token from dev tools or OAuth | *(securestring)* |

### How to get a Bearer Token

1. Open [admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com) or [make.powerapps.com](https://make.powerapps.com)
2. Press F12 to open Developer Tools
3. Go to the **Network** tab
4. Perform any action in the UI
5. Click a request → **Headers** → copy the `Authorization` header value
6. Remove the `Bearer ` prefix — paste just the token

## API Collections (119 operations)

### BAP — Business Application Platform (9 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| Get Tenant Info | GET | Tenant ID, geo, language |
| Get Tenant Settings | GET | All admin-level settings |
| Update Tenant Settings | POST | Toggle admin settings |
| List Environments | GET | All environments with capacity and metadata |
| Get Environment Features | GET | Copilot policies per environment |
| Get D365 Templates | GET | Available D365 templates |
| List Desktop Connectors | GET | PAD connectors |
| List Unblockable Connectors | GET | DLP-exempt connectors |
| List DLP Policies | GET | All Data Loss Prevention policies |

### Flow — Power Automate (24 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| Admin: List Flows | GET | All flows in environment (all makers) |
| Admin: Get Flow Definition | GET | Full flow JSON (admin) |
| Admin: Get Flow Owners | GET | Owners of any flow (admin) |
| Admin: Share Flow | POST | Share as admin |
| List Environments | GET | Environments with permissions |
| List My Flows | GET | Current user's flows |
| Get Flow Definition | GET | Full flow JSON with connections |
| Get Flow Connections | GET | Connections used by a flow |
| Get Flow Run History | GET | Run history for a flow |
| Get Flow Run Detail | GET | Single run detail |
| Resubmit Flow Run | POST | Re-run a flow |
| Export Run Logs | POST | Create CSV export of run history |
| Download Run Logs | GET | Download exported CSV |
| Turn On Flow | POST | Enable a disabled flow |
| Turn Off Flow | POST | Disable an active flow |
| Get Flow Owners | GET | List flow owners |
| Share Flow | POST | Share with users |
| Unshare Flow | POST | Remove users |
| Cancel All Flow Runs | POST | Abort all running instances |
| Check Flow Warnings | POST | Check for warnings |
| Check Flow Errors | POST | Check for errors |
| Add Flow to Dataverse | POST | Migrate legacy flow to solution |
| Batch Flow Requests | POST | Multiple operations in one call |
| Get Per-Flow Plan Allocations | GET | Run-only flow allocations |

### Dataverse (50 operations)

**Environment:**

| Operation | Method | Description |
|-----------|--------|-------------|
| List Entities | GET | All Dataverse tables |
| Get Organization Info | GET | Organization details |
| Who Am I | GET | Current user identity |
| Get My Privileges | GET | Current user privileges |
| List Environment Features | GET | Available feature settings |
| Get Feature State | POST | Check if a feature is enabled |
| Update Environment Features | PATCH | Update feature settings |
| Get/Update Environment Settings | GET/PATCH | Organization-level settings |
| Turn On Save in Solution | PATCH | Enable save-in-solution default |
| Turn Off App Control | PATCH | Disable app control |
| Update Block Customizations | PATCH | Block/unblock customizations |

**Component Tables:**

| Operation | Method | Description |
|-----------|--------|-------------|
| List/Get/Delete Flows | GET/DELETE | Dataverse workflow table |
| Turn Flow On/Off | PATCH | Activate/deactivate via Dataverse |
| List/Get/Update Canvas Apps | GET/PATCH | Canvas app metadata |
| List/Get/Update/Delete Agents | GET/PATCH/POST | Copilot Studio agents |
| List/Get Solutions | GET | Solution management |
| List/Get Solution Components | GET | Component inventory |
| Add Solution Component | PATCH | Add component to solution |
| List/Get/Update Custom Connectors | GET/PATCH | Connector management |

**Standard Tables:**

| Operation | Method | Description |
|-----------|--------|-------------|
| Get All From Table | GET | Records from any table |
| Get Record | GET | Specific record by ID |
| Update Record | PATCH | Update any record |
| Delete Record | DELETE | Delete any record |
| Get All Records (with expand) | GET | Records with related data |
| Get All Entities | GET | Entity metadata |
| Batch Operations | POST | Multiple operations in one request |
| Count Table Rows | GET | Total record count |
| Find User | GET | Find user by UPN |
| Call Unbound Action | POST | Execute custom API/plugin |

**Sharing & Permissions:**

| Operation | Method | Description |
|-----------|--------|-------------|
| Share/Revoke Record (User) | POST | Record-level sharing for users |
| Share/Revoke Record (Team) | POST | Record-level sharing for teams |
| Check Role Permissions | GET | Privileges for a security role |
| Find Privileges | GET | Search privileges by name |
| Add Permission to Role | POST | Add privilege to security role |

### Power Apps (5 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| Create/Modify SPN Connection | PUT | Service Principal connections |
| Rename Connection | PUT | Update connection display name |
| Share Connection | POST | Share with user/SPN |
| Revoke Connection Share | POST | Remove share |
| List Connections | GET | All connections in environment |

### PP-Environment (12 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| Validate Connector Swagger | POST | Validate before publishing |
| Get/List Custom Connectors | GET | Connector management |
| Get Connector Swagger | GET | OpenAPI definition for any connector |
| Get Apps I Can Use | GET | Apps user can run |
| Get Apps Shared With Me | GET | Apps shared for editing |
| My Apps | GET | Apps owned by user |
| Share Canvas App | POST | Share with users/groups |
| My Connections | GET | User's connections |
| Enable Code Apps | PATCH | Enable code app development |
| List Environment Security Settings | GET | Security config |
| Update Code App CPS | PATCH | Update CPS setting |

### PP-Tenant (3 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| Get Tenant Advisor | GET | Governance/security recommendations |
| List Gateways | GET | On-premises data gateways |
| Get Connection Shared With | GET | Connection sharing audit |

### PP-Licensing (9 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| List License Reports | GET | Available reports |
| Get Add-On Licenses | GET | Capacity pack allocations |
| Update Add-On Licenses | PATCH | Update allocations |
| Request License Report | POST | Generate report |
| Download License Report | POST | Download generated report |
| Get Licenses | GET | Full license assignments |
| Get Tenant Capacity | GET | Storage/capacity info |
| Licenses by Environment | GET | Per-environment breakdown |
| Security Insights | GET | Security analytics |

### Admin Analytics (5 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| App Diagnostics | POST | App usage analytics |
| Power App Resources | POST | Canvas app inventory |
| List Desktop Flows | POST | RPA inventory |
| List Cloud Flows | POST | Cloud flow inventory |
| List Copilot Studio Agents | POST | Agent inventory |

### Admin (1 operation)

| Operation | Method | Description |
|-----------|--------|-------------|
| D365 App List | GET | D365 apps in tenant |

## MCP Tools (34 tools)

### Discovery & Inventory
| Tool | Description |
|------|-------------|
| `list_environments` | All environments with capacity and metadata |
| `who_am_i` | Current user identity |
| `find_user` | Find user by UPN email |
| `list_agents` | All Copilot Studio agents |
| `list_solutions` | All solutions |
| `list_custom_connectors` | All custom connectors |
| `list_gateways` | On-premises data gateways |
| `list_connections` | Connections in an environment |

### Governance & Security
| Tool | Description |
|------|-------------|
| `get_tenant_settings` | All admin-level settings |
| `update_tenant_settings` | Toggle admin settings |
| `list_dlp_policies` | DLP policies |
| `list_unblockable_connectors` | DLP-exempt connectors |
| `check_role_permissions` | Security role audit |
| `security_insights` | Security analytics summary |
| `get_tenant_advisor` | Governance recommendations |

### Flow Management
| Tool | Description |
|------|-------------|
| `list_flows_admin` | All flows (all makers) |
| `get_flow_definition` | Full flow JSON |
| `get_flow_run_history` | Run history |
| `get_flow_owners` | Flow owners |
| `share_flow` | Share with user |
| `turn_on_flow` | Enable a flow |
| `turn_off_flow` | Disable a flow |
| `cancel_all_runs` | Abort running instances |
| `check_flow_errors` | Diagnose errors |

### Licensing & Capacity
| Tool | Description |
|------|-------------|
| `get_licenses` | License assignments |
| `get_tenant_capacity` | Storage/capacity |
| `licenses_by_environment` | Per-environment breakdown |
| `add_on_licenses` | Capacity packs, AI Builder credits |
| `request_license_report` | Generate reports |

### Analytics
| Tool | Description |
|------|-------------|
| `list_canvas_apps_analytics` | App inventory with usage |
| `list_desktop_flows` | RPA inventory |
| `list_cloud_flows_analytics` | Cloud flow usage |
| `app_diagnostics` | Per-app usage analytics |
| `list_copilot_agents_analytics` | Agent inventory with usage |

## API Hosts

This connector routes to 10 different API hosts depending on the operation:

| Host | Collections |
|------|-------------|
| `api.bap.microsoft.com` | BAP |
| `api.flow.microsoft.com` | Flow |
| `{dataverseUrl}` | Dataverse |
| `api.powerapps.com` | Power Apps |
| `{envId}.environment.api.powerplatform.com` | PP-Environment |
| `{tenantId}.tenant.api.powerplatform.com` | PP-Tenant |
| `licensing.powerplatform.microsoft.com` | PP-Licensing |
| `na.adminanalytics.powerplatform.microsoft.com` | Admin Analytics |
| `api.admin.powerplatform.microsoft.com` | Admin |
| `api.powerplatform.com` | PP-Environment Management, MCP |

## Known Limitations

- **Undocumented APIs** — may break without notice. Not supported by Microsoft.
- **Bearer token auth** — tokens expire (typically 60 minutes). Must be refreshed manually or via OAuth flow.
- **Admin Analytics** — requires Global Admin role. Region prefix (`na.`) may differ for non-US tenants.
- **BAP API** — limited SPN support. May require user-context token from browser.
- **PP-Environment API** — requires special environment ID format (no hyphens, period before last 2 chars).
- **Per-user UPN usage data** — not available through any of these APIs. Use Power Platform Admin Center UI or Center of Excellence Starter Kit.

## Credits

Endpoint catalogue sourced from [ppundoc.com](https://ppundoc.com) by [David Wyatt](https://dev.to/wyattdave) ([@WyattDave](https://x.com/WyattDave)). Postman collections available at [GitHub](https://github.com/wyattdave/Power-Platform/tree/main/Power%20Platform%20APIs).
