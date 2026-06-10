# Power Platform Connector CLI Commands Reference

A comprehensive reference for all CLI commands related to connectors across the **Power Platform CLI (`pac`)** and the **Power Platform Connectors CLI (`paconn`)**.

| | pac (Power Platform CLI) | paconn (Connectors CLI) |
|---|---|---|
| **Install** | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` or VS Code extension | `pip install paconn` (requires Python 3.5+) |
| **Auth** | `pac auth create` (supports SPN, interactive, device-code) | `paconn login` (device-code only; no SPN) |
| **Scope** | Solution-aware connectors in Dataverse | All custom connectors (solution-aware or not) |
| **Docs** | [pac connector](https://learn.microsoft.com/power-platform/developer/cli/reference/connector) | [paconn CLI](https://learn.microsoft.com/connectors/custom-connectors/paconn-cli) |

---

## Table of Contents

- [pac connector commands](#pac-connector-commands)
  - [pac connector init](#pac-connector-init)
  - [pac connector create](#pac-connector-create)
  - [pac connector list](#pac-connector-list)
  - [pac connector download](#pac-connector-download)
  - [pac connector update](#pac-connector-update)
- [pac connection commands](#pac-connection-commands)
  - [pac connection create](#pac-connection-create)
  - [pac connection list](#pac-connection-list)
  - [pac connection update](#pac-connection-update)
  - [pac connection delete](#pac-connection-delete)
- [paconn commands](#paconn-commands)
  - [paconn login](#paconn-login)
  - [paconn logout](#paconn-logout)
  - [paconn download](#paconn-download)
  - [paconn create](#paconn-create)
  - [paconn update](#paconn-update)
  - [paconn validate](#paconn-validate)
- [Connector file structure](#connector-file-structure)
- [settings.json reference](#settingsjson-reference)

---

## pac connector commands

Commands for working with Power Platform Connectors via the Power Platform CLI. These commands operate on **solution-aware** connectors in Dataverse.

### pac connector init

Initializes a new API Properties file for a connector, scaffolding the required project files.

```powershell
pac connector init `
  --connection-template "OAuthAAD" `
  --generate-script-file `
  --generate-settings-file `
  --outputDirectory "MyConnector"
```

| Parameter | Alias | Description |
|---|---|---|
| `--connection-template` | `-ct` | Auth template: `NoAuth`, `BasicAuth`, `ApiKey`, `OAuthGeneric`, `OAuthAAD` |
| `--generate-script-file` | | Switch. Generate an initial `script.csx` file. |
| `--generate-settings-file` | | Switch. Generate an initial `settings.json` file. |
| `--outputDirectory` | `-o` | Output directory for generated files. |

---

### pac connector create

Creates a new connector row in the Dataverse Connector table.

```powershell
pac connector create `
  --api-definition-file ./apiDefinition.json `
  --api-properties-file ./apiProperties.json `
  --environment 00000000-0000-0000-0000-000000000000
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--api-definition-file` | `-df` | No | Path to the OpenAPI definition file. |
| `--api-properties-file` | `-pf` | No | Path to the API properties file. |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). Defaults to active auth profile. |
| `--icon-file` | `-if` | No | Path to an icon `.png` file. |
| `--script-file` | `-sf` | No | Path to a script `.csx` file. |
| `--settings-file` | | No | Path to a connector settings file. |
| `--solution-unique-name` | `-sol` | No | Solution to add the connector to. |

---

### pac connector list

Lists connectors registered in Dataverse. **Only solution-aware connectors are returned.**

```powershell
pac connector list
pac connector list --environment 00000000-0000-0000-0000-000000000000
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |
| `--json` | | No | Return output as JSON. |

---

### pac connector download

Downloads a connector's OpenAPI definition and API properties file.

```powershell
pac connector download `
  --connector-id 00000000-0000-0000-0000-000000000000 `
  --environment 00000000-0000-0000-0000-000000000000 `
  --outputDirectory "MyConnector"
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--connector-id` | `-id` | **Yes** | The connector ID (must be a valid GUID). |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |
| `--outputDirectory` | `-o` | No | Output directory. |

---

### pac connector update

Updates an existing connector entity in Dataverse.

```powershell
pac connector update `
  --api-definition-file ./apiDefinition.json `
  --api-properties-file ./apiProperties.json `
  --environment 00000000-0000-0000-0000-000000000000
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--api-definition-file` | `-df` | No | Path to the OpenAPI definition file. |
| `--api-properties-file` | `-pf` | No | Path to the API properties file. |
| `--connector-id` | `-id` | No | Connector ID (GUID). |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |
| `--icon-file` | `-if` | No | Path to an icon `.png` file. |
| `--script-file` | `-sf` | No | Path to a script `.csx` file. |
| `--settings-file` | | No | Path to a connector settings file. |
| `--solution-unique-name` | `-sol` | No | Solution to add the connector to. |

---

## pac connection commands

Commands for managing Dataverse **connections** (the runtime instances that link a connector to credentials).

### pac connection create

Creates a new Dataverse connection.

```powershell
pac connection create `
  --name "MyConnection" `
  --application-id 00000000-0000-0000-0000-000000000000 `
  --tenant-id 00000000-0000-0000-0000-000000000000 `
  --client-secret "secret"
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--name` | `-n` | **Yes** | Connection name. |
| `--application-id` | `-a` | **Yes** | Application (client) ID. |
| `--tenant-id` | `-t` | **Yes** | Tenant ID. |
| `--client-secret` | `-cs` | **Yes** | Client secret. |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |

---

### pac connection list

Lists all connections in the environment.

```powershell
pac connection list
pac connection list --environment 00000000-0000-0000-0000-000000000000
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |

---

### pac connection update

Updates an existing Dataverse connection.

```powershell
pac connection update `
  --connection-id 00000000-0000-0000-0000-000000000000 `
  --application-id 00000000-0000-0000-0000-000000000000 `
  --tenant-id 00000000-0000-0000-0000-000000000000 `
  --client-secret "newSecret"
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--connection-id` | `-id` | **Yes** | Connection ID. |
| `--application-id` | `-a` | **Yes** | Application (client) ID. |
| `--tenant-id` | `-t` | **Yes** | Tenant ID. |
| `--client-secret` | `-cs` | **Yes** | Client secret. |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |

---

### pac connection delete

Deletes a Dataverse connection.

```powershell
pac connection delete --connection-id 00000000-0000-0000-0000-000000000000
```

| Parameter | Alias | Required | Description |
|---|---|---|---|
| `--connection-id` | `-id` | **Yes** | Connection ID. |
| `--environment` | `-env` | No | Target Dataverse environment (GUID or URL). |

---

## paconn commands

The `paconn` CLI is a Python-based tool (`pip install paconn`) designed specifically for custom connector lifecycle management. It works with **all custom connectors**, not just solution-aware ones.

### paconn login

Authenticates to Power Platform using device-code flow.

```bash
paconn login
```

> **Note:** Service Principal authentication is not supported by `paconn`.

---

### paconn logout

Logs out of the current Power Platform session.

```bash
paconn logout
```

---

### paconn download

Downloads custom connector files (OpenAPI definition, API properties, icon, script) into a subdirectory named with the connector ID. Also writes a `settings.json` file.

```bash
paconn download
paconn download -e <environment-guid> -c <connector-id>
paconn download -s settings.json
```

| Parameter | Alias | Description |
|---|---|---|
| `--env` | `-e` | Power Platform environment GUID. |
| `--cid` | `-c` | Custom connector ID. |
| `--dest` | `-d` | Destination directory. |
| `--overwrite` | `-w` | Overwrite existing connector and settings files. |
| `--pau` | `-u` | Power Platform URL. |
| `--pav` | `-v` | Power Platform API version. |
| `--settings` | `-s` | Path to `settings.json` (overrides other arguments). |

If environment or connector ID are omitted, the CLI prompts interactively.

---

### paconn create

Creates a new custom connector from local files. Prints the new connector ID on success.

```bash
paconn create --api-prop apiProperties.json --api-def apiDefinition.swagger.json
paconn create -e <environment-guid> --api-prop apiProperties.json --api-def apiDefinition.swagger.json --icon icon.png --secret <oauth2-secret>
paconn create -s settings.json --secret <oauth2-secret>
```

| Parameter | Alias | Description |
|---|---|---|
| `--api-def` | | Path to the OpenAPI definition JSON. |
| `--api-prop` | | Path to the API properties JSON. |
| `--env` | `-e` | Power Platform environment GUID. |
| `--icon` | | Path to icon file. |
| `--script` | `-x` | Path to script `.csx` file. |
| `--secret` | `-r` | OAuth2 client secret for the connector. |
| `--pau` | `-u` | Power Platform URL. |
| `--pav` | `-v` | Power Platform API version. |
| `--settings` | `-s` | Path to `settings.json`. |

> **Tip:** After creating, update your `settings.json` with the new connector ID before running subsequent updates.

---

### paconn update

Updates an existing custom connector. Prints the updated connector ID on success.

```bash
paconn update --api-prop apiProperties.json --api-def apiDefinition.swagger.json
paconn update -e <environment-guid> -c <connector-id> --api-prop apiProperties.json --api-def apiDefinition.swagger.json --icon icon.png --secret <oauth2-secret>
paconn update -s settings.json --secret <oauth2-secret>
```

| Parameter | Alias | Description |
|---|---|---|
| `--api-def` | | Path to the OpenAPI definition JSON. |
| `--api-prop` | | Path to the API properties JSON. |
| `--cid` | `-c` | Custom connector ID. |
| `--env` | `-e` | Power Platform environment GUID. |
| `--icon` | | Path to icon file. |
| `--script` | `-x` | Path to script `.csx` file. |
| `--secret` | `-r` | OAuth2 client secret for the connector. |
| `--pau` | `-u` | Power Platform URL. |
| `--pav` | `-v` | Power Platform API version. |
| `--settings` | `-s` | Path to `settings.json`. |

---

### paconn validate

Validates a swagger/OpenAPI definition file against Power Platform connector rules.

```bash
paconn validate --api-def apiDefinition.swagger.json
paconn validate -s settings.json
```

| Parameter | Alias | Description |
|---|---|---|
| `--api-def` | | Path to the OpenAPI definition JSON. |
| `--pau` | `-u` | Power Platform URL. |
| `--pav` | `-v` | Power Platform API version. |
| `--settings` | `-s` | Path to `settings.json`. |

Outputs errors, warnings, or a success message.

---

## Connector file structure

Both CLIs work with the same set of connector artifacts:

```
<connector-id>/
  ├── apiDefinition.swagger.json   # OpenAPI/Swagger definition (required)
  ├── apiProperties.json           # Auth, branding, policies, script ops (required)
  ├── icon.png                     # Connector icon (optional)
  ├── script.csx                   # C# script for custom code (optional)
  └── settings.json                # CLI argument store (optional, not part of connector)
```

---

## settings.json reference

Used by both `pac connector` and `paconn` to store CLI arguments. The `paconn` variant includes additional Power Apps-specific fields.

```json
{
  "connectorId": "CONNECTOR-ID",
  "environment": "ENVIRONMENT-GUID",
  "apiProperties": "apiProperties.json",
  "apiDefinition": "apiDefinition.swagger.json",
  "icon": "icon.png",
  "script": "script.csx",
  "powerAppsApiVersion": "2016-11-01",
  "powerAppsUrl": "https://api.powerapps.com"
}
```

| Field | Used By | Description |
|---|---|---|
| `connectorId` | paconn | Connector ID. Required for download/update; not needed for create/validate. |
| `environment` | both | Environment GUID. Required for all operations except validate. |
| `apiProperties` | both | Path to `apiProperties.json`. |
| `apiDefinition` | both | Path to the swagger file. |
| `icon` | both | Path to icon file. |
| `script` | both | Path to script `.csx` file. |
| `powerAppsUrl` | paconn | API URL (default: `https://api.powerapps.com`). |
| `powerAppsApiVersion` | paconn | API version (default: `2016-11-01`). |

---

## Known limitations

| Limitation | Applies to |
|---|---|
| Only solution-aware connectors are shown in `pac connector list`. | `pac` |
| No Service Principal authentication support. | `paconn` |
| Cannot update connector when `stackOwner` is in `apiProperties.json`. Workaround: maintain two versions of artifacts (one with `stackOwner` for certification, one without for dev updates). | `paconn` |
| Limited to custom connectors only; cannot download swagger for first-party connectors. | `paconn` |

---

*Sources: [pac connector reference](https://learn.microsoft.com/power-platform/developer/cli/reference/connector) | [pac connection reference](https://learn.microsoft.com/power-platform/developer/cli/reference/connection) | [paconn CLI](https://learn.microsoft.com/connectors/custom-connectors/paconn-cli)*
