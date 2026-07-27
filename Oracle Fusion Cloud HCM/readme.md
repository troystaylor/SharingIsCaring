# Oracle Fusion Cloud HCM

A dual-mode custom MCP connector for the [Oracle Fusion Cloud HCM REST API](https://docs.oracle.com/en/cloud/saas/human-resources/farws/rest-endpoints.html). It exposes:

- **Typed operations** for Power Automate — generic CRUD over any HCM resource, plus a curated **Search workers** action, all with IntelliSense.
- **An MCP endpoint** (`/mcp`) for Copilot Studio — the same capabilities as agent tools.

Because the Oracle Fusion HCM REST surface is uniform (every resource follows the same ADF REST conventions — `q`, `fields`, `expand`, `limit`, `onlyData`, and a standard `{ items, count, hasMore }` envelope), a small set of generic operations covers the **entire** API: workers, employees, jobs, positions, grades, departments, absences, payroll, and hundreds more.

The designer experience is **metadata-driven**: the **Resource** picker, **Fields** / **Expand** pickers, and the **Create/Update body form** are all populated live from your pod's own `/describe` metadata, so makers get real dropdowns and a typed body form for whatever resource they pick — without the connector hard-coding hundreds of schemas.

## Capabilities

### Typed operations (Power Automate)

| Operation | Method | Path |
|-----------|--------|------|
| Query records | GET | `/{resource}` |
| Get record | GET | `/{resource}/{id}` |
| Create record | POST | `/{resource}` |
| Update record | PATCH | `/{resource}/{id}` |
| Delete record | DELETE | `/{resource}/{id}` |
| Describe resource | GET | `/{resource}/describe` |
| Search workers | GET | `/workers` |

All paths are relative to `/hcmRestApi/resources/11.13.18.05`.

### MCP tools (Copilot Studio)

| Tool | Purpose |
|------|---------|
| `list_common_resources` | Lists frequently used HCM resource names. |
| `describe_resource` | Returns a resource's fields, actions, and child resources. |
| `query_records` | Queries a resource with `q` / `fields` / `expand` / `orderBy` / `limit`. |
| `get_record` | Retrieves a single record by key. |
| `create_record` | Creates a record from a JSON object. |
| `update_record` | Partially updates (PATCH) a record. |
| `delete_record` | Deletes a record. |
| `search_workers` | Free-text or RSQL search over `workers`. |

## Prerequisites

1. **An Oracle Fusion Cloud HCM pod** and a user account with REST access and the appropriate HCM data security roles.
2. **An OAuth 2.0 confidential application** registered in your identity provider (Oracle IDCS or OCI IAM) that is authorized to call the Fusion Applications resource:
   - Allowed grant types: **Authorization Code** and **Refresh Token**.
   - Redirect URL: `https://global.consent.azure-apis.com/redirect` (Power Platform global redirect).
   - Add the **Oracle Applications Cloud (Fusion apps)** resource as an allowed scope so the issued token can call `/hcmRestApi`.
   - Record the **Client ID** and **Client secret**.

## Configuration — replace the placeholders

Before deploying, replace every placeholder across the three files. They must be consistent.

| Placeholder | Found in | Replace with |
|-------------|----------|--------------|
| `replace-pod-host.oraclecloud.com` | `apiDefinition.swagger.json` (`host`), `apiProperties.json` (`scopes`) | Your Fusion pod host, e.g. `myserver.fa.us2.oraclecloud.com` |
| `POD_HOST` constant | `script.csx` | The same pod host (used by the MCP tools) |
| `replace-idcs-host.identity.oraclecloud.com` | `apiDefinition.swagger.json`, `apiProperties.json` | Your IDCS / OCI IAM host |
| `REPLACE_WITH_IDCS_CLIENT_ID` | `apiProperties.json` | Your confidential app's Client ID |
| OAuth **scope** | `apiProperties.json` (`scopes`) | The scope your app requires (see below) |

### OAuth scope

The default scope is the common Fusion pattern:

```
https://<pod-host>:443/urn:opc:resource:consumer::all offline_access
```

`offline_access` is required so Power Platform can refresh the token. Confirm the exact primary audience/scope on your registered application — some tenants expose it as an `urn:opc:...` audience shown on the app's **Configuration → Resources** page. The `token` endpoints for IDCS are `/oauth2/v1/authorize` and `/oauth2/v1/token`; OCI IAM Identity Domains use the domain URL with the same paths.

## Deployment (PAC CLI)

> OAuth `connectionParameters` combined with `scriptOperations` can fail on a single `pac connector create`. Deploy in two steps: push the swagger, script, and script operations first, then configure OAuth on the portal **Security** tab.

```powershell
# 1. Create the connector with the definition, properties, and script
pac connector create `
  --api-definition-file "apiDefinition.swagger.json" `
  --api-properties-file "apiProperties.json" `
  --script-file "script.csx" `
  --environment c4f149b0-9f42-e8c4-97d8-bc69b59f971c

# 2. Open the connector in the maker portal → Security tab:
#    - Set the OAuth 2.0 Client ID and Client secret
#    - Confirm the Authorization URL, Token URL, Refresh URL, and Scope
#    - Save, then create a connection and sign in
```

To update later, use `pac connector update -id <connector-id>` with the same files.

## Usage examples

The fictional company **Zava** wants to look up and update worker data.

**Find a worker (Search workers):**
- Filter (q): `DisplayName LIKE '%Ramirez%'`
- Fields: `PersonId,PersonNumber,DisplayName`

**Query any resource (Query records):**
- Resource: `jobs`
- Filter (q): `Name LIKE 'Software%'`
- Limit: `50`

**Update a record (Update record):**
- Resource: `emps`
- Record Key: `00020000000EACED...` (the key from the record's self link)
- Body: `{ "AssignmentName": "Senior Engineer" }`

**Copilot Studio agent:** connect the MCP endpoint and ask *"Find Zava workers with the last name Ramirez and show their person numbers."* The agent calls `search_workers`, then `describe_resource` / `query_records` as needed.

## Notes

- **Filtering** uses the ADF query grammar (RSQL-like): `Attr='value'`, `Attr LIKE 'v%'`, `A=1 and B=2`. Quote string literals with single quotes.
- **Record keys** for `get`/`update`/`delete` are the primary key values Oracle returns in each record's `links[].href` (self link) — not always a plain numeric id.
- **Child resources** (e.g. a worker's assignments) can be reached with `expand` or by passing a nested resource path such as `workers/{key}/child/assignments` as the `resource` value.
- Not every resource supports every verb; use **Describe resource** to confirm available actions and required attributes.
- **Dynamic pickers** (Resource, Fields, Expand, and the Create/Update body form) apply to the **Power Automate / Power Apps designer** only. The **Fields** and **Expand** pickers select one attribute at a time — for multiple values, pick one and add the rest as a comma-separated list, or type a custom value. Copilot Studio agents use the MCP tools, which accept free-form JSON and are unaffected.
