# Azure Key Vault — Multi-Secret Operations

This folder contains a script template that adds **multi-secret operations** to the existing [Azure Key Vault connector](https://learn.microsoft.com/en-us/connectors/keyvault/).

## Why This Exists

The built-in Key Vault connector handles individual operations (Get Secret, List Secrets, List Keys, Encrypt/Decrypt, etc.) but requires one action per secret. For Copilot Studio agents (which can't loop) and Power Apps (which can't easily chain connector calls), that's a hard limitation.

This script adds five operations that work across multiple secrets in a single action:

| Operation | What It Does |
|---|---|
| **BulkGetSecrets** | Retrieve values of multiple secrets at once, with optional name prefix filter |
| **CheckExpiringSecrets** | Find secrets expiring within N days, plus already-expired ones |
| **SearchSecretsByTags** | Filter secrets by tag key and optional value |
| **SecretRotationReport** | Flag stale secrets not updated within N days |
| **BulkSetSecrets** | Create or update multiple secrets in one action |

## Files

- **script.csx** — All five operations with routing by operationId

## How to Use

### Option 1: Add to an Existing Key Vault Custom Connector

1. Add operations to your connector's `apiDefinition.swagger.json`
2. Add operation IDs to the `scriptOperations` array in `apiProperties.json`
3. Copy the script logic into your connector's `script.csx`

### Option 2: Use as a Standalone Custom Connector

1. Create a new custom connector pointing to your vault (e.g., `myvault.vault.azure.net`)
2. Configure OAuth with scope `https://vault.azure.net/.default`
3. Add the operations and use this script

## Operations

### BulkGetSecrets

Retrieve the values of multiple secrets in one call.

**Input:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `nameFilter` | string | No | Filter by name prefix (e.g., `api-key-`) |
| `maxSecrets` | integer | No | Max secrets to retrieve (default 25) |

**Example:**
```json
{ "nameFilter": "connection-string-", "maxSecrets": 10 }
```

**Response:**
```json
{
    "count": 2,
    "secrets": [
        { "name": "connection-string-sql", "value": "Server=myserver;...", "contentType": "text/plain", "enabled": true },
        { "name": "connection-string-redis", "value": "myredis.redis.cache...", "contentType": "text/plain", "enabled": true }
    ]
}
```

### CheckExpiringSecrets

Find secrets expiring soon or already expired.

**Input:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `daysUntilExpiry` | integer | No | Days from now to check (default 30) |
| `nameFilter` | string | No | Filter by name prefix |

**Example:**
```json
{ "daysUntilExpiry": 14 }
```

**Response:**
```json
{
    "daysChecked": 14,
    "expiringCount": 1,
    "expiredCount": 1,
    "expiring": [
        { "name": "api-key-partner", "expiryDate": "2026-05-12T00:00:00Z", "daysRemaining": 11, "status": "expiring", "enabled": true }
    ],
    "expired": [
        { "name": "old-token", "expiryDate": "2026-04-15T00:00:00Z", "status": "expired", "enabled": true }
    ]
}
```

### SearchSecretsByTags

Filter secrets by tag key and optional value.

**Input:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `tagKey` | string | Yes | Tag key to search for |
| `tagValue` | string | No | Tag value to match (omit for any value) |
| `includeValues` | boolean | No | Include secret values in results (default false) |

**Example:**
```json
{ "tagKey": "environment", "tagValue": "production", "includeValues": true }
```

**Response:**
```json
{
    "tagKey": "environment",
    "tagValue": "production",
    "count": 2,
    "secrets": [
        { "name": "db-password", "tags": { "environment": "production", "team": "backend" }, "contentType": "text/plain", "enabled": true, "value": "..." }
    ]
}
```

### SecretRotationReport

Flag secrets not updated within N days — rotation candidates.

**Input:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `staleDays` | integer | No | Days since last update to consider stale (default 90) |
| `nameFilter` | string | No | Filter by name prefix |

**Example:**
```json
{ "staleDays": 60 }
```

**Response:**
```json
{
    "staleDays": 60,
    "staleCount": 2,
    "healthyCount": 5,
    "staleSecrets": [
        { "name": "legacy-api-key", "lastUpdated": "2025-11-01T10:00:00Z", "daysSinceUpdate": 182, "enabled": true, "contentType": "text/plain" }
    ]
}
```

### BulkSetSecrets

Create or update multiple secrets at once.

**Input:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `secrets` | array | Yes | Array of secrets (max 25) |
| `secrets[].name` | string | Yes | Secret name |
| `secrets[].value` | string | Yes | Secret value |
| `secrets[].contentType` | string | No | Content type |
| `secrets[].tags` | object | No | Tags as key-value pairs |

**Example:**
```json
{
    "secrets": [
        { "name": "api-key-service-a", "value": "abc123", "contentType": "text/plain", "tags": { "environment": "staging" } },
        { "name": "api-key-service-b", "value": "def456", "contentType": "text/plain" }
    ]
}
```

**Response:**
```json
{
    "successCount": 2,
    "errorCount": 0,
    "results": [
        { "name": "api-key-service-a", "success": true, "id": "https://myvault.vault.azure.net/secrets/api-key-service-a/..." },
        { "name": "api-key-service-b", "success": true, "id": "https://myvault.vault.azure.net/secrets/api-key-service-b/..." }
    ]
}
```

## Required Permissions

| Operation | Required RBAC Role |
|---|---|
| BulkGetSecrets, CheckExpiringSecrets, SearchSecretsByTags, SecretRotationReport | **Key Vault Secrets User** (`secrets/get` + `secrets/list`) |
| BulkSetSecrets | **Key Vault Secrets Officer** (`secrets/get` + `secrets/list` + `secrets/set`) |

## Key Vault REST API Reference

- [Get Secret](https://learn.microsoft.com/en-us/rest/api/keyvault/secrets/get-secret/get-secret?view=rest-keyvault-secrets-2025-07-01)
- [Get Secrets (List)](https://learn.microsoft.com/en-us/rest/api/keyvault/secrets/get-secrets/get-secrets?view=rest-keyvault-secrets-2025-07-01)
- [Set Secret](https://learn.microsoft.com/en-us/rest/api/keyvault/secrets/set-secret?view=rest-keyvault-secrets-2025-07-01)
- [Built-in Key Vault Connector](https://learn.microsoft.com/en-us/connectors/keyvault/)
