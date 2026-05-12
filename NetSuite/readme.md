# NetSuite

Power Platform custom MCP connector for Oracle NetSuite REST Web Services. Exposes record CRUD, SuiteQL queries, collection filtering, sublist operations, and metadata retrieval — both as individual connector actions and as an MCP endpoint for Copilot Studio.

## Operations

| Operation | Method | Description |
|-----------|--------|-------------|
| Invoke NetSuite MCP | POST `/` | MCP protocol endpoint for Copilot Studio agents |
| Run SuiteQL Query | POST `/suiteql` | Execute SQL-like SuiteQL queries |
| List Records | GET `/record/{recordType}` | List/filter record instances |
| Get Record | GET `/record/{recordType}/{recordId}` | Get a single record by ID |
| Create Record | POST `/record/{recordType}` | Create a new record |
| Update Record | PATCH `/record/{recordType}/{recordId}` | Update record fields (partial) |
| Delete Record | DELETE `/record/{recordType}/{recordId}` | Permanently delete a record |
| List Record Types | GET `/metadata` | List all available record types |
| Get Record Metadata | GET `/metadata/{recordType}` | Get field schema for a record type |
| Get Sublist | GET `/record/{recordType}/{recordId}/{sublistId}` | Get sublist line items |
| Add Sublist Line | POST `/record/{recordType}/{recordId}/{sublistId}` | Add a line to a sublist |
| Update Sublist Line | PATCH `/record/{recordType}/{recordId}/{sublistId}/{lineId}` | Update a sublist line |
| Delete Sublist Line | DELETE `/record/{recordType}/{recordId}/{sublistId}/{lineId}` | Remove a sublist line |

## Prerequisites

1. **NetSuite Account** with REST Web Services enabled (Setup > Company > Enable Features > SuiteTalk)
2. **OAuth 2.0 Integration Record** in NetSuite:
   - Navigate to Setup > Integration > Manage Integrations > New
   - Enable **Token-Based Authentication** and **OAuth 2.0**
   - Set callback URL to `https://global.consent.azure-apim.net/redirect`
   - Note the **Client ID** and **Client Secret** (shown once)
   - Under Scope, enable `rest_webservices`
3. **Account ID**: Found at Setup > Company > Company Information (e.g., `1234567` or `TSTDRV1234567_SB1` for sandbox — replace hyphens with underscores)

## Setup

1. In [apiProperties.json](apiProperties.json), replace `[[REPLACE_WITH_NETSUITE_CLIENT_ID]]` with your Client ID
2. In [script.csx](script.csx), replace `[[REPLACE_WITH_ACCOUNT_ID]]` in the `NETSUITE_BASE_URL` constant with your NetSuite account ID
3. Import via Power Platform Maker portal → Custom connectors → Import OpenAPI file
4. Configure the OAuth 2.0 security with your Client ID and Client Secret
5. Create a connection and authorize with your NetSuite credentials

## Common Record Types

`customer`, `vendor`, `employee`, `contact`, `salesorder`, `purchaseorder`, `invoice`, `creditmemo`, `journalentry`, `item`, `inventoryitem`, `account`, `department`, `location`, `subsidiary`, `opportunity`, `transaction`

## SuiteQL Examples

```sql
-- List active customers
SELECT id, companyname, email FROM customer WHERE isinactive = 'F' ORDER BY companyname

-- Open sales orders over $10,000
SELECT id, tranid, entity, total FROM transaction WHERE type = 'SalesOrd' AND status = 'open' AND total > 10000

-- Employee directory
SELECT id, firstname, lastname, email, department FROM employee WHERE isinactive = 'F'

-- Join example: invoices with customer names
SELECT t.id, t.tranid, t.total, c.companyname FROM transaction t JOIN customer c ON t.entity = c.id WHERE t.type = 'CustInvc'
```

## Notes

- The `Prefer: transient` header is automatically added for SuiteQL queries (required by NetSuite)
- Record collection filtering returns only IDs and HATEOAS links — use SuiteQL for richer queries
- Metadata endpoints return `application/schema+json` content
- NetSuite loads records in edit mode even for GET requests — this can affect permission checks
- Custom sublists are not available through REST Web Services
- Maximum 100,000 results per SuiteQL query; use pagination (`limit`/`offset`) for large result sets

## Files

- [apiDefinition.swagger.json](apiDefinition.swagger.json) — OpenAPI 2.0 with MCP endpoint + individual operations
- [apiProperties.json](apiProperties.json) — OAuth 2.0 configuration
- [script.csx](script.csx) — Dual-mode script: MCP JSON-RPC handler + direct operation forwarding to NetSuite REST API

## Files

- [apiDefinition.swagger.json](apiDefinition.swagger.json) — OpenAPI 2.0 with MCP endpoint + individual operations
- [apiProperties.json](apiProperties.json) — OAuth 2.0 configuration and routing policy
- [script.csx](script.csx) — Dual-mode script: MCP JSON-RPC handler + direct operation forwarding
