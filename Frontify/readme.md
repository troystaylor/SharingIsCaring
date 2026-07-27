# Frontify

A Power Platform **custom MCP connector** for the [Frontify](https://www.frontify.com) GraphQL API. It is dual-mode: typed, REST-style operations for Power Automate (simple JSON in, clean JSON out) **and** an `/mcp` endpoint that exposes Frontify tools to Copilot Studio agents. The connector builds every GraphQL request for you — no GraphQL knowledge required.

Built on the [GraphQL Bridge](../Connector-Code/GraphQL%20Bridge) template, which handles the JSON → GraphQL → JSON translation: it builds the `{ query, variables }` envelope, converts GraphQL errors (returned as HTTP 200) into real HTTP failures, and unwraps the `data` root so response schemas map cleanly. The MCP endpoint reuses the same document library, so both surfaces stay in sync.

## Operations

| Operation | Description |
| --- | --- |
| Get account | Current Frontify account details |
| List brands | All brands available to the account |
| Get brand | A single brand by ID |
| List brand libraries | Paged libraries belonging to a brand |
| Get library | A single library by ID |
| List library assets | Paged assets in a library |
| Search library assets | Search a library's assets with an AssetQueryInput filter |
| List library collections | Paged collections within a library |
| List library metadata properties | Custom metadata properties defined on a library |
| Browse library | Root folders and assets of a library |
| List asset comments | Paged comments on an asset |
| List related assets | Paged assets related to an asset |
| List asset revisions | Paged revisions of an asset |
| List account users | Users in the account |
| List account user groups | User groups in the account |
| Get asset | A single asset by ID (title, description, status, creator, timestamps) |
| Get asset download | An asset's download URL, preview URL, and file details |
| Get current user | The user the API token belongs to |
| Upload asset | Upload a file and create an asset in one step — `uploadFile` → chunked upload → `createAsset` (`basic:write`) |
| Replace asset | Upload a new file and replace an existing asset's file (`basic:write`) |
| Update asset | Update an asset's title, description, and other fields (`basic:write`) |
| Add asset tags | Add one or more tags to an asset (`basic:write`) |
| Add collection assets | Add assets to a collection (`basic:write`) |
| Add custom metadata | Add custom metadata values to assets or projects (`basic:write`) |
| Delete asset | Delete an asset by ID |
| Run query | Run any Frontify GraphQL query or mutation |
| Invoke Frontify MCP | Model Context Protocol endpoint for Copilot Studio |

## Setup

### 1. Set your Frontify instance

The Frontify GraphQL endpoint is per-instance: `https://{domain}.frontify.com/graphql`. Because Power Platform requires a fixed host, the domain is baked into the script.

- In `script.csx`, replace `[[REPLACE_WITH_FRONTIFY_DOMAIN]]` with your instance subdomain. For `https://demo.frontify.com` that is `demo`.
- Optionally update the `host` in `apiDefinition.swagger.json` to match (the script overrides the request target at runtime, so this is cosmetic).

### 2. Get an API token

In your Frontify instance, go to **Settings → API tokens** (or **Developer → API tokens**) and create a token with the scopes your operations need (for example `basic:read`, `basic:write`). When you create a connection, paste **just the token value** — the connector adds the `Bearer ` prefix and the required `X-Frontify-Beta: enabled` header automatically.

### 3. Deploy

Deploy with the [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction):

```powershell
pac connector create `
  --api-definition-file "apiDefinition.swagger.json" `
  --api-properties-file "apiProperties.json" `
  --script-file "script.csx"
```

## How it works

Each typed operation maps to a GraphQL document registered in `script.csx` (`GraphQlDocuments`). The connector body is passed straight through as GraphQL **variables**, and the response is unwrapped to the relevant object via `ResponseRootPaths` (for example, `List brand libraries` returns `data.brand.libraries` directly).

Example — **Get brand** request body:

```json
{ "id": "1234" }
```

The script sends:

```graphql
query GetBrand($id: ID!) {
  brand(id: $id) { id name slug avatar }
}
```

…and returns just the brand object:

```json
{ "id": "1234", "name": "Zava", "slug": "zava", "avatar": "https://..." }
```

## MCP (Copilot Studio)

The `/mcp` operation (`Invoke Frontify MCP`) implements the Model Context Protocol so Copilot Studio agents can call Frontify conversationally. The script embeds the full **MCP framework** (`McpRequestHandler`, spec 2025-11-25) — the same one used by the [Power MCP Template](../Connector-Code/Power%20MCP%20Template) — covering `initialize`, `ping`, `tools/*`, `resources/*`, `prompts/*`, `completion/complete`, `logging/setLevel`, and all notifications. Frontify tools are registered with the fluent `AddTool` / `McpSchemaBuilder` API, and each tool reuses the same GraphQL documents as the typed operations.

Exposed tools: `list_brands`, `get_brand`, `list_brand_libraries`, `list_library_assets`, `search_library_assets`, `get_asset`, `get_asset_download`, `add_asset_tags`, and `upload_asset`.

To add tools, resources, or prompts, edit `RegisterMcpTools` in `script.csx` (or call `AddResource` / `AddPrompt` on the handler). To use the connector, add it as a tool in Copilot Studio — the agent discovers everything via `tools/list`.

## Extending it

To add an operation:

1. Add the GraphQL document to `GraphQlDocuments` in `script.csx`, keyed by a new `OperationId`.
2. Add an unwrap path to `ResponseRootPaths` if you want a nested object returned.
3. Add the matching path, request schema, and response schema in `apiDefinition.swagger.json`.
4. Add the `OperationId` to `scriptOperations` in `apiProperties.json`.

For quick or one-off needs, use **Run query** instead — send `{ "query": "...", "variables": { } }` and the raw `data` object comes back. Browse the full schema at the [Frontify GraphQL reference](https://frontify.github.io/graphql-reference/).

## Notes

- **Dynamic pickers**: Brand ID fields offer a dropdown backed by **List brands**. Library ID fields offer a cascading dropdown — pick the **Brand (for library picker)** first to populate the **Library ID** list (backed by **List brand libraries**). The `brandId` helper is picker-only and is never sent to Frontify.
- The GraphQL documents request a **conservative, interface-level set of fields** (e.g. `id`, `name`, `title`). The operations cover a broad surface (search, metadata, collections, upload/replace, tagging), but each returns a focused subset — extend the documents in `GraphQlDocuments` to pull the exact fields you need.
- `Delete asset` is a destructive mutation; it is marked as advanced in the connector.
- Uses `this.Context.SendAsync(...)` (never `new HttpClient()`) and fully qualifies `Newtonsoft.Json.Formatting` per Power Platform runtime requirements.
