# Dataverse Ask

Dual-purpose Power Platform custom connector for the [Dataverse Ask APIs](https://learn.microsoft.com/power-apps/developer/data-platform/ask/) (preview). Ask a question about your business data in plain language and get back the rows that answer it, a natural-language summary and links to the source records — without knowing table relationships, column logical names or query syntax.

It works two ways from one deployment:

- **MCP endpoint** at `/mcp` — Copilot Studio agents discover five tools and reason over your Dataverse data conversationally
- **Typed operations** — Power Automate and Power Apps get four REST actions with parameter validation and IntelliSense

A *semantic model* names the Dataverse tables that can answer a question. You create one, then ask questions against it. The service decides how to answer from those tables, and returns only data the signed-in user is allowed to read.

## What this is not

This is not the Dataverse Web API. The Ask APIs live at `/api/iq/v1.0/` and are deliberately not an OData service: there is no metadata document and no `$select`, `$filter` or `$expand`. Use the Web API when you need precise create, retrieve, update, delete or query operations. Use this connector when the input is a question.

The connector does make one Web API call, to read your table list for the **Tables** picker, because the Ask API has no table endpoint of its own. That is the only place `/api/data/` is used.

## Prerequisites

- A Dataverse environment and its URL, such as `https://contoso.crm.dynamics.com`
- **Business Applications in Work IQ** turned on for that environment in the Power Platform admin center. Without it, every call returns `400`. [Manage feature settings](https://learn.microsoft.com/power-platform/admin/settings-features)
- A security role for the connection user that grants semantic model privileges — **Dataverse Search Role**, **Environment Maker**, **System Customizer** or **System Administrator**
- Permission to create an app registration in your Microsoft Entra tenant
- Permission to create custom connectors in your Power Platform environment
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`)

Ask API usage is billed through Copilot Credits. See [Licensing requirements](https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq/api-overview#licensing-requirements).

## Setup

### 1. Register an application

Create an app registration in Microsoft Entra ID and add the delegated **Dynamics CRM → user_impersonation** permission. Add `https://global.consent.azure-apim.net/redirect` to its web redirect URIs.

Copy the **Application (client) ID** and create a client secret.

### 2. Point the connector at your environment

In `apiDefinition.swagger.json`, replace the `host` with your environment hostname:

```json
"host": "contoso.crm.dynamics.com"
```

In `apiProperties.json`, replace `[YOUR_CLIENT_ID]` with your application ID, and replace `https://org.crm.dynamics.com` in both `AzureActiveDirectoryResourceId` and `resourceUri` with your environment URL. Leave `redirectUrl` as `[YOUR_REDIRECT_URL]` — the platform generates and substitutes the real value when you deploy.

### 3. Deploy

```powershell
pac auth create --environment <your-environment-url>
pac connector create --api-definition-file apiDefinition.swagger.json `
                     --api-properties-file apiProperties.json `
                     --script-file script.csx
```

`--script-file` is required. Without it the connector deploys but the MCP endpoint returns nothing usable, because the script is what answers the JSON-RPC calls.

To supply the client secret without committing it, copy `apiProperties.json` to a temporary file, set `clientSecret` there, and pass that copy to `pac`. Alternatively, leave it out and set the secret in the connector's **Security** tab in the maker portal.

### 4. Register the generated redirect URL

The platform creates a redirect URL unique to this connector and environment. Read it back:

```powershell
pac connector download --connector-id <connector-id> --outputDirectory ./verify
```

The value is at `properties.connectionParameters.token.oAuthSettings.redirectUrl`. Add it to your app registration's web redirect URIs alongside the generic one. OAuth consent fails until it is registered. Deploying the same connector to a second environment produces a different URL, and both must be registered.

### 5. Create a connection and test

In **Power Apps → Custom connectors**, click the **+** on the Dataverse Ask row to create a connection, and sign in.

Test with **List semantic models** and no parameters. A `200` confirms auth, host and feature enablement in one call — it is the right first test because it answers immediately and does not depend on indexing. An empty list is still a pass; the environment simply has no models yet.

Then run **Create semantic model**. Give it a unique name and pick tables from the dropdown:

| Field | Value |
|-------|-------|
| Unique name | `Sales pipeline` |
| Tables | `Account (account)`, `Opportunity (opportunity)` |

A populated **Tables** dropdown is itself a second check: it proves the connection can read table metadata.

**Do not ask a question yet.** Indexing starts on creation and initial generation can take up to two hours. Come back afterwards and run **Ask a question** against `Sales pipeline` with something like *Which open opportunities have the highest estimated revenue?* Rows and a summary confirm the model is live.

To test the MCP endpoint instead, add the connector to a Copilot Studio agent and ask it what semantic models exist — that exercises discovery and a tool call without waiting on indexing.

## Operations

| Operation | Method | Description |
|-----------|--------|-------------|
| **Invoke Dataverse Ask MCP** | `POST /mcp` | MCP endpoint for Copilot Studio |
| **List semantic models** | `GET /api/iq/v1.0/semanticmodel` | The models available in this environment |
| **Create semantic model** | `POST /api/iq/v1.0/semanticmodel` | Associate a unique name with one or more tables |
| **Delete semantic model** | `DELETE /api/iq/v1.0/semanticmodel/{id}` | Delete a model by ID |
| **Ask a question** | `POST /api/iq/v1.0/ask` | Submit a question against a model |

A sixth operation, **List tables**, is marked internal. It backs the **Tables** picker and does not appear in the action list.

### Pickers

| Field | Shows | Sends |
|-------|-------|-------|
| **Semantic model name** on Ask | Model name | Model name |
| **Semantic model** on Delete | Model name | Model ID |
| **Tables** on Create | `Account (account)` | `account` |

The Delete picker matters: that operation is keyed by ID while everything else is keyed by name, so choosing from the list avoids having to look the ID up.

The typed operations go straight to Dataverse; the MCP endpoint and the table picker run through `script.csx`.

## Copilot Studio

Add the connector to an agent and Copilot Studio discovers the MCP endpoint, which advertises five tools:

| Tool | Purpose |
|------|---------|
| `list_semantic_models` | Find the available models and their exact names |
| `ask_dataverse` | Ask a question against a model |
| `list_tables` | Find table logical names, with an optional search term |
| `create_semantic_model` | Create a model over a set of tables |
| `delete_semantic_model` | Delete a model created through this API |

The tool descriptions steer the agent to call `list_semantic_models` before `ask_dataverse`, because the model name has to match exactly, and `list_tables` before `create_semantic_model`, because table names must be logical names. Failures come back as tool results rather than protocol errors, so an agent that passes an invalid search mode is told the three valid values and retries instead of stalling.

`ask_dataverse` returns the summary, the rows, the citation links and a `hasMore` flag. When more rows exist it also returns the paging token and tells the agent how to use it.

Try prompts such as *Which open opportunities have the highest estimated revenue?* or *Summarize the opportunities for Contoso.*

## Choosing tables

Use table **logical names** — `account`, `contact`, `opportunity`. Display names, entity set names and collection names are rejected. The **Tables** picker handles this for you, and the `list_tables` MCP tool does the same for agents.

The picker reads table metadata from the Dataverse Web API on the same host and connection — the Ask API has no table endpoint of its own. Private and intersect tables are filtered out; a table with no localized label falls back to showing its logical name.

Include only the tables relevant to the questions the model needs to answer. Creating a model indexes those tables, and that index consumes Dataverse database storage; you can see how much in the **DataverseSearch** table, counted against database storage on the **Summary** and **Dataverse** tabs.

## Two kinds of semantic model

**List semantic models** returns more than the models you create here, and the `source` property tells them apart.

| Kind | Created by | This connector can |
|------|-----------|--------------------|
| API model | **Create semantic model**, `source` of `SemanticModelAPI` | Create, list, delete, ask against |
| Provisioned or app-associated model | Auto-provisioned when Copilot over Dataverse is enabled, or owned by a model-driven app, bot component or agent | List, ask against |

You cannot delete a model you did not create through this API, and the maker portal cannot create a brand-new one — creation is what this connector adds. Run **List semantic models** with **Include app and agent usage** set to `botinfo` to see which apps and agents depend on a model before you change or delete it.

## Indexing takes time

A model does not answer well the instant it is created. Initial generation can take **up to two hours**; later incremental changes typically settle within **30 minutes**. An Ask sent immediately after Create can return thin results or none. Models then regenerate automatically every 12 hours to pick up schema changes.

Semantic models do not support ALM. Environment copy and move operations do not carry them, and after either one an administrator has to turn the feature off and back on in the Power Platform admin center to force a restart.

## Working with the answer

**Rows** (`rawResult`) is an array of dynamic objects. Its shape depends on the question and on the query the service generated to answer it, so there is no fixed schema and no design-time dynamic content for the individual fields. Parse it with the property names you expect from the question, or run the question once and read the output to learn the shape.

**Summary**, **Citation links** and **Rows** are all nullable. Display the summary when it is there, but do not require it in order to process the rows.

Results are trimmed to the caller's Dataverse privileges. Record ownership, business unit access, sharing and column-level security all change what comes back, so two users asking the same question can get different answers.

## Search mode

| Mode | Use when |
|------|----------|
| `Auto` | Default. Let the service choose |
| `QuickResponse` | A faster answer matters more than depth |
| `ThinkDeeper` | The question needs deeper analysis and a longer wait is acceptable |

`ThinkDeeper` can take noticeably longer. In a flow, raise the action timeout; in an app, show progress.

## Paging

Set **Row count** to cap the page size. When more rows exist, the response carries a **Paging token** — send the same question, model, search mode and count again with that token to get the next page. Continue until the token comes back null or empty.

Pass the token back exactly as returned. Do not decode, edit or construct one.

## Retrying

**List** and **Ask** are safe to retry after a transient failure. **Create** and **Delete** are not, because model names must be unique — list the models first to find out whether the original request actually succeeded, and treat a missing model after an uncertain delete as a completed delete.

Retry `429`, `500`, `502`, `503` and `504`. Honor `Retry-After` when present; otherwise back off exponentially with jitter and cap the attempts.

## Fine-tuning a model

Answer quality comes from more than the table list. The model extracts signals from metadata you already have — table relationships, public and sub-grid views, form display names, table and column description summaries, and optionally sample data rows. Sample rows are off by default. System views such as Quick Find, Advanced Find, Associated and Lookup are excluded, as are personal views.

Well-written table and column descriptions are therefore the cheapest way to improve answers, because the model reads them directly.

Once a model exists, tune it on the **Semantic model** page in [Power Apps](https://make.powerapps.com):

- **Signals** — turn inferred signal types on or off, exclude sensitive tables, disable views and relationships that mislead
- **Glossary** — add acronyms, synonyms and organization-specific vocabulary the system cannot infer
- **Refresh** — trigger a manual regeneration to apply changes without waiting for the 12-hour cycle

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `400` on every operation | Business Applications in Work IQ is not enabled for the environment | Turn on the feature in the Power Platform admin center |
| `400` on Ask, other operations fine | No table in the model is one the caller may read, or the request is malformed | Check the caller's read access to the model's tables; do not retry unchanged |
| `401` on every call | Missing **Dynamics CRM → user_impersonation**, or `resourceUri` does not match the environment | Check the permission and both URLs in `apiProperties.json` |
| `403` on Create or Delete | The connection user has no semantic model privileges | Assign Dataverse Search Role, Environment Maker, System Customizer or System Administrator |
| `404` on Ask | The semantic model name does not match | Names are exact — pick one from the dropdown |
| `404` on Delete | The model is already gone, or it belongs to an app module, bot or agent | Models not created through this API cannot be deleted here |
| `429` | More than 30 Ask requests per user per minute | Wait for `Retry-After` seconds; avoid retrying several requests in parallel |
| Sign-in fails during connection creation | The generated redirect URL is not on the app registration | Complete step 4 |
| Ask returns nothing just after Create | Indexing has not finished | Initial generation takes up to two hours; retry later |
| Answers ignore a business term or acronym | The term cannot be inferred from metadata | Add a glossary entry, then regenerate the model |
| Answers got worse after a schema change | The model has not regenerated yet | Wait for the 12-hour cycle or trigger a manual regeneration |
| Models missing after an environment copy or move | Semantic models have no ALM support | Have an admin turn the feature off and back on to force a restart |
| No dynamic content for row fields | Rows are dynamic by design | Expected. Parse by property name, or run once to learn the shape |
| Semantic model dropdown is empty | No models exist yet, or the connection cannot read them | Run **List semantic models** to confirm; create one if the list is genuinely empty |
| Tables dropdown is empty or errors | The connection cannot read table metadata | The same **Dynamics CRM → user_impersonation** permission covers it; check the user's security role |
| Agent does not see the tools | The connector was deployed without `--script-file` | Redeploy including the script |
| Agent reports an invalid JSON-RPC response | An older script is deployed | Confirm the deployed `script.csx` matches this folder |

## Application Insights

Telemetry is off by default. To turn it on, set `APP_INSIGHTS_ENABLED` to `true` in `script.csx` and replace `APP_INSIGHTS_KEY` with your instrumentation key. Tool calls are then logged with the tool name and outcome, and failures with their error code and message. Telemetry never fails a tool call.

## Related connectors

| Connector | Use for |
|-----------|---------|
| **Dataverse Ask** (this) | Natural-language questions over a defined set of Dataverse tables |
| **Dataverse SQL** | Precise read-only T-SQL queries against the Web API |
| **Work IQ** | Natural-language questions across Microsoft 365 — mail, meetings, documents, Teams, people |

Work IQ and Dataverse Ask complement each other. Once an administrator enables Business Applications in Work IQ, the Work IQ connector can also reach Dataverse; this connector is the direct path, and the only one that can create a semantic model.
