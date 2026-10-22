# Salesforce MCP Server

An [MCP](https://modelcontextprotocol.io/) server for Salesforce that you host yourself on **Azure Container Apps**. It calls the **Salesforce REST API** directly. It does not use the Salesforce-hosted MCP servers, so you control the tools, the data returned, and where the server runs.

**Each person signs in to Salesforce as themselves.** The server holds no Salesforce credential and no shared service account. It uses the caller's own access token, so Salesforce's sharing rules and field-level security decide what each person sees and can change, and Salesforce's history records who did it.

Started from the [MCP Container App Template](../MCP%20Container%20App%20Template/readme.md).

## What it does

**Data**

| Tool | What it's for | Changes data |
|------|---------------|:------------:|
| `soql_query` | Run a SOQL query. Records come back with a `recordUrl` that opens them in Salesforce | No |
| `query_more` | Fetch the next batch of a query that returned `done: false` | No |
| `sosl_search` | Full-text search across several objects at once | No |
| `get_record` | Read one record by ID, optionally limited to named fields | No |
| `get_limits` | Show API limits and how much remains | No |
| `get_connection_info` | Show the org, sandbox status, and the Salesforce user the request runs as | No |
| `create_record` | Create a record | Yes |
| `update_record` | Change fields on a record | Yes |
| `delete_record` | Delete a record | Yes |

**Schema**

| Tool | What it's for |
|------|---------------|
| `list_objects` | Find an object's API name, with a name filter |
| `describe_object` | List an object's fields, types, picklist values, and lookups |
| `get_object_info` | The bare `/sobjects/{Object}` resource: basic properties and the user's recently viewed records |
| `get_record_types` | An object's record types, with IDs, and which is the default |
| `get_page_layout` | The sections and fields on a layout, which are required or read-only, and the related lists |
| `get_picklist_values` | A picklist's allowed values, narrowed by record type, with the controlling values for a dependent picklist |
| `get_schema_snapshot` | A compact snapshot of an object's fields and a fingerprint of them |
| `compare_schema` | What was added, removed, or changed since a snapshot you took earlier |
| `compare_api_versions` | The same object's fields as two API versions describe them |

**Versions**

| Tool | What it's for |
|------|---------------|
| `list_api_versions` | The API versions the org supports, the server default, and the latest |

**Setup metadata** (Tooling API)

| Tool | What it's for |
|------|---------------|
| `get_object_metadata` | An object's label, counts, and the description an admin wrote in Setup |
| `list_custom_fields` | Fields with their descriptions and inline help text |
| `get_field_metadata` | One field's description, help text, and for a custom field its formula, default value, and constraints |
| `list_validation_rules` | An object's validation rules, with error messages and, on request, their formulas |

The three write tools are **off by default**. They are not registered, so a client cannot see or call them, until you set `Salesforce:EnableWriteTools` to `true`. The setup metadata tools are on by default and can be switched off with `Salesforce:EnableToolingTools`.

Responses are shaped for a model. Salesforce's REST plumbing is stripped out, each record gets a `recordUrl` to cite, and results are capped (200 records and 100,000 characters per call by default). An empty result is returned as a success.

## How access works

The caller signs in to Salesforce with the OAuth 2.0 **authorization code flow** (with PKCE) and sends the resulting access token to the server as a bearer token. The server does two things with it:

1. **Checks it.** It asks Salesforce's `userinfo` endpoint whether the token is good and who it belongs to. A request with no token, or one Salesforce refuses, gets a `401` that points the client at how to sign in. A good result is trusted for 60 seconds (`Auth:ValidationCacheSeconds`), so a burst of tool calls costs one check.
2. **Uses it.** Every call to Salesforce carries that same token, so it runs as that person.

What this means in practice:

- **People only see their own data.** There is no run-as user to over-provision. A user who can't see a record in Salesforce can't see it here.
- **Nothing is stored.** The server keeps no token, no refresh token, and no Salesforce secret. Tokens are read from each request, held in memory for that request, and never logged. The check cache holds a hash of the token, not the token.
- **Revoking access works.** Revoking a token in Salesforce stops it within `Auth:ValidationCacheSeconds` (60 by default). Set it to `0` to check every call.
- **Tokens only work against your org.** The server checks and uses tokens against the single `Salesforce:InstanceUrl` you configured. A token from another org is refused by Salesforce.
- **One person's schema is never served to another.** Field-level security means two people can see different fields of the same object, so cached object descriptions are kept per user.
- **The server never receives your Salesforce consumer secret.** The client that signs people in holds it. For Copilot, that is the Teams OAuth registration.

The server accepts a token issued for Salesforce, not for itself. The MCP authorization specification discourages servers from accepting tokens that were not issued to them. This design contains that risk because the server only ever sends the token back to Salesforce, to the one org you configured, and never stores or logs it. If your security review requires the server to be its own OAuth resource, this design is not the right fit.

## Prerequisites

- An Azure subscription where you can create resources and role assignments
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and test locally
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.62 or later, signed in with `az login`
- A Salesforce org with [My Domain](https://help.salesforce.com/s/articleView?id=sf.domain_name_overview.htm) deployed, and permission to create an External Client App

## Setup

### 1. Create the Salesforce app

Salesforce [restricts creating new Connected Apps as of Spring '26](https://developer.salesforce.com/docs/platform/api-rest/guide/intro-oauth-and-connected-apps.html) and recommends External Client Apps. An existing Connected App works too.

1. In **Setup**, open **External Client App Manager** and create a new **External Client App**.
2. Turn on **OAuth Settings** and add the callback URL of the client that will sign people in. For Microsoft 365 Copilot that is `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`.
3. Add these scopes: **Manage user data via APIs (api)**, **Perform requests at any time (refresh_token, offline_access)**, and **Access the identity URL service (id, profile, email, address, phone)** or **OpenID**. The server checks tokens with Salesforce's `userinfo` endpoint, which needs an identity scope.
4. Turn on the **Authorization Code and Credentials Flow**. Keep **PKCE** required.
5. In the app's policies, let users authorize it themselves (for example **All users may self-authorize**), and set the refresh token policy to stay valid until revoked.
6. Copy the **Consumer Key** and **Consumer Secret**. They go into the client that signs people in, not into this server.

Menu labels move between Salesforce releases. If you can't find one, search Setup for "External Client App".

Note your **My Domain URL** (for example `https://acme.my.salesforce.com`).

### 2. Deploy

```powershell
.\infra\deploy.ps1 `
    -ResourceGroup rg-sf-mcp `
    -SalesforceInstanceUrl https://acme.my.salesforce.com
```

There are no secrets to supply. Add `-EnableWriteTools` to register the create, update, and delete tools, `-DisableToolingTools` to leave out the setup metadata tools, `-SalesforceApiVersion v52.0` to change the default API version, and `-SchemaCacheSeconds` to change how long descriptions are cached. `-AppName` is 3 to 12 characters and drives resource names and the public hostname, so choose it once. The script builds the image in Azure Container Registry, so you don't need Docker.

The script prints your MCP endpoint, `https://<fqdn>/mcp`.

### 3. Check it

```powershell
curl https://<fqdn>/healthz        # 200. Never depends on Salesforce
curl https://<fqdn>/ready          # 200 "Healthy", or "Degraded" if Salesforce is unreachable
curl -i -X POST https://<fqdn>/mcp # 401 with a WWW-Authenticate header
```

The server can't call Salesforce on its own, so there is no readiness test of sign-in. Sign in from your client and call `get_connection_info`. It confirms the org and the person the request runs as.

## Connect a client

**Microsoft 365 Copilot.** The [plugin](./plugin/README.md) packages this server as a connector with five skills. Its OAuth registration in the Teams developer portal points at your Salesforce org, with the Consumer Key and Secret from step 1.

**Copilot Studio.** The `connector/` folder is a Power Platform custom connector that signs each person in to Salesforce and passes MCP traffic to the server. Replace each `[YOUR_...]` value in `connector/apiDefinition.swagger.json` and `connector/apiProperties.json`, then:

```powershell
pac connector create --api-definition-file connector/apiDefinition.swagger.json `
                     --api-properties-file connector/apiProperties.json
```

Put your Consumer Secret in a temporary copy of `apiProperties.json` for the deploy, or set it in the connector's **Security** tab, and don't save it in this folder. After the connector is created, the platform generates its redirect URL. Read it from the Security tab and add it as a callback URL on your Salesforce External Client App.

**Other MCP clients.** Obtain a Salesforce access token with the authorization code flow and send it as a bearer token to `https://<fqdn>/mcp`. The server publishes [protected resource metadata](https://datatracker.ietf.org/doc/html/rfc9728) naming your Salesforce org as the authorization server. Salesforce doesn't offer dynamic client registration, so the client needs your Consumer Key configured in advance.

## Configuration

Set these as environment variables with `__` for nesting, for example `Salesforce__InstanceUrl`. The Bicep template already does this.

| Setting | Default | Purpose |
|---------|---------|---------|
| `Salesforce:InstanceUrl` | | Your My Domain URL. Must be `https` with no path. Tokens are checked against and used with this org only |
| `Salesforce:ApiVersion` | `v66.0` | Default REST API version. Any tool call can override it with `apiVersion` |
| `Salesforce:EnableWriteTools` | `false` | Registers `create_record`, `update_record`, `delete_record` |
| `Salesforce:EnableToolingTools` | `true` | Registers the setup metadata tools: `get_object_metadata`, `list_custom_fields`, `get_field_metadata`, `list_validation_rules` |
| `Salesforce:SchemaCacheSeconds` | `300` | How long an object description is served from memory before it is revalidated. `0` revalidates every call |
| `Salesforce:SchemaCacheMaxEntries` | `200` | Most object descriptions held in memory per replica, across all users |
| `Salesforce:MaxRecords` | `200` | Most records one call returns |
| `Salesforce:MaxResponseChars` | `100000` | Most record characters one call returns |
| `Auth:Enabled` | `true` | Check each caller's Salesforce token at the door. When off, tools can be listed without a token. Salesforce still checks every token on every call |
| `Auth:ValidationCacheSeconds` | `60` | How long a token Salesforce accepted is trusted without asking again. `0` checks every call |
| `Auth:ResourceUri` | inferred | Overrides the resource identifier in protected resource metadata |
| `RateLimit:PermitsPerMinute` | `120` | Per-person limit. `0` turns it off |

See the [template readme](../MCP%20Container%20App%20Template/readme.md#configuration) for the infrastructure parameters (replicas, IP restrictions, CORS).

## API versions

The server uses one default API version, `Salesforce:ApiVersion` (`v66.0` out of the box), and every data and schema tool accepts an optional `apiVersion` to override it for a single call. `v52.0`, `52.0`, `v52` and `52` all mean the same thing.

- An override is checked against the versions the org reports, so a retired or mistyped version fails with a message that names the supported range instead of a bare 404 from Salesforce. The org's version list is cached for an hour and shared by everyone, because it is the same for everyone. If it can't be fetched, the version is passed through and Salesforce decides.
- Descriptions differ by version. A field added in a later release isn't visible under an earlier version. `compare_api_versions` shows exactly which fields appear, disappear, or change between two versions, which is the quickest way to see what moving an integration forward would expose.
- `describe_object` and the other schema tools report the `apiVersion` they used.
- `query_more` follows the version already inside the `nextRecordsUrl` it is given.
- `delete_record` and `get_connection_info` always use the server default.
- `list_api_versions` reports whether the default is still supported. Run it after a Salesforce release, or when a sandbox is on a preview release.

## Schema caching

Object descriptions are large and rarely change, so the server keeps them in memory:

1. A description is served from memory for `Salesforce:SchemaCacheSeconds` (300 by default).
2. After that, the server asks Salesforce whether it changed, using `If-Modified-Since`. If not, Salesforce answers `304 Not Modified` with no payload, and the stored copy is used for another window.
3. If it did change, the new description replaces the old one.

Each schema result includes `schema.source` (`cache`, `revalidated`, or `fetched`) and `schema.retrievedAt`, so a caller can judge how current it is. Set `SchemaCacheSeconds` to `0` to revalidate on every call.

**Descriptions are cached per person.** Field-level security means two people can see different fields of the same object, and serving one person's description to another would reveal fields they aren't allowed to see. The cache key is the Salesforce user and the API version, so people never share an entry. Up to `Salesforce:SchemaCacheMaxEntries` (200) are held per replica across all users, oldest dropped first. Each replica has its own cache, and nothing is shared between replicas or kept across a restart.

## Detecting schema change

`get_schema_snapshot` returns one short signature per field (type and size, whether it is required or writable, what a lookup points to, a hash of its active picklist values) and a fingerprint of the whole set. Labels and help text are left out on purpose, because a relabel doesn't break an integration and would hide the changes that do.

The server doesn't store snapshots. Replicas share no storage, and a baseline held in one replica's memory would disappear on restart or scale-in. Keep the snapshot yourself, then pass its `fields` (and `fingerprint`) to `compare_schema` later. For a change between API versions, use `compare_api_versions`, which needs no stored baseline.

A snapshot reflects what **you** can see. Another person with different field-level security may get a different one.

## Setup metadata

The setup metadata tools use the Salesforce Tooling API, which has its own permissions. The signed-in user usually needs **View Setup and Configuration**, and reading a formula may need more. When a lookup is refused, the tool returns the basic result with a note that includes Salesforce's reason, not a failure.

- Every query is fixed. Only validated object names, field names, and IDs are placed into them.
- There is deliberately no tool that accepts a free-form Tooling API query. That API also exposes Apex source and other setup that these tools have no reason to reach.
- Descriptions exist only if an admin wrote one. `get_object_metadata` returns a description for custom objects only.
- Formulas are available for custom fields. `list_validation_rules` returns formulas only when asked, because each one costs a separate API call (at most 25 per request).

## Run it locally

```powershell
$env:Salesforce__InstanceUrl = "https://acme.my.salesforce.com"
dotnet run --project src/SalesforceMcp --environment Development
```

In `Development`, `Auth:Enabled` is `false`, so you can list tools at `http://localhost:5000/mcp` (the port is printed at startup) with [MCP Inspector](https://github.com/modelcontextprotocol/inspector) without a token. To call a tool, send your own Salesforce access token in the `Authorization: Bearer ...` header; the server passes it straight to Salesforce. Point it at a sandbox.

```powershell
dotnet test SalesforceMcp.slnx
```

## Security notes

- Run it against a **sandbox first**, and keep write tools off until you have watched how your agent uses the read tools.
- **Changes are attributed to the person who made them.** Salesforce's history shows the signed-in user, not a service account.
- The server never logs or stores a token. HTTP client logging is set to `Warning` so Salesforce request URLs, which carry query text, stay out of the logs. Application Insights dependency telemetry records request URLs too; the OpenTelemetry HTTP instrumentation redacts query-string values by default, but confirm that in your own workspace if queries may contain sensitive text.
- Tool arguments come from a model, so each one is validated before use: object and field names against Salesforce's identifier shape, IDs against the 15 or 18 character format, `apiVersion` against the form `v52.0` and the versions the org supports, and the continuation URL for `query_more` against the exact path a query returns. A model cannot use them to request an arbitrary Salesforce path.
- Only `SELECT` queries and `FIND` searches are accepted by the query tools, but the real limit is the signed-in user's permissions. Rely on those, not on the tool filter.
- The setup metadata tools return formulas, validation rules, and field descriptions, and every caller who has the Salesforce permission can read them. Switch the tools off with `Salesforce:EnableToolingTools` if that logic shouldn't be visible.
- Failed writes are not retried automatically, so a transient error cannot create a duplicate record.
- **Rate limits are per person.** A caller who is not signed in is limited by IP address.
- If Salesforce is down while a token is being checked, the request is refused rather than let through.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| `401` from the server | The request has no Salesforce token, or Salesforce refused it. The client has to sign in again |
| `401` even though the person signed in | The token lacks an identity scope. The server checks tokens with Salesforce's `userinfo` endpoint, which needs one. Add the `openid` (or `id`) scope to the client's sign-in and the External Client App |
| `This request carried no Salesforce sign-in` | A tool was called without a token. Sign in to Salesforce for this connection |
| `Salesforce rejected this sign-in. It has expired or been revoked` | The token expired or was revoked after it was checked. Sign in again. Copilot refreshes tokens automatically when `refresh_token` is granted |
| Sign-in fails in Salesforce with `redirect_uri_mismatch` | The client's callback URL isn't registered on the External Client App |
| Sign-in fails with `invalid_scope` or `OAUTH_APPROVAL_ERROR_GENERIC` | A requested scope isn't enabled on the app, or the app isn't approved for this user. Check the scopes and the policy that lets users self-authorize |
| `/ready` returns `Degraded` | Salesforce is unreachable from the server. Check `Salesforce:InstanceUrl` and Salesforce's status |
| `INVALID_FIELD` or `INVALID_TYPE` | A field or object name is wrong, or the signed-in user cannot see it. Call `describe_object` or `list_objects` |
| `INSUFFICIENT_ACCESS` or a record that "does not exist" | The signed-in user's permissions or sharing do not include the record. Call `get_connection_info` to see who the request runs as |
| `REQUEST_LIMIT_EXCEEDED` | Your org's API limit is spent. Call `get_limits` |
| `The org does not support API version vNN.0` | The version was retired, mistyped, or is newer than the org. Call `list_api_versions` |
| A field is missing under one API version | Descriptions differ by version. Call `compare_api_versions` to see which fields appear or disappear |
| A person can't see a field another person can | Field-level security differs between them. That is Salesforce working as designed |
| A schema result looks out of date | It may be served from memory. `schema.source` says so. Wait out `SchemaCacheSeconds`, or set it to `0` |
| `get_object_metadata`, `get_field_metadata`, or `list_validation_rules` returns a note instead of data | The Tooling API refused the lookup, usually for permissions. The note carries Salesforce's reason. Give the user View Setup and Configuration |
| `get_picklist_values` says there is no restriction for a record type | Salesforce lists no per-record-type values for that field, so every active value applies |
| A query returns fewer records than `totalSize` | The response hit the record or character cap. Narrow the `WHERE`, select fewer fields, or add a `LIMIT` |
| `429` | The person exceeded `RateLimit:PermitsPerMinute` |
| The write tools are missing | `Salesforce:EnableWriteTools` is `false`. Redeploy with `-EnableWriteTools` |
