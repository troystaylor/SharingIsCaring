# Azure Maps plugin

Copilot plugin for the [Azure Maps MCP server](../../Container%20Apps/Azure%20Maps%20MCP%20Server/README.md). It lets Microsoft 365 Copilot find places and addresses, plan routes, compare travel times, show reachable areas, find the best time to leave, check the weather and traffic, plan across time zones, geocode address lists and track storms. Many answers appear as interactive apps (a map, route planner, weather dashboard and more) in clients that support MCP Apps.

The plugin contains no IDs, secrets or server addresses. You supply your own through placeholders, so you can share this folder as is.

| Component | Path |
| --- | --- |
| Connector `azure-maps` (remote MCP, Microsoft Entra OAuth) | `appPackage/manifest.json` |
| 14 skills | `appPackage/skills/*/SKILL.md` |

| Skill | Tools it uses |
| --- | --- |
| Find Places | `search_poi`, `search_fuzzy`, `search_poi_category`, `search_nearby`, `geocode`, `reverse_geocode`, `get_poi_categories`, and more |
| Plan a Route | `get_route_directions`, `get_route_directions_batch`, `get_route_directions_advanced` |
| Best Time to Leave | `compare_departure_times` |
| Compare Travel Times | `get_route_matrix`, `start_route_matrix_async`, `get_route_operation` |
| Find a Service Area | `get_route_range`, `get_route_range_batch`, `get_reachable_range_advanced` |
| Find Stops Along a Route | `search_along_route` |
| Check the Weather | `get_weather` and the specific forecast, air quality and history tools |
| Weather on the Road | `get_weather_along_route` |
| Check Traffic | `get_traffic_incidents`, `get_traffic_flow_segment` |
| Plan Across Time Zones | `get_timezone` |
| Geocode an Address List | `batch_geocode`, `batch_reverse_geocode` |
| Track a Storm | `get_tropical_storms` |
| Show a Map | `show_map`, `show_weather_radar`, `get_boundary`, `get_static_map_image` |
| Clean a GPS Trace | `snap_to_roads`, `snap_to_roads_batch` |

## Prerequisites

- Node.js and [Work IQ Dev Tools (WIQD)](https://aka.ms/wiqd/docs): `wiqd --version` should work.
- A Microsoft 365 tenant where you can upload custom plugins.
- The hosted Azure Maps MCP server, deployed to your own Azure subscription.
- A Microsoft Entra app registration for it, and an OAuth client registration in the Teams Developer Portal.

## Setup

1. **Deploy the server.** Follow the [server README](../../Container%20Apps/Azure%20Maps%20MCP%20Server/README.md). It covers the Azure Maps account, the Entra app registration (with an `access_as_user` scope) and the **Azure Maps Data Reader** role. Note the server's URL, which ends in `/mcp`, and its domain.
2. **Register an OAuth client** in the Teams Developer Portal ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) under Tools > OAuth client registration > Register client:
   - **Base URL**: your server URL, ending in `/mcp`.
   - **Restrict usage by org**: **My organization only**.
   - **Restrict usage by app**: **Any Teams app**.
   - **Client ID** and **Client secret**: from your Entra app registration. Add the redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` to that app.
   - **Authorization endpoint**: `https://login.microsoftonline.com/<your-tenant-id>/oauth2/v2.0/authorize`
   - **Token endpoint** and **Refresh endpoint**: `https://login.microsoftonline.com/<your-tenant-id>/oauth2/v2.0/token`
   - **Scope**: `api://<your-client-id>/access_as_user offline_access`
   - **Enable PKCE**: leave enabled.
   - Note the **OAuth client registration ID** the portal shows.
3. **Create your environment files.** From this folder:

   ```powershell
   Copy-Item env\.env.dev.sample env\.env.dev
   Copy-Item env\.env.dev.user.sample env\.env.dev.user
   ```

   Edit `env/.env.dev.user` and replace the three placeholders: `AZURE_MAPS_MCP_SERVER_URL`, `AZURE_MAPS_MCP_SERVER_DOMAIN` and `AZURE_MAPS_OAUTH_REFERENCE_ID`. Both files are gitignored. The manifest only references these variables.

## Build and share

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

Bump `version` in `appPackage/manifest.json` before uploading a rebuilt package. After provisioning, ask your admin to review and assign the plugin in the Microsoft 365 admin center under **Agents > Tools**.

## Sharing this folder

Share the folder without `env/.env.dev`, `env/.env.dev.user` and `appPackage/build/`. The `.gitignore` already excludes them. Whoever receives it follows Setup with their own server and tenant.

## Notes

- Tools accept addresses and place names as well as coordinates, so users can ask in plain language.
- The apps need a client that supports MCP Apps, such as Cowork. In other clients each tool still returns a full text answer.
- Replace `appPackage/color.png` and `appPackage/outline.png` with your own icons before publishing if you want your own branding.
- The server calls Azure Maps with its own managed identity, so no Azure Maps key is stored in the plugin or sent by users.
