# Application Insights Telemetry Template

This folder contains a simple template script demonstrating how to integrate Application Insights telemetry and logging into Power Platform custom connectors.

## Files

- **script.csx** - Minimal template showing Application Insights integration

## Features Demonstrated

### 1. Application Insights Integration
- Custom event logging with structured properties
- Correlation ID tracking across requests
- Performance metrics (request duration)
- Error tracking with stack traces
- Optional telemetry (works without connection string)

### 2. Context.Logger Usage
- Basic logging for development/debugging
- Information, Warning, and Error log levels
- Correlation with Application Insights events

### 3. Logging Patterns
- Request lifecycle tracking (start → execution → completion)
- Operation processing with telemetry
- Error handling with detailed context
- Non-blocking telemetry (operations work even if telemetry fails)

## Setup Instructions

### Step 1: Create Application Insights Resource

1. Open [Azure Portal](https://portal.azure.com)
2. Click **Create a resource** → Search for "Application Insights"
3. Fill in resource details:
   - **Resource Group**: Choose or create a resource group
   - **Name**: e.g., "my-connector-telemetry"
   - **Region**: Choose region closest to your users
   - **Workspace**: Create new or use existing Log Analytics workspace
4. Click **Review + Create** → **Create**
5. Once deployed, go to resource → **Overview**
6. Copy the **Connection String** (format: `InstrumentationKey=xxx;IngestionEndpoint=https://xxx`)

### Step 2: Configure Your Connector Script

1. Copy `script.csx` to your connector folder (or use it as a starting point)
2. Find the configuration section:
   ```csharp
   private const string APP_INSIGHTS_CONNECTION_STRING = "";
   ```
3. Paste your Application Insights connection string:
   ```csharp
   private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=abc123...;IngestionEndpoint=https://westus2-1.in.applicationinsights.azure.com/;LiveEndpoint=https://westus2.livediagnostics.monitor.azure.com/";
   ```

**Note:** The script works perfectly with an empty connection string. Operations will succeed, but telemetry will be disabled. This allows you to:
- Deploy without Application Insights initially
- Enable telemetry later by adding the connection string
- Test locally without Azure dependencies

### Step 3: Add Telemetry to Your Code

Use the `LogToAppInsights` method throughout your code:

```csharp
// Log simple events
await LogToAppInsights("UserAction", new { 
    Action = "CreateItem",
    UserId = userId
});

// Log with multiple properties
await LogToAppInsights("APICallSuccess", new { 
    Endpoint = "/api/items",
    StatusCode = 200,
    DurationMs = duration.TotalMilliseconds,
    ItemCount = items.Count
});

// Log errors
await LogToAppInsights("APICallError", new { 
    Endpoint = "/api/items",
    ErrorMessage = ex.Message,
    ErrorType = ex.GetType().Name
});
```

### Step 4: Deploy and Test

1. Deploy your connector to Power Platform
2. Create a connection and test some operations
3. Check Application Insights for telemetry data (may take 1-2 minutes to appear)

## Viewing Telemetry in Azure Portal

### Live Metrics
- Go to Application Insights resource → **Live Metrics**
- See real-time requests, failures, and custom events

### Custom Events
1. Go to **Logs** in left menu
2. Run KQL query:
   ```kusto
   customEvents
   | where timestamp > ago(1h)
   | order by timestamp desc
   | project timestamp, name, customDimensions
   ```

### Track Request Duration
```kusto
customEvents
| where name in ("RequestReceived", "RequestCompleted")
| extend CorrelationId = tostring(customDimensions.CorrelationId)
| summarize 
    StartTime = minif(timestamp, name == "RequestReceived"),
    EndTime = maxif(timestamp, name == "RequestCompleted")
    by CorrelationId
| extend DurationMs = datetime_diff('millisecond', EndTime, StartTime)
| order by StartTime desc
| project CorrelationId, StartTime, EndTime, DurationMs
```

### Find Errors
```kusto
customEvents
| where name contains "Error"
| order by timestamp desc
| extend ErrorMessage = tostring(customDimensions.ErrorMessage)
| extend ErrorType = tostring(customDimensions.ErrorType)
| project timestamp, name, ErrorMessage, ErrorType, customDimensions
```

### Track Operations
```kusto
customEvents
| where name == "OperationProcessed"
| order by timestamp desc
| extend Operation = tostring(customDimensions.Operation)
| extend CorrelationId = tostring(customDimensions.CorrelationId)
| project timestamp, Operation, CorrelationId, customDimensions
```

## Event Types in Template

The script.csx includes these event types:

| Event Name | Purpose | Key Properties |
|------------|---------|----------------|
| `RequestReceived` | Tracks incoming requests | CorrelationId, Path, Method, UserAgent, BodyPreview |
| `OperationProcessed` | Logs operation processing | CorrelationId, Operation, HasPayload |
| `RequestCompleted` | Tracks request completion | CorrelationId, DurationMs |
| `RequestError` | Logs request failures | CorrelationId, ErrorMessage, ErrorType, StackTrace |


## Context.Logger vs Application Insights

### Use `this.Context.Logger` for:
- Development/debugging logs
- Simple status messages
- Logs that appear in Power Platform connector test console
- When Application Insights is unavailable

```csharp
this.Context.Logger.LogInformation("Processing started");
this.Context.Logger.LogWarning("Optional parameter missing, using default");
this.Context.Logger.LogError($"Failed to process: {ex.Message}");
```

### Use `LogToAppInsights` for:
- Production telemetry and analytics
- Structured data with multiple properties
- Performance metrics and tracking
- Business intelligence queries
- Long-term historical analysis
- Correlation across multiple requests

```csharp
await LogToAppInsights("DataProcessed", new {
    RecordCount = 150,
    DurationMs = 342,
    CacheHit = true,
    UserId = "user123"
});
```

### Best Practice: Use Both

```csharp
try 
{
    this.Context.Logger.LogInformation($"Calling external API: {apiUrl}");
    var response = await CallAPI(apiUrl);
    
    await LogToAppInsights("ExternalAPISuccess", new {
        Url = apiUrl,
        StatusCode = response.StatusCode,
        DurationMs = duration.TotalMilliseconds
    });
}
catch (Exception ex)
{
    this.Context.Logger.LogError($"API call failed: {ex.Message}");
    
    await LogToAppInsights("ExternalAPIError", new {
        Url = apiUrl,
        ErrorMessage = ex.Message,
        ErrorType = ex.GetType().Name
    });
    
    throw;
}
```

## Privacy and Security Considerations

### What to Log
✅ **Safe to log:**
- Correlation IDs, request IDs
- Operation names, tool names
- Status codes, error types
- Performance metrics (duration, count)
- Non-sensitive parameters (counts, flags, enums)
- Aggregate statistics

❌ **Never log:**
- OAuth tokens, API keys, passwords
- Personally Identifiable Information (PII)
- Full email addresses, phone numbers
- Credit card numbers, SSNs
- Full request/response bodies with user data
- Internal system secrets

### Sanitizing Data

```csharp
// GOOD: Preview only, limit length
RequestBody = body?.Substring(0, Math.Min(200, body?.Length ?? 0))

// GOOD: Count only
UserCount = users.Count

// GOOD: Redacted email
Email = email?.Replace(email.Substring(0, email.IndexOf('@')), "***")

// BAD: Full token
Token = authToken  // ❌ NEVER DO THIS

// BAD: Full PII
UserEmail = user.Email  // ❌ Avoid
```

## Performance Impact

Application Insights logging is **asynchronous** and uses **fire-and-forget** pattern:
- Minimal performance impact (~1-5ms overhead)
- Telemetry errors don't fail main request (wrapped in try-catch)
- Recommended to leave enabled in production

If you need to disable temporarily:
```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = ""; // Empty = disabled
```

## Cost Considerations

Application Insights pricing based on data ingestion:
- **First 5 GB/month**: Free
- **Additional data**: ~$2.30 per GB (varies by region)
- **Typical connector**: 10-50 MB/day for moderate usage
- **Data retention**: 90 days (default, configurable)

**Estimate:** A connector with 1,000 requests/day logging ~5 events per request at ~1 KB each = ~5 MB/day = ~150 MB/month (well within free tier)

## Troubleshooting

### Telemetry Not Appearing

1. **Check connection string**: Ensure it's correctly copied from Azure Portal
2. **Wait 1-2 minutes**: Initial ingestion has slight delay
3. **Check quota**: Go to Application Insights → **Usage and estimated costs**
4. **Verify format**: Connection string must include both `InstrumentationKey` and `IngestionEndpoint`

### Context.Logger Not Visible

- Logs only appear in Power Platform connector **Test** tab during testing
- Not visible in production flow runs (use Application Insights for production)
- Check connector's **Code** tab for any compilation errors

### High Data Volume

Reduce logging frequency:
```csharp
// Log only on errors
if (!success) {
    await LogToAppInsights("OperationFailed", properties);
}

// Sample logging (10% of requests)
if (new Random().Next(100) < 10) {
    await LogToAppInsights("SampledRequest", properties);
}
```

## Example Queries for Common Scenarios

### Daily Request Count by Event
```kusto
customEvents
| where timestamp > ago(7d)
| summarize Count = count() by bin(timestamp, 1d), name
| render columnchart
```

### Average Request Duration
```kusto
customEvents
| where name == "RequestCompleted"
| extend DurationMs = todouble(customDimensions.DurationMs)
| summarize AvgDuration = avg(DurationMs), Count = count()
| project AvgDuration, Count
```

### Error Rate Over Time
```kusto
customEvents
| where timestamp > ago(24h)
| summarize 
    Total = count(),
    Errors = countif(name contains "Error")
    by bin(timestamp, 1h)
| extend ErrorRate = (Errors * 100.0) / Total
| render timechart
```

### Most Common Operations
```kusto
customEvents
| where name == "OperationProcessed"
| extend Operation = tostring(customDimensions.Operation)
| summarize CallCount = count() by Operation
| order by CallCount desc
| render piechart
```

## Next Steps

1. Copy `script.csx` to your connector folder (for basic telemetry)
2. Or use `template-script.csx` for full MCP protocol support
3. Add your Application Insights connection string (optional)
4. Customize the operation logic for your connector
5. Deploy and test
6. Monitor telemetry in Azure Portal

## Related Resources

- [Application Insights Overview](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [Kusto Query Language (KQL) Reference](https://learn.microsoft.com/azure/data-explorer/kusto/query/)
- [Custom Connector Logging](https://learn.microsoft.com/connectors/custom-connectors/write-code#logging)
