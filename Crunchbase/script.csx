public class Script : ScriptBase
{
    private const string SERVER_NAME = "crunchbase-mcp";
    private const string VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-12-01";

    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "searchOrganizations",
            ["description"] = "Search for companies/organizations in Crunchbase with filters like location, industry, funding",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["field_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Fields to return (e.g., identifier, website, categories, location_identifiers)"
                    },
                    ["query"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "object" },
                        ["description"] = "Search filters (AND logic). Example: {\"type\":\"predicate\",\"field_id\":\"location_identifiers\",\"operator_id\":\"includes\",\"values\":[\"sf\"]}"
                    },
                    ["limit"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Results per page (default 50, max 1000)"
                    },
                    ["after_id"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "UUID of last item for pagination (next page)"
                    }
                },
                ["required"] = new JArray { "field_ids" }
            }
        },
        new JObject
        {
            ["name"] = "getOrganization",
            ["description"] = "Retrieve detailed data for a specific organization by UUID or permalink",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_id"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Organization UUID or permalink (e.g., crunchbase, tesla-motors)"
                    },
                    ["field_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Optional: specific fields to return"
                    },
                    ["card_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Optional: relationships to include (e.g., founders, raised_funding_rounds, acquisitions)"
                    }
                },
                ["required"] = new JArray { "entity_id" }
            }
        },
        new JObject
        {
            ["name"] = "searchPeople",
            ["description"] = "Search for people/individuals in Crunchbase",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["field_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Fields to return (e.g., identifier, title, location_identifiers)"
                    },
                    ["query"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "object" },
                        ["description"] = "Search filters"
                    },
                    ["limit"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Results per page (default 50, max 1000)"
                    }
                },
                ["required"] = new JArray { "field_ids" }
            }
        },
        new JObject
        {
            ["name"] = "getPerson",
            ["description"] = "Retrieve detailed data for a specific person by UUID or permalink",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_id"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Person UUID or permalink"
                    },
                    ["field_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Optional: specific fields to return"
                    },
                    ["card_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Optional: relationships (e.g., current_roles, past_roles, investors)"
                    }
                },
                ["required"] = new JArray { "entity_id" }
            }
        },
        new JObject
        {
            ["name"] = "searchFundingRounds",
            ["description"] = "Search for funding rounds with filters like amount raised, date range, investment type",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["field_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Fields to return (e.g., identifier, announced_on, money_raised, investment_type)"
                    },
                    ["query"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "object" },
                        ["description"] = "Search filters"
                    },
                    ["limit"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Results per page (default 50, max 1000)"
                    }
                },
                ["required"] = new JArray { "field_ids" }
            }
        },
        new JObject
        {
            ["name"] = "getAutocomplete",
            ["description"] = "Get autocomplete suggestions for companies, people, and locations",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Search query string (e.g., 'microsoft', 'steve jobs')"
                    },
                    ["collection"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Entity type: organizations, people, or locations"
                    }
                },
                ["required"] = new JArray { "query", "collection" }
            }
        }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var requestPath = this.Context.Request.RequestUri.AbsolutePath;
        if (requestPath.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            return await HandleMCPProtocolAsync().ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleMCPProtocolAsync()
    {
        try
        {
            var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var requestObj = JObject.Parse(requestBody);

            var method = requestObj["method"]?.ToString();
            var id = requestObj["id"];

            return method switch
            {
                "initialize" => CreateMCPSuccessResponse(new JObject
                {
                    ["protocolVersion"] = PROTOCOL_VERSION,
                    ["capabilities"] = new JObject(),
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = SERVER_NAME,
                        ["version"] = VERSION
                    }
                }, id),

                "tools/list" => CreateMCPSuccessResponse(new JObject { ["tools"] = AVAILABLE_TOOLS }, id),

                "tools/call" => await HandleToolsCallAsync(requestObj, id).ConfigureAwait(false),

                "ping" => CreateMCPSuccessResponse(new JObject(), id),

                _ => CreateMCPErrorResponse("Method not found", id)
            };
        }
        catch (Exception ex)
        {
            return CreateMCPErrorResponse($"Error: {ex.Message}", null);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject requestObj, JToken id)
    {
        var toolName = requestObj["params"]?["name"]?.ToString();
        var args = requestObj["params"]?["arguments"] as JObject;

        try
        {
            var result = toolName switch
            {
                "searchOrganizations" => await ExecuteSearchAsync("organizations", args).ConfigureAwait(false),
                "getOrganization" => await ExecuteGetEntityAsync("organizations", args).ConfigureAwait(false),
                "searchPeople" => await ExecuteSearchAsync("people", args).ConfigureAwait(false),
                "getPerson" => await ExecuteGetEntityAsync("people", args).ConfigureAwait(false),
                "searchFundingRounds" => await ExecuteSearchAsync("funding_rounds", args).ConfigureAwait(false),
                "getAutocomplete" => await ExecuteAutocompleteAsync(args).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown tool: {toolName}")
            };

            var content = new JArray { new JObject { ["type"] = "text", ["text"] = result } };
            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            return CreateMCPErrorResponse($"Tool execution failed: {ex.Message}", id);
        }
    }

    private async Task<string> ExecuteSearchAsync(string collection, JObject args)
    {
        if (args == null) throw new ArgumentException("Arguments required");

        var fieldIds = args["field_ids"];
        var query = args["query"];
        var limit = args["limit"]?.Value<int>() ?? 50;
        var afterId = args["after_id"]?.ToString();

        var body = new JObject
        {
            ["field_ids"] = fieldIds ?? new JArray(),
            ["limit"] = limit
        };

        if (query != null)
            body["query"] = query;
        if (!string.IsNullOrWhiteSpace(afterId))
            body["after_id"] = afterId;

        var url = $"searches/{collection}";
        var response = await MakeRequestAsync("POST", url, body.ToString()).ConfigureAwait(false);
        return response;
    }

    private async Task<string> ExecuteGetEntityAsync(string collection, JObject args)
    {
        if (args == null) throw new ArgumentException("Arguments required");

        var entityId = args["entity_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("entity_id is required");

        var fieldIds = args["field_ids"];
        var cardIds = args["card_ids"];

        var url = $"entities/{collection}/{Uri.EscapeDataString(entityId)}";
        var queryParams = new List<string>();

        if (fieldIds is JArray fArray && fArray.Count > 0)
            queryParams.Add($"field_ids={string.Join(",", fArray.Values<string>())}");

        if (cardIds is JArray cArray && cArray.Count > 0)
            queryParams.Add($"card_ids={string.Join(",", cArray.Values<string>())}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await MakeRequestAsync("GET", url, null).ConfigureAwait(false);
        return response;
    }

    private async Task<string> ExecuteAutocompleteAsync(JObject args)
    {
        if (args == null) throw new ArgumentException("Arguments required");

        var query = args["query"]?.ToString();
        var collection = args["collection"]?.ToString();

        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("query is required");
        if (string.IsNullOrWhiteSpace(collection)) throw new ArgumentException("collection is required");

        var url = $"autocompletes?query={Uri.EscapeDataString(query)}&collection={Uri.EscapeDataString(collection)}";
        var response = await MakeRequestAsync("GET", url, null).ConfigureAwait(false);
        return response;
    }

    private async Task<string> MakeRequestAsync(string method, string relativeUrl, string body)
    {
        var userKey = this.Context.Request.Headers.FirstOrDefault(h => h.Key == "X-cb-user-key").Value?.FirstOrDefault();

        using (var client = new HttpClient())
        {
            var url = $"https://api.crunchbase.com/v4/data/{relativeUrl}";
            if (!url.Contains("?") && !string.IsNullOrWhiteSpace(userKey))
                url += $"?user_key={userKey}";
            else if (string.IsNullOrWhiteSpace(userKey))
                url += (url.Contains("?") ? "&" : "?") + $"user_key=";

            var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.Add("X-cb-user-key", userKey ?? "");

            if (method == "POST" && !string.IsNullOrWhiteSpace(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? content
                : $"Error ({response.StatusCode}): {content}";
        }
    }

    private HttpResponseMessage CreateMCPSuccessResponse(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (id != null) response["id"] = id;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMCPErrorResponse(string message, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject
            {
                ["code"] = -1,
                ["message"] = message
            }
        };
        if (id != null) response["id"] = id;

        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }
}
