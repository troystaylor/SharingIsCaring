# OAuth setup for Decision Duck (Cowork)

This connector uses `OAuthPluginVault`. The Vault `referenceId` in `manifest.json` points at credentials registered in the Microsoft Enterprise Token Store via Teams Developer Portal. Secrets never live in the plugin package.

## 1. Register the Entra app (MCP server tenant)

App registration in the tenant where the Decision Duck MCP server runs.

- **Name:** `Decision Duck MCP`
- **Supported account types:** **Accounts in any organizational directory** (`AzureADMultipleOrgs`) — required so users in any Cowork tenant can sign in.
- **Redirect URI (Web):** `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`
  > Cowork uses the Teams platform OAuth redirect endpoint. Do **not** use `https://token.botframework.com/.auth/web/redirect` (that's Bot Framework, not the Plugin Vault).
- **Expose an API** → **Set Application ID URI** (accept the default, e.g. `api://{appId}`) → **Add a scope**:
  - **Scope name:** `access_as_user`
  - **Who can consent:** Admins and users
  - **Admin consent display name:** `Use Decision Duck`
  - **Admin consent description:** `Allows the signed-in user to call the Decision Duck MCP server.`
- **Certificates & secrets** → create a client secret. Copy the value — you'll paste it into Teams Developer Portal in step 2.

Note the **Application (client) ID**, the **Application ID URI** (e.g. `api://8a3f1c5b-…`), and the **client secret value**.

## 2. Register the Plugin Vault entry (Teams Developer Portal)

Teams Developer Portal → **Tools** → **Plugin Vault** → **New OAuth entry**.

| Field | Value |
|-------|-------|
| **Name** | `decision-duck-oauth` |
| **Client ID** | Application (client) ID from step 1 |
| **Client secret** | Secret value from step 1 |
| **Authorization URL** | `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` |
| **Token URL** | `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token` |
| **Scopes** | `api://{appId}/access_as_user offline_access` |
| **PKCE** | Enabled |

> `{tenantId}` must be the **Cowork user's tenant id**, not the MCP server's tenant id. Using the wrong tenant gives `AADSTS700016: Application … not found in directory`. For multi-tenant rollout, use a tenant-specific URL per customer or document that customers run the registration in their own Dev Portal.

Save the entry. Copy the **referenceId** that Teams Developer Portal generates.

## 3. Wire the referenceId into the manifest

Edit [`../manifest.json`](../manifest.json):

```json
"authorization": {
  "type": "OAuthPluginVault",
  "referenceId": "PASTE_REFERENCEID_HERE"
}
```
