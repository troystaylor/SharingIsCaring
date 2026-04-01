# Coupa

Connector for the Coupa procurement platform. Access purchase orders, invoices, requisitions, suppliers, and other procurement resources via the Coupa Core API. Includes an MCP endpoint for Copilot Studio.

## Publisher: Troy Taylor

## Prerequisites

- A Coupa instance with API access enabled
- An OAuth 2.0 / OpenID Connect client configured in Coupa (Setup > Integrations > OAuth2/OpenID Connect Clients)
- Appropriate API scopes assigned to the OIDC client
- (Optional) An Azure Application Insights resource for telemetry

## Setting Up the Coupa OIDC Client

1. In Coupa, navigate to **Setup > Integrations > OAuth2/OpenID Connect Clients**
2. Click **Create**
3. Select **Authorization Code** as the grant type
4. Enter a name for the client (e.g., "Power Platform Connector")
5. Set the Redirect URI to the Power Platform connector redirect URL
6. Assign the following scopes (at minimum):
   - `core.purchase_order.read` / `core.purchase_order.write`
   - `core.invoice.read` / `core.invoice.write`
   - `core.requisition.read` / `core.requisition.write`
   - `core.supplier.read` / `core.supplier.write`
   - `core.user.read` / `core.user.write`
   - `core.accounting.read`
   - `core.common.read`
   - `core.expense.read`
   - `core.contract.read`
   - `core.approval.read`
7. Save and note the **Client ID** and **Client Secret**

## Configuring the Connector

Before importing, update the following placeholders:

### apiDefinition.swagger.json
- Replace `YOUR_INSTANCE.coupahost.com` in the `host` field with your Coupa instance URL
- Replace `YOUR_INSTANCE` in `securityDefinitions` URLs with your instance name

### apiProperties.json
- Replace `YOUR_CLIENT_ID` with your OIDC client identifier
- Replace `YOUR_INSTANCE` in the `AuthorizationUrl`, `TokenUrl`, and `RefreshUrl` with your instance name

### script.csx (Application Insights)
- Replace the empty `APP_INSIGHTS_CONNECTION_STRING` value with your Application Insights connection string
- Get the connection string from: **Azure Portal > Application Insights resource > Overview > Connection String**
- Format: `InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;LiveEndpoint=https://REGION.livediagnostics.monitor.azure.com/`
- Leave empty to disable telemetry (no performance impact when disabled)

## Application Insights Telemetry

The connector includes optional Application Insights integration for monitoring and diagnostics. When enabled, the following events are logged:

| Event | Description |
|-------|-------------|
| `RestOperation` | Fired on each REST passthrough operation with the operationId |
| `McpRequest` | Fired on each MCP JSON-RPC method call |
| `McpError` | Fired on parse or internal errors with error type and message |
| `RequestCompleted` | Fired after every request with duration in milliseconds |

All events include a `CorrelationId` for end-to-end request tracing.

## Supported Operations

### Reference Data
| Operation | Description |
|-----------|-------------|
| List Suppliers | Query suppliers with optional filters |
| Get Supplier | Get supplier details by ID |
| Create Supplier | Create a new supplier |
| Update Supplier | Update an existing supplier |
| List Users | Query users |
| Get User | Get user details by ID |
| Create User | Create a new user |
| Update User | Update an existing user |
| List Accounts | Query chart of accounts |
| Get Account | Get account details by ID |
| List Departments | Query departments |
| Get Department | Get department by ID |
| List Addresses | Query addresses |
| Get Address | Get address by ID |
| List Items | Query catalog items |
| Get Item | Get item by ID |
| List Currencies | Query currencies |
| List Payment Terms | Query payment terms |
| List Exchange Rates | Query exchange rates |
| List Lookup Values | Query lookup values |
| List Projects | Query projects |
| Get Project | Get project by ID |

### Transactional
| Operation | Description |
|-----------|-------------|
| List Purchase Orders | Query POs with status/supplier/date filters |
| Get Purchase Order | Get PO details with line items |
| Create Purchase Order | Create an external purchase order |
| Update Purchase Order | Update a PO |
| Issue Purchase Order | Issue and send PO to supplier |
| Cancel Purchase Order | Cancel a PO |
| Close Purchase Order | Close a PO |
| List Purchase Order Lines | Query PO lines |
| List Invoices | Query invoices with filters |
| Get Invoice | Get invoice details with line items |
| Create Invoice | Create a draft invoice |
| Update Invoice | Update an invoice |
| Submit Invoice | Submit draft invoice for approval |
| Void Invoice | Void an approved invoice |
| List Requisitions | Query requisitions |
| Get Requisition | Get requisition details |
| Create Requisition | Create a draft requisition |
| Update Requisition | Update a requisition |
| List Approvals | Query approvals |
| Get Approval | Get approval details |
| List Expense Reports | Query expense reports |
| Get Expense Report | Get expense report details |
| List Contracts | Query contracts |
| Get Contract | Get contract details |
| List Receipts | Query receipts |

### MCP (Copilot Studio)
| Operation | Description |
|-----------|-------------|
| Invoke Coupa MCP | JSON-RPC 2.0 endpoint with 13 procurement tools |

## Design Notes

- **No DELETE operations**: Coupa does not support DELETE on any resource. Use PUT to deactivate records.
- **Pagination**: List operations return up to 50 records by default. Use `offset` and `limit` parameters for pagination.
- **JSON only**: This connector uses JSON format. Coupa requires matching `Content-Type` and `Accept` headers, which the script handles automatically.
- **Token expiry**: OAuth tokens expire after 24 hours. Refresh tokens expire after 90 days.
- **Rate limiting**: Coupa recommends including a 5-second buffer between token generation and first API call.

## Known Issues and Limitations

- Country-specific compliance fields are not included in the schema definitions to keep the connector manageable
- The GraphQL endpoint is not included; use the REST operations instead
- CSV flat file operations are not supported through this connector
