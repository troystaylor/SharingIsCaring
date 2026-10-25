# Power A2A Template v2

A Power Platform custom connector that **hosts an [Agent2Agent (A2A)](https://a2a-protocol.org/) agent for a REST API that has no A2A support**. You define skills in the connector's script. The script routes each message to a skill, the skill calls your API, and the answer goes back as an A2A task.

Copilot Studio adds the connector as an A2A agent. Power Automate calls the same skills with typed inputs.

## What's new in v2

[Power A2A Template](../Power%20A2A%20Template/) (v1) is a client for an agent that already speaks A2A. v2 is the other half: it is the agent.

| | v1 | v2 |
|---|---|---|
| Role | Client that forwards to a remote A2A agent | Hosts the agent for a REST API |
| Copilot Studio | MCP tools | Native A2A agent (`InvokeA2A`) |
| Power Automate | Send, get, list, and cancel tasks, push configs | Send Message, Run Skill, List Skills, Test Routing, Get Agent Card |
| Skills | None | Defined in the script, with typed arguments and confirmation |

If you need to call an existing A2A agent, keep using v1. v2 has no proxy mode, no MCP endpoint, and no task or push-notification operations, because a hosted agent finishes every task in one call.

## How it works

1. A message arrives from Copilot Studio, a flow, or any A2A client that calls the connector.
2. The script picks a **skill** and works out its arguments.
3. If an argument is missing or a value isn't acceptable, the agent asks. If the skill changes data, the agent asks for confirmation.
4. The skill calls your API with the connection's `Authorization` header.
5. The result goes back as an A2A task with a Markdown answer and structured data.

**Choose the right tool.** For one fixed API call, a plain connector action is simpler. v2 pays off when you want the orchestrator to delegate a whole area of an API to one agent, with skills, clarifying questions, and a card that describes what the agent can do.

## Operations

| Operation | Description |
|---|---|
| **Send Message** | Send a natural-language message. The agent picks a skill, calls your API, and answers. |
| **Run Skill** | Run one skill with a typed input form and no message routing |
| **List Skills** | List the agent's skills and their inputs |
| **Test Routing** | Show which skill a message would reach, without calling your API |
| **Get Agent Card** | Read the agent card that the connector serves |
| `InvokeA2A` | The A2A endpoint that Copilot Studio calls. It isn't a flow action. |

**Send Message** returns:

| Output | Description |
|---|---|
| **Task ID** | Identifies the task. Send it back with your answer when **State** is `input_required`. |
| **Context ID** | Identifies the conversation. Send it with each follow-up. |
| **State** | `completed`, `failed`, `rejected`, `canceled`, or `input_required` |
| **Response Text** | The skill's answer as Markdown text |
| **Status Message** | The question the agent is asking, or the reason a task failed or was rejected |
| **Data** | The skill's structured result, such as a record or a list of rows. Use **Parse JSON** to read its fields. |
| **Artifacts** | The artifacts, including every part |

## Prerequisites

- The base URL of your REST API, and credentials for it (not needed for the demo)
- A Power Platform environment, and permission to create custom connectors
- [PAC CLI](https://aka.ms/PowerAppsCLI), signed in to your tenant (`pac auth create`)
- PowerShell 7, for the deploy script

## Try it with a demo API

You can deploy the connector against a public demo API first, to see every part work before you connect your own API. The `demo` folder holds a product-catalog demo for [DummyJSON](https://dummyjson.com), a free API with no sign-in. DummyJSON simulates writes and saves nothing, so every skill is safe to run.

```powershell
.\Deploy-PowerA2A.ps1 -Environment <YOUR_ENVIRONMENT_ID> -Demo DummyJson
```

`-Demo` swaps the demo's skills and settings into a temporary copy of `script.csx` and deploys that. The skills in `script.csx` don't change, and the released script contains no demo. Then follow the [first-run checklist](#first-run-checklist), using the demo's example messages: "Get product 12", "Find phones", "Is the API working?", and "Set the price of product 12 to 19.99".

Add `-DryRun` to build and inspect the connector files without deploying anything.

## Set up your own

### 1. Configure the script

Everything you edit is at the top of `script.csx`, in three sections:

| Section | What it holds |
|---|---|
| **1. Agent settings** | The agent's name and description, and your API's base URL |
| **2. Your skills** | `RegisterHostedSkills`, where you define what the agent can do |
| **3. Advanced settings** | The signing key, limits, retries, the intent resolver, and Application Insights. All optional. |

Everything below section 3 is the framework. You don't need to change it.

Start with section 1:

```csharp
private const string HOST_AGENT_NAME = "Your API Agent";
private const string HOST_AGENT_DESCRIPTION = "Describe what your API does and which requests the agent should handle.";
private const string HOST_API_BASE_URL = "https://api.example.com";
```

Then replace the starter skills in section 2. See [Define skills](#define-skills). The deploy script copies the name, the description, and your API's host into the connector definition, so you don't edit `apiDefinition.swagger.json`. The description is what the Copilot Studio orchestrator reads to decide when to use the agent, so write it for that.

### 2. Choose how users sign in

The sign-in options are in the `auth` folder. Pick one with `-Auth` when you deploy.

| Option | Use when | Parameters |
|---|---|---|
| **None** | The API is public, or you're trying a demo | `-Auth None` |
| **API key** | Each user enters a key when they create a connection | `-Auth ApiKey -ApiKeyHeader X-Acme-Key` |
| **Microsoft Entra ID** | Entra ID protects your API | `-Auth EntraId -ClientId <id> -ResourceUri api://your-api -Scope <permission>` |

With **API key**, the connector sends the key to your API in the header you name. The script passes that header through, so set `-ApiKeyHeader` to the header your API expects.

With **Microsoft Entra ID**, create an app registration first:

1. In the Microsoft Entra admin center, create an app registration.
2. Under **API permissions**, add the delegated permission that your API requires, and grant admin consent.
3. Under **Certificates & secrets**, create a client secret.

`-ClientId` is the app's Application (client) ID, `-ResourceUri` is the Application ID URI of your API, and `-Scope` is the delegated permission.

### 3. Deploy

```powershell
.\Deploy-PowerA2A.ps1 -Environment <YOUR_ENVIRONMENT_ID> -Auth None
```

The script builds the connector in a temporary folder, deploys it with PAC CLI, and deletes the folder. It never changes the files in this folder, so keys and secrets don't end up in a file you commit. It:

- applies the sign-in option you chose
- sets `HOST_SIGNING_KEY` to a new random value, which signs the task IDs for questions and confirmations. Pass `-SigningKey` to keep the same value on every deployment. See [Signed task IDs](#signed-task-ids).
- fills in the intent resolver's endpoint and key, if you pass `-IntentResolver`, `-LlmEndpoint`, and `-LlmApiKey`
- prints the connector ID

To update an existing connector, add `-ConnectorId <id>`. A new signing key is generated on each deployment, so confirmations that were waiting for an answer stop working. Pass `-SigningKey` if that matters.

The files in this folder are the **None** variant, so they deploy as they are. To deploy by hand with PAC CLI, copy the settings from the `auth` folder into `apiProperties.json` and `apiDefinition.swagger.json`, and set `HOST_SIGNING_KEY` in a temporary copy of `script.csx`.

### 4. Register the redirect URL and add the secret (Entra ID only)

After the first deployment, the script prints the connector's redirect URL. Power Platform generates a different one for each connector in each environment.

1. Register it on your app registration:

   ```powershell
   az ad app update --id <YOUR_CLIENT_ID> --web-redirect-uris `
       "https://global.consent.azure-apim.net/redirect" "<redirect-url-from-the-script>"
   ```

2. Store the client secret on the connector by running the script again with `-ConnectorId` and `-ClientSecret`:

   ```powershell
   .\Deploy-PowerA2A.ps1 -Environment <YOUR_ENVIRONMENT_ID> -Auth EntraId -ClientId <id> -ResourceUri api://your-api -Scope <permission> `
       -ConnectorId <CONNECTOR_ID> -ClientSecret <your-client-secret> -SigningKey <the-same-key-as-before>
   ```

   You can also enter the secret on the connector's **Security** tab in Power Automate.

### 5. Create a connection

In Power Automate, go to **Connections → New connection**, select your connector, and sign in or enter the key.

## First-run checklist

Work through these in order after the first deployment. Each step tests one thing, so a failure tells you where the problem is.

1. **Get Agent Card.** It needs no sign-in to your API. It confirms that the connector deployed and that your skills load.
2. **Send Message: "Is the API working?"** This runs `check_connection`. It confirms that the connector reaches your API and that the sign-in works. A failure shows what your API returned, for example `The API returned 401`.
3. **Test Routing** with the phrases that your users will say. It shows which skill each phrase reaches, without calling your API.
4. **Run Skill** with a skill that reads data. Check that the **Inputs** form matches the skill and that the fields of **Data** appear as dynamic content.
5. **Send Message** with a skill that changes data. Check that **State** is `input_required`, then send **Yes** with the same **Task ID** and **Context ID**. This confirms that confirmations work with your sign-in.
6. **Copilot Studio.** Add the agent, then ask for something that needs one of your skills. See [Use the agent in Copilot Studio](#use-the-agent-in-copilot-studio).

## Define skills

Skills are defined in `RegisterHostedSkills`, in section 2 near the top of `script.csx`. A cheat sheet of the builder methods sits just above it.

### Starter skills

The template ships with six starter skills that follow a standard shape for an API. They call placeholder endpoints on `HOST_API_BASE_URL`. Keep the ones that fit your API, change their URLs and fields, and delete the rest.

| Skill | What it shows | Example message |
|---|---|---|
| `get_item` | Read one record by ID, with a question when the ID is missing | "Get item 1234" |
| `search_items` | A list with a typed `limit`, as the default skill | "Find items about invoices" |
| `create_item` | A write that confirms first, with a length limit on text | "Create an item called Quarterly report" |
| `update_item_status` | A write with a fixed set of choices and a confirmation | "Set item 1234 to closed" |
| `delete_item` | A destructive write with a stronger confirmation | "Delete item 1234" |
| `check_connection` | A recommended diagnostic. It calls `/health`, so a failure shows the API's error. | "Is the API working?" |

**Run `check_connection` first after you deploy.** If the connector can't reach your API, or the sign-in is wrong, the answer says what the API returned, for example `The API returned 401`. Change its path to a cheap endpoint that your API has.

### Built-in help skill

The agent also has a built-in `help` skill that answers "help" and "what can you do?" with a list of your skills and an example for each, without calling your API. It's registered first, so a broad pattern in your skills can't shadow it, and its pattern only matches those exact phrases, so "help me find invoices" still goes to your skills. Set `HOST_INCLUDE_HELP` to `false` in section 1 to remove it.

### Write your own

Here's a skill in full:

```csharp
_skills.Add(new HostedSkill("get_order", "Get order")
    .Describe("Looks up one order by its number.", "Where is order 1042?", "Show me order A-77")
    .Match(@"\border\s+#?(?<orderId>[A-Za-z0-9-]+)")
    .Arg("orderId", "The order number", true, "Which order number should I look up?")
    .Handle(async call =>
    {
        var order = await HostCallApiAsync(HttpMethod.Get,
            HOST_API_BASE_URL + "/orders/" + Uri.EscapeDataString(call.Arg("orderId")));
        return HostResult.FromRecord(order, new[] { "number", "customer.name:Customer", "status" },
            "Order " + call.Arg("orderId"));
    }));
```

| Method | Purpose |
|---|---|
| `Describe(description, examples…)` | What the skill does, and example requests. They appear in the agent card. |
| `Match(pattern)` | A case-insensitive regular expression. Each **named group** becomes an argument with that name. |
| `Arg(name, description, required, prompt)` | Declares an argument. If a required argument is missing, the agent asks `prompt`. |
| `Arg(name, description, a => …)` | Declares an argument with a type, choices, a default, or limits. See [Typed arguments](#typed-arguments). |
| `Confirm(question)` | Asks the user to confirm before the handler runs. See [Confirm before changing data](#confirm-before-changing-data). |
| `Returns(sampleJson)` | Declares the shape of the skill's data, so flows show its fields as dynamic content. See [Declare the shape of a skill's data](#declare-the-shape-of-a-skills-data). |
| `FromText(argName)` | Uses the whole message text as that argument when no pattern supplies it. |
| `Default()` | Handles messages that no skill's pattern matches. |
| `Handle(async call => …)` | Calls your API and returns a `HostResult`. |

In a handler, `call.Arg("name")` reads an argument as text, and `call.Int`, `call.Number`, and `call.Bool` read typed values. `call.Text` is the message text, and `HostCallApiAsync` calls your API with the connection's `Authorization` header. Return a `HostResult`, as described in [Shape the answer](#shape-the-answer). Throw an exception to fail the task. The task's status message carries the exception text.

Register skills that change data before skills with broad patterns, because the first matching pattern wins.

### How a message is routed

The first rule that applies wins:

1. **A data part with a skill ID**, for example `{ "skill": "get_order", "input": { "orderId": "1042" } }`. Callers that know your skills use this for exact control.
2. **A pending skill.** The message answers a question or a confirmation, so it continues that skill.
3. **A pattern match.** The first skill whose `Match` pattern matches the text.
4. **The intent resolver**, if you've turned it on. A model picks the skill. See [Let a model route messages](#let-a-model-route-messages-optional).
5. **The default skill**, or the only skill.

If none apply, the task comes back `rejected` with a list of what the agent can do. Use **Test Routing** to see which rule a phrase reaches. See [Test your routing](#test-your-routing).

### Clarifying questions

When a required argument is missing, or a value isn't acceptable, the task comes back with state `input_required` and the question in its status message. The connector keeps no state, so it stores the arguments collected so far, and which argument it asked about, in the task ID. The client sends that task ID back with the answer, which the A2A protocol requires for `input_required` tasks. The answer goes to the argument that was asked, and the skill continues.

### Typed arguments

Declare a type, choices, a default, or limits in the lambda of `Arg`:

```csharp
.Arg("status", "The new status", a => a.OneOf("open", "closed", "archived"))
.Arg("limit", "How many results to return", a => a.OfType("integer").Between(1, 25).DefaultTo("10").Optional())
.Arg("email", "The customer's email", a => a.Matches(@"^[^@\s]+@[^@\s]+$", "That doesn't look like an email address."))
```

| Method | Effect |
|---|---|
| `OfType("integer" \| "number" \| "boolean")` | Checks and converts the value. `call.Int`, `call.Number`, and `call.Bool` return it typed. Text is the default type. |
| `OneOf(values…)` | Accepts only these values, ignoring case, and stores the exact spelling you listed. The question lists the choices. |
| `DefaultTo(value)` | Uses this value when none is given, so the agent doesn't ask. |
| `Between(min, max)` | Limits integers and numbers. Use `null` for no limit. |
| `MaxChars(length)` and `Matches(pattern, message)` | Limit the length and format of text. |
| `Optional()` and `Ask(prompt)` | Make the argument optional, or set the question to ask when it's missing. |

When a value fails a check, the agent says why and asks again, and your handler doesn't run. The same rules fill in the **Run Skill** form: choices become dropdowns, limits and defaults carry over, and optional arguments appear under advanced options.

### Confirm before changing data

Add `Confirm` to any skill that changes data:

```csharp
.Confirm("Set item {itemId} to {status}?")
```

When the skill has all its arguments, the agent asks the question, with `{name}` replaced by each argument's value, and waits. **Yes** runs the handler. **No** cancels with state `canceled`, and your API isn't called. Any other reply asks again.

- A data part can't change what was confirmed. The confirmed arguments are fixed in the task ID.
- A model that routes the message, including the optional intent resolver, can choose a write skill, but it can't skip the confirmation.
- **Run Skill** doesn't ask, because a flow author chose the inputs. Don't expose a write skill to people who shouldn't run it, and secure the connector's connection accordingly.
- Confirmation needs a signing key. See [Signed task IDs](#signed-task-ids).

### Signed task IDs

Questions and confirmations travel in the task ID, so the connector signs them. The ID can't be edited, it expires after 15 minutes (`HOST_PENDING_TTL_MINUTES`), and it only works for the context it came from.

The signature is built from two things: `HOST_SIGNING_KEY`, and who is calling.

- **Who is calling.** For a signed-in user with a Microsoft Entra ID token, it's the tenant and user claims in the token. A refreshed token carries the same claims, so a confirmation survives a token refresh. For any other connection, it's the whole `Authorization` header. Set `HOST_SIGN_WITH_CLAIMS` to `false` to always use the whole header, which makes a task ID stop working when the token refreshes, usually after about an hour.
- **The key.** The connector doesn't validate the token's claims, because your API does. Without a key, someone who could forge a token with the right claims could forge a task ID. `HOST_SIGNING_KEY` adds a secret that a forged token can't supply, so set it.
- **No sign-in.** With an API-key or no-auth connection there's no `Authorization` header, so the key is all there is. A skill with `Confirm` fails, with a message that names the missing key, instead of running unconfirmed. Questions still work, unsigned.

`Deploy-PowerA2A.ps1` generates a random `HOST_SIGNING_KEY` for you on every deployment. Pass `-SigningKey` to keep the same one, so confirmations that are waiting for an answer survive a redeployment. Don't commit a real value.

### Shape the answer

A skill returns a `HostResult` with text for the model, structured data for flows, or both:

| Builder | Result |
|---|---|
| `HostResult.FromText(text)` | Text only |
| `HostResult.FromData(json, text)` | Data, with optional text |
| `HostResult.FromRows(json, columns, maxRows, title, rowsPath)` | A Markdown table and trimmed data for a list |
| `HostResult.FromRecord(json, columns, title)` | A Markdown list for one record |

Columns are JSON paths, optionally with a header: `"id"`, `"customer.name:Customer"`. In the data, a path such as `customer.name` becomes the property `customer_name`. With no columns, the first scalar properties are used. `rowsPath` names the array inside the response, for example `"value"`.

`FromRows` puts only `maxRows` rows into the table and the data, and adds "Showing 10 of 240." when it trims. That keeps answers small, which matters because everything a skill returns goes into the model's context on every later turn. For a list that a flow needs in full, return it with `FromData` and set the limit yourself.

As a guard, a data artifact over `HOST_MAX_RESULT_CHARS` (30,000 characters) is trimmed. An array keeps as many items as fit, with a note, and a larger single object is dropped, with a note. The `Md` helper has `Table`, `KeyValues`, `Link`, and `Bullets` if you want to build text yourself.

### Declare the shape of a skill's data

Add `.Returns(sampleJson)` to a skill so that **Run Skill** shows the fields of **Data** as dynamic content in a flow, and builders don't need **Parse JSON**:

```csharp
.Returns(@"{ ""id"": ""1234"", ""name"": ""Quarterly report"", ""status"": ""open"" }")
```

The sample is JSON that looks like what the handler puts in `HostResult.Data`. An object gives its fields, and an array gives the fields of its first item. Numbers, text, and `true` or `false` give their types, and a `null` gives text. Field names become readable titles, so `customer_name` shows as "Customer name". Keep the sample in step with the columns that the handler returns. A skill with no `.Returns` leaves **Data** as one untyped value.

### How a skill calls your API

`HostCallApiAsync(method, url, body)` calls your API with the connection's `Authorization` header and handles the rest:

- **Retries.** A 429 is retried up to twice, after the wait that the API's `Retry-After` header asks for, capped at 10 seconds. With no header the waits are 1 and 2 seconds. A GET is also retried after a 502, 503, or 504. A write isn't retried after a server error, because it might have gone through. Change `HOST_API_RETRIES` and `HOST_API_MAX_RETRY_WAIT_SECONDS` in the advanced settings.
- **Error text.** When your API returns an error, the skill fails with one short line: `The API returned 404: Product not found`. A JSON error contributes its message, HTML is stripped, and the text is cut at `HOST_MAX_ERROR_CHARS` (300) characters, so a long error page doesn't fill the model's context.
- **Other headers.** If your API takes its key in a header other than `Authorization`, list it in `HOST_FORWARD_HEADERS`, for example `"X-API-Key"`. The deploy script sets this when you use `-Auth ApiKey`.

### Test your routing

**Test Routing** takes a message and shows what the agent would do with it, without calling your API:

| Output | Meaning |
|---|---|
| **Routed By** | `pattern`, `llm`, `default`, or `none` |
| **Skill ID** and **Skill Name** | The skill that would run |
| **Arguments** | The values the skill would receive, after defaults and type checks |
| **Missing** | Required arguments with no value. The agent would ask for them. |
| **Problem** | Why a value was rejected, such as one outside the allowed choices |
| **Confirmation Required** | Whether the agent would ask before running |
| **Reply** | What the agent would say when no skill matches |

Run it with the phrases that people use, including the awkward ones, then adjust your `Match` patterns until each phrase reaches the right skill. If the intent resolver is on, **Test Routing** calls it too, so it shows what a real message would do.

### Let a model route messages (optional)

Patterns are exact but narrow. The intent resolver sends the message and your skill catalog to a chat-completions endpoint, which picks the skill and pulls out its arguments. Set it up in `script.csx`:

```csharp
// "off", "fallback" (ask the model only when no pattern matches), or "always"
private static readonly string HOST_INTENT_RESOLVER = "fallback";

// The full chat-completions URL, for example an Azure OpenAI deployment URL with its api-version
private const string HOST_LLM_ENDPOINT = "https://<your-resource>.openai.azure.com/openai/deployments/<deployment>/chat/completions?api-version=<version>";

private const string HOST_LLM_API_KEY = "[INSERT_YOUR_LLM_API_KEY]";

// "api-key" for Azure OpenAI. "Authorization" for OpenAI-compatible endpoints, sent as Bearer.
private const string HOST_LLM_KEY_HEADER = "api-key";

// Required by OpenAI-compatible endpoints. Azure OpenAI takes it from the deployment URL.
private const string HOST_LLM_MODEL = "";
```

Use `fallback` unless you have a reason not to. Patterns stay fast, free, and predictable, and the model handles only what they miss. Each message that reaches the model adds one call and its latency, and the connector waits up to `HOST_LLM_TIMEOUT_SECONDS` (20) before it falls back to patterns.

**What the model sees.** It gets the message text, truncated to 2,000 characters, and a catalog of your skills: IDs, descriptions, examples, and argument names, types, and choices. It never sees your API's data, the user's token, or earlier messages. Messages from your users do go to the endpoint you choose, so use one that fits your data-handling requirements.

**What it can and can't do.**

- It can only pick a skill that you defined and fill arguments that the skill declares. Anything else is ignored.
- Every value it supplies goes through your argument rules, and `Confirm` still applies, so an instruction hidden in a message can't change data without the user saying yes.
- If the endpoint is slow, down, or answers in an unusable way, the connector falls back to patterns, and the request doesn't fail.
- The model can't ask clarifying questions itself. The agent asks for what's missing, as it does for any skill.

**Keep the keys out of source control.** The keys are in the script, so anyone who can edit the connector can read them. Keep the placeholders in the file you keep, and let `Deploy-PowerA2A.ps1` put the real values into a temporary copy:

```powershell
.\Deploy-PowerA2A.ps1 -Environment <YOUR_ENVIRONMENT_ID> -Auth <your-option> -ConnectorId <CONNECTOR_ID> `
    -IntentResolver fallback `
    -LlmEndpoint "https://<your-resource>.openai.azure.com/openai/deployments/<deployment>/chat/completions?api-version=<version>" `
    -LlmApiKey <your-llm-key> -SigningKey <the-same-key-as-before>
```

Pass `-LlmKeyHeader Authorization` for an OpenAI-compatible endpoint, and `-LlmModel` for the model name.

## Use the connector in a flow

### Run a skill with a typed form

**Run Skill** is the most direct way to call your API from a flow. You don't write a message and the agent doesn't have to interpret it.

1. Add **Run Skill** and pick the **Skill** from the list. The list comes from **List Skills**.
2. The **Inputs** fields change to match the skill. Required inputs show first, and optional ones are under **Show advanced options**. Choices appear as dropdowns, and numbers and yes/no values have the right types.
3. Use **Response Text** and **Data** in later actions. For a skill that declares its shape with [`.Returns`](#declare-the-shape-of-a-skills-data), the fields of **Data** appear as dynamic content. For any other skill, run **Parse JSON** on **Data** to use its fields.

**Run Skill** differs from **Send Message** in three ways:

- It never asks follow-up questions. A missing or invalid input fails the action with a message that names the input.
- A skill that fails makes the action fail, so your flow's error handling applies.
- It doesn't ask for confirmation. You chose the skill and its inputs in the flow, so the confirmation that protects a conversation isn't needed.

### Send a message and use the answer

1. Add **Send Message** and enter the **Message**.
2. Check **State** before you rely on the answer. `completed` means the skill ran. `failed` means your API returned an error, and **Status Message** has the detail. `rejected` means no skill matched.
3. Use **Response Text** or **Data** in later actions.

### Hold a multi-turn conversation

1. Send the first message with **Send Message**. Save its **Task ID** and **Context ID**.
2. If **State** is `input_required`, **Status Message** holds the agent's question. Get the answer, for example from an Approvals or Teams action.
3. Call **Send Message** again with the answer as the **Message**, and the saved **Context ID** and **Task ID**. **Task ID** is an advanced parameter.

Repeat until **State** is `completed`. The **Task ID** is required, because it carries the arguments collected so far.

Every skill finishes inside the call. For work that takes longer than the connector's two-minute limit, have the skill start the job and return a job ID, and add a second skill that checks it.

## Use the agent in Copilot Studio

`InvokeA2A` is a native A2A operation (`x-ms-agentic-protocol: a2a-1.0`). To add the agent:

1. Make sure the `InvokeA2A` `description` in `apiDefinition.swagger.json` says what the agent does and which requests it should handle, and that its `summary` matches `info.title`.
2. Open [Copilot Studio](https://copilotstudio.microsoft.com) in the same environment, open your agent, and go to **Agents → Add agent**.
3. Select your connector by its `info.title`. Don't select **A2A agent**, which creates a separate connector.
4. Create a connection, then select **Add and configure**.

Copilot Studio copies the title and description once, when you add the agent. Later connector updates don't change an agent you've already added. Ask for the task, not the tool, when you test: the agent doesn't know the name you gave it in Copilot Studio.

The `InvokeA2A` operation has an optional `x-ms-a2a-card-json` field. Leave it as `"null"`, or paste the **Get Agent Card** output into it as a JSON string if you want the card embedded.

### A2A versions

Copilot Studio sends A2A **0.3** requests (`message/send`, parts with a `kind` field, lowercase task states). `InvokeA2A` also accepts A2A **1.0** (`SendMessage`, parts without `kind`, `TASK_STATE_*` states) and answers in the era the caller used.

| A2A 0.3 | A2A 1.0 |
|---|---|
| `message/send` | `SendMessage` |
| `tasks/get` | `GetTask` |
| `tasks/cancel` | `CancelTask` |
| `agent/getAuthenticatedExtendedCard` | `GetExtendedAgentCard` |
| `role: "user"` or `"agent"` | `ROLE_USER` or `ROLE_AGENT` |
| `{ "kind": "text", "text": "…" }` | `{ "text": "…" }` |
| `{ "kind": "data", "data": {…} }` | `{ "data": {…}, "mediaType": "application/json" }` |
| `{ "kind": "file", "file": { "bytes" or "uri", "name", "mimeType" } }` | `{ "raw" or "url", "filename", "mediaType" }` |
| `input-required`, `completed`, … | `TASK_STATE_INPUT_REQUIRED`, `TASK_STATE_COMPLETED`, … |

### What a hosted agent doesn't do

A connector returns one buffered response and keeps no state between calls, so:

- Every task finishes in the call. `GetTask` and `CancelTask` return `-32001 Task not found`, and `ListTasks` returns an empty list.
- Streaming (`message/stream`, `tasks/resubscribe`, `SendStreamingMessage`, `SubscribeToTask`) returns error `-32004`. Use `SendMessage`.
- Push notifications return `-32003`.
- There's no extended agent card. `GetExtendedAgentCard` returns `-32007`.
- A call must finish within the connector's two-minute limit.
- Routing uses your patterns unless you turn on the intent resolver, so write patterns for how people phrase requests.

### Agent card

**Get Agent Card** returns an A2A 1.0 agent card built from `HOST_AGENT_NAME`, `HOST_AGENT_DESCRIPTION`, and your skills. Skills that use `Confirm` are tagged `write`. The card's URL is `HOST_PUBLIC_URL`, or the connector's request URL if you leave it empty.

## Files

| File | Purpose |
|---|---|
| `script.csx` | Your settings and skills at the top, then the framework: the A2A endpoint, routing, and the Power Automate handlers |
| `apiDefinition.swagger.json` | `InvokeA2A` plus the Power Automate operations |
| `apiProperties.json` | Connection settings and the list of script operations. As shipped, it's the **None** sign-in variant. |
| `auth\none.json`, `auth\api-key.json`, `auth\entra-id.json` | The sign-in options. `Deploy-PowerA2A.ps1` merges the one you choose. |
| `Deploy-PowerA2A.ps1` | Deploys the connector from a temporary copy, with keys, sign-in, and an optional demo |
| `demo\DummyJson.skills.txt` | The DummyJSON demo's skills and settings. Used by `-Demo` and by the tests. Not part of the connector. |
| `demo\Get-DemoScript.ps1` | The helper that swaps a demo into a copy of `script.csx` |
| `tests\` | Offline tests for the script. See [Test your skills](#test-your-skills). |

## Test your skills

`tests\Run-Tests.ps1` compiles `script.csx` with a fake connector runtime and a fake API, and runs about 200 checks. Run it after you change skills or settings:

```powershell
.\tests\Run-Tests.ps1
```

It runs six variants: `shipped` (the released script, with its generic skills), `host` (the framework, with the demo skills swapped in), and variants with the help skill off, claims-based signing off, header forwarding on, and the intent resolver on in both modes. Add `-ShowAll` to list every check.

- It builds in a temporary folder and restores only from your local NuGet cache. It never contacts nuget.org. It needs Newtonsoft.Json 13.0.3 in `%USERPROFILE%\.nuget\packages`, and stops with a clear message if it isn't there.
- The framework checks don't depend on your skills. The `shipped` variant tests the generic starter skills, so after you replace them, replace those checks in `ShippedTests` in `tests\Program.cs` with checks for your own skills. A check is a call to the script with a fake API, then assertions on the answer.
- The project file is named `Harness.csproj.template` so that editors don't try to restore it inside the repository.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **State** is `input_required` | The agent needs more information, or a confirmation | Read **Status Message**, then call **Send Message** again with the answer, the same **Context ID**, and the **Task ID** |
| **State** is `rejected` | No skill matched the message | Read **Status Message** for the list of skills. Add a `Match` pattern for the phrasing, or mark a skill `Default()`. |
| **State** is `failed` | Your API returned an error, and **Status Message** carries it | Fix the request or the API call in the skill's handler |
| The agent asks the same question again | The client didn't send back the task ID from the `input_required` task | Send the follow-up with the previous **Task ID** |
| The agent says "That request is no longer valid" | The task ID expired after 15 minutes, was edited, came from another user, or you redeployed with a new signing key | Ask again from the start. Pass `-SigningKey` to the deploy script to keep the key between deployments. |
| A write skill returns `failed` and mentions a signing key | The skill uses `Confirm`, and the connection sends no `Authorization` header | Set `HOST_SIGNING_KEY`. The deploy script does this for you. |
| `Deploy-PowerA2A.ps1` says "Choose a sign-in option" | You didn't pass `-Auth`, and you didn't use `-Demo` | Pass `-Auth None`, `ApiKey`, or `EntraId` |
| An API-key connection gets a 401 from your API | The key header doesn't match what your API expects | Redeploy with `-ApiKeyHeader` set to the header your API uses |
| **Run Skill** shows no input fields | No skill is chosen yet | Pick a skill |
| **Run Skill** shows **Data** as one value, not fields | The skill has no `.Returns(...)` | Add `.Returns` with a sample of the data, or use **Parse JSON** |
| **Run Skill** fails with a message naming an input | The value is missing or breaks an argument rule | Fix the input in the flow |
| Everything goes to the default skill | The patterns don't match how people phrase requests | Use **Test Routing** with real phrases, then adjust `Match` patterns or turn on the intent resolver |
| `check_connection` fails with `The API returned 404` | The starter skill calls `/health`, which your API may not have | Change its path to an endpoint your API has |
| The API returns a 429 and the skill fails | Your API's rate limit, even after two retries | Slow the caller down, or raise `HOST_API_RETRIES` |
| "What can you do?" goes to a skill instead of `help` | `HOST_INCLUDE_HELP` is `false`, or the phrase isn't one that `help` matches | Check `HOST_INCLUDE_HELP`. The phrases it matches are in `BuildHelpSkill`. |
| The intent resolver never runs | `HOST_INTENT_RESOLVER` is `off`, or the endpoint or key is still a placeholder | Set all three. **Test Routing** shows `routedBy: llm` when it works. |
| The intent resolver picks the wrong skill | The skill descriptions and examples are vague | Make each skill's `Describe` text specific, and add examples |
| Error `-32004` | The caller used a streaming method | Use `SendMessage` or `message/send` |
| `-32001 Task not found` | A hosted agent keeps no tasks | Read the result from **Send Message** |
| Sign-in fails with a redirect URI mismatch | The connector's redirect URL isn't registered | Repeat step 4 of [Set up your own](#set-up-your-own) |
| Sign-in fails with `AADSTS65001` | Admin consent wasn't granted | Grant admin consent for the app's API permission |
| Your API returns 401 | The token is for the wrong API | Check `scopes` and `resourceUri` against your API's Application ID URI |
| The connector isn't listed under **Agents → Add agent** | It's in a different environment, or `InvokeA2A` isn't marked `a2a-1.0` | Deploy to the environment that contains your agent. Check `x-ms-agentic-protocol`. |
| The orchestrator never calls the agent | The `InvokeA2A` description doesn't match users' requests | Edit the agent's description on the **Agents** page in Copilot Studio |

## Links

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [Use custom code in a custom connector](https://learn.microsoft.com/connectors/custom-connectors/write-code)
- [Connect an agent available over A2A in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/add-agent-agent-to-agent)
