# Salesforce MCP Server: Microsoft 365 Copilot plugin

A plugin that connects Microsoft 365 Copilot to your [Salesforce MCP Server](../readme.md). It packages:

- **A connector** that points Copilot at your server's `/mcp` endpoint and signs each person in to **Salesforce** with the OAuth authorization code flow.
- **Five skills** that teach Copilot how to use the tools well: finding records, understanding an object's schema, changing records safely, comparing schema across API versions or over time, and diagnosing errors.

You build and register it with the Work IQ Dev Tools CLI (`wiqd`). Admins then review and assign it in the Microsoft 365 admin center under **Agents > Tools**.

Because each person signs in to Salesforce as themselves, Copilot only sees the records and fields that person is allowed to see, and changes are attributed to them in Salesforce.

## Before you start

- Your Salesforce MCP Server is [deployed](../readme.md) and `https://<fqdn>/ready` returns `Healthy`.
- You have a Salesforce External Client App set up for the authorization code flow ([step 1 of the server setup](../readme.md#1-create-the-salesforce-app)), with the **Consumer Key** and **Consumer Secret** to hand.
- You know your **Microsoft 365 tenant ID**.
- The `wiqd` CLI is installed, and you can sign in to Microsoft 365 with `wiqd auth login --interactive`.
- You can sign in to the [Teams developer portal](https://dev.teams.microsoft.com/tools).

## 1. Check the Salesforce app

The connector signs people in through your Salesforce External Client App, so it needs:

| Setting | Value |
|---|---|
| Callback URL | `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` (the same for every plugin) |
| Scopes | `api`, `refresh_token` (also shown as `offline_access`), and `openid` or `id` |
| Flow | Authorization Code and Credentials Flow, with PKCE |
| Who can authorize | Users may self-authorize |

The `openid` or `id` scope matters: the server checks each token with Salesforce's `userinfo` endpoint, which needs an identity scope. Without it, sign-in succeeds but every request is refused with `401`.

## 2. Register the OAuth client in the Teams developer portal

Salesforce does not offer dynamic client registration, so the sign-in configuration is created by hand. In the [Teams developer portal](https://dev.teams.microsoft.com/tools), open **Tools > OAuth client registration** and create a registration:

| Field | Value |
|---|---|
| Identity provider | `Custom` |
| Base URL / target URLs start with | your server's origin, for example `https://<fqdn>` |
| **Applicable to apps** | **Any app** |
| Client ID | your Salesforce **Consumer Key** |
| Client secret | your Salesforce **Consumer Secret** |
| Authorization endpoint | `https://<my-domain>.my.salesforce.com/services/oauth2/authorize` |
| Token exchange and refresh endpoints | `https://<my-domain>.my.salesforce.com/services/oauth2/token` |
| Scope | `api refresh_token openid` |
| PKCE | on |
| Token exchange method | `POST request body` |
| Target audience | `Home tenant` |

Use your own **My Domain** in the endpoints, not `login.salesforce.com`, so people sign in to the right org.

Save it and copy the **registration ID**. You need it in the next step. The portal's labels may differ slightly from the names above; the values are what matter.

> **Applicable to apps must be Any app.** Binding the registration to one Teams app lets provisioning succeed and then makes every tool call return 404.
>
> Keep `refresh_token` in the scope, or sign-in can't be refreshed and users are asked to sign in again when the Salesforce session ends.

## 3. Build and register the plugin

On a fresh clone, copy `env\.env.dev.template` to `env\.env.dev` first. Provisioning fills it in, and it isn't committed because the IDs it holds belong to your tenant.

```powershell
Copy-Item env\.env.dev.template env\.env.dev
wiqd auth login --interactive

.\Build-Plugin.ps1 `
    -McpUrl https://<fqdn>/mcp `
    -TenantId <microsoft-365-tenant-id> `
    -AuthConfigId <registration-id> `
    -WriteTools `
    -Provision
```

The script:

1. Starts the server locally and captures its tool list into `appPackage\mcp-tools.json`.
2. Warns about any tool that no skill mentions.
3. Registers the plugin (`-Provision`, required the first time so an app ID is created), packages it, and runs deep validation.

Pass `-WriteTools` only if you turned the write tools on when you deployed the server. The tool list the plugin describes has to match what the server really offers.

The server URL and the OAuth reference are passed in and exported as environment variables for the manifest. They are not stored in this folder.

## 4. Share it

```powershell
wiqd plugin share --env dev --scope users --email someone@contoso.com
```

Use `--scope tenant` to share with everyone. Run it from this folder and keep `--env dev`, which matches the environment `Build-Plugin.ps1` registers; without it, `wiqd` targets a `local` environment that has no registered app. Your admin can also review, assign, or block the plugin in the Microsoft 365 admin center under **Agents > Tools**.

The first time someone uses it, Copilot asks them to sign in to Salesforce and approve access. After that it refreshes their sign-in automatically.

## Keeping it current

- **Run `Build-Plugin.ps1` again whenever the server's tools change.** A plugin that describes tools the server no longer has misleads Copilot, which follows the description confidently. Add or update a skill if a new tool needs guidance on when to use it.
- **Raise `version` in `appPackage\manifest.json` before you register a change.** Registering an existing version is rejected.
- If you change the server's URL, run the script again with the new `-McpUrl`.
- If you rotate the Consumer Secret in Salesforce, update it in the OAuth registration in the developer portal. The server doesn't hold it.

## What's in the plugin

| Skill | Handles |
|---|---|
| `find-salesforce-records` | "show my open opportunities", "find the Acme account", "look up record 001..." |
| `understand-salesforce-schema` | "what fields does Case have", "what are the Status values", "how is this formula calculated", "what validation rules are on Opportunity" |
| `change-salesforce-records` | "create an account", "change the stage to Closed Won", "delete this record". Confirms before writing, and never creates twice |
| `compare-salesforce-schema` | "what changed on this object", "what's different in API v52" |
| `diagnose-salesforce-errors` | "it says insufficient access", "why did this fail", "who am I connected as" |

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `Missing environment variables 'SALESFORCE_MCP_URL, ...'` | You ran `wiqd` directly. Run `Build-Plugin.ps1`, which sets them |
| `lacks a valid TEAMS_APP_ID` | The plugin hasn't been registered yet. Run `Build-Plugin.ps1` with `-Provision` |
| `must contain a non-empty 'tools' array` | `mcp-tools.json` has the wrong shape. It must be `{ "tools": [ ... ] }`. `Build-Plugin.ps1` writes it that way |
| Copilot reports **Could not verify connection** | `mcp-tools.json` is missing or empty. Run `Build-Plugin.ps1` and check that it wrote the tool list |
| Every tool call returns 404 | The OAuth registration is bound to one app. Recreate it with **Applicable to apps** set to **Any app** |
| Salesforce shows `redirect_uri_mismatch` | The Teams callback URL isn't on the External Client App. Repeat step 1 |
| Salesforce shows `invalid_scope` or an approval error | A scope in the registration isn't enabled on the External Client App, or users aren't allowed to self-authorize |
| Sign-in succeeds but every request is `401` | The token has no identity scope. Add `openid` (or `id`) to the registration's scope and to the External Client App |
| Users are asked to sign in again often | The scope is missing `refresh_token`, or the app's refresh token policy expires tokens quickly |
| `Salesforce rejected this sign-in` | The person's Salesforce session or token was revoked. They sign in again |
| Tools return Salesforce permission errors | The signed-in person lacks access in Salesforce. See the [server readme](../readme.md#troubleshooting) |
