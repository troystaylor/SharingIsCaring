# Azure Maps Route plugin

Copilot plugin for the Azure Maps Route tools. It lets Microsoft 365 Copilot plan routes, compare travel times, find reachable areas, snap GPS points to roads and draw routes on a map image. Route results are GeoJSON.

This plugin pairs with the [Azure Maps Route custom connector](../../Azure%20Maps%20Route/readme.md), which exposes the same tools through its `InvokeMCP` operation.

| Component | Path |
| --- | --- |
| Connector `azure-maps-route` (remote MCP, API key) | `appPackage/manifest.json` |
| Skills: Plan a Route, Compare Travel Times, Find a Service Area, Clean a GPS Trace, Map a Route | `appPackage/skills/*/SKILL.md` |

Tools used by the skills: `get_route_directions`, `get_route_matrix`, `get_route_range`, `snap_to_roads`, `get_static_map`, `get_route_map`.

## Setup

The plugin connects to an MCP server over a URL. Plugins can't call a Power Platform custom connector directly, so this repo includes a small hosted server with the same tools: [hosted-mcp](../../Azure%20Maps%20Route/hosted-mcp/README.md). Deploy it to Azure Container Apps, or host your own.

Authentication is Microsoft Entra OAuth. Connector authorization in `agentConnectors` has no Entra SSO option, so the plugin uses `OAuthPluginVault` with an Entra app registration you own, and users sign in once. API-key authorization isn't available in Cowork, and an Azure Maps key can't be sent in the `subscription-key` header anyway. The hosted server validates the user's Entra token and calls Azure Maps with its own managed identity, so no Azure Maps key is stored or sent.

1. **Azure Maps account**: create one in the Azure portal with Microsoft Entra authentication enabled.
2. **Hosted server**: deploy [hosted-mcp](../../Azure%20Maps%20Route/hosted-mcp/README.md), which describes the Entra app registration and the **Azure Maps Data Reader** role it needs. Note its URL (ending in `/mcp`) and domain.
3. **Teams Developer Portal** ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) > Tools > OAuth client registration > Register client:
   - **Base URL**: the server URL, ending in `/mcp`.
   - **Restrict usage by org**: **My organization only**.
   - **Restrict usage by app**: **Any Teams app**. Don't bind it to an app ID.
   - **Client ID** and **Client secret**: from the Entra app registration.
   - **Authorization endpoint**: `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/authorize`
   - **Token endpoint** and **Refresh endpoint**: `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token`
   - **Scope**: `api://<client-id>/access_as_user offline_access`
   - **Enable PKCE**: leave enabled.
   - The redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` must be on the Entra app. Note the **OAuth client registration ID** the portal shows.
4. Copy `env/.env.dev.user.sample` to `env/.env.dev.user` and fill in `AZURE_MAPS_MCP_SERVER_URL`, `AZURE_MAPS_MCP_SERVER_DOMAIN` and `AZURE_MAPS_OAUTH_REFERENCE_ID`. Nothing secret is stored in the manifest; it only references these variables.

## Build and test

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

Bump `version` in `appPackage/manifest.json` before uploading a rebuilt package. After provisioning, ask your admin to review and assign the plugin in the Microsoft 365 admin center under **Agents > Tools**.

## Notes

- The tools take coordinates (longitude, then latitude) and don't look up addresses. Pair this plugin with a geocoding tool if you want to ask about addresses or place names.
- Route matrix is limited to 2,500 origin and destination pairs per call; matrices over 100 pairs have extra restrictions.
- Route range supports driving and truck, not walking.
- The map tools (`get_route_map`, `get_static_map`) show the map in a Fluent 2 widget in clients that support MCP Apps, such as Cowork. The widget gets the image from the server through an app-only `render_map_image` tool, so no image link is exposed. In other clients the agent gets the distance, time and a Bing Maps link, with no image.
- Replace the default icons in `appPackage` before publishing if you want your own branding.
