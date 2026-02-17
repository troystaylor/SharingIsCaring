# Lemon Squeezy Connector

A Power Platform custom connector for the [Lemon Squeezy](https://www.lemonsqueezy.com/) digital products platform. Manage products, customers, orders, subscriptions, license keys, discounts, and webhooks.

## Prerequisites

- A Lemon Squeezy account
- An API key from your Lemon Squeezy dashboard

## Getting Your API Key

1. Log in to your Lemon Squeezy account
2. Navigate to **Settings** > **API**
3. Click **Create API Key**
4. Copy the generated key (it won't be shown again)

## Setup

1. Import the connector into your Power Platform environment
2. Create a new connection
3. Enter your API key when prompted

## Operations

### Account (1 operation)
| Operation | Description |
|-----------|-------------|
| Get Current User | Returns the authenticated user account |

### Stores (2 operations)
| Operation | Description |
|-----------|-------------|
| List Stores | Returns stores the authenticated user has access to |
| Get Store | Retrieves a single store by ID |

### Products (4 operations)
| Operation | Description |
|-----------|-------------|
| List Products | Returns a list of products |
| Get Product | Retrieves a single product by ID |
| List Variants | Returns a list of product variants |
| Get Variant | Retrieves a single variant by ID |

### Customers (4 operations)
| Operation | Description |
|-----------|-------------|
| List Customers | Returns a list of customers |
| Get Customer | Retrieves a single customer by ID |
| Create Customer | Creates a new customer |
| Update Customer | Updates an existing customer |

### Orders (6 operations)
| Operation | Description |
|-----------|-------------|
| List Orders | Returns a list of orders |
| Get Order | Retrieves a single order by ID |
| Generate Invoice | Generates an invoice PDF for an order |
| List Order Items | Returns a list of order items |
| Get Order Item | Retrieves a single order item by ID |

### Subscriptions (6 operations)
| Operation | Description |
|-----------|-------------|
| List Subscriptions | Returns a list of subscriptions |
| Get Subscription | Retrieves a single subscription by ID |
| Update Subscription | Updates a subscription (upgrade, pause, etc.) |
| Cancel Subscription | Cancels a subscription |
| List Subscription Invoices | Returns subscription invoices |
| Get Subscription Invoice | Retrieves a single subscription invoice |

### License Keys (3 operations)
| Operation | Description |
|-----------|-------------|
| List License Keys | Returns a list of license keys |
| Get License Key | Retrieves a single license key by ID |
| Update License Key | Updates a license key (limit, expiration, disable) |

### Discounts (4 operations)
| Operation | Description |
|-----------|-------------|
| List Discounts | Returns a list of discount codes |
| Get Discount | Retrieves a single discount by ID |
| Create Discount | Creates a new discount code |
| Delete Discount | Deletes a discount code |

### Webhooks (5 operations)
| Operation | Description |
|-----------|-------------|
| List Webhooks | Returns a list of webhooks |
| Get Webhook | Retrieves a single webhook by ID |
| Create Webhook | Creates a new webhook |
| Update Webhook | Updates an existing webhook |
| Delete Webhook | Deletes a webhook |

### MCP (Model Context Protocol)
| Operation | Description |
|-----------|-------------|
| Invoke Lemon Squeezy MCP | Agentic endpoint for Copilot Studio integration |

## MCP Tools

The MCP endpoint exposes the following tools for use with Copilot Studio agents:

### Store Tools
| Tool | Description |
|------|-------------|
| `list_stores` | List all stores for the authenticated user |
| `get_store` | Get details of a specific store |

### Product Tools
| Tool | Description |
|------|-------------|
| `list_products` | List products, optionally filtered by store |
| `get_product` | Get details of a specific product |
| `list_variants` | List product variants, optionally filtered by product |
| `get_variant` | Get details of a specific variant |

### Customer Tools
| Tool | Description |
|------|-------------|
| `list_customers` | List customers, optionally filtered by store or email |
| `get_customer` | Get details of a specific customer |
| `create_customer` | Create a new customer |
| `update_customer` | Update an existing customer |

### Order Tools
| Tool | Description |
|------|-------------|
| `list_orders` | List orders, optionally filtered by store or email |
| `get_order` | Get details of a specific order |
| `list_order_items` | List order items for an order |

### Subscription Tools
| Tool | Description |
|------|-------------|
| `list_subscriptions` | List subscriptions with optional filters |
| `get_subscription` | Get details of a specific subscription |
| `cancel_subscription` | Cancel a subscription |
| `pause_subscription` | Pause a subscription |
| `resume_subscription` | Resume a paused subscription |

### License Key Tools
| Tool | Description |
|------|-------------|
| `list_license_keys` | List license keys with optional filters |
| `get_license_key` | Get details of a specific license key |
| `update_license_key` | Update activation limit or disable status |

### Discount Tools
| Tool | Description |
|------|-------------|
| `list_discounts` | List discounts, optionally filtered by store |
| `get_discount` | Get details of a specific discount |
| `create_discount` | Create a new discount code |
| `delete_discount` | Delete a discount |

## Application Insights Telemetry

The connector supports Application Insights telemetry for monitoring and debugging. To enable:

1. Create an Application Insights resource in Azure
2. Copy the connection string from the resource's Overview page
3. Update the `APP_INSIGHTS_CONNECTION_STRING` constant in [script.csx](script.csx)

### Logged Events
| Event | Description |
|-------|-------------|
| `RequestReceived` | Initial request with correlation ID and operation |
| `RequestCompleted` | Successful completion with status code and duration |
| `RequestError` | Error details if request fails |
| `MCPRequest` | MCP method invocation details |
| `MCPToolCall` | Tool name and arguments |
| `MCPToolError` | Tool-specific errors |

### Sample KQL Query
```kql
customEvents
| where name startswith "Lemon"
| extend CorrelationId = tostring(customDimensions.CorrelationId)
| extend OperationId = tostring(customDimensions.OperationId)
| extend Tool = tostring(customDimensions.Tool)
| summarize count() by OperationId, bin(timestamp, 1h)
| render timechart
```

## Subscription Status Values

| Status | Description |
|--------|-------------|
| `on_trial` | Subscription is in trial period |
| `active` | Subscription is active |
| `paused` | Subscription is paused |
| `past_due` | Payment failed, grace period |
| `unpaid` | Payment failed, service suspended |
| `cancelled` | Subscription cancelled but active until end of period |
| `expired` | Subscription has ended |

## Order Status Values

| Status | Description |
|--------|-------------|
| `pending` | Payment pending |
| `failed` | Payment failed |
| `paid` | Payment successful |
| `refunded` | Fully refunded |
| `partial_refund` | Partially refunded |
| `fraudulent` | Marked as fraudulent |

## License Key Status Values

| Status | Description |
|--------|-------------|
| `inactive` | Not yet activated |
| `active` | Currently active |
| `expired` | Past expiration date |
| `disabled` | Manually disabled |

## API Notes

- **Rate Limit**: 300 requests per minute
- **Response Format**: JSON:API specification with `data`, `attributes`, and `relationships` structure
- **Pagination**: Use `page[number]` and `page[size]` parameters
- **Filtering**: Most list endpoints support `filter[field]` parameters
- **Test Mode**: Products and orders created in test mode will have `test_mode: true`

## Webhook Events

Common webhook events you can subscribe to:
- `order_created`, `order_refunded`
- `subscription_created`, `subscription_updated`, `subscription_cancelled`, `subscription_resumed`, `subscription_expired`, `subscription_paused`, `subscription_unpaused`
- `subscription_payment_success`, `subscription_payment_failed`, `subscription_payment_recovered`, `subscription_payment_refunded`
- `license_key_created`, `license_key_updated`

## Example: List Active Subscriptions

In Power Automate, use the **List Subscriptions** action with:
- **Status**: `active`
- **Store ID**: Your store ID (optional)

## Example: Create a Discount Code

Use the **Create Discount** action with the JSON:API structure:
- Set **Name** to your internal reference
- Set **Code** to the code customers will enter (e.g., "SAVE20")
- Set **Amount** to the discount value (e.g., 20 for 20% or 2000 for $20.00)
- Set **Amount Type** to "percent" or "fixed"
- Set **Store ID** in the relationships section

## Resources

- [Lemon Squeezy API Documentation](https://docs.lemonsqueezy.com/api)
- [Lemon Squeezy Developer Guide](https://docs.lemonsqueezy.com/guides)
- [JSON:API Specification](https://jsonapi.org/)
