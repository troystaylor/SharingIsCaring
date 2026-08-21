# Oracle Fusion Cloud Financials

A dual-mode custom MCP connector for the [Oracle Fusion Cloud Financials REST API](https://docs.oracle.com/en/cloud/saas/financials/26c/farfa/rest-endpoints.html). It exposes:

- **Typed operations** for Power Automate — generic CRUD over any Financials resource, child-record reads, resource actions, ERP integration job submission, and a curated **Search payables invoices** action, all with IntelliSense.
- **An MCP endpoint** (`/mcp`) for Copilot Studio — the same capabilities as agent tools.

Because the Oracle Fusion Financials REST surface is uniform (every resource follows the same ADF REST conventions — `q`, `finder`, `fields`, `expand`, `limit`, `onlyData`, and a standard `{ items, count, hasMore }` envelope), a small set of generic operations covers the **entire** API: roughly 300 top-level resources spanning General Ledger, Payables, Receivables, Cash Management, Expenses, Tax, Budgetary Control, Intercompany, Joint Venture, Revenue Management, and Federal Financials.

The designer experience is **metadata-driven**: the **Resource** picker, **Fields** / **Expand** / **Child Resource** pickers, the **Action** picker, and the **Create/Update body form** are all populated live from your pod's own `/describe` metadata, so makers get real dropdowns and a typed body form for whatever resource they pick — without the connector hard-coding hundreds of schemas.

## Capabilities

### Typed operations (Power Automate)

| Operation | Method | Path |
|-----------|--------|------|
| Query records | GET | `/{resource}` |
| Get record | GET | `/{resource}/{id}` |
| Create record | POST | `/{resource}` |
| Update record | PATCH | `/{resource}/{id}` |
| Delete record | DELETE | `/{resource}/{id}` |
| Get child records | GET | `/{resource}/{id}/child/{childResource}` |
| Describe resource | GET | `/{resource}/describe` |
| Invoke resource action | POST | `/{resource}/action/{action}` |
| Search payables invoices | GET | `/invoices` |
| Submit ERP integration request | POST | `/erpintegrations` |
| Get ERP job status | GET | `/erpintegrations` |

All paths are relative to `/fscmRestApi/resources/11.13.18.05`.

### MCP tools (Copilot Studio)

| Tool | Purpose |
|------|---------|
| `list_common_resources` | Lists frequently used Financials resource names, grouped by module. Accepts an optional `module` filter. |
| `describe_resource` | Returns a resource's fields, actions, and child resources. |
| `query_records` | Queries a resource with `q` / `finder` / `fields` / `expand` / `orderBy` / `limit` / `offset`. |
| `get_record` | Retrieves a single record by key. |
| `get_child_records` | Retrieves a parent record's child rows (invoice lines, distributions, installments). |
| `create_record` | Creates a record from a JSON object. |
| `update_record` | Partially updates (PATCH) a record. |
| `delete_record` | Deletes a record. |
| `list_resource_actions` | Lists the custom business actions a resource publishes. |
| `invoke_resource_action` | Runs an action such as `validateInvoice` or `cancelInvoice`. |
| `search_invoices` | Convenience search over payables invoices by number, supplier, or business unit. |
| `submit_ess_job` | Submits an Enterprise Scheduler job (imports, posting, payment runs). |
| `get_ess_job_status` | Polls a submitted ESS job for its status. |

## Prerequisites

1. **An Oracle Fusion Cloud Financials pod** and a user account with REST access and the appropriate Financials data security roles (for example *Accounts Payable Manager*, *General Accountant*, or *Accounts Receivable Specialist* — role assignment determines which resources and business units the connection can reach).
2. **An OAuth 2.0 confidential application** registered in your identity provider (Oracle IDCS or OCI IAM) that is authorized to call the Fusion Applications resource:
   - Allowed grant types: **Authorization Code** and **Refresh Token**.
   - Redirect URL: `https://global.consent.azure-apis.com/redirect` (Power Platform global redirect).
   - Add the **Oracle Applications Cloud (Fusion apps)** resource as an allowed scope so the issued token can call `/fscmRestApi`.
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

### Script operation coverage

`script.csx` routes **every** operation in the swagger, so it is safe to enable the custom code for all 18:

- **`InvokeMCP`** is answered in-process as JSON-RPC 2.0.
- **The six `/resolvers/*` operations** are answered in-process from the pod's `/describe` metadata.
- **The eleven typed REST operations** are forwarded to the pod unchanged, apart from `InvokeResourceAction`, which is re-tagged with Oracle's required `application/vnd.oracle.adf.action+json` media type.
- **Unrecognized operation ids** are forwarded rather than treated as MCP traffic, so adding a new pass-through operation to the swagger needs no script change.

If you add the script through the maker portal instead of the CLI, enable it for all operations listed in `scriptOperations`.

> **Custom code requires compute.** Saving custom code provisions a function app for the environment. If that pool is exhausted in your region, both `pac connector create --script-file` and the portal's **Code** tab fail with `CustomScriptProvisioningFailed` / `[FindAndAssignFunctionApp] Unable to find an unassigned function app`. This is a platform capacity condition, not a connector defect — retry later, or use an environment in another region. The swagger alone deploys fine in the meantime, but omitting the script leaves the MCP endpoint and the dynamic pickers non-functional.

## Usage examples

The fictional company **Zava** wants to automate its accounts payable and month-end close.

**Find an invoice (Search payables invoices):**
- Filter (q): `Supplier LIKE '%Zava Supply%' and InvoiceAmount>5000`
- Fields: `InvoiceId,InvoiceNumber,InvoiceAmount,InvoiceDate,PaidStatus`

**Read the invoice lines (Get child records):**
- Resource: `invoices`
- Record Key: `300100012345678`
- Child Resource: `invoiceLines`

**Validate an invoice (Invoke resource action):**
- Resource: `invoices`
- Action: `validateInvoice`
- Body: `{ "invoiceId": 300100012345678 }`

**Query any resource (Query records):**
- Resource: `ledgerBalances`
- Filter (q): `PeriodName='JAN-26' and Currency='USD'`
- Limit: `50`

**Run the invoice import (Submit ERP integration request):**
- Operation Name: `submitESSJobRequest`
- Job Package Name: `/oracle/apps/ess/financials/payables/invoices/transactions`
- Job Definition Name: `APXIIMPT`
- ESS Parameters: the comma-separated parameter list the job expects

Then poll **Get ERP job status** with `finder` set to `ESSJobStatusRF;requestId=<ReqstId>` until `RequestStatus` is `SUCCEEDED`.

**Copilot Studio agent:** connect the MCP endpoint and ask *"Find unpaid Zava invoices over $5,000, show me the lines on the largest one, then validate it."* The agent calls `search_invoices`, `get_child_records`, then `list_resource_actions` and `invoke_resource_action`.

## Notes

- **Filtering** uses the ADF query grammar (RSQL-like): `Attr='value'`, `Attr LIKE 'v%'`, `A=1 and B=2`. Quote string literals with single quotes.
- **Finders** are Oracle's named queries and are often the fastest path to a record: `PrimaryKey;InvoiceId=300100012345678`. `Get ERP job status` uses the `ESSJobStatusRF` finder.
- **Record keys** for `get`/`update`/`delete` are the primary key values Oracle returns in each record's `links[].href` (self link) — for invoices this is the `invoicesUniqID`, which is not always a plain numeric id.
- **Updates do not cascade.** Oracle applies a PATCH only to the attributes you send; dependent fields such as terms date or due date are **not** recalculated. Review related attributes after changing a key field like the invoice date.
- **Actions vs. delete.** Accounted or paid documents generally cannot be deleted. Use the resource action instead — `cancelInvoice`, `cancelLine`, `initiateStopPayment`, and so on. Actions are sent with Oracle's `application/vnd.oracle.adf.action+json` content type, which the connector sets for you.
- **Action discovery** reads the live `/describe` document. If a pod is unreachable or omits the actions block, the connector falls back to the documented action list for the well-known resources (`invoices`, `payablesPayments`, `expenses`, `intercompanyAgreements`, and others).
- **Long-running work is asynchronous.** Imports, journal posting, payment process requests, and accounting programs all run as ESS jobs. Submit them with `erpintegrations` and poll for status rather than expecting a synchronous result.
- **Dynamic pickers** (Resource, Fields, Expand, Child Resource, Action, and the Create/Update body form) apply to the **Power Automate / Power Apps designer** only. The **Fields** and **Expand** pickers select one attribute at a time — for multiple values, pick one and add the rest as a comma-separated list, or type a custom value. Copilot Studio agents use the MCP tools, which accept free-form JSON and are unaffected.
- **Security is enforced by Oracle**, not by the connector. The signed-in user's data roles determine which business units, ledgers, and resources are visible, so scope the service account deliberately.
