# Salesforce PP Plugin

A Microsoft 365 Copilot plugin for **Copilot Cowork** that reaches Salesforce only through a Power Platform custom connector. The connector is based on the [Salesforce MCP](../../Salesforce%20MCP) connector in this repository. The plugin has no remote MCP server URL and no OAuth registration of its own: Salesforce sign-in and the endpoint both live in the connector and your Power Platform connection. Other Copilot hosts are covered in [Power Platform Connector Plugins - Learnings](<./Power Platform Connector Plugins - Learnings.md>).

| Component | Path |
| --- | --- |
| Connector `salesforce-plugin` (existing Power Platform custom connector, `ConnectionVault`) | `appPackage/manifest.json` |
| Skills: Query Salesforce Data, Manage Salesforce Records, Salesforce Reports and Chatter, Salesforce Knowledge and Search, Salesforce Case Service | `appPackage/skills/*/SKILL.md` |

The skills call the connector's REST operations by operation Id: `Query`, `Search`, `GetRecord`, `CreateRecord`, `UpdateRecord`, `DeleteRecord`, `DescribeGlobal`, `DescribeObject`, `GetLimits`, the report, dashboard, Chatter, Knowledge and synonym operations, and `CreateCase`, `GetCase`, `UpdateCase` and `AddCaseComment`. Cowork invokes those operations directly, so the script's MCP-only tools (`log_interaction`, `get_case_timeline`, `send_case_email` and the other case service tools) are not used. The Case Service skill rebuilds them from `Query`, `CreateRecord` and the case operations.

## Prerequisites

- **Work IQ Dev Tools** `wiqd` 0.21.0 or later (`npm install -g @microsoft/wiqd@latest`). Earlier versions can't add an OpenAPI custom connector.
- A Power Platform environment where you can create custom connectors, and the Power Platform CLI (`pac`).
- A Salesforce **External Client App** (or Connected App) that allows the `api` and `refresh_token` scopes. The connector itself requests only `api`; the `salesforcev2` identity provider asks for `refresh_token`. See the [Salesforce MCP readme](../../Salesforce%20MCP/readme.md) for setup.

## Setup

Cowork looks for the connector in its own Power Platform environment, **Copilot Cowork**, not in your default or demo environment. A connector that exists only elsewhere fails with "Could not verify connection" (the connections gateway answers `Connector not found`). Deploy the connector to the environment your plugin host uses. More background: [Power Platform Connector Plugins - Learnings](<./Power Platform Connector Plugins - Learnings.md>).

1. **Deploy the connector.** From the [Salesforce MCP](../../Salesforce%20MCP) folder, make a temporary copy of `apiProperties.json` with your Salesforce client ID, then create the connector with the display name **Salesforce Plugin**. `pac connector create` works for environments that have Dataverse. The Copilot Cowork environment has none, so use `paconn`:
   ```powershell
   paconn login
   paconn create --env <environment id> --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --secret <client secret>
   ```
   Set the `host` in the definition to your Salesforce instance first, and make the body change described under [Static bodies only](#tool-exposure) below.
2. **Register the redirect URL.** Read the generated per-connector redirect URL from the deployed connector and add it to your Salesforce app's callback URLs.
3. **Find the connector's API name** with `pac connection list` (or `pac connector list` once a connection exists). It's the part of the **API Id** after `/apis/`, in the form `shared_<connector name>-<environment suffix>`. For a connector named **Salesforce Plugin** with publisher prefix `new`, it looks like `shared_new-5fsalesforce-20plugin-<suffix>`. Copy `env/.env.dev.user.sample` to `env/.env.dev.user` and set `SALESFORCE_CONNECTOR_REFERENCE_ID` to it. The manifest references that variable, and the file is gitignored because the suffix is specific to your environment.
4. **Create a connection** to the connector in your environment and sign in to Salesforce.

## Build and test

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

If `package` or deep validation reports `SALESFORCE_CONNECTOR_REFERENCE_ID` missing, set it in the shell: `$env:SALESFORCE_CONNECTOR_REFERENCE_ID = "<api name>"`. Bump `version` in `appPackage/manifest.json` before provisioning a changed package again. Add `--env dev` to `share` and `package`; they default to `local`.

## Tool exposure

The OpenAPI connector shape is **devPreview-only**. The plugin stores only a pointer to the connector; the platform resolves the endpoint and operations at runtime. In Cowork, a request runs "Hydrate connector" and then "Invoke connector operation" with the connector's operation Id (for example `CreateRecord`) and flat parameters (for example `sObject` and `Name`). So skills must name REST operation Ids, not the script's MCP tool names. Cowork asks you to approve a write before it runs, and the skills also state a confirmation rule up front.

**Static bodies only.** Cowork ignores a request body whose schema is declared with `x-ms-dynamic-schema`, so `CreateRecord` and `UpdateRecord` fail with an empty POST unless their bodies are static. The connector definition in this repository still uses the dynamic schema, which suits Power Automate and Power Apps. Before you deploy it for Cowork, edit a copy of `apiDefinition.swagger.json`: remove `x-ms-dynamic-schema` from the body of `CreateRecord` and `UpdateRecord`, and declare the fields you need under that body's `properties` (use `GetObjectSchema` output for your objects as a guide). Keep the dynamic version for any connector copy used in Power Automate or Power Apps. Enum values in declared properties must be unique, and the definition must be compact ASCII JSON for `paconn` to accept it.

## Environment files

`env/.env.dev` holds IDs that `wiqd plugin provision` writes for your tenant, so it is gitignored. Copy `env/.env.dev.sample` to `env/.env.dev` before your first provision. `env/.env.dev.user` (gitignored) holds your connector reference.

## Notes

- Every call runs as the signed-in Salesforce user, so Salesforce object permissions, field-level security and sharing rules apply.
- Open Plugin export isn't supported for `ConnectionVault` connectors.
- Replace the default icons (`appPackage/color.png` 192x192 and `outline.png` 32x32) before publishing.
- Admins approve and assign plugins in the Microsoft 365 admin center under **Agents > Tools**.
