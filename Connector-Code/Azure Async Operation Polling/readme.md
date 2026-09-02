# Azure Async Operation Polling

A reusable pattern for calling long-running Azure and Microsoft 365 operations from a custom connector.

## The problem

Custom connector scripts are capped at **two minutes**. Many operations exceed that:

| Operation | Typical duration |
|---|---|
| Storage account create | 15–30 seconds |
| ARM template deployment | seconds to hours |
| Fabric notebook or Spark job | minutes |
| Foundry agent run | seconds to minutes |
| Power BI export | seconds to minutes |
| Document Intelligence analyze | seconds to minutes |

Waiting inline works in testing and fails intermittently under load. Intermittent failures are worse than consistent ones because they surface late and are hard to reproduce.

## The pattern

Split the operation across two connector actions and let the **flow** do the waiting.

| Action | Behavior |
|---|---|
| **Start** | Issues the request. The moment it detects a long-running operation, returns an opaque handle. Never polls. |
| **Poll** | Takes the handle and performs **exactly one** status check. |

Both return the same envelope:

```json
{
  "status": "Running",
  "isComplete": false,
  "retryAfterSeconds": 15,
  "operationHandle": "eyJ1IjoiaHR0cHM6Ly8...",
  "result": null,
  "error": null
}
```

`status` is normalized to `Running`, `Succeeded`, `Failed`, or `Canceled`, so the calling flow only ever branches on four values.

Because the script returns in seconds regardless of how long the operation takes, the two-minute cap is never approached.

## Flow shape

1. **Start** — returns `operationHandle` and `retryAfterSeconds`.
2. **Initialize variables** — `handle`, `isComplete`, `finalStatus` from the start response.
3. **Do-Until** `isComplete` is `true`:
   - **Delay** by `retryAfterSeconds`
   - **Poll** with the current `handle`
   - **Set** `isComplete` and `finalStatus`
   - **Condition:** if the response's `operationHandle` is non-empty, set `handle` to it
4. After the loop, branch on `status`.

> **Why the condition in step 3?** Power Automate rejects a `Set variable` action whose value references the variable being set — *"Self reference is not supported when updating the value of variable"*. So `coalesce(body('Poll')?['operationHandle'], variables('handle'))` will not save. A condition also correctly leaves the handle untouched on the final poll, when the connector returns a null handle.

## Usage

Copy [`script.csx`](./script.csx) into your connector and adapt three things:

```csharp
// 1. Your start operationIds
private static readonly HashSet<string> START_OPERATIONS = new HashSet<string>(StringComparer.Ordinal)
{
    "StartLongRunningOperation"
};

// 2. Your poll operationId
private const string POLL_OPERATION = "GetOperationStatus";

// 3. The query parameter carrying the handle
private const string HANDLE_PARAMETER = "operationHandle";
```

Then review `NormalizeStatus` and `NormalizeError` against your service — see the tables below.

Add every start and poll operation to `scriptOperations` in `apiProperties.json`.

## Two async shapes

Services signal long-running operations in two different ways, and they behave differently on poll. The helper detects which applies at start time and records it in the handle, so it is not re-sniffed on every poll.

| Shape | Signalled by | Poll returns |
|---|---|---|
| **Status document** | `Azure-AsyncOperation` or `Operation-Location` | `200` with `{"status": "..."}` — inspect the field |
| **Location** | `Location` on a `202` | `202` while running, then `200` carrying the **result payload** |

Two details that are easy to get wrong, both confirmed against live Azure Resource Manager:

- **An async header wins over the status code.** Network security group creation returns **HTTP 201 — not 202 — with `Azure-AsyncOperation`**. Treating any non-202 as synchronous completion reports success while the resource is still provisioning.
- **`Location` only means "poll me" on a 202.** On a 201 it usually points at the resource just created, so treating it as a poll target would poll the resource and mistake its body for an operation status.

## Per-service differences

The pattern is uniform; the details are not. Verify these for your service before shipping.

| Service | Header | Status field | Error shape |
|---|---|---|---|
| Azure Resource Manager | `Azure-AsyncOperation`, or `Location` on 202 | `status`, or `properties.provisioningState` | `{ "error": { "code", "message", "details" } }` |
| Microsoft Fabric | `Location` + `Retry-After` | `status` | `{ "errorCode", "message", "moreDetails": [] }` |
| Foundry / Cognitive Services | `Operation-Location` | `status` | `{ "error": { "code", "innererror": {} } }` |
| Power BI | polled by export or refresh ID | `status` | `{ "error": { "code", "message" } }` |

### Status vocabularies

`NormalizeStatus` collapses these. Extend the `Running` case for any service that introduces new terms.

| Service | Running states | Terminal states |
|---|---|---|
| Azure Resource Manager | `InProgress`, `Accepted`, `Updating`, `Resuming` | `Succeeded`, `Failed`, `Canceled` |
| Microsoft Fabric | `NotStarted`, `Running`, `Undetermined` | `Completed`, `Failed`, `Cancelled` |
| Foundry / Cognitive Services | `notStarted`, `running` | `succeeded`, `failed` |

An unrecognized value is treated as **`Running`**, never as success, so a new or misspelled state can never be mistaken for completion.

## One connector cannot span services

Each service requires a **different OAuth audience**, and a connector connection carries one audience:

| Service | Audience |
|---|---|
| Azure Resource Manager | `https://management.azure.com` |
| Microsoft Fabric | `https://api.fabric.microsoft.com` |
| Foundry Agent Service | `https://ai.azure.com` |
| Cognitive Services | `https://cognitiveservices.azure.com` |
| Power BI | `https://analysis.windows.net/powerbi/api` |

So a single "async operations" connector spanning ARM, Fabric, and Foundry is not possible. Build one connector per service and embed this helper in each. That is why this is a **template** rather than a shared connector.

## The operation handle

Base64url-encoded JSON holding the poll URL and its shape — opaque but debuggable:

```json
{ "u": "https://management.azure.com/...", "k": "location", "v": 1 }
```

It round-trips through a flow variable where a user can edit it, and each poll carries a bearer token. The helper therefore **rejects any handle whose poll URL is not absolute HTTPS**, so a tampered handle cannot redirect credentials to another host. Keep that check when adapting the template.

## Error reporting

Both actions always return **HTTP 200**. Operation failure is reported inside the envelope so the calling flow branches on `status` rather than handling a raised connector error mid-loop.

`error.httpStatus` is present **only when the transport itself failed**. An operation that fails while the poll returns HTTP 200 omits it, since `"httpStatus": 200` on an error object reads as a contradiction.

## Verification

The helper was validated against live Azure Resource Manager with 66 assertions covering:

- Both async shapes — `Location` (storage account create) and `Azure-AsyncOperation` (network security group create, which returns 201).
- Timing guarantees — start makes exactly one outbound call and returns in under a second; the slowest poll observed was 0.3 seconds.
- Synchronous failures — 404 for a missing resource group, 400 for an invalid resource name.
- Asynchronous failure — an ARM template deployment that fails during provisioning, with the inner error preserved in `details`.
- Inline completion — a POST action that returns a result with no async header.
- Handle tampering — non-HTTPS poll URLs, malformed and empty handles all rejected.
- All three error shapes and the full status vocabulary.

## Reference implementation

[Azure Async Operations](../Azure%20Async%20Operations/) is a complete connector built on this template, deployed and exercised end to end through a Power Automate flow.

## Author

Troy Taylor — [troy@troystaylor.com](mailto:troy@troystaylor.com) — [GitHub](https://github.com/troystaylor)
