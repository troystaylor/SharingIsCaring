# Azure Functions Hosted Skills

Calls [Azure Functions hosted skills](https://learn.microsoft.com/azure/azure-functions/functions-hosted-skills) from Power Automate, Power Apps and Copilot Studio.

A hosted skill is a unit of AI work defined in an `.agent.md` file: natural-language instructions, a trigger, and whatever tools you bind to it. Most hosted skills start themselves — a timer fires, a queue message arrives, a connector trigger sees new mail. This connector covers the other direction. It calls a skill on demand, waits for the answer, and hands it back to a flow or an agent.

Azure Functions hosted skills is in **preview**. Endpoint names and configuration keys can change before general availability.

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | The six operations and their schemas |
| `apiProperties.json` | Connection using a **function key**. The default; start here |
| `apiProperties-entra.json` | Connection using **Microsoft Entra ID**. Use when your skills set `http_auth.mode: entra` or your tenant disables function keys |
| `script.csx` | Request and response handling, the MCP tools, and skill discovery |

Import one properties file, not both — they describe two ways of authenticating the same connector.

## What you get

| Surface | Use it for |
|---------|-----------|
| Actions in Power Automate and Power Apps | Send a prompt to a named skill and use its response in a flow |
| MCP tools in Copilot Studio | Let an agent call your hosted skills as tools, including across turns |

Both surfaces talk to the same endpoints, so a skill you expose to a flow is immediately available to an agent.

## Before you start

**The skill must expose a chat API.** The built-in endpoints are off by default. Turn them on in each `.agent.md` file you want to call:

```markdown
---
name: Order Triage
description: Classifies an order problem and drafts a reply.
builtin_endpoints:
  chat_api: true
---

You classify order problems. Given a customer message, return the severity,
the responsible team, and a draft reply.
```

A skill with `builtin_endpoints` enabled does not need a `trigger`. A skill can have both — a timer trigger for its scheduled run and a chat API for on-demand calls.

`builtin_endpoints: true` is shorthand for switching on everything — the chat API, the browser debug UI and the runtime's own MCP surface. The official quickstart uses that form. Setting `debug_chat_ui: true` also switches `chat_api` on, because the browser UI calls the same endpoints.

**The skill's slug is its file name.** `order_triage.agent.md` has the slug `order_triage`. The slug is what you pass to every operation in this connector, and it is what appears in the function name in the Azure portal (`agent_order_triage_builtin_chat`). Skills can sit at the app root or in an `agents/` folder — the slug is the file name either way, with no folder in it.

**Only built-in endpoints are reachable.** A skill whose `trigger` is `http_trigger` gets its own route, such as `/summarize`, and is not callable through this connector's operations — use a plain HTTP action for those, or add `builtin_endpoints` alongside the trigger.

**Pick an authentication mode.** The connector ships two connection configurations. `apiProperties.json` uses a function key and suits most apps. `apiProperties-entra.json` uses Microsoft Entra ID and is the one to import when your skills set `http_auth.mode: entra`, or when your tenant disables function keys — see [Authenticating with Microsoft Entra ID](#authenticating-with-microsoft-entra-id). A skill set to `anonymous` works with either; the credential is sent and ignored.

**The route prefix is handled for you.** The runtime registers its endpoints under the Functions route prefix, which defaults to `api` but is set to empty in the official quickstart's `host.json`. The connector tries the declared path first and retries against the other layout on a 404, so both work without configuration.

## Setup

1. **Deploy the function app** with `chat_api` enabled on every skill you plan to call.

2. **Collect the hostname.** In the Azure portal, open the function app and copy the default domain from **Overview** — for example `contoso-skills.azurewebsites.net`. Enter the hostname only, with no scheme and no path.

3. **Collect a key.** Under **Functions** → **App keys**, copy a host key. A host key works for every skill in the app. If you prefer to scope access to a single skill, open that skill's `_builtin_chat` function, select **Function Keys**, and copy a key from there instead.

   Copy the `_master` key instead if you want the connector to list your skills — see [Discovering skills](#discovering-skills). That is a trade, not a requirement, and you can change the key on the connection later.

4. **Import the connector.** From this folder:

   ```powershell
   pac connector create `
       --api-definition-file ./apiDefinition.swagger.json `
       --api-properties-file ./apiProperties.json `
       --script-file ./script.csx
   ```

   Substitute `./apiProperties-entra.json` if you are using Microsoft Entra ID. You can also create the connector in the maker portal and import `apiDefinition.swagger.json`, then paste `script.csx` into the **Code** tab and enable it for all six operations.

5. **Create a connection** and supply the hostname and the key. Those are the only two settings.

6. **Test it.** Run **Invoke a hosted skill** with the slug and a prompt. A successful call returns `response`, `session_id` and `tool_calls`.

## Operations

| Operation | What it does |
|-----------|--------------|
| Invoke a hosted skill | Sends a prompt and returns the skill's final response, the session it ran in, and the tools it called |
| Get session transcript | Returns the user and assistant turns recorded in a session, oldest first |
| List dynamic workflows for a session | Lists durable workflows the skill started in a session |
| Get dynamic workflow status | Returns the runtime status and, once finished, the output of one workflow run |
| List hosted skills | Lists the skills in the function app and the endpoints each one exposes |
| Invoke Azure Functions Hosted Skills MCP | The MCP endpoint Copilot Studio connects to |

### Discovering skills

Every skill operation takes a slug, and **Skill** is a dropdown rather than a free-text box. It is populated by **List hosted skills**, which reads the function app's admin API and reports every skill with built-in endpoints, along with which endpoints each one has — chat, transcript, dynamic workflows, debug UI. You can still type a slug the list does not contain.

**Discovery works only when the connection's Function Key is the app's `_master` key**, because that is the only key the Functions admin API accepts. This is a deliberate trade:

| Key on the connection | Skill operations | Skill list |
|---|---|---|
| Host or function key | Work | Empty, with the reason in `message` |
| Master key | Work | Populated |

A host key is the least-privilege choice and costs you only the picker — you type slugs instead, and the slug is always the `.agent.md` file name without its extension. Nothing else changes.

The runtime itself publishes no catalogue endpoint — the admin API is what makes enumeration possible, so discovery also stops working if the function app has admin isolation enabled.

## Authenticating with Microsoft Entra ID

Import `apiProperties-entra.json` instead of `apiProperties.json` to authenticate with Microsoft Entra ID. Everything else — the Swagger definition, the script, the operations — is identical.

This path is built from the runtime's documented behaviour but has not been exercised against a live Entra-protected app, so treat the steps below as a starting point rather than a verified recipe. The function-key path has been tested end to end.

**The function app must have App Service Authentication turned on.** The runtime does not validate tokens itself; it accepts an Entra caller only when the platform has already validated the token and injected a client principal. Without App Service Authentication in front of it, a skill in `entra` mode rejects every request no matter how good the token is.

To set it up:

1. **Register an application for the function app**, or reuse the one App Service Authentication creates for you. Give it an Application ID URI, such as `api://<app-id>`, and expose a `user_impersonation` scope.

2. **Turn on App Service Authentication** on the function app with Microsoft Entra ID as the provider, pointing at that registration.

3. **Fill in `apiProperties-entra.json`.** Replace `[YOUR_CLIENT_ID]` with the client ID your connector signs in with, and both `[YOUR_FUNCTION_APP_APPLICATION_ID_URI]` placeholders with the Application ID URI from step 1.

4. **Import the connector** with that file, then read back the redirect URL the platform generated for it and add that URL to the app registration's redirect URIs. The value is specific to the connector *and* the environment, so a connector deployed to two environments produces two URLs and both need registering.

5. **Create a connection** and sign in.

Skill discovery is unavailable in this mode. The Functions admin API accepts only the app's master key, and this configuration has no key parameter to put it in — deliberately, since the reason to run in Entra mode is usually that you do not want master keys stored in connections. Type slugs instead, and name the skill explicitly on every MCP tool call.

## Reliability

Reads — transcript, workflow list, workflow status and skill discovery — are retried up to three times when the app responds with 429, 502, 503 or 504, honouring `Retry-After` when the app sends one. A function app scaling from zero produces exactly these codes, and retrying is invisible to your flow.

**Invoking a skill is never retried.** A hosted skill can send mail, update a record or start a workflow before the throttle was applied, so replaying it risks doing that work twice. A throttled invoke fails with the wait time the app asked for, and it is your decision whether repeating it is safe.

### Sessions

Omit the session ID on the first call and the runtime mints one, returns it in `session_id`, and stores the transcript in the function app's `AzureWebJobsStorage` account. Pass that value back on the next call and the skill sees everything said before.

Two calls without a session ID are two unrelated conversations, even against the same skill. In a flow, capture `session_id` from the first response and feed it into every later call in the same run.

Session IDs accept letters, digits, dots, hyphens and underscores, up to 128 characters. Supplying your own is fine — a support ticket number makes a good session ID, because it resumes the same conversation days later.

**Get session transcript** returns at most the 200 most recent turns and sets `truncated` when older ones were dropped. The skill's own memory of the conversation is unaffected; only the transcript you read back is capped.

### Tool calls

`tool_calls` reports what the skill did to produce its answer: one entry per tool the skill used, carrying the tool's name, the arguments it was given and what it returned. The `type` field reads `tool_start` even on a completed call, because the runtime merges the call and its result into a single entry on this path.

Arguments and results arrive as text, with structured values rendered as JSON, so they map onto a single string column and can be shown in an approval card or written to a log table.

### Long-running work

A Power Platform request times out at roughly 120 seconds, and a skill that searches the web, runs sandboxed Python, and calls three connectors can exceed that. Two ways around it:

- **Let the skill start a dynamic workflow.** Skills with `workflows.enabled: true` in their front matter return quickly with a workflow started in the background. Poll it with **Get dynamic workflow status** until `runtime_status` leaves `Running`, then read `output`. The **Workflow ID** field is a dropdown, filled from the workflows the chosen skill started in the session you name, each labelled with its status and start time.
- **Trigger the skill instead of calling it.** If nothing in the flow needs the answer, give the skill a queue or Event Grid trigger and let the function app own the run.

The workflow operations exist only for skills that enable dynamic workflows; against any other skill they return an error saying so.

**Stopping a workflow is not an operation here.** The runtime's `cancel_workflow` and `terminate_workflow` are tools the skill itself holds, not HTTP endpoints, so there is nothing for a connector to call. To stop a run, send the skill a prompt in the same session asking it to cancel that workflow.

A workflow that fans out to specialist skills does not make those specialists visible here. Subagents are separate `.agent.md` files that usually have no `builtin_endpoints` of their own, so they will not appear in the Skill list and cannot be invoked directly — which is the intent.

## Copilot Studio

Add the connector to an agent as an MCP tool. It publishes five tools:

| Tool | Purpose |
|------|---------|
| `list_skills` | Discover which hosted skills the function app offers and what each supports |
| `invoke_skill` | Send a prompt to a hosted skill and get its response |
| `get_session_history` | Recall what was said earlier in a session |
| `list_session_workflows` | Check on long-running work the skill started |
| `get_workflow_status` | Read the result of one workflow run |

Each tool except `list_skills` takes an optional `agent` argument naming the skill. Omit it and the connector resolves the skill itself when the function app hosts exactly one callable one — a common case, and it means a single-skill app needs no configuration at all. When the app hosts several, or the skills cannot be listed, the tool returns an error telling the agent to call `list_skills` and choose. Auto-resolution needs the skill list, so it works on the same terms as [discovery](#discovering-skills).

The connector also sends the agent a short set of instructions when Copilot Studio connects: discover skills before guessing at one, carry `session_id` between turns to keep a conversation, and poll a dynamic workflow rather than assuming the skill finished. That guidance reaches the planner without you writing it into every agent.

`invoke_skill` returns `session_id` in its result, which lets an agent continue the same hosted-skill conversation across turns by passing that value back. The agent has to carry it deliberately — Copilot Studio's own conversation state and the hosted skill's session are separate things.

**This is worth doing when the hosted skill knows something the agent does not.** A skill wired to a Connector Namespace can reach SAP, Salesforce or a line-of-business SQL database, run model reasoning over what it finds, and return a conclusion. The agent gets one clean tool instead of a dozen raw API calls.

You do not need `builtin_endpoints.mcp: true` for any of this. That setting exposes the skill on the runtime's own MCP endpoint, which is owned by the Functions MCP extension and requires a separate system key. This connector builds its MCP surface from the chat API instead, so one key covers everything and you get five tools rather than one.

Tool results are trimmed before they reach the agent. A skill that scraped a dozen pages can return more than an agent's context window can hold, so oversized results have their tool-call detail shortened first and, if that is not enough, are truncated with a note saying so. The skill's own answer is preserved, and **Invoke a hosted skill** always returns the complete payload.

## Telemetry

`script.csx` ships with Application Insights logging switched off. To turn it on, set `APP_INSIGHTS_ENABLED` to `true` and replace `APP_INSIGHTS_KEY` with your instrumentation key. It then records skill invocations, MCP tool calls, and failures. Telemetry failures are swallowed and never affect a call.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Every call returns 401 | The function key is wrong or has been rotated | Copy a current host key from **App keys** and update the connection |
| `No hosted skill named 'x' exposes a chat API` | The slug is wrong, or the skill does not set `builtin_endpoints` | Match the slug to the `.agent.md` file name and set `chat_api: true`, then redeploy |
| A skill you can reach in the browser returns 401 here | The skill uses `http_auth.mode: entra` | Import `apiProperties-entra.json` and follow [Authenticating with Microsoft Entra ID](#authenticating-with-microsoft-entra-id) |
| Every call returns 401 after switching to Entra ID | App Service Authentication is off, or the token audience does not match the app registration | Enable App Service Authentication on the function app and confirm the Application ID URI in `apiProperties-entra.json` matches it |
| Sign-in fails with a redirect URI error | The connector's generated redirect URL is not on the app registration | Read the redirect URL back from the deployed connector and add it to the registration |
| Every call returns 404 including ones you know exist | `host.json` uses a route prefix other than `api` or empty | The connector handles those two automatically; for any other prefix, change the `/api/agents/...` paths in `apiDefinition.swagger.json` to match |
| The skill forgets the previous turn | No session ID was passed | Capture `session_id` from the first response and send it on later calls |
| The action times out after about two minutes | The skill run is longer than a Power Platform request allows | Move the work into a dynamic workflow and poll it, or trigger the skill instead |
| Workflow operations return an error about dynamic workflows | The skill does not set `workflows.enabled: true` | Add it to the skill's front matter and redeploy, or drop those operations from the flow |
| The transcript comes back empty | The app has no storage configured, or the session never ran | Confirm `AzureWebJobsStorage` is set on the function app and that the session ID matches one that was returned |
| The Skill dropdown is empty | The connection uses a host or function key, so the admin API refused the listing | Use the app's `_master` key as the Function Key, or type the slug |
| `Listing skills needs the function app's master key` | Same cause, reported in the operation's `message` | Same fix, or ignore it and type slugs |
| `The function app's admin API is unavailable` | Admin isolation is enabled on the function app | Discovery is not possible in that configuration; type slugs instead |
| The action fails with `The function app is throttling requests` | The app hit its scale or model rate limit mid-invoke | Wait for the reported interval and rerun, once you are satisfied that repeating the skill is safe |
| An agent says no skill was named | The app hosts several skills, or they could not be listed | Have the agent call `list_skills` and pass `agent`, or use the master key so auto-resolution can work |
| An agent says a tool result was truncated | The skill returned more than the agent output budget allows | Use **Invoke a hosted skill** in a flow for the full payload, or have the skill summarise before returning |

## Reference

- [Azure Functions hosted skills](https://learn.microsoft.com/azure/azure-functions/functions-hosted-skills)
- [Hosted skills reference](https://learn.microsoft.com/azure/azure-functions/functions-hosted-skills-reference)
- [Build an event-driven AI app with hosted skills](https://learn.microsoft.com/azure/azure-functions/scenario-hosted-skills)
- [Dynamic workflows](https://learn.microsoft.com/azure/azure-functions/functions-hosted-skills-dynamic-workflows) and [how to create one](https://learn.microsoft.com/azure/azure-functions/functions-hosted-skills-dynamic-workflows-how-to)
- [Azure Functions Agents Runtime](https://azure.github.io/azure-functions-agents-runtime/)
