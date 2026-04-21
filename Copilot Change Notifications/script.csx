using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string ServerName = "copilot-ai-change-notifications";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP":           return await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                case "CreateSubscription": return await HandleCreateSubscriptionAsync(correlationId).ConfigureAwait(false);
                case "ListSubscriptions":  return await HandleListSubscriptionsAsync(correlationId).ConfigureAwait(false);
                case "GetSubscription":    return await HandleGetSubscriptionAsync(correlationId).ConfigureAwait(false);
                case "TestWebhookConnectivity": return await HandleTestWebhookAsync(correlationId).ConfigureAwait(false);
                case "DeleteSubscription": return await HandleDeleteSubscriptionAsync(correlationId).ConfigureAwait(false);
                case "RenewSubscription":  return await HandleRenewSubscriptionAsync(correlationId).ConfigureAwait(false);
                case "GetAIInteraction":   return await HandlePassthroughAsync().ConfigureAwait(false);
                case "GetAIInsight":       return await HandlePassthroughAsync().ConfigureAwait(false);
                case "ListAIInteractions": return await HandlePassthroughAsync().ConfigureAwait(false);
                default:                   return await HandlePassthroughAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message, StackTrace = ex.StackTrace });
            throw;
        }
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        JToken requestId = null;
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");

            JObject request;
            try { request = JObject.Parse(body); }
            catch (JsonException) { return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON"); }

            var method = request.Value<string>("method") ?? "";
            requestId = request["id"];

            switch (method)
            {
                case "initialize": 
                    return HandleMcpInitialize(request, requestId);
                case "initialized": 
                case "notifications/initialized": 
                case "notifications/cancelled": 
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                case "tools/list": 
                    return HandleMcpToolsList(requestId);
                case "tools/call": 
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                case "resources/list": 
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                case "resources/templates/list": 
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                case "prompts/list": 
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
                case "completion/complete": 
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } });
                case "logging/setLevel": 
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                default: 
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex) 
        { 
            _ = LogToAppInsights("McpError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message); 
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Copilot Change Notifications",
                ["description"] = "Monitor Copilot AI interactions and meeting insights via Microsoft Graph change notifications."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("create_tenant_interaction_subscription", 
                "Subscribe to tenant-wide Copilot AI interactions. Receives notifications whenever any user queries or Copilot responds in the tenant.",
                new JObject
                {
                    ["notification_url"] = new JObject { ["type"] = "string", ["description"] = "HTTPS endpoint to receive notifications" },
                    ["expiration_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Minutes until subscription expires (max 1440). Default: 60" },
                    ["include_resource_data"] = new JObject { ["type"] = "boolean", ["description"] = "Include full resource data in notifications. Default: false" },
                    ["filter_app_class"] = new JObject { ["type"] = "string", ["description"] = "Optional filter by appClass (e.g., IPM.SkypeTeams.Message.Copilot.Teams)" }
                }, new[] { "notification_url" }),
            McpTool("create_user_interaction_subscription", 
                "Subscribe to Copilot AI interactions for a specific user.",
                new JObject
                {
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "The user ID (UPN or object ID)" },
                    ["notification_url"] = new JObject { ["type"] = "string", ["description"] = "HTTPS endpoint to receive notifications" },
                    ["expiration_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Minutes until subscription expires (max 1440). Default: 60" }
                }, new[] { "user_id", "notification_url" }),
            McpTool("create_meeting_insight_subscription", 
                "Subscribe to AI insights for a specific meeting. Notified when meeting summaries are generated.",
                new JObject
                {
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "The user ID who owns the meeting" },
                    ["meeting_id"] = new JObject { ["type"] = "string", ["description"] = "The meeting ID" },
                    ["notification_url"] = new JObject { ["type"] = "string", ["description"] = "HTTPS endpoint to receive notifications" },
                    ["expiration_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Minutes until subscription expires. Default: 60" }
                }, new[] { "user_id", "meeting_id", "notification_url" }),
            McpTool("list_subscriptions", 
                "List all active change notification subscriptions.",
                new JObject(), new string[0]),
            McpTool("delete_subscription", 
                "Delete a change notification subscription by ID.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "The subscription ID to delete" }
                }, new[] { "subscription_id" }),
            McpTool("renew_subscription", 
                "Extend the expiration of a subscription.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "The subscription ID to renew" },
                    ["expiration_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Minutes until new expiration (max 1440)" }
                }, new[] { "subscription_id", "expiration_minutes" }),
            McpTool("process_interaction_notification", 
                "Process a change notification webhook payload for AI interactions. Validates, decrypts (if needed), and extracts interaction data.",
                new JObject
                {
                    ["notification_payload"] = new JObject { ["type"] = "string", ["description"] = "The complete webhook notification JSON as a string" },
                    ["client_state"] = new JObject { ["type"] = "string", ["description"] = "Optional client state for validation" }
                }, new[] { "notification_payload" }),
            McpTool("process_insight_notification", 
                "Process a change notification webhook payload for meeting AI insights.",
                new JObject
                {
                    ["notification_payload"] = new JObject { ["type"] = "string", ["description"] = "The complete webhook notification JSON as a string" }
                }, new[] { "notification_payload" }),
            McpTool("get_interaction", 
                "Retrieve details of a specific AI interaction by ID.",
                new JObject
                {
                    ["interaction_id"] = new JObject { ["type"] = "string", ["description"] = "The interaction ID from a notification" }
                }, new[] { "interaction_id" }),
            McpTool("get_meeting_insight", 
                "Retrieve a specific meeting AI insight.",
                new JObject
                {
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "The user ID" },
                    ["meeting_id"] = new JObject { ["type"] = "string", ["description"] = "The meeting ID" },
                    ["insight_id"] = new JObject { ["type"] = "string", ["description"] = "The insight ID" }
                }, new[] { "user_id", "meeting_id", "insight_id" }),
            McpTool("list_interactions", 
                "List recent AI interactions with optional filtering.",
                new JObject
                {
                    ["filter_expression"] = new JObject { ["type"] = "string", ["description"] = "OData filter (e.g., \"appClass eq 'IPM.SkypeTeams.Message.Copilot.Teams'\")" },
                    ["max_results"] = new JObject { ["type"] = "integer", ["description"] = "Max interactions to return (1-100). Default: 10" }
                }, new string[0])
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject McpTool(string name, string description, JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");

        try
        {
            JObject toolResult;
            switch (toolName.ToLowerInvariant())
            {
                case "create_tenant_interaction_subscription":
                    toolResult = await ExecuteCreateTenantInteractionSubscriptionAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "create_user_interaction_subscription":
                    toolResult = await ExecuteCreateUserInteractionSubscriptionAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "create_meeting_insight_subscription":
                    toolResult = await ExecuteCreateMeetingInsightSubscriptionAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "list_subscriptions":
                    toolResult = await ExecuteListSubscriptionsAsync(correlationId).ConfigureAwait(false);
                    break;
                case "delete_subscription":
                    toolResult = await ExecuteDeleteSubscriptionToolAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "renew_subscription":
                    toolResult = await ExecuteRenewSubscriptionToolAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "process_interaction_notification":
                    toolResult = ExecuteProcessInteractionNotification(arguments);
                    break;
                case "process_insight_notification":
                    toolResult = ExecuteProcessInsightNotification(arguments);
                    break;
                case "get_interaction":
                    toolResult = await ExecuteGetInteractionAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "get_meeting_insight":
                    toolResult = await ExecuteGetMeetingInsightAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                case "list_interactions":
                    toolResult = await ExecuteListInteractionsAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", toolName);
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } } });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteCreateTenantInteractionSubscriptionAsync(JObject args, string correlationId)
    {
        var notificationUrl = args.Value<string>("notification_url") ?? "";
        var expirationMinutes = args.Value<int?>("expiration_minutes") ?? 60;
        var includeResourceData = args.Value<bool?>("include_resource_data") ?? false;
        var filterAppClass = args.Value<string>("filter_app_class");

        if (string.IsNullOrWhiteSpace(notificationUrl))
            return CreateErrorResult("notification_url is required");

        var resource = "/copilot/interactionHistory/getAllEnterpriseInteractions";
        if (!string.IsNullOrWhiteSpace(filterAppClass))
            resource += $"?$filter=appClass eq '{filterAppClass}'";

        var subscription = new JObject
        {
            ["changeType"] = "created,updated,deleted",
            ["notificationUrl"] = notificationUrl,
            ["resource"] = resource,
            ["includeResourceData"] = includeResourceData,
            ["expirationDateTime"] = DateTime.UtcNow.AddMinutes(expirationMinutes).ToString("O"),
            ["clientState"] = Guid.NewGuid().ToString()
        };

        if (expirationMinutes > 60)
            subscription["lifecycleNotificationUrl"] = notificationUrl;

        return await ExecuteGraphRequestAsync("POST", "/subscriptions", subscription, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateUserInteractionSubscriptionAsync(JObject args, string correlationId)
    {
        var userId = args.Value<string>("user_id") ?? "";
        var notificationUrl = args.Value<string>("notification_url") ?? "";
        var expirationMinutes = args.Value<int?>("expiration_minutes") ?? 60;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(notificationUrl))
            return CreateErrorResult("user_id and notification_url are required");

        var resource = $"/copilot/users/{userId}/interactionHistory/getAllEnterpriseInteractions";
        var subscription = new JObject
        {
            ["changeType"] = "created,updated,deleted",
            ["notificationUrl"] = notificationUrl,
            ["resource"] = resource,
            ["includeResourceData"] = false,
            ["expirationDateTime"] = DateTime.UtcNow.AddMinutes(expirationMinutes).ToString("O"),
            ["clientState"] = Guid.NewGuid().ToString()
        };

        if (expirationMinutes > 60)
            subscription["lifecycleNotificationUrl"] = notificationUrl;

        return await ExecuteGraphRequestAsync("POST", "/subscriptions", subscription, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateMeetingInsightSubscriptionAsync(JObject args, string correlationId)
    {
        var userId = args.Value<string>("user_id") ?? "";
        var meetingId = args.Value<string>("meeting_id") ?? "";
        var notificationUrl = args.Value<string>("notification_url") ?? "";
        var expirationMinutes = args.Value<int?>("expiration_minutes") ?? 60;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(meetingId) || string.IsNullOrWhiteSpace(notificationUrl))
            return CreateErrorResult("user_id, meeting_id, and notification_url are required");

        var resource = $"/copilot/users/{userId}/onlineMeetings/{meetingId}/aiInsights";
        var subscription = new JObject
        {
            ["changeType"] = "created",
            ["notificationUrl"] = notificationUrl,
            ["resource"] = resource,
            ["includeResourceData"] = false,
            ["expirationDateTime"] = DateTime.UtcNow.AddMinutes(expirationMinutes).ToString("O"),
            ["clientState"] = Guid.NewGuid().ToString()
        };

        return await ExecuteGraphRequestAsync("POST", "/subscriptions", subscription, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListSubscriptionsAsync(string correlationId)
    {
        return await ExecuteGraphRequestAsync("GET", "/subscriptions", null, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteSubscriptionToolAsync(JObject args, string correlationId)
    {
        var subscriptionId = args.Value<string>("subscription_id") ?? "";
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return CreateErrorResult("subscription_id is required");

        var result = await ExecuteGraphRequestAsync("DELETE", $"/subscriptions/{subscriptionId}", null, correlationId).ConfigureAwait(false);
        return result;
    }

    private async Task<JObject> ExecuteRenewSubscriptionToolAsync(JObject args, string correlationId)
    {
        var subscriptionId = args.Value<string>("subscription_id") ?? "";
        var expirationMinutes = args.Value<int?>("expiration_minutes") ?? 60;

        if (string.IsNullOrWhiteSpace(subscriptionId))
            return CreateErrorResult("subscription_id is required");

        var body = new JObject { ["expirationDateTime"] = DateTime.UtcNow.AddMinutes(expirationMinutes).ToString("O") };
        return await ExecuteGraphRequestAsync("PATCH", $"/subscriptions/{subscriptionId}", body, correlationId).ConfigureAwait(false);
    }

    private JObject ExecuteProcessInteractionNotification(JObject args)
    {
        var payloadStr = args.Value<string>("notification_payload") ?? "";
        var clientState = args.Value<string>("client_state");

        if (string.IsNullOrWhiteSpace(payloadStr))
            return CreateErrorResult("notification_payload is required");

        try
        {
            var payload = JObject.Parse(payloadStr);
            var value = payload["value"] as JArray;
            if (value == null || value.Count == 0)
                return CreateErrorResult("No notifications in payload");

            var notifications = new JArray();
            foreach (var notif in value)
            {
                var resourceData = notif["resourceData"] as JObject;
                notifications.Add(new JObject
                {
                    ["subscription_id"] = notif["subscriptionId"],
                    ["change_type"] = notif["changeType"],
                    ["resource_id"] = resourceData?["id"],
                    ["resource_type"] = resourceData?["@odata.type"],
                    ["timestamp"] = notif["subscriptionExpirationDateTime"]
                });
            }

            return new JObject { ["success"] = true, ["notifications_count"] = notifications.Count, ["notifications"] = notifications };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to process notification: {ex.Message}");
        }
    }

    private JObject ExecuteProcessInsightNotification(JObject args)
    {
        var payloadStr = args.Value<string>("notification_payload") ?? "";

        if (string.IsNullOrWhiteSpace(payloadStr))
            return CreateErrorResult("notification_payload is required");

        try
        {
            var payload = JObject.Parse(payloadStr);
            var value = payload["value"] as JArray;
            if (value == null || value.Count == 0)
                return CreateErrorResult("No notifications in payload");

            var notifications = new JArray();
            foreach (var notif in value)
            {
                var resourceData = notif["resourceData"] as JObject;
                notifications.Add(new JObject
                {
                    ["subscription_id"] = notif["subscriptionId"],
                    ["change_type"] = notif["changeType"],
                    ["insight_id"] = resourceData?["id"],
                    ["timestamp"] = notif["subscriptionExpirationDateTime"]
                });
            }

            return new JObject { ["success"] = true, ["notifications_count"] = notifications.Count, ["notifications"] = notifications };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to process notification: {ex.Message}");
        }
    }

    private async Task<JObject> ExecuteGetInteractionAsync(JObject args, string correlationId)
    {
        var interactionId = args.Value<string>("interaction_id") ?? "";
        if (string.IsNullOrWhiteSpace(interactionId))
            return CreateErrorResult("interaction_id is required");

        return await ExecuteGraphRequestAsync("GET", $"/copilot/interactionHistory/interactions('{interactionId}')", null, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetMeetingInsightAsync(JObject args, string correlationId)
    {
        var userId = args.Value<string>("user_id") ?? "";
        var meetingId = args.Value<string>("meeting_id") ?? "";
        var insightId = args.Value<string>("insight_id") ?? "";

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(meetingId) || string.IsNullOrWhiteSpace(insightId))
            return CreateErrorResult("user_id, meeting_id, and insight_id are required");

        var path = $"/copilot/users/{userId}/onlineMeetings/{meetingId}/aiInsights/{insightId}";
        return await ExecuteGraphRequestAsync("GET", path, null, correlationId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListInteractionsAsync(JObject args, string correlationId)
    {
        var filterExpression = args.Value<string>("filter_expression");
        var maxResults = args.Value<int?>("max_results") ?? 10;

        var path = "/copilot/interactionHistory/interactions?$top=" + maxResults;
        if (!string.IsNullOrWhiteSpace(filterExpression))
            path += $"&$filter={Uri.EscapeDataString(filterExpression)}";

        return await ExecuteGraphRequestAsync("GET", path, null, correlationId).ConfigureAwait(false);
    }

    // ========================================
    // SUBSCRIPTION OPERATION HANDLERS
    // ========================================

    private async Task<HttpResponseMessage> HandleCreateSubscriptionAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var result = await ExecuteGraphRequestAsync("POST", "/subscriptions", body, correlationId).ConfigureAwait(false);
        
        var statusCode = result.Value<bool?>("success") == false ? HttpStatusCode.BadRequest : HttpStatusCode.Created;
        return CreateJsonResponse(statusCode, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<HttpResponseMessage> HandleListSubscriptionsAsync(string correlationId)
    {
        var result = await ExecuteGraphRequestAsync("GET", "/subscriptions", null, correlationId).ConfigureAwait(false);
        return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<HttpResponseMessage> HandleGetSubscriptionAsync(string correlationId)
    {
        var subscriptionId = this.Context.Request.RequestUri.Segments.LastOrDefault()?.TrimEnd('/');
        
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "subscriptionId is required" }.ToString());

        var validationError = ValidateSubscriptionId(subscriptionId);
        if (!string.IsNullOrWhiteSpace(validationError))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = validationError }.ToString());

        var result = await ExecuteGraphRequestAsync("GET", $"/subscriptions/{subscriptionId}", null, correlationId).ConfigureAwait(false);
        return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<HttpResponseMessage> HandleTestWebhookAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        
        var webhookUrl = body.Value<string>("webhookUrl");
        var clientState = body.Value<string>("clientState") ?? Guid.NewGuid().ToString();

        var validationError = ValidateWebhookUrl(webhookUrl);
        if (!string.IsNullOrWhiteSpace(validationError))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = validationError }.ToString());

        try
        {
            var testPayload = new JObject
            {
                ["value"] = new JArray
                {
                    new JObject
                    {
                        ["subscriptionId"] = "test-webhook-validation",
                        ["changeType"] = "created",
                        ["clientState"] = clientState,
                        ["resource"] = "test",
                        ["resourceData"] = new JObject { ["id"] = "test-id" }
                    }
                }
            };

            var startTime = DateTime.UtcNow;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                var content = new StringContent(testPayload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(webhookUrl, content).ConfigureAwait(false);
                var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                _ = LogToAppInsights("WebhookTest", new { CorrelationId = correlationId, StatusCode = (int)response.StatusCode, ResponseTime = elapsed, WebhookUrl = webhookUrl });

                var result = new JObject
                {
                    ["success"] = response.IsSuccessStatusCode,
                    ["statusCode"] = (int)response.StatusCode,
                    ["responseTime"] = elapsed,
                    ["message"] = response.IsSuccessStatusCode ? "Webhook is reachable and responding" : $"Webhook returned {response.StatusCode}"
                };

                return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
            }
        }
        catch (HttpRequestException ex)
        {
            _ = LogToAppInsights("WebhookTestError", new { CorrelationId = correlationId, Error = ex.Message, WebhookUrl = webhookUrl });
            var result = new JObject
            {
                ["success"] = false,
                ["statusCode"] = 0,
                ["message"] = $"Webhook unreachable: {ex.Message}"
            };
            return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (TaskCanceledException ex)
        {
            _ = LogToAppInsights("WebhookTestTimeout", new { CorrelationId = correlationId, Error = ex.Message, WebhookUrl = webhookUrl });
            var result = new JObject
            {
                ["success"] = false,
                ["statusCode"] = 0,
                ["message"] = "Webhook test timed out (10 second timeout exceeded)"
            };
            return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    private async Task<HttpResponseMessage> HandleDeleteSubscriptionAsync(string correlationId)
    {
        var subscriptionId = this.Context.Request.Headers.GetValues("subscriptionId").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "subscriptionId parameter required" }.ToString());

        await ExecuteGraphRequestAsync("DELETE", $"/subscriptions/{subscriptionId}", null, correlationId).ConfigureAwait(false);
        return CreateJsonResponse(HttpStatusCode.NoContent, "");
    }

    private async Task<HttpResponseMessage> HandleRenewSubscriptionAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var subscriptionId = this.Context.Request.Headers.GetValues("subscriptionId").FirstOrDefault();

        if (string.IsNullOrWhiteSpace(subscriptionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "subscriptionId parameter required" }.ToString());

        var result = await ExecuteGraphRequestAsync("PATCH", $"/subscriptions/{subscriptionId}", body, correlationId).ConfigureAwait(false);
        return CreateJsonResponse(HttpStatusCode.OK, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    private async Task<JObject> ExecuteGraphRequestAsync(string method, string path, JObject body, string correlationId)
    {
        try
        {
            var uri = new UriBuilder(this.Context.Request.RequestUri)
            {
                Path = "/v1.0" + path
            }.Uri;

            var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (body != null)
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _ = LogToAppInsights("GraphRequestSuccess", new { CorrelationId = correlationId, Method = method, Path = path, StatusCode = (int)response.StatusCode });
                if (string.IsNullOrWhiteSpace(responseBody))
                    return new JObject { ["success"] = true };
                return JObject.Parse(responseBody);
            }

            _ = LogToAppInsights("GraphRequestError", new { CorrelationId = correlationId, Method = method, Path = path, StatusCode = (int)response.StatusCode, Error = responseBody });
            return new JObject { ["success"] = false, ["error"] = $"Graph API error: {response.StatusCode}", ["details"] = responseBody };
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("GraphRequestException", new { CorrelationId = correlationId, Method = method, Path = path, Error = ex.Message });
            return CreateErrorResult($"Graph request failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string content)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        return response;
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int errorCode, string errorMessage, string errorData)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = errorCode,
                ["message"] = errorMessage,
                ["data"] = errorData
            }
        };
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private JObject CreateErrorResult(string message)
    {
        return new JObject { ["success"] = false, ["error"] = message };
    }

    private bool LogToAppInsights(string eventName, dynamic properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return false;

        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                var propDict = new Dictionary<string, string>();
                if (properties != null)
                {
                    foreach (var prop in (IDictionary<string, object>)properties)
                    {
                        propDict[prop.Key] = prop.Value?.ToString() ?? "";
                    }
                }

                var telemetryData = new
                {
                    name = "Microsoft.ApplicationInsights.Event",
                    time = DateTime.UtcNow.ToString("O"),
                    iKey = APP_INSIGHTS_KEY,
                    data = new
                    {
                        baseType = "EventData",
                        baseData = new
                        {
                            ver = 2,
                            name = eventName,
                            properties = propDict
                        }
                    }
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json"
                );

                _ = client.PostAsync(APP_INSIGHTS_ENDPOINT, content).ConfigureAwait(false);
            }
        }
        catch { /* Silent fail for telemetry */ }
        return true;
    }

    // ========================================
    // INPUT VALIDATION
    // ========================================

    private string ValidateWebhookUrl(string webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return "webhookUrl is required";

        if (!webhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "webhookUrl must use HTTPS protocol";

        if (webhookUrl.Length > 2048)
            return "webhookUrl exceeds maximum length of 2048 characters";

        try
        {
            _ = new Uri(webhookUrl);
        }
        catch
        {
            return "webhookUrl is not a valid URI";
        }

        return null;
    }

    private string ValidateSubscriptionId(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return "subscriptionId is required";

        // Subscription IDs are typically GUIDs
        if (subscriptionId.Length > 100)
            return "subscriptionId is invalid";

        return null;
    }

    private string ValidateExpirationDateTime(DateTime expirationDateTime)
    {
        var minutesUntilExpiration = (expirationDateTime - DateTime.UtcNow).TotalMinutes;

        if (minutesUntilExpiration < 1)
            return "expirationDateTime must be at least 1 minute in the future";

        // Maximum subscription lifetime is 4,230 minutes (70.5 hours)
        if (minutesUntilExpiration > 4230)
            return "expirationDateTime exceeds maximum subscription lifetime of 4,230 minutes (70.5 hours)";

        return null;
    }

    private string ValidateNotificationUrl(string notificationUrl)
    {
        return ValidateWebhookUrl(notificationUrl);
    }

    private string ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "userId is required";

        // Accept both UPN and object IDs
        if (userId.Length > 255)
            return "userId is invalid (max 255 characters)";

        return null;
    }

    private string ValidateMeetingId(string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return "meetingId is required";

        if (meetingId.Length > 255)
            return "meetingId is invalid (max 255 characters)";

        return null;
    }
}
