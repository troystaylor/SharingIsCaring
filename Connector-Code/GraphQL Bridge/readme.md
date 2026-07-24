# GraphQL Bridge

A reusable `script.csx` template that turns a REST-style Power Platform custom connector operation into a **JSON → GraphQL → JSON** bridge. Callers send flat JSON, the script builds the GraphQL `{ query, variables }` envelope, POSTs it to the GraphQL endpoint, and returns clean JSON — no GraphQL knowledge required in the connector operations, Power Automate, or Copilot Studio.

## Why this is needed

GraphQL and Power Platform's Swagger model disagree in three ways this script reconciles:

| GraphQL behavior | Problem in Power Platform | What the script does |
| --- | --- | --- |
| Single endpoint, `POST { query, variables }` | Operations are modeled per path/verb | Builds the envelope from a flat body and retargets the request at the GraphQL path |
| Returns **HTTP 200 even on failure** (`errors` array) | Power Automate sees a false success | Inspects `errors` and returns a real **HTTP 400** with the messages |
| Nests results under `data` (and deeper) | Response schemas map poorly to `data.brand.assets` | Unwraps `data` (and an optional per-operation sub-path) |

## How it works

Two modes, chosen per `OperationId`:

### Mode A — Server-defined document (recommended)

Register the GraphQL document in `GraphQlDocuments`, keyed by `OperationId`. The connector body is passed straight through as GraphQL **variables**, so the operation looks like a normal typed REST call.

```csharp
private static readonly Dictionary<string, string> GraphQlDocuments = new(...)
{
    ["GetAssetById"] = @"query GetAssetById($id: ID!) {
        asset(id: $id) { id title status createdAt }
    }",
};

// Optional: return exactly the nested object the Swagger schema expects
private static readonly Dictionary<string, string> ResponseRootPaths = new(...)
{
    ["GetAssetById"] = "asset",   // returns data.asset instead of the whole data object
};
```

Connector body the caller sends:

```json
{ "id": "abc123" }
```

### Mode B — Passthrough

If no document is registered for the `OperationId`, the body must carry the query itself. Wire this to a generic `RunQuery` operation for ad-hoc queries.

```json
{
  "query": "query ($id: ID!) { asset(id: $id) { id title } }",
  "variables": { "id": "abc123" }
}
```

Reserved body keys (`query`, `variables`, `operationName`) are always honored; everything else becomes variables in Mode A.

## Configuration

| Setting | Purpose |
| --- | --- |
| `GraphQlPath` | Path appended to the connector host (e.g. `/graphql`) |
| `GraphQlDocuments` | `OperationId` → GraphQL document (Mode A) |
| `ResponseRootPaths` | `OperationId` → dot-path into `data` to unwrap (e.g. `customer.orders`) |
| `PassthroughOperationId` | OperationId reserved for the generic query runner (informational) |

Auth is untouched — the incoming request already carries the connector's `Authorization` header; the script only changes method, URI path, and content.

## Error shapes returned

- Invalid request JSON → `400 INVALID_JSON`
- No query available → `400 NO_QUERY`
- GraphQL `errors` array present → `400 GRAPHQL_ERROR` (with `errors` and any `partialData`)
- Non-2xx from the GraphQL server → original status, wrapped as `HTTP_<code>`
- Non-JSON GraphQL response → `502 INVALID_GRAPHQL_RESPONSE`

## Using it for a real API

Most GraphQL APIs expose a single endpoint and use a bearer token:

- **Host:** the API's host (e.g. `api.example.com`)
- **Path:** `/graphql` (the default `GraphQlPath` — change it if the API differs)
- **Auth:** OAuth 2.0 or an API/personal access token → configure as the connector's security definition; the token flows through automatically.

Steps:

1. Point the connector host at the API and set `GraphQlPath` to its GraphQL endpoint path.
2. Add one entry to `GraphQlDocuments` per typed operation you want to expose (queries and mutations).
3. Optionally set `ResponseRootPaths` so each operation returns the exact nested object.
4. Add a generic `RunQuery` operation for anything not yet modeled (Mode B).

> Because it implements schema-shaped typed operations (not a raw GraphQL passthrough), pair each build with full Swagger response schemas for IntelliSense and dynamic value support.

> Per-tenant hosts: if the GraphQL host varies per customer (e.g. `{tenant}.example.com`), Power Platform's Swagger host is static — bake the host as a replaceable constant in the script and set the request URI there instead of using `GraphQlPath` alone.

## Conventions

- Uses `this.Context.SendAsync(...)` — never `new HttpClient()` (blocked by the runtime).
- Fully qualifies `Newtonsoft.Json.Formatting` to avoid the `System.Xml` ambiguity.
- No connection parameters required; endpoint path is a compile-time constant.
