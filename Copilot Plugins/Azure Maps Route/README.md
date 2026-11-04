# Azure Maps Route plugin

Copilot plugin for the Azure Maps Route tools. It lets Microsoft 365 Copilot plan routes, compare travel times, find reachable areas and snap GPS points to roads. Every result is GeoJSON.

This plugin pairs with the [Azure Maps Route custom connector](../../Azure%20Maps%20Route/readme.md), which exposes the same tools through its `InvokeMCP` operation.

| Component | Path |
| --- | --- |
| Connector `azure-maps-route` (remote MCP, API key) | `appPackage/manifest.json` |
| Skills: Plan a Route, Compare Travel Times, Find a Service Area, Clean a GPS Trace | `appPackage/skills/*/SKILL.md` |

Tools used by the skills: `get_route_directions`, `get_route_matrix`, `get_route_range`, `snap_to_roads`.

## Setup

The plugin connects to an MCP server over a URL. Plugins can't call a Power Platform custom connector directly, so you need the Azure Maps Route tools hosted at an HTTPS MCP endpoint you control.

1. **Azure Maps account**: create one in the Azure portal and copy a key from **Authentication**.
2. **MCP server URL**: host the Azure Maps Route MCP server and note its URL and domain. The URL value is not set up for you.
3. **Teams Developer Portal** ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) > Tools > API key registration: register your Azure Maps key and note the registration ID.
4. Copy `env/.env.dev.user.sample` to `env/.env.dev.user` and fill in `AZURE_MAPS_MCP_SERVER_URL`, `AZURE_MAPS_MCP_SERVER_DOMAIN` and `AZURE_MAPS_API_KEY_REFERENCE_ID`. Nothing secret is stored in the manifest; it only references these variables.

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
- Replace the default icons in `appPackage` before publishing if you want your own branding.
