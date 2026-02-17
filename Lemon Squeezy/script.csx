using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                Path = Context.Request.RequestUri?.AbsolutePath ?? "unknown"
            });

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                default:
                    response = await Context.SendAsync(Context.Request, CancellationToken);
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            throw;
        }
    }

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await Context.Request.Content.ReadAsStringAsync();
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("MCPRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasParams = @params.HasValues
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCall(@params, requestId, correlationId);

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "lemon-squeezy-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Store Tools
            CreateTool("list_stores", "List all stores for the authenticated user.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("get_store", "Get details of a specific store.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Store ID" }
                },
                new[] { "store_id" }),

            // Product Tools
            CreateTool("list_products", "List products, optionally filtered by store.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_product", "Get details of a specific product.",
                new JObject
                {
                    ["product_id"] = new JObject { ["type"] = "string", ["description"] = "Product ID" }
                },
                new[] { "product_id" }),

            // Variant Tools
            CreateTool("list_variants", "List product variants, optionally filtered by product.",
                new JObject
                {
                    ["product_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by product ID (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_variant", "Get details of a specific variant.",
                new JObject
                {
                    ["variant_id"] = new JObject { ["type"] = "string", ["description"] = "Variant ID" }
                },
                new[] { "variant_id" }),

            // Customer Tools
            CreateTool("list_customers", "List customers, optionally filtered by store or email.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" },
                    ["email"] = new JObject { ["type"] = "string", ["description"] = "Filter by email address (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_customer", "Get details of a specific customer.",
                new JObject
                {
                    ["customer_id"] = new JObject { ["type"] = "string", ["description"] = "Customer ID" }
                },
                new[] { "customer_id" }),

            CreateTool("create_customer", "Create a new customer.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Store ID" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Customer name" },
                    ["email"] = new JObject { ["type"] = "string", ["description"] = "Customer email address" },
                    ["city"] = new JObject { ["type"] = "string", ["description"] = "City (optional)" },
                    ["region"] = new JObject { ["type"] = "string", ["description"] = "Region/state (optional)" },
                    ["country"] = new JObject { ["type"] = "string", ["description"] = "Country code (optional)" }
                },
                new[] { "store_id", "name", "email" }),

            CreateTool("update_customer", "Update an existing customer.",
                new JObject
                {
                    ["customer_id"] = new JObject { ["type"] = "string", ["description"] = "Customer ID" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Customer name (optional)" },
                    ["email"] = new JObject { ["type"] = "string", ["description"] = "Customer email (optional)" },
                    ["city"] = new JObject { ["type"] = "string", ["description"] = "City (optional)" },
                    ["region"] = new JObject { ["type"] = "string", ["description"] = "Region/state (optional)" },
                    ["country"] = new JObject { ["type"] = "string", ["description"] = "Country code (optional)" },
                    ["status"] = new JObject { ["type"] = "string", ["description"] = "Status: archived or active (optional)" }
                },
                new[] { "customer_id" }),

            // Order Tools
            CreateTool("list_orders", "List orders, optionally filtered by store, customer, or email.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" },
                    ["user_email"] = new JObject { ["type"] = "string", ["description"] = "Filter by customer email (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_order", "Get details of a specific order.",
                new JObject
                {
                    ["order_id"] = new JObject { ["type"] = "string", ["description"] = "Order ID" }
                },
                new[] { "order_id" }),

            CreateTool("list_order_items", "List order items for an order.",
                new JObject
                {
                    ["order_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by order ID (optional)" }
                },
                Array.Empty<string>()),

            // Subscription Tools
            CreateTool("list_subscriptions", "List subscriptions, optionally filtered by store, order, or product.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" },
                    ["order_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by order ID (optional)" },
                    ["product_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by product ID (optional)" },
                    ["status"] = new JObject { ["type"] = "string", ["description"] = "Filter by status: on_trial, active, paused, past_due, unpaid, cancelled, expired (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_subscription", "Get details of a specific subscription.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID" }
                },
                new[] { "subscription_id" }),

            CreateTool("cancel_subscription", "Cancel a subscription.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID" }
                },
                new[] { "subscription_id" }),

            CreateTool("pause_subscription", "Pause a subscription.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID" },
                    ["mode"] = new JObject { ["type"] = "string", ["description"] = "Pause mode: void or free (optional, default: void)" }
                },
                new[] { "subscription_id" }),

            CreateTool("resume_subscription", "Resume a paused subscription.",
                new JObject
                {
                    ["subscription_id"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID" }
                },
                new[] { "subscription_id" }),

            // License Key Tools
            CreateTool("list_license_keys", "List license keys, optionally filtered by store, order, or product.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" },
                    ["order_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by order ID (optional)" },
                    ["product_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by product ID (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_license_key", "Get details of a specific license key.",
                new JObject
                {
                    ["license_key_id"] = new JObject { ["type"] = "string", ["description"] = "License key ID" }
                },
                new[] { "license_key_id" }),

            CreateTool("update_license_key", "Update a license key's activation limit or expiration.",
                new JObject
                {
                    ["license_key_id"] = new JObject { ["type"] = "string", ["description"] = "License key ID" },
                    ["activation_limit"] = new JObject { ["type"] = "integer", ["description"] = "Activation limit (optional)" },
                    ["disabled"] = new JObject { ["type"] = "boolean", ["description"] = "Disable the license key (optional)" }
                },
                new[] { "license_key_id" }),

            // Discount Tools
            CreateTool("list_discounts", "List discounts, optionally filtered by store.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by store ID (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_discount", "Get details of a specific discount.",
                new JObject
                {
                    ["discount_id"] = new JObject { ["type"] = "string", ["description"] = "Discount ID" }
                },
                new[] { "discount_id" }),

            CreateTool("create_discount", "Create a new discount code.",
                new JObject
                {
                    ["store_id"] = new JObject { ["type"] = "string", ["description"] = "Store ID" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Discount name" },
                    ["code"] = new JObject { ["type"] = "string", ["description"] = "Discount code" },
                    ["amount"] = new JObject { ["type"] = "integer", ["description"] = "Discount amount (cents for fixed, percentage for percent)" },
                    ["amount_type"] = new JObject { ["type"] = "string", ["description"] = "Amount type: percent or fixed" },
                    ["variant_ids"] = new JObject { ["type"] = "array", ["description"] = "Array of variant IDs this discount applies to (optional)", ["items"] = new JObject { ["type"] = "integer" } },
                    ["is_limited_to_products"] = new JObject { ["type"] = "boolean", ["description"] = "Limit to specific products (optional)" },
                    ["is_limited_redemptions"] = new JObject { ["type"] = "boolean", ["description"] = "Limit number of redemptions (optional)" },
                    ["max_redemptions"] = new JObject { ["type"] = "integer", ["description"] = "Max redemptions if limited (optional)" },
                    ["starts_at"] = new JObject { ["type"] = "string", ["description"] = "Start date ISO 8601 (optional)" },
                    ["expires_at"] = new JObject { ["type"] = "string", ["description"] = "Expiration date ISO 8601 (optional)" },
                    ["duration"] = new JObject { ["type"] = "string", ["description"] = "For subscriptions: once, repeating, or forever (optional)" },
                    ["duration_in_months"] = new JObject { ["type"] = "integer", ["description"] = "Months for repeating duration (optional)" }
                },
                new[] { "store_id", "name", "code", "amount", "amount_type" }),

            CreateTool("delete_discount", "Delete a discount.",
                new JObject
                {
                    ["discount_id"] = new JObject { ["type"] = "string", ["description"] = "Discount ID" }
                },
                new[] { "discount_id" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
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

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("MCPToolCall", new
        {
            CorrelationId = correlationId,
            Tool = toolName,
            HasArguments = arguments.HasValues
        });

        try
        {
            var result = await ExecuteToolAsync(toolName, arguments);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPToolError", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                ErrorMessage = ex.Message
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // Stores
            case "list_stores":
                return await CallLemonSqueezyApi("GET", "/stores");

            case "get_store":
                return await CallLemonSqueezyApi("GET", $"/stores/{args["store_id"]}");

            // Products
            case "list_products":
                var productQuery = BuildFilterQuery(args, "store_id");
                return await CallLemonSqueezyApi("GET", $"/products{productQuery}");

            case "get_product":
                return await CallLemonSqueezyApi("GET", $"/products/{args["product_id"]}");

            // Variants
            case "list_variants":
                var variantQuery = BuildFilterQuery(args, "product_id");
                return await CallLemonSqueezyApi("GET", $"/variants{variantQuery}");

            case "get_variant":
                return await CallLemonSqueezyApi("GET", $"/variants/{args["variant_id"]}");

            // Customers
            case "list_customers":
                var customerFilters = new List<string>();
                if (args["store_id"] != null) customerFilters.Add($"filter[store_id]={args["store_id"]}");
                if (args["email"] != null) customerFilters.Add($"filter[email]={Uri.EscapeDataString(args["email"].ToString())}");
                var customerQuery = customerFilters.Count > 0 ? "?" + string.Join("&", customerFilters) : "";
                return await CallLemonSqueezyApi("GET", $"/customers{customerQuery}");

            case "get_customer":
                return await CallLemonSqueezyApi("GET", $"/customers/{args["customer_id"]}");

            case "create_customer":
                var createCustomerBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "customers",
                        ["attributes"] = new JObject
                        {
                            ["name"] = args["name"],
                            ["email"] = args["email"]
                        },
                        ["relationships"] = new JObject
                        {
                            ["store"] = new JObject
                            {
                                ["data"] = new JObject
                                {
                                    ["type"] = "stores",
                                    ["id"] = args["store_id"]
                                }
                            }
                        }
                    }
                };
                if (args["city"] != null) createCustomerBody["data"]["attributes"]["city"] = args["city"];
                if (args["region"] != null) createCustomerBody["data"]["attributes"]["region"] = args["region"];
                if (args["country"] != null) createCustomerBody["data"]["attributes"]["country"] = args["country"];
                return await CallLemonSqueezyApi("POST", "/customers", createCustomerBody);

            case "update_customer":
                var updateCustomerBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "customers",
                        ["id"] = args["customer_id"],
                        ["attributes"] = new JObject()
                    }
                };
                var custAttrs = updateCustomerBody["data"]["attributes"] as JObject;
                if (args["name"] != null) custAttrs["name"] = args["name"];
                if (args["email"] != null) custAttrs["email"] = args["email"];
                if (args["city"] != null) custAttrs["city"] = args["city"];
                if (args["region"] != null) custAttrs["region"] = args["region"];
                if (args["country"] != null) custAttrs["country"] = args["country"];
                if (args["status"] != null) custAttrs["status"] = args["status"];
                return await CallLemonSqueezyApi("PATCH", $"/customers/{args["customer_id"]}", updateCustomerBody);

            // Orders
            case "list_orders":
                var orderFilters = new List<string>();
                if (args["store_id"] != null) orderFilters.Add($"filter[store_id]={args["store_id"]}");
                if (args["user_email"] != null) orderFilters.Add($"filter[user_email]={Uri.EscapeDataString(args["user_email"].ToString())}");
                var orderQuery = orderFilters.Count > 0 ? "?" + string.Join("&", orderFilters) : "";
                return await CallLemonSqueezyApi("GET", $"/orders{orderQuery}");

            case "get_order":
                return await CallLemonSqueezyApi("GET", $"/orders/{args["order_id"]}");

            case "list_order_items":
                var orderItemQuery = BuildFilterQuery(args, "order_id");
                return await CallLemonSqueezyApi("GET", $"/order-items{orderItemQuery}");

            // Subscriptions
            case "list_subscriptions":
                var subFilters = new List<string>();
                if (args["store_id"] != null) subFilters.Add($"filter[store_id]={args["store_id"]}");
                if (args["order_id"] != null) subFilters.Add($"filter[order_id]={args["order_id"]}");
                if (args["product_id"] != null) subFilters.Add($"filter[product_id]={args["product_id"]}");
                if (args["status"] != null) subFilters.Add($"filter[status]={args["status"]}");
                var subQuery = subFilters.Count > 0 ? "?" + string.Join("&", subFilters) : "";
                return await CallLemonSqueezyApi("GET", $"/subscriptions{subQuery}");

            case "get_subscription":
                return await CallLemonSqueezyApi("GET", $"/subscriptions/{args["subscription_id"]}");

            case "cancel_subscription":
                return await CallLemonSqueezyApi("DELETE", $"/subscriptions/{args["subscription_id"]}");

            case "pause_subscription":
                var pauseBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "subscriptions",
                        ["id"] = args["subscription_id"],
                        ["attributes"] = new JObject
                        {
                            ["pause"] = new JObject
                            {
                                ["mode"] = args["mode"]?.ToString() ?? "void"
                            }
                        }
                    }
                };
                return await CallLemonSqueezyApi("PATCH", $"/subscriptions/{args["subscription_id"]}", pauseBody);

            case "resume_subscription":
                var resumeBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "subscriptions",
                        ["id"] = args["subscription_id"],
                        ["attributes"] = new JObject
                        {
                            ["pause"] = null
                        }
                    }
                };
                return await CallLemonSqueezyApi("PATCH", $"/subscriptions/{args["subscription_id"]}", resumeBody);

            // License Keys
            case "list_license_keys":
                var licenseFilters = new List<string>();
                if (args["store_id"] != null) licenseFilters.Add($"filter[store_id]={args["store_id"]}");
                if (args["order_id"] != null) licenseFilters.Add($"filter[order_id]={args["order_id"]}");
                if (args["product_id"] != null) licenseFilters.Add($"filter[product_id]={args["product_id"]}");
                var licenseQuery = licenseFilters.Count > 0 ? "?" + string.Join("&", licenseFilters) : "";
                return await CallLemonSqueezyApi("GET", $"/license-keys{licenseQuery}");

            case "get_license_key":
                return await CallLemonSqueezyApi("GET", $"/license-keys/{args["license_key_id"]}");

            case "update_license_key":
                var updateLicenseBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "license-keys",
                        ["id"] = args["license_key_id"],
                        ["attributes"] = new JObject()
                    }
                };
                var licAttrs = updateLicenseBody["data"]["attributes"] as JObject;
                if (args["activation_limit"] != null) licAttrs["activation_limit"] = args["activation_limit"];
                if (args["disabled"] != null) licAttrs["disabled"] = args["disabled"];
                return await CallLemonSqueezyApi("PATCH", $"/license-keys/{args["license_key_id"]}", updateLicenseBody);

            // Discounts
            case "list_discounts":
                var discountQuery = BuildFilterQuery(args, "store_id");
                return await CallLemonSqueezyApi("GET", $"/discounts{discountQuery}");

            case "get_discount":
                return await CallLemonSqueezyApi("GET", $"/discounts/{args["discount_id"]}");

            case "create_discount":
                var createDiscountBody = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "discounts",
                        ["attributes"] = new JObject
                        {
                            ["name"] = args["name"],
                            ["code"] = args["code"],
                            ["amount"] = args["amount"],
                            ["amount_type"] = args["amount_type"]
                        },
                        ["relationships"] = new JObject
                        {
                            ["store"] = new JObject
                            {
                                ["data"] = new JObject
                                {
                                    ["type"] = "stores",
                                    ["id"] = args["store_id"]
                                }
                            }
                        }
                    }
                };
                var discAttrs = createDiscountBody["data"]["attributes"] as JObject;
                if (args["variant_ids"] != null) 
                {
                    createDiscountBody["data"]["relationships"]["variants"] = new JObject
                    {
                        ["data"] = args["variant_ids"]
                    };
                }
                if (args["is_limited_to_products"] != null) discAttrs["is_limited_to_products"] = args["is_limited_to_products"];
                if (args["is_limited_redemptions"] != null) discAttrs["is_limited_redemptions"] = args["is_limited_redemptions"];
                if (args["max_redemptions"] != null) discAttrs["max_redemptions"] = args["max_redemptions"];
                if (args["starts_at"] != null) discAttrs["starts_at"] = args["starts_at"];
                if (args["expires_at"] != null) discAttrs["expires_at"] = args["expires_at"];
                if (args["duration"] != null) discAttrs["duration"] = args["duration"];
                if (args["duration_in_months"] != null) discAttrs["duration_in_months"] = args["duration_in_months"];
                return await CallLemonSqueezyApi("POST", "/discounts", createDiscountBody);

            case "delete_discount":
                await CallLemonSqueezyApi("DELETE", $"/discounts/{args["discount_id"]}");
                return new JObject { ["success"] = true, ["deleted"] = args["discount_id"] };

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private string BuildFilterQuery(JObject args, string filterField)
    {
        var value = args[filterField];
        return value != null ? $"?filter[{filterField}]={value}" : "";
    }

    private async Task<JObject> CallLemonSqueezyApi(string method, string path, JObject body = null)
    {
        var baseUri = "https://api.lemonsqueezy.com/v1";
        var fullPath = $"{baseUri}{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);

        // Copy authorization header from original request
        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Lemon Squeezy API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrEmpty(content))
            return new JObject { ["success"] = true };

        // Handle array responses
        if (content.TrimStart().StartsWith("["))
        {
            var arr = JArray.Parse(content);
            return new JObject { ["items"] = arr };
        }

        return JObject.Parse(content);
    }

    #endregion

    #region JSON-RPC Helpers

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    #endregion

    #region Application Insights Logging

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return; // Telemetry disabled

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        }
        catch { } // Suppress telemetry errors
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("InstrumentationKey=".Length);
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion
}
