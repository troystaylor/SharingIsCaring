# Power Platform Connectors for MCP in Copilot Plugins: What We Learned

How to give a Copilot plugin (Work IQ Dev Tools, `wiqd`) an MCP tool surface that is backed only by a Power Platform custom connector, instead of a hosted MCP server. The worked example is [Salesforce PP Plugin](./README.md), built on the [Salesforce MCP](../../Salesforce%20MCP/) connector and its `script.csx` `InvokeMCP` operation.

**Status of this document.** Provisioning, packaging, server-side deep validation, the Cowork connection, live reads and writes, and the operation names Cowork calls are confirmed. Still unconfirmed: behavior outside Cowork, and `UpdateRecord` in Cowork after its dynamic schema was removed. Each finding is marked **Confirmed**, **Observed** (seen, cause not proven) or **Untested**.

**Where this works today.** This pattern works in **Copilot Cowork**, and every result here was observed there. **Microsoft Learn does not document it for Microsoft 365 Copilot chat.** As of the Learn pages checked on 2026-10-29, the published `agentConnectors[].toolSource` schema lists only `remoteMcpServer` (manifest 1.27 to 1.30, plus `plugin` and `localMcpServer` in the preview schema only). It has no `openApiConnector`, and the Cowork plugin guide's validation rules say "exactly one of `plugin` or `remoteMcpServer`". The `openApiConnector` shape comes from Work IQ Dev Tools and is described there as devPreview-only. Learn also warns that a schema value does not guarantee support in a host, and that "support depends on the target experience".

An earlier version of this note said the pattern was "coming to Copilot", on the author's word. Nothing in Learn supports that, so treat it as unconfirmed. **What works in Copilot chat today, with a limitation** (confirmed by the author on 2026-10-29) is the documented route: a **declarative agent with an API plugin built from an OpenAPI file** (see "Other routes", route 2), authenticated with `OAuthPluginVault` and an OAuth client registered with PKCE. It signs in and runs, but because it bypasses the Power Platform connection, **token refresh is expected to fail** and users will be asked to sign in again (see route 2). What Learn also documents for Copilot chat is a **remote MCP server** connector with `OAuthPluginVault` authentication (the Cowork guide notes that omitting `authorization` works only in Cowork, not in Copilot Chat). Learn lists `ConnectionVault` (developer preview, "Power Platform connector-managed connection") as an authorization type on `remoteMcpServer`, but this tenant's Developer Portal rejected it there (route 3). The `openApiConnector` pointer used for Cowork remains untested in Copilot chat.

## The idea

A plugin normally points at a remote MCP server URL (`remoteMcpServer`). A plugin can instead point at an existing Power Platform custom connector (`openApiConnector`). The plugin stores only a pointer. The platform resolves the endpoint and operations at runtime, and the user signs in through a Power Platform connection.

| | Remote MCP connector | Power Platform connector |
|---|---|---|
| Manifest node | `toolSource.remoteMcpServer` | `toolSource.openApiConnector` |
| Needs a hosted MCP URL | Yes | No |
| Sign-in | Teams Developer Portal OAuth registration (`OAuthPluginVault`) | Power Platform connection (`ConnectionVault`) |
| Manifest version | 1.29 | `devPreview` only |
| Tool list | `tools/list` from the server, optionally `--tool-description` | Resolved by the platform from the connector |

## Build steps

1. **Upgrade the CLI.** `wiqd` 0.17.0 has no OpenAPI connector option. Use 0.21.0 or later: `npm install -g @microsoft/wiqd@latest`. Check `wiqd plugin add connector --help` for `--kind` and `--dev-preview`.
2. **Deploy the connector first.** The plugin cannot create it. See [Where to deploy](#where-to-deploy-the-connector).
3. **Scaffold and add the connector.**
   ```powershell
   wiqd plugin create --name "My Plugin" --output "My Plugin"   # nests a folder; flatten it
   wiqd plugin add connector --kind openapi --name "My Connector" `
       --description "<what it does>" --auth-reference-id "<connector API name>" --dev-preview
   ```
   `--url`, `--tool-description` and `--auth-type` are rejected for this kind.
4. **Replace the scaffold placeholders** in `appPackage/manifest.json` (developer name and URLs, descriptions, accent colour) and add skills under `appPackage/skills/`.
5. **Provision, package, deep-validate, share.**
   ```powershell
   wiqd plugin provision --env dev
   wiqd plugin package --env dev
   wiqd plugin validate --mode deep --env dev
   wiqd plugin share --scope users --email <address> --env dev
   ```

## The reference value

The `referenceId` in `authorization` is **the connector's API name, in the form `shared_<name>-<suffix>`**. It is not the Dataverse logical name shown by `pac connector list` (for example `new_my-20connector`).

- **Where to read it:** `pac connection list`, in the API Id column, the part after `/apis/`. Or the API name returned when the connector is created.
- **It changes per environment.** The same connector deployed twice has two API names. Keep it out of source control: use `${{MY_CONNECTOR_REFERENCE_ID}}` in the manifest and put the value in the gitignored `env/.env.<env>.user`, with a committed `.env.<env>.user.sample`.
- **Status: Observed.** A plugin built with the Dataverse logical name still provisioned and passed deep validation. The API name is what the Cowork connections gateway looked up, and the WIQD docs' own example is `shared_kusto`.

## Where to deploy the connector

**Cowork looks in its own Power Platform environment.** Confirmed by a browser HAR of the Connect click: Cowork calls its connections gateway for `/v1/connections/<api name>/parameter-schema` and `POST /v1/connections`. Both returned `{"detail":"Connector not found"}`. The gateway's environment was a separate Production environment named **Copilot Cowork**, with no custom connectors and no Dataverse. The connector lived only in the demo environment, so Cowork could not find it. The user-facing error was **"Could not verify connection. Try again."**

**Fix confirmed.** After deploying the connector to the Copilot Cowork environment with `paconn`, pointing the plugin's reference at the new API name, re-provisioning, and registering the new redirect URL on the OAuth app, Connect opened the OAuth dialog and finished with "You're connected". A connection with status Connected then existed in that environment. A connection in the demo environment did not carry over.

| Tool | Works in a Dataverse-less environment | Notes |
|---|---|---|
| `pac connector create` | No | Fails with "No Dataverse organization was found matching the specified criteria" |
| `paconn create` | Yes (**Confirmed** for a script-enabled connector) | `paconn create --env <id> --api-def ... --api-prop ... --script ... --icon ... --secret <client secret>` |

Practical `paconn` notes:

- `paconn login` is a device-code sign-in. When its output is redirected to a file, set `PYTHONUNBUFFERED=1`, or the code never appears.
- `--secret` puts the client secret on the command line. Read it from an environment variable, and delete any temp copies of `apiProperties.json` afterward.
- `paconn` names the connector `shared_<name>-<hash>` without the `new-5f` publisher prefix that `pac` adds, and the per-connector redirect URL follows the same pattern.
- **To find the environment a host uses:** capture a HAR of the failing Connect click and read the `environment_id` in the `GET /v1/connections` response body. Then look it up with the BAP admin API.

**Register the new redirect URL on the OAuth app.** Each environment produces a different `https://global.consent.azure-apim.net/redirect/<per-connector value>`. Read it back from the deployed connector (`properties.connectionParameters.token.oAuthSettings.redirectUrl`) and add it to the identity provider's allowed callbacks. Keep the earlier ones.

## What the platform did with the plugin

- **Static validation is vacuous.** For a skills-plus-connector plugin, `wiqd plugin validate` reports "No declarative-agent surface to validate" and passes. Package first, then run `validate --mode deep`. **Confirmed:** deep validation accepted the `openApiConnector` shape with 0 errors and 0 warnings.
- **Agent 365 catalog.** After the first provision (placeholder reference), the plugin appeared in `discoverMCPServers` as `OOB_<connector id>`, status Ready. After re-provisioning with a resolved API name it no longer appeared. **Observed.** One reading is that a resolved reference makes the platform treat it as an OpenAPI connector, not an MCP server. Not proven.
- **Gateway access.** `tools/list` against the plugin's `agents/servers/OOB_<id>` URL with an Agent Tools token returns `403 invalid_audience`, the same as other plugin connectors. You cannot enumerate the tools that way.
- **Admin center.** The plugin showed as **Available** under Agents > Tools with the connector and all skills listed. Its Users tab showed "The owner hasn't shared this tool" even after a successful `wiqd plugin share`. It still showed up in Cowork for the owner account.
- **End-to-end call: Confirmed.** In Cowork, asking for Salesforce accounts ran a "Hydrate connector" step, then "Checking the query options", then "Invoke connector operation", and returned real Account rows (name, industry, type, location, phone, record Id). The user's own words in the reasoning trace: the usable connection is on the Salesforce Plugin connector, and Copilot hydrates it first. So Cowork treats the plugin as a connector it hydrates and calls by operation, not as a server it lists MCP tools from.
- **Which operation Cowork invokes: Confirmed.** Cowork invokes the connector's **REST operation Id** directly, through its Power Platform tool provider. A create request produced `Tool provider: powerplatform`, `Connector id: shared_<api name>`, `Operation id: CreateRecord`, with flat parameters (`sObject`, `Name`). The script's MCP tool names (`create_record`) and `InvokeMCP` were not used. Consequences:
  - Write skills against operation Ids (`Query`, `GetRecord`, `CreateRecord`), not MCP tool names.
  - MCP-only tools in the script (anything with no REST operation, such as `send_case_email`) are unreachable. Rebuild them from REST operations in the skill.
  - Body fields are flattened into the parameter list, so a skill can just name the fields.
  - **Cowork gates writes itself.** A later create request showed an **"Approved 1 action"** step before the write ran, so the host asks for approval of write operations. The first test's trace showed no such step, so don't rely on it alone: keep a "no write without a yes" rule at the top of any skill that has write operations, and name each write operation in it.
  - **Operations with a dynamic-schema body failed in Cowork.** `CreateRecord` and `UpdateRecord` take a body whose fields come from `x-ms-dynamic-schema` (`GetObjectSchema`), so the swagger gives the body no `properties`. In Cowork, a `CreateRecord` call with flat parameters (`sObject`, `Name`) failed with "Couldn't complete 'Invoke connector operation'". Cowork then switched to `CompositeTree`, whose body has a fixed schema (`records[].attributes.type`), and that succeeded and returned the new record Id.
  - **The connector is not the problem: Confirmed.** Calling the same operations directly through the connection's runtime URL (token audience `https://apihub.azure.com`) worked as expected: `GetObjectSchema` returned the Account fields, and `CreateRecord` with a rejected body returned Salesforce's own validation error (`REQUIRED_FIELD_MISSING`), so the path, script and OAuth are fine.
  - **Probable cause (superseded):** an early theory was that a body with no declared properties gave Cowork nothing to place `Name` into. Declaring 206 properties did not help, and the confirmed cause is the dynamic schema (see the experiment result below). Salesforce's answers to wrong request shapes are still useful when debugging: no body returns `415 UNSUPPORTED_MEDIA_TYPE`, and a body that also contains `sObject` returns `INVALID_FIELD: No such column 'sObject'`.
  - **Fix applied to the deployed connector (static properties): deployed, but it did not fix `CreateRecord` in Cowork.** Declaring the union of createable fields from 11 common objects (206 properties) as static `properties` on the `CreateRecord` and `UpdateRecord` bodies, alongside the existing `x-ms-dynamic-schema`, was accepted and the runtime still behaved. A retest in a new Cowork chat still failed: two `CreateRecord` attempts returned "Couldn't complete 'Invoke connector operation'", and Cowork reported that Salesforce rejected the request "and didn't give a reason". So the missing-schema theory is not the (whole) cause, and Cowork does not surface the error body of a failed operation. Next step is to capture what Cowork actually sends (HAR of the failing step).
  - **What Cowork sends, from a HAR of the approval calls: Confirmed.** Writes go through a `POST /v1/tool-approval` request carrying the tool call (`tool_name: InvokeConnectorOperation`, `server_name: powerplatform`, and the `connector_id`, `connection_id`, `operation_id` and `params`). For one create request Cowork tried three calls in order, each needing its own approval:
    1. `CreateRecord` with flat params: `{"sObject":"Account","Name":"..."}`. Failed.
    2. `CreateRecord` with the body wrapped under the swagger parameter name: `{"sObject":"Account","body":{"Name":"..."}}`. Failed.
    3. `Composite` with flat params that match its declared body properties: `{"allOrNone":true,"compositeRequest":[{"method":"POST","url":"/services/data/v66.0/sobjects/Account","referenceId":"...","body":{"Name":"..."}}]}`. This is the shape that worked.
  - **Cowork's own diagnosis: Confirmed.** Asked to explain the failure, Cowork reported that its view of `CreateRecord` had only the path parameters (`connectionId`, `sObject`) and **no request body**, so the field values were never sent and Salesforce rejected an empty POST (`HTTP 400`, `executed:false`, no message). It said `UpdateRecord` had the same problem, and that operations with a static body (`Composite`, `CreateCase`) work. It also checked that the failed attempts left no partial record. That is stronger than the earlier theory: the deployed swagger did have a body with 206 declared properties, so the tool **drops the whole body parameter when it carries `x-ms-dynamic-schema`**, even with static `properties` beside it.
  - **Experiment result: Confirmed for `CreateRecord`.** Removing `x-ms-dynamic-schema` from the `CreateRecord` body (keeping the 206 static properties) on the Cowork environment copy of the connector fixed it. In a new chat, Cowork called `CreateRecord` with flat params (`sObject`, `Name`) and Salesforce returned `201` with the new Id. A read-only SOQL query through the connector found the record. Same definition, same connection, same script: the only change was dropping the dynamic schema. So **Cowork drops a body parameter that carries `x-ms-dynamic-schema`, even when static `properties` sit beside it**, and then sends an empty POST.
  - **Applied to `UpdateRecord` as well** (read back from the service: 206 static properties, no dynamic schema). Update behavior in Cowork: pending a test on a throwaway record.
  - **Trade-off.** The dynamic schema is what gives Power Automate and Power Apps their per-object fields in the designer. Without it they see only the declared properties. Keep the dynamic version as the shared source and deploy a static-body variant where the host is Cowork. If one definition must serve both, expect to lose one behavior.
  - **Rule for connectors used from Cowork.** Give every operation a fully static body schema. A dynamic-schema body, with or without static properties, is invisible to it. Operations already built that way (`Composite`, `CompositeTree`, `CreateCase`) work.
  - **The HAR does not contain tool results.** They stream over the conversation subscription, so the real error is only in the Cowork UI. Expand the failed step ("Show details") to read it.
  - **The skill's confirmation rule worked: Confirmed.** Before the write, Cowork showed the exact change ("Create → Account, Name = ..., no other fields"), noted a similar existing record, and asked "Go ahead?". It waited for the user's yes. Reads (the duplicate lookup) ran without a prompt.
  - **Rules the platform enforced when declaring properties:**
    - Enum values must be unique. Salesforce state and country code picklists repeat values across countries, and the platform rejected the definition with `InvalidSwagger ... valid against no schemas from 'oneOf'` pointing at the body parameter. Dedupe enums, and drop an enum that differs between objects.
    - Send compact JSON. The same definition pretty-printed was about 374 KB and the update failed with a dropped connection; compacted it was about 132 KB and succeeded.
    - A `paconn update` that prints `Expecting value: line 1 column 1` can still have applied. The service returned an empty body that paconn could not parse. Read the connector back to check.
    - Write the definition as ASCII-only JSON (`ensure_ascii=True`). `paconn` reads it as cp1252, and raw UTF-8 characters fail with a `charmap` decode error.
    - A failed `paconn update` leaves the deployed connector unchanged. Verify by reading the connector back.
    - `paconn` sign-in tokens expire within the hour. Expect to repeat the device-code login.
    - The deployed swagger returned by the API prefixes every path with `/{connectionId}`.
  - **Per-object fields are a limit of this approach.** A single static list cannot be exact for every sObject. Custom fields (`__c`) from the sample org end up in it, so build the list from your own org's objects and keep it out of any shared template.
  - **Possible routing problem: `GetObjectSchema` for Case returns 404.** `/sobjects/Case/{caseId}` (`GetCase`) and `/sobjects/Case` (`CreateCase`) are more specific than `/sobjects/{sObject}/schema`, so `/sobjects/Case/schema` is routed to the record path and Salesforce looks for a Case with the Id `schema`. Observed, not fixed.
  - **How to test a connector without Cowork:** read `runtimeUrls` from the connector (`GET .../apis/<api name>?$filter=environment eq '<env id>'`), then call `<runtimeUrl><connection id>/<path>` with a token for `https://apihub.azure.com`. Use payloads you expect Salesforce to reject, so nothing is written.
  - **The failed `CreateRecord` call wrote nothing.** Salesforce held exactly one new Account afterward, the one `CompositeTree` created. Still check the target system before retrying a failed write, since not every failure is guaranteed to be clean.

## CLI gotchas

| Symptom | Cause and fix |
|---|---|
| `package` or `share` says a zip or `TEAMS_APP_ID` is missing for `local` | They default to `--env local`. Pass `--env dev`. |
| `package` says an environment variable is missing | It reads the variable from the shell, not from `.env.<env>.user`. Set `$env:MY_CONNECTOR_REFERENCE_ID` first. |
| `provision` fails with "New title must have a higher version" | Bump `version` in `appPackage/manifest.json` before every re-provision. It also failed once spuriously and succeeded on retry. |
| `share` returns HTTP 429 | Throttled. Wait a minute and retry. |
| `plugin publish` fails with `publishTeamsAppUpdate` BadRequest | Seen for a skills-plus-connector plugin with no declarative agent. The dry run skips its checks for the same reason. Provision plus share is the path that works. |
| `wiqd agent info --title-id` returns 404 | Expected for plugins. It cannot be used to check a plugin. |
| Cowork page still shows the previous version after a re-provision | Cached page. The service rejects a same-version re-provision, which shows it already has the version. Hard refresh (Ctrl+F5) or open a fresh browser profile; bump `version` once more if it persists. |
| `wiqd plugin delete` prints `Cancelled.` | It asks for confirmation and treats closed input as no. Pass `--yes`. Without `--env dev` it also targets `local`, so use `--title-id` for a plugin in another environment. |
| A deleted plugin still shows in the Agent 365 catalog | Seen for several minutes after `delete --title-id ... --yes` printed its summary. Recheck later; it was not resolved in this session. |
| `plugin create --output <dir>` nests the project one level deep | Move the contents up and remove the extra folder. |

## Connector-side rules that mattered

- **Run `ppcv`** on the connector before deploying. Version 1.0.5 or later matches `identityProvider` and the `type` value case-insensitively and knows the `salesforcev2` provider.
- **A connector whose `scriptOperations` is non-empty must be deployed with its script.** The same applies with `paconn`.
- **Do not request `refresh_token`** when the identity provider is `salesforcev2`. The provider already asks for it. The Salesforce app must still allow that scope.
- **Never commit** a client ID, client secret or generated redirect URL. Use placeholders in `apiProperties.json`.

## Diagnosing a failed connect

1. **Capture a HAR** of the Connect click in the browser developer tools (Network tab, keep the log). Remove tokens before sharing it.
2. **Read the gateway calls.** `Connector not found` means the environment is wrong. A success on `parameter-schema` followed by a popup that closes quickly points at the redirect URL or the identity provider's callback list.
3. **Identify the environment** from `environment_id` in `GET /v1/connections`.
4. **List connections** where the connector was deployed with `pac connection list` to confirm a Connected connection exists there. A connection in one environment does not carry over to another.

## Other routes to the same tools

Tested on 2026-10-29 unless marked untested. These are alternatives to the `openApiConnector` pointer, mainly for hosts where that shape is not documented (Copilot chat, Copilot Studio).

**1. The connector's own MCP endpoint, called directly: Confirmed.** Through the connection's runtime URL (token audience `https://apihub.azure.com`), `POST <runtime url><connection id>/mcp` answered a full MCP handshake: `initialize` returned the script's server info, `tools/list` returned **all 37 tools**, and `tools/call` ran `get_limits` and `query` against Salesforce. This is the call a Copilot Studio MCP client makes. It includes the tools Cowork can never reach, because Cowork invokes only the REST operations (see above). So any host that talks MCP to the connector gets the full script tool set.

**2. Feed WIQD an OpenAPI file (declarative agent with an API plugin): works in Microsoft 365 Copilot chat, reported by the author.** `wiqd agent add action --openapi-spec <file> --operations "GET /query,..."` accepted the connector's own **Swagger 2.0** file and:
- converted it to OpenAPI 3.0 and kept only the selected operations (about 6 KB of YAML for three operations, down from about 147 KB),
- wrote an `ai-plugin.json` with `runtimes[].auth` of `OAuthPluginVault` and a `reference_id` variable, plus adaptive cards and an `instruction.txt`,
- added an `oauth/register` step to `m365agents.yml` that registers the OAuth client from `clientId` and a `SECRET_` environment variable, so no Developer Portal visit is needed,
- passed `wiqd agent validate` with no issues.

This is a documented route for Microsoft 365 Copilot chat (declarative agents with API plugins), unlike `openApiConnector`. Things to know before provisioning:
- **The spec needs a real server URL.** The converted `servers` entry was the repo's placeholder (`https://instance.my.salesforce.com/...`). Set it to the org's host, so the agent is tied to one org.
- **Fix the security scheme name in the swagger first.** The repo's Salesforce MCP swagger defines `securityDefinitions.oauth2_auth` but references `oauth2-auth` in `security`. Power Platform accepts that. `wiqd agent add action` crashed with `Cannot read properties of undefined (reading 'type')` until the names matched. `ppcv` does not catch it.
- **Scopes.** The Developer Portal registration is not the `salesforcev2` provider, so nothing adds `refresh_token` for you. Declare `api refresh_token` in the spec's scopes for this route. This is the reverse of the connector's own setting.
- **Salesforce callback URL.** Add `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` to the Salesforce app.
- **Operation count.** Choose a subset with `--operations`. Whether the host caps API plugin functions was not checked.
- **Dynamic bodies.** A body defined only by `x-ms-dynamic-schema` has no fields in the converted spec. Cowork showed the same blind spot, so list properties explicitly.
- **Provisioned and shared, sign-in not yet tested.** I fed the connector's Swagger to `wiqd agent add action` with six read operations, set the real server URL, declared `api refresh_token` scopes, and provisioned. The OAuth client was registered from the CLI (a registration id was written to the env file), the agent was shared with the owner account, and the callback URL `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` was added to the Salesforce app. `wiqd agent ask` reported the agent "not found" right after provisioning, and it was not in the tenant listing yet, so propagation or a first sign-in is needed first. Open it from the deep link in Microsoft 365 Copilot to test.
- **Salesforce rejected the first sign-in with `error=invalid_request&error_description=missing required code challenge`.** The OAuth client that `wiqd agent add action` registers had PKCE off: the generated `oauth/register` step has no PKCE setting, and wiqd's `--openapi-auth-pkce` flag defaults to `false`, even though Learn says PKCE is on by default in Agents Toolkit. Salesforce External Client Apps require PKCE, so the authorize request (which had no `code_challenge`) was refused.
- **Adding `isPKCEEnabled: true` and re-provisioning did not fix it, and the error persisted for 10+ minutes of retries.** Cause, read from the toolkit's own code: `oauth/register` only creates a registration when `OAUTH2_AUTH_REGISTRATION_ID` is not set in the environment file. If it is set and the registration still exists, the step logs "skip create" and **never updates the existing registration**. The first provision had already stored a PKCE-less registration, so every later provision left it alone. Fix: set `isPKCEEnabled: true` in `m365agents.yml` (or pass `--openapi-auth-pkce` at `add action` time, so the very first registration is right), **delete the `OAUTH2_AUTH_REGISTRATION_ID` line from `env/.env.<env>`**, bump the manifest `version`, and re-provision. A new registration is created and its id is written back. The old registration is left orphaned in the Developer Portal. General rule: changing anything in `oauth/register` needs a cleared registration id to take effect.
- **Sign-in after the new registration: worked, per the author.** After the registration was recreated with PKCE on, retrying from a fresh chat in Microsoft 365 Copilot completed the Salesforce sign-in and the agent worked. This is the first route in this document that works in Copilot chat, and the only one Learn documents for it (declarative agent plus API plugin, `OAuthPluginVault`). It calls the Salesforce REST API directly, with no Power Platform connector involved at runtime, so the connector's script, MCP tools and connection are not used.
- **What the author did not report:** how many operations were exercised and whether any write was tried. Only six read operations were added. Writes would need the body-schema caveat below.
- **Token refresh will fail on this route (reported by the author, consistent with an earlier finding in this repo).** The token is held by the Microsoft token store behind `OAuthPluginVault` (the Teams Developer Portal registration), not by a Power Platform connection. That store cannot find out when a Salesforce access token expires, because it has no introspection step for it, so it never knows when to refresh. When the access token lapses the user gets a sign-in prompt, or a failed call, instead of a silent refresh, even though a direct `refresh_token` grant against Salesforce succeeds. The same limitation is recorded in the Salesforce SObject All plugin README ("Tokens are not refreshed"; JWT lifetime equals the Salesforce session timeout, 12 hours at most). I did not test the expiry itself, so the exact failure mode and timing are unverified. My understanding, not verified here, is that Salesforce's token response omits `expires_in`, which would leave the store with nothing to schedule a refresh from.
- **Why the Power Platform connector route does not have this problem (Observed in Cowork, not forced to expire):** the `salesforcev2` identity provider on the connector requests `refresh_token` itself and the connection platform manages renewal. This is the main reason to prefer the connector-backed routes over a plain OpenAPI plugin when the backend is Salesforce.
- **Ideas to reduce the impact, all untested:** raise the session timeout on the Salesforce External Client App so access tokens live longer, accept a periodic re-sign-in for read-only use, or use a connector-backed host (Cowork with the `openApiConnector` pointer, or Copilot Studio with the connector tool) where refresh is handled.
- **Untested:** packaging this agent into a plugin with `wiqd plugin add agent`, so it ships alongside skills.

**3. Managed-connection pattern from the WIQD catalog: Tested, rejected by the service.** The catalog's Azure DevOps plugin declares a `remoteMcpServer` whose `mcpServerUrl` is the **real upstream server** (`https://mcp.dev.azure.com`) and whose authorization is `ConnectionVault` with `referenceId: shared_adomcpserver`. I copied that shape for Salesforce: deployed the "Salesforce MCP SObject Control" pass-through connector (`InvokeMCP` only, `salesforcev2`, `mcp_api` scope, its own External Client App) to the Cowork environment, then built a plugin with a `remoteMcpServer` at `https://api.salesforce.com/platform/mcp/v1/platform/sobject-all` and `authorization: {type: ConnectionVault, referenceId: <that connector's API name>}` on a `devPreview` manifest.
- **Result: `wiqd plugin provision` fails** with a 422 from the Developer Portal (`create-app`): `Error converting value "ConnectionVault" to type 'AgentConnectorAuthorizationType'. Path 'agentConnectors[0].toolSource.remoteMcpServer.authorization.type'`. Learn lists `ConnectionVault` as developer preview, but the Developer Portal service of this tenant does not accept it on a `remoteMcpServer`.
- **Control: Confirmed.** The identical manifest with `OAuthPluginVault` and a placeholder reference provisioned normally. So the rejection is only about `ConnectionVault` on `remoteMcpServer`, not the URL, publisher fields or `devPreview`.
- **Contrast with `openApiConnector`**: the `ConnectionVault` authorization on an `openApiConnector` provisioned fine. The same enum value is accepted there and refused here.
- **Possible reason (not proven):** the catalog entries that use it (Azure DevOps) are created by Microsoft through another path, not by the public Developer Portal API. Treat `ConnectionVault` on `remoteMcpServer` as Microsoft-only until a tenant accepts it.
- The shape remains untested at runtime, because it never provisioned.

**4. Copilot Studio: Learn documents two routes, and WIQD has none.** Learn's pages for Copilot Studio describe the MCP onboarding wizard (a server URL with None, API key or OAuth 2.0) and a custom connector imported in Power Apps. A connector with an `x-ms-agentic-protocol` operation, like this one, fits the second route. The add-tool flow needs a connection that only the signed-in user can create through consent, so it can't be scripted from a CLI. The connector's MCP endpoint itself passed the full protocol test (route 1); the agent-level test is pending.

## Open questions

- Does the OpenAPI-agent route (route 2) really lose its Salesforce session when the access token expires, and what does the user see? Test by waiting past the session timeout, or by shortening it on the Salesforce app. Does lengthening the timeout make it livable?

- Does Cowork ever call the connector's `InvokeMCP` operation, or only the REST operations?
- Does `UpdateRecord` now work in Cowork with a static body? Do other dynamic-schema operations (dynamic values, dynamic properties) behave the same way, or is it only `x-ms-dynamic-schema` on a body?
- Does the skill's "no write without a yes" rule add anything beyond Cowork's own approval step?
- Does the plugin reach users beyond the owner without a separate assignment in the admin center?
- Does Copilot chat support `openApiConnector` plugins? Learn does not document it (see "Where this works today"), and the earlier claim that it is coming is unconfirmed. If it ever lands: which Power Platform environment does Copilot look in (Cowork used its own dedicated one), does it also drop dynamic-schema bodies, and does it call REST operation Ids the way Cowork does?
- For Copilot chat today, is the route a hosted remote MCP server with `OAuthPluginVault`, as Learn documents? Does `ConnectionVault` on a `remoteMcpServer` work there?
- For Copilot Studio, the documented routes are the MCP onboarding wizard (URL, with None, API key or OAuth 2.0) and a custom connector imported in Power Apps. WIQD has no Copilot Studio path. Does this repo's dual-purpose connector (`InvokeMCP`, `x-ms-agentic-protocol`) work there as a tool? Untested in this session.
- Is the `devPreview` connector shape stable enough to publish to the plugin registry? The WIQD docs call runtime support "not yet broad".

## References

- WIQD plugin authoring reference: connector requirements, existing OpenAPI custom connectors, auth types (`microsoft/wiqd`, `docs/src/content/docs/getting-started/plugin-reference.md`).
- WIQD connector command: catalog discovery and `--selector` (`docs/src/content/docs/cli/connector.md`).
- Work IQ Dev Tools: https://aka.ms/wiqd/docs
- This repo: [Salesforce PP Plugin](./README.md) and [Salesforce MCP](../../Salesforce%20MCP/readme.md).
