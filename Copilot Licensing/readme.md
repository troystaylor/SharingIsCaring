# Copilot Licensing Connector

A Power Platform custom connector for retrieving Copilot Studio credits consumption data with **dynamic environment dropdowns**.

## Overview

This connector uses custom code to provide:

- **Dynamic environment dropdown** - No more copying GUIDs! Select environments from a dropdown
- **Automatic tenant ID** - Extracted from your environments, no manual entry needed
- **Billed credits** - Copilot Credits consumed that count against your entitlement
- **Non-billed credits** - Credits consumed that don't count against billing
- **Per-agent breakdown** - Consumption data per Copilot Studio agent

## ⚠️ Important Notes

1. **Undocumented API**: The licensing API (`v0.1-alpha`) is internal and may change without notice
2. **Date Format**: The connector automatically converts ISO dates to the required MM-dd-yyyy format
3. **Lookback Period**: For complete historical data, use a 365-day lookback

## Endpoints

### List Environments (Internal)

Retrieves environments for the dropdown picker. Called automatically by the `GetEnvironmentCredits` action.

**Endpoint**: `GET /environments`

### Get Environment Credits

Retrieve Copilot Studio credits for a selected environment within a date range.

**Endpoint**: `GET /credits`

| Parameter | Required | Description |
|-----------|----------|-------------|
| `environmentId` | Yes | Select from dropdown |
| `fromDate` | Yes | Start date (date picker) |
| `toDate` | Yes | End date (date picker) |

**Response includes:**
- `resourceId` - Unique agent identifier
- `resourceName` - Agent display name
- `productName` - Product name (Copilot Studio)
- `featureName` - Feature name (MCS Messages)
- `billedCredits` - Billed credits consumed
- `nonBilledCredits` - Non-billed credits consumed

### Get Tenant Entitlements

Retrieve the Copilot Studio entitlements summary for your tenant.

**Endpoint**: `GET /entitlements`

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Power Automate / App                      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Copilot Licensing Connector                     │
│                      (script.csx)                            │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────────┐     │
│  │/environments│  │  /credits   │  │  /entitlements   │     │
│  └──────┬──────┘  └──────┬──────┘  └────────┬─────────┘     │
└─────────┼────────────────┼──────────────────┼───────────────┘
          │                │                  │
          ▼                ▼                  ▼
┌─────────────────┐  ┌─────────────────────────────────────────┐
│api.powerplatform│  │    licensing.powerplatform.microsoft.com │
│     .com        │  │           (v0.1-alpha API)               │
└─────────────────┘  └─────────────────────────────────────────┘
```

## Custom Code Flow

1. **ListEnvironments**: Calls `api.powerplatform.com` to get environments for dropdown
2. **GetEnvironmentCredits**: 
   - Extracts tenant ID from environments API
   - Converts dates to MM-dd-yyyy format
   - Calls licensing API with proper parameters
   - Flattens response for easier consumption
3. **GetTenantEntitlements**: Gets tenant ID, then calls entitlements endpoint

## Prerequisites

### App Registration

Create an Azure AD app registration with:

1. **API Permissions**: 
   - `https://api.powerplatform.com/.default` (Delegated)
   - Note: The same token works for both APIs due to Microsoft's internal trust

2. **Supported Account Types**: Single tenant or multi-tenant

3. **Redirect URI**: `https://global.consent.azure-apim.net/redirect`

## Setup

1. Create an Azure AD app registration with the required permissions
2. Update `apiProperties.json` with your app registration's Client ID
3. Import the connector via Power Platform maker portal:
   - Custom connectors → Import OpenAPI file
   - Upload `apiDefinition.swagger.json`
   - Go to "Code" tab and enable custom code
   - Paste contents of `script.csx`
4. Create a connection using OAuth

## Usage Examples

### Power Automate Flow

```
1. Add "Get Environment Credits" action
2. Select environment from dropdown (no GUID needed!)
3. Pick date range from date pickers
4. Parse the response to analyze credits:
   - Loop through resources array
   - Sum billedCredits for total consumption
   - Group by resourceName for per-agent breakdown
```

### Sample Response

```json
{
  "environmentId": "abc-123-def",
  "tenantId": "your-tenant-id",
  "fromDate": "2025-01-12",
  "toDate": "2026-01-12",
  "resourceCount": 5,
  "resources": [
    {
      "resourceId": "agent-guid-1",
      "resourceName": "Customer Service Agent",
      "productName": "Copilot Studio",
      "featureName": "MCS Messages",
      "billedCredits": 1234.56,
      "nonBilledCredits": 45.67,
      "unit": "MB",
      "lastRefreshed": "2026-01-12"
    }
  ]
}
```

## Microsoft Learn Documentation

- [Copilot Studio Licensing](https://learn.microsoft.com/en-us/microsoft-copilot-studio/billing-licensing)
- [Manage Copilot Credits and Capacity](https://learn.microsoft.com/en-us/power-platform/admin/manage-copilot-studio-messages-capacity)
- [Billing Rates and Management](https://learn.microsoft.com/en-us/microsoft-copilot-studio/requirements-messages-management)
- [Power Platform API](https://learn.microsoft.com/en-us/rest/api/power-platform/)

## Licensing Context

As of September 2025, Copilot Studio uses **Copilot Credits** as the common currency:

| Feature | Rate |
|---------|------|
| Regular (non-Gen AI) | 1 credit per message |
| Generative AI answers | 2 credits per message |
| Text/Gen AI tools (basic) | 0.1 credit per 1K tokens |
| Text/Gen AI tools (standard) | 1.5 credits per 1K tokens |
| Text/Gen AI tools (premium) | 10 credits per 1K tokens |

Credits reset monthly on the first of each month.

## Author

**Troy Taylor**  
- GitHub: [troystaylor](https://github.com/troystaylor)
- LinkedIn: [introtroytaylor](https://www.linkedin.com/in/introtroytaylor/)
