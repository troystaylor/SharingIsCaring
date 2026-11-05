# Azure Maps Route hosted MCP server

A small Node.js MCP server that exposes the Azure Maps Route tools, and a map widget, over HTTPS. It exists for clients that connect to an MCP server by URL, such as a Microsoft 365 Copilot plugin. A Power Platform custom connector can't serve that role.

Callers sign in with Microsoft Entra. The server validates the user's access token, then calls Azure Maps with its own managed identity, so no Azure Maps key is stored or sent anywhere.

| Tool | Purpose |
| --- | --- |
| `get_route_directions` | Route between an origin and destination, as GeoJSON |
| `get_route_matrix` | Travel times and distances between origins and destinations |
| `get_route_range` | Area reachable within a time or distance budget |
| `snap_to_roads` | Match GPS points to roads |
| `get_route_map` | Directions shown on a map, with distance and time. Declares the map widget. |
| `get_static_map` | Map for a center point and zoom, or a bounding box. Declares the map widget. |
| `render_map_image` | App-only. The map widget calls it to get the image. Hidden from the agent. |

The endpoint is `POST /mcp`. `GET /health` returns `{"status":"ok"}`. Discovery (`initialize`, `tools/list`, `resources/list`, `resources/read`) is open so clients can read the tool descriptions and the widget. `tools/call` needs a valid access token and returns `401` with a `WWW-Authenticate` header otherwise.

A token is accepted only if it is signed by your tenant, issued by `login.microsoftonline.com/<tenant>/v2.0`, has your app registration as its audience, hasn't expired, comes from your tenant, and has the `access_as_user` scope.

## Prerequisites

- An Azure Maps account (Gen2).
- A Microsoft Entra app registration for the server:
  - Sign-in audience: your organization only.
  - Application ID URI `api://<client-id>`, and a delegated scope named `access_as_user`.
  - Access token version 2 (`requestedAccessTokenVersion: 2` in the manifest).
  - Redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, and a client secret, if the Teams Developer Portal will use this app as its OAuth client.

## Settings

| Setting | Value |
| --- | --- |
| `TENANT_ID` | Your Microsoft Entra tenant ID |
| `CLIENT_ID` | The app registration's client ID |
| `MAPS_CLIENT_ID` | The Azure Maps account's client ID (its unique ID), used as `x-ms-client-id` |

The container's system-assigned managed identity needs the **Azure Maps Data Reader** role on the Azure Maps account. The server stores no secrets.

## Map widget

`get_route_map` and `get_static_map` declare an MCP Apps widget (`ui://azure-maps-route/map.html`), which the server serves through `resources/read` with the MIME type `text/html;profile=mcp-app`. Hosts that support MCP Apps, such as Cowork, render it inline.

How the map reaches the user:
1. The tool validates the request and returns the distance and time, a Bing Maps link, and the map request in `structuredContent` (a route line simplified to at most 100 points, so it stays far under the 64 KiB widget limit). It doesn't render an image.
2. The widget calls the app-only `render_map_image` tool back through the host, which attaches the user's sign-in.
3. The server renders the image with its managed identity and returns it. The widget shows it as a `data:` URL.

So there is no public image link, no image storage and no signing secret, and the widget needs no network access. Its Content Security Policy declares no external domains.

In clients without MCP Apps support, the agent gets the text and the Bing Maps link. Set `embedImage` on a map tool to also return the image bytes, for a client that renders MCP image content but has no widget support.

The widget follows Fluent 2: the Segoe UI type ramp (14/20 body, semibold title), Fluent neutral and stroke tokens in light and dark themes (it follows the host's theme), a 4 px radius card with the Fluent shadow, a secondary button with the Fluent focus ring, a skeleton loader, and a message bar for errors. It also honors reduced motion and forced colors.

## Result size

Some clients drop large tool results, so results are kept small:
- Images default to a 640 by 400 JPEG, about 60 KB. A request for something larger is reduced in steps until it is under about 150 KB.
- `get_route_directions` returns distance and time by default. The route line (`routePath`), turn-by-turn instructions (`itinerary`) and extra routes (`maxRouteCount`) are opt-in, and if a result is still very large the route line coordinates are removed.

## Run locally

```powershell
$env:TENANT_ID = "<tenant-id>"
$env:CLIENT_ID = "<client-id>"
$env:MAPS_SUBSCRIPTION_KEY = "<azure maps key>"   # local only; in Azure the managed identity is used
node server.js                                     # listens on port 8080, or PORT
```

`MAPS_SUBSCRIPTION_KEY` is for local development. It is used only when no managed identity is available.

## Test

`npm test` starts a server on a random port and checks every tool over HTTP, including that bad tokens are rejected. It needs a real access token for the API:

```powershell
$env:TOKEN = az account get-access-token --scope "api://<client-id>/access_as_user" --query accessToken -o tsv
$env:BAD_AUD = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv   # optional: proves other audiences are rejected
npm test
```

To get a token with the Azure CLI, the CLI's client ID must be pre-authorized on the app registration. Remove that pre-authorization when you're done. Set `BASE_URL` to test a deployed server.

## Deploy to Azure Container Apps

```powershell
az acr create -n <registry> -g <resource-group> -l <region> --sku Basic
az acr build -r <registry> -t azure-maps-route-mcp:v7 .

az containerapp env create -n <environment> -g <resource-group> -l <region>
az containerapp create -n azure-maps-route-mcp -g <resource-group> --environment <environment> `
  --image <registry>.azurecr.io/azure-maps-route-mcp:v7 `
  --registry-server <registry>.azurecr.io --registry-identity system `
  --target-port 8080 --ingress external --min-replicas 1 --cpu 0.25 --memory 0.5Gi `
  --env-vars TENANT_ID=<tenant-id> CLIENT_ID=<client-id> MAPS_CLIENT_ID=<maps-client-id>

az role assignment create --assignee-object-id <app-identity-principal-id> --assignee-principal-type ServicePrincipal `
  --role "Azure Maps Data Reader" --scope <azure-maps-account-resource-id>
```

The app's URL is shown when the app is created. The MCP endpoint is that URL plus `/mcp`. After updating the image, the previous revision takes a minute or so to stop; wait for the new revision to receive all traffic before testing.

## Use with the plugin

Register an OAuth client in the Teams Developer Portal with the app registration's client ID and secret. The server's domain must be in the plugin's `validDomains`. See the [plugin README](../../Copilot%20Plugins/Azure%20Maps%20Route/README.md).

## Notes

- Anyone in your tenant who can sign in to the app and holds the `access_as_user` scope can call the server, and all calls use the server's Azure Maps identity. Restrict who can use the app registration (assignment required on its enterprise application) to limit that.
- Keep `--min-replicas 1` for responsive tool calls. At 0, the first call after an idle period waits for a cold start.
