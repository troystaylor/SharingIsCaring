# Azure Async Operations

Start long-running Azure Resource Manager operations from Power Automate and poll them to completion.

Custom connector scripts are capped at **two minutes**. Azure long-running operations routinely exceed that — a storage account takes 15–30 seconds, but virtual machines, App Service plans, AKS clusters, and template deployments take far longer. Waiting inline produces intermittent timeouts, which are worse than consistent failures because they only appear under load.

This connector never waits. Starting an operation returns an opaque handle immediately; a separate action performs exactly one status check per call. **The waiting belongs in your flow's Do-Until loop, not in the script.**

## Operations

| Operation | Method | Description |
|-----------|--------|-------------|
| `CreateOrUpdateResource` | PUT | Create or update any ARM resource. |
| `DeleteResource` | DELETE | Delete any ARM resource. |
| `InvokeResourceAction` | POST | Invoke an action such as `restart`, `start`, `deallocate`, or `regenerateKey`. |
| `CreateOrUpdateChildResource` | PUT | Create or update a nested resource such as a subnet or VM extension. |
| `DeleteChildResource` | DELETE | Delete a nested resource. |
| `GetOperationStatus` | GET | Perform one status check for a handle. |

All operations return the same envelope:

```json
{
  "status": "Running",
  "isComplete": false,
  "retryAfterSeconds": 15,
  "operationHandle": "eyJ1IjoiaHR0cHM6Ly9tYW5hZ2VtZW50...",
  "result": null,
  "error": null
}
```

`status` is normalized to one of `Running`, `Succeeded`, `Failed`, or `Canceled`, so your flow only ever branches on four values.

### Example paths

| Goal | Path |
|------|------|
| Create a storage account | `/subscriptions/{sub}/resourcegroups/{rg}/providers/Microsoft.Storage/storageAccounts/{name}` |
| Restart a virtual machine | `/subscriptions/{sub}/resourcegroups/{rg}/providers/Microsoft.Compute/virtualMachines/{vm}/restart` |
| Add a subnet to a virtual network | `/subscriptions/{sub}/resourcegroups/{rg}/providers/Microsoft.Network/virtualNetworks/{vnet}/subnets/{subnet}` |
| Deploy an ARM template | `/subscriptions/{sub}/resourcegroups/{rg}/providers/Microsoft.Resources/deployments/{name}` |

Template deployments use `CreateOrUpdateResource` — they fit the standard resource path shape.

## Flow pattern

1. **Start action** — returns `operationHandle` and `retryAfterSeconds`.
2. **Initialize variables** — `handle`, `isComplete`, `finalStatus` from the start response.
3. **Do-Until** `isComplete` is `true`:
   - **Delay** by `retryAfterSeconds`
   - **Get Operation Status** with the current `handle`
   - **Set** `isComplete` and `finalStatus` from the response
   - **Condition:** if the response's `operationHandle` is non-empty, set `handle` to it
4. After the loop, check `status`. On `Succeeded` read `result`; on `Failed` read `error`.

Set the Do-Until iteration limit to cover your worst-case duration. Each iteration costs one connector call.

> **Why the condition in step 3?** Power Automate rejects a `Set variable` action whose value references the variable being set — *"Self reference is not supported when updating the value of variable"*. So `coalesce(body('Get_status')?['operationHandle'], variables('handle'))` will not save. Guard the update with a condition instead, which also correctly leaves the handle untouched on the final poll, when the connector returns `operationHandle: null`.

## Error handling

Every action returns **HTTP 200**. Operation failure is reported inside the envelope so the flow branches on `status` rather than handling a raised connector error mid-loop.

```json
{
  "status": "Failed",
  "isComplete": true,
  "retryAfterSeconds": 0,
  "operationHandle": null,
  "error": {
    "code": "ResourceGroupNotFound",
    "message": "Resource group 'rg-missing' could not be found.",
    "httpStatus": 404
  }
}
```

Azure error shapes differ by service, and all are normalized to `code`, `message`, and optional `details`:

| Service | Raw shape |
|---|---|
| Azure Resource Manager | `{ "error": { "code", "message", "details" } }` |
| Microsoft Fabric | `{ "errorCode", "message", "moreDetails": [] }` |
| Cognitive Services | `{ "error": { "code", "innererror": {} } }` |

`error.httpStatus` is present **only when the request itself failed**. An operation that fails while the status check returns HTTP 200 omits it, since `"httpStatus": 200` on an error object reads as a contradiction.

For a failed template deployment, the underlying cause is preserved in `details`:

```json
"error": {
  "code": "DeploymentFailed",
  "message": "At least one resource deployment operation failed.",
  "details": [{ "code": "Conflict", "message": "...StorageAccountAlreadyTaken..." }]
}
```

Connector-level failures use their own codes: `InvalidOperationHandle`, `NoPollingLocation`, and `ConnectorScriptError`.

## Prerequisites

- An Azure subscription and permission to the resources you intend to manage.
- An Entra app registration for the connector (see below).
- A Power Platform environment with custom connector permissions.

## Setup

### 1. Register an Entra application

```powershell
az ad app create --display-name "Azure Async Operations Connector" `
    --sign-in-audience AzureADMyOrg `
    --web-redirect-uris "https://global.consent.azure-apim.net/redirect"
```

Add the **Azure Service Management** `user_impersonation` delegated permission and grant admin consent:

```powershell
az ad app permission add --id <appId> `
    --api 797f4846-ba00-4fd7-ba43-dac1f8f63013 `
    --api-permissions 41094075-9dad-400e-a0bd-54e686782033=Scope
az ad app permission admin-consent --id <appId>
```

Create a client secret:

```powershell
az ad app credential reset --id <appId> --append --display-name "connector" --years 1
```

### 2. Deploy the connector

`apiProperties.json` ships with placeholders. Replace `[YOUR_CLIENT_ID]` with your application ID before deploying.

Leave `[YOUR_REDIRECT_URL]` as-is — the platform discards whatever is supplied and generates its own value, as described below.

```powershell
pac connector create `
    -df apiDefinition.swagger.json `
    -pf apiProperties.json `
    -sf script.csx
```

> Never commit the client secret. Supply it through a temporary copy of `apiProperties.json` with `clientSecret` added under `oAuthSettings`, passed to `pac connector update`, or set it in the portal's **Security** tab.

### 3. Register the generated redirect URI

**This step is required and easy to miss.** On deploy, the platform replaces `redirectUrl` with a generated value:

```text
https://global.consent.azure-apim.net/redirect/{publisher}_{connector-name}_{environment-id}
```

The name is encoded lossily — `_` becomes `-5f` and a space becomes `-20` — so a connector named `Azure Async Operations` with publisher prefix `new` produces:

```text
new-5fazure-20async-20operations-5fa1b2c3d4e5f6a7b8
```

The trailing segment identifies the **environment** and is constant across every connector in it; only the name portion varies. Read the value back and add it to the app registration, or OAuth consent will fail:

```powershell
pac connector download --connector-id <id> --outputDirectory ./verify
# read properties.connectionParameters.token.oAuthSettings.redirectUrl

az ad app update --id <appId> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<generated-url>"
```

Deploying the same connector to a second environment produces a different URL. Both must be registered.

## How the polling works

Azure has two distinct async shapes that behave differently on poll. The connector detects which applies at start time and records it in the handle, so it is not re-sniffed on every poll.

| Shape | Signalled by | Poll returns |
|---|---|---|
| Status document | `Azure-AsyncOperation` or `Operation-Location` | `200` with `{"status": "..."}` — inspect the field |
| Location | `Location` on a `202` | `202` while running, then `200` carrying the **provisioned resource** |

Two details that are easy to get wrong, both confirmed against live ARM:

- **An async header wins over the status code.** Network security group creation returns **HTTP 201 — not 202 — with `Azure-AsyncOperation`**. Treating any non-202 as synchronous completion reports success while the resource is still provisioning.
- **`Location` only means "poll me" on a 202.** On a 201 it usually points at the resource just created, so treating it as a poll target would poll the resource and mistake its body for an operation status.

Some operations complete inline with no async header at all — `listKeys`, for example. Those return `isComplete: true` with the response body in `result` on the first call, and never need polling.

Unrecognized status vocabulary is treated as `Running`, never as success, so an unknown state cannot be mistaken for done.

The polling logic is available as a standalone template at [Azure Async Operation Polling](../Azure%20Async%20Operation%20Polling/), along with per-service differences for Fabric, Foundry, and Power BI. This connector is ARM-scoped because a connector connection carries a single OAuth audience.

## The operation handle

The handle is base64url-encoded JSON holding the poll URL and its shape. It is opaque but debuggable:

```json
{ "u": "https://management.azure.com/...", "k": "location", "v": 1 }
```

It round-trips through a Power Automate variable where a user can edit it, and each poll carries a bearer token. The connector therefore **rejects any handle whose poll URL is not absolute HTTPS**, so a tampered handle cannot redirect credentials to another host.

## Verification

The script was exercised against live Azure Resource Manager with **66 assertions**:

| Area | Coverage |
|---|---|
| Async shapes | `Location` (storage account create) and `Azure-AsyncOperation` (NSG create, which returns 201) |
| Timing | Start makes exactly one outbound call and returns in under a second; slowest poll observed was 0.3s |
| Synchronous failures | 404 `ResourceGroupNotFound`, 400 `AccountNameInvalid` |
| Asynchronous failure | Template deployment failing during provisioning, inner conflict preserved in `details` |
| Inline completion | `listKeys` POST action returning a result with no async header |
| Child resources | Subnet create and delete on a nested path |
| Error shapes | ARM, Fabric, and Cognitive Services, plus empty, non-JSON, and array bodies |
| Status vocabulary | All known running and terminal states, plus unknown values defaulting to `Running` |
| Handle tampering | Non-HTTPS poll URLs, malformed and empty handles all rejected |

After `pac connector create`, the uploaded `script.csx` was downloaded back and compared byte-for-byte against source — identical, confirming the platform accepted and compiled it.

The deployed connector was then exercised **end to end through a Power Automate flow** using the Do-Until pattern above:

```json
{
  "startStatus": "Running",
  "startIsComplete": "False",
  "startRetryAfter": "17",
  "finalStatus": "Succeeded",
  "isComplete": "True",
  "polls": "3",
  "provisioningState": "Succeeded"
}
```

The start action returned `Running` without blocking, `Retry-After: 17` propagated from ARM into `retryAfterSeconds`, three polls were required, and the storage account was independently confirmed to exist. The conditional handle update was correctly **skipped** on the final poll, where the connector returns a null handle.

## Notes on deployment behaviour

The platform rewrites parts of `apiProperties.json` on ingest, so a downloaded file will not match what was uploaded.

| Field | Sent | Stored |
|---|---|---|
| `redirectMode` | `Global` | `GlobalPerConnector` |
| `redirectUrl` | `[YOUR_REDIRECT_URL]` | generated per-connector URL |
| `IsOnbehalfofLoginSupported` | `false` | `true` |
| `capabilities` | `["actions"]` | `[]` |
| `publisher` / `stackOwner` | set | empty |
| `token:TenantId` | absent | auto-added |

Preserved verbatim: `clientId`, `scopes`, `AzureActiveDirectoryResourceId`, `resourceUri`, `loginUri`, `tenantId`, `enableOnbehalfOfLogin`, `scriptOperations`.

Because `clientId` is preserved, a placeholder left there deploys as-is and the connector will not authenticate. `redirectUrl` is discarded either way.

## Application Insights

`script.csx` carries the repo-standard hardcoded telemetry constant. Replace `[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]` to enable it; while the placeholder is present, telemetry is skipped. Events emitted: `AsyncOperation_Started`, `AsyncOperation_StartFailed`, and `AsyncOperation_Error`.

## Author

Troy Taylor — [troy@troystaylor.com](mailto:troy@troystaylor.com) — [GitHub](https://github.com/troystaylor)
