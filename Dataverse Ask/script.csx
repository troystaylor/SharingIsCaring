public class Script : ScriptBase
{
    private const string ApiPath = "/api/iq/v1.0/";
    private const string WebApiPath = "/api/data/v9.2/";

    private const int MaxTablesReturned = 250;

    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const int MaxThrottleRetries = 2;
    private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromSeconds(20);

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (string.Equals(this.Context.OperationId, "ListTables", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleListTablesOperationAsync().ConfigureAwait(false);
        }

        if (!string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
        {
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }

        JObject request;

        try
        {
            var body = this.Context.Request.Content == null
                ? string.Empty
                : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

            request = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
        catch (JsonReaderException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
        }

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var parameters = request["params"] as JObject ?? new JObject();

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
                return await HandleToolsCallAsync(parameters, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

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
                ["name"] = "dataverse-ask",
                ["version"] = "1.0.0"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "list_semantic_models",
                ["description"] = "List the semantic models in this Dataverse environment. A semantic model names the tables that can answer a question. Call this first to find the exact model name to pass to ask_dataverse, because the name must match exactly. Set include_usage to true to also see which apps and agents depend on each model.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["include_usage"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Also return the model-driven apps, bot components and agents that use each model. Defaults to false.",
                            ["default"] = false
                        }
                    }
                }
            },
            new JObject
            {
                ["name"] = "ask_dataverse",
                ["description"] = "Ask a natural-language question about Dataverse business data using a semantic model, and get back the rows that answer it plus a plain-language summary and links to the source records. Use this for questions about business data such as opportunities, accounts, cases or leads. Results are limited to what the signed-in user is allowed to read. Get the semantic model name from list_semantic_models first.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The question to answer, for example: Which open opportunities have the highest estimated revenue?"
                        },
                        ["semantic_model_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The exact unique name of the semantic model, as returned by list_semantic_models."
                        },
                        ["search_mode"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "Auto", "QuickResponse", "ThinkDeeper" },
                            ["description"] = "Search strategy. Auto lets the service choose, QuickResponse favors speed, ThinkDeeper favors depth and takes longer. Defaults to Auto.",
                            ["default"] = "Auto"
                        },
                        ["count"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum rows to return in this page. Defaults to 100.",
                            ["default"] = 100
                        },
                        ["paging_token"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The paging token from a previous answer, to get the next page. Pass it back exactly as returned. Omit for the first page."
                        }
                    },
                    ["required"] = new JArray { "query", "semantic_model_name" }
                }
            },
            new JObject
            {
                ["name"] = "list_tables",
                ["description"] = "List the Dataverse tables in this environment with their logical names. Call this before create_semantic_model to get exact logical names, because table names must be logical names such as account or opportunity — display names and plural entity set names such as Accounts are rejected. Use the search argument to narrow a large environment.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["search"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional text to match against the logical name or display name, for example: opportunity."
                        }
                    }
                }
            },
            new JObject
            {
                ["name"] = "create_semantic_model",
                ["description"] = "Create a semantic model over one or more Dataverse tables so questions can be asked against them. Use table logical names such as account or opportunity, not display names or plural entity set names. Names must be unique in the environment. Indexing takes up to two hours, so questions asked immediately after creation may return little or nothing.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["unique_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The unique name for the new semantic model, for example: Sales pipeline."
                        },
                        ["tables"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Dataverse table logical names to include, for example: account, opportunity.",
                            ["items"] = new JObject { ["type"] = "string" }
                        }
                    },
                    ["required"] = new JArray { "unique_name", "tables" }
                }
            },
            new JObject
            {
                ["name"] = "delete_semantic_model",
                ["description"] = "Delete a semantic model by its ID, which comes from list_semantic_models. Only models created through this API can be deleted; models belonging to a model-driven app, bot component or agent cannot. Check which apps and agents use a model first by calling list_semantic_models with include_usage set to true.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The unique identifier of the semantic model to delete."
                        }
                    },
                    ["required"] = new JArray { "id" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject parameters, JToken requestId)
    {
        var toolName = parameters["name"]?.ToString();
        var arguments = parameters["arguments"] as JObject ?? new JObject();

        try
        {
            JObject result;

            switch (toolName)
            {
                case "list_semantic_models":
                    result = await ListSemanticModelsAsync(arguments).ConfigureAwait(false);
                    break;

                case "ask_dataverse":
                    result = await AskAsync(arguments).ConfigureAwait(false);
                    break;

                case "create_semantic_model":
                    result = await CreateSemanticModelAsync(arguments).ConfigureAwait(false);
                    break;

                case "list_tables":
                    result = await ListTablesToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "delete_semantic_model":
                    result = await DeleteSemanticModelAsync(arguments).ConfigureAwait(false);
                    break;

                default:
                    throw new AskException("unknown_tool", "No tool named '" + toolName + "'. Call tools/list to see the available tools.");
            }

            await LogToAppInsightsAsync("ToolCall", new Dictionary<string, string>
            {
                ["connector"] = "Dataverse Ask",
                ["tool"] = toolName ?? string.Empty,
                ["outcome"] = "success"
            }).ConfigureAwait(false);

            return CreateToolResult(requestId, result, false);
        }
        catch (AskException ex)
        {
            await LogToAppInsightsAsync("ToolCallFailed", new Dictionary<string, string>
            {
                ["connector"] = "Dataverse Ask",
                ["tool"] = toolName ?? string.Empty,
                ["errorCode"] = ex.Code,
                ["errorMessage"] = ex.Message
            }).ConfigureAwait(false);

            return CreateToolResult(requestId, ex.ToJson(), true);
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("ToolCallFailed", new Dictionary<string, string>
            {
                ["connector"] = "Dataverse Ask",
                ["tool"] = toolName ?? string.Empty,
                ["errorCode"] = "unexpected",
                ["errorMessage"] = ex.Message,
                ["stackTrace"] = ex.StackTrace ?? string.Empty
            }).ConfigureAwait(false);

            var payload = new JObject
            {
                ["error"] = "unexpected",
                ["message"] = ex.Message
            };

            return CreateToolResult(requestId, payload, true);
        }
    }

    private async Task<JObject> ListSemanticModelsAsync(JObject arguments)
    {
        var includeUsage = arguments["include_usage"]?.Type == JTokenType.Boolean
            && arguments["include_usage"].Value<bool>();

        var url = BaseUrl() + "semanticmodel" + (includeUsage ? "?include=botinfo" : string.Empty);
        var response = await SendAskAsync(HttpMethod.Get, url, null).ConfigureAwait(false);

        var models = response["data"] as JArray ?? new JArray();

        // The agent picks a model by name on the next call, so the summary leads with the
        // names and the tables behind them rather than making it read the whole record.
        var summary = new JArray();

        foreach (var model in models.OfType<JObject>())
        {
            var entry = new JObject
            {
                ["id"] = model["id"],
                ["uniqueName"] = model["uniqueName"],
                ["tables"] = model["tables"],
                ["description"] = model["description"],
                ["createdByApi"] = string.Equals(model["source"]?.ToString(), "SemanticModelAPI", StringComparison.OrdinalIgnoreCase)
            };

            if (includeUsage)
            {
                entry["usedByApps"] = new JArray(
                    new[] { model["appModulePrimaryName"], model["appModuleSecondaryName"] }
                        .Where(n => n != null && n.Type != JTokenType.Null && !string.IsNullOrEmpty(n.ToString()))
                        .Select(n => n.ToString()));

                entry["usedByAgents"] = new JArray(
                    (model["bots"] as JArray ?? new JArray())
                        .OfType<JObject>()
                        .Select(b => b["agentName"])
                        .Where(n => n != null && n.Type != JTokenType.Null)
                        .Select(n => n.ToString())
                        .Distinct());
            }

            summary.Add(entry);
        }

        return new JObject
        {
            ["count"] = summary.Count,
            ["semanticModels"] = summary,
            ["note"] = summary.Count == 0
                ? "No semantic models exist in this environment yet. Use create_semantic_model to make one."
                : "Only models where createdByApi is true can be deleted through delete_semantic_model."
        };
    }

    private async Task<JObject> AskAsync(JObject arguments)
    {
        var query = RequiredString(arguments, "query");
        var modelName = RequiredString(arguments, "semantic_model_name");

        var payload = new JObject
        {
            ["query"] = query,
            ["semanticModelName"] = modelName
        };

        var searchMode = arguments["search_mode"]?.ToString();

        if (!string.IsNullOrWhiteSpace(searchMode))
        {
            if (!string.Equals(searchMode, "Auto", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(searchMode, "QuickResponse", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(searchMode, "ThinkDeeper", StringComparison.OrdinalIgnoreCase))
            {
                throw new AskException("invalid_search_mode",
                    "search_mode must be Auto, QuickResponse or ThinkDeeper.",
                    new JArray { "Auto", "QuickResponse", "ThinkDeeper" });
            }

            payload["searchMode"] = searchMode;
        }

        if (arguments["count"]?.Type == JTokenType.Integer)
        {
            payload["count"] = arguments["count"].Value<int>();
        }

        var pagingToken = arguments["paging_token"]?.ToString();

        if (!string.IsNullOrWhiteSpace(pagingToken))
        {
            payload["pagingToken"] = pagingToken;
        }

        var response = await SendAskAsync(HttpMethod.Post, BaseUrl() + "ask", payload).ConfigureAwait(false);

        var rows = response["rawResult"] as JArray ?? new JArray();
        var token = response["pagingToken"]?.Type == JTokenType.Null ? null : response["pagingToken"]?.ToString();

        var result = new JObject
        {
            ["summary"] = response["summary"],
            ["rows"] = rows,
            ["rowCount"] = rows.Count,
            ["totalResultCount"] = response["totalResultCount"],
            ["citationLinks"] = response["citationLinks"] ?? new JArray(),
            ["hasMore"] = !string.IsNullOrEmpty(token)
        };

        if (!string.IsNullOrEmpty(token))
        {
            result["pagingToken"] = token;
            result["note"] = "More rows are available. Call ask_dataverse again with the same query, semantic_model_name, search_mode and count, adding this paging_token unchanged.";
        }

        return result;
    }

    private async Task<JObject> CreateSemanticModelAsync(JObject arguments)
    {
        var uniqueName = RequiredString(arguments, "unique_name");
        var tables = arguments["tables"] as JArray;

        if (tables == null || tables.Count == 0)
        {
            throw new AskException("missing_tables",
                "tables must list at least one Dataverse table logical name, such as account or opportunity.");
        }

        var logicalNames = new JArray();

        foreach (var table in tables)
        {
            var name = table?.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            logicalNames.Add(name.Trim());
        }

        if (logicalNames.Count == 0)
        {
            throw new AskException("missing_tables",
                "tables must list at least one Dataverse table logical name, such as account or opportunity.");
        }

        var payload = new JObject
        {
            ["uniqueName"] = uniqueName,
            ["tables"] = logicalNames
        };

        var response = await SendAskAsync(HttpMethod.Post, BaseUrl() + "semanticmodel", payload).ConfigureAwait(false);
        var data = response["data"] as JObject ?? new JObject();

        return new JObject
        {
            ["id"] = data["id"],
            ["uniqueName"] = data["uniqueName"],
            ["createdOn"] = data["createdOn"],
            ["tables"] = logicalNames,
            ["message"] = response["message"],
            ["note"] = "Indexing has started. Initial generation can take up to two hours, so questions asked now may return few or no rows."
        };
    }

    private async Task<JObject> DeleteSemanticModelAsync(JObject arguments)
    {
        var id = RequiredString(arguments, "id");

        var response = await SendAskAsync(HttpMethod.Delete, BaseUrl() + "semanticmodel/" + Uri.EscapeDataString(id), null)
            .ConfigureAwait(false);

        var data = response["data"] as JObject ?? new JObject();

        return new JObject
        {
            ["id"] = data["id"] ?? id,
            ["message"] = response["message"] ?? "Semantic model deleted."
        };
    }

    /// <summary>
    /// Reads the environment's table list from the Web API. The Ask API has no table endpoint, so the
    /// logical names that Create needs come from EntityDefinitions on the same host and token.
    /// </summary>
    private async Task<List<JObject>> GetTablesAsync()
    {
        var url = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority)
            + WebApiPath
            + "EntityDefinitions?$select=LogicalName,DisplayName&$filter=IsPrivate eq false and IsIntersect eq false";

        var response = await SendJsonAsync(HttpMethod.Get, url, null, false).ConfigureAwait(false);
        var rows = response["value"] as JArray ?? new JArray();
        var tables = new List<JObject>();

        foreach (var row in rows.OfType<JObject>())
        {
            var logicalName = row["LogicalName"]?.ToString();

            if (string.IsNullOrEmpty(logicalName))
            {
                continue;
            }

            // UserLocalizedLabel is JSON null for tables with no localized label, which is a JValue
            // rather than a missing token, so indexing into it throws. SelectToken walks it safely.
            var labelToken = row.SelectToken("DisplayName.UserLocalizedLabel.Label");
            var label = labelToken == null || labelToken.Type == JTokenType.Null
                ? null
                : labelToken.ToString();

            var displayName = string.IsNullOrWhiteSpace(label) ? logicalName : label;

            tables.Add(new JObject
            {
                ["logicalName"] = logicalName,
                ["displayName"] = displayName,
                ["title"] = string.Equals(displayName, logicalName, StringComparison.Ordinal)
                    ? logicalName
                    : displayName + " (" + logicalName + ")"
            });
        }

        return tables
            .OrderBy(t => t["displayName"].ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Backs the x-ms-dynamic-values dropdown on Create semantic model.</summary>
    private async Task<HttpResponseMessage> HandleListTablesOperationAsync()
    {
        try
        {
            var tables = await GetTablesAsync().ConfigureAwait(false);

            var payload = new JObject { ["value"] = new JArray(tables) };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
        }
        catch (AskException ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    ex.ToJson().ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private async Task<JObject> ListTablesToolAsync(JObject arguments)
    {
        var tables = await GetTablesAsync().ConfigureAwait(false);
        var search = arguments["search"]?.ToString();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            tables = tables
                .Where(t => t["logicalName"].ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || t["displayName"].ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        var total = tables.Count;
        var truncated = total > MaxTablesReturned;

        var result = new JObject
        {
            ["count"] = truncated ? MaxTablesReturned : total,
            ["totalMatching"] = total,
            ["tables"] = new JArray(tables
                .Take(MaxTablesReturned)
                .Select(t => new JObject
                {
                    ["logicalName"] = t["logicalName"],
                    ["displayName"] = t["displayName"]
                }))
        };

        if (total == 0)
        {
            result["note"] = string.IsNullOrWhiteSpace(search)
                ? "No tables were returned. Check that the connection has read access to table metadata."
                : "No table matched '" + search + "'. Try a shorter term, or omit search to see everything.";
        }
        else if (truncated)
        {
            result["note"] = "Showing the first " + MaxTablesReturned + " of " + total + " tables. Pass a search term to narrow the list.";
        }
        else
        {
            result["note"] = "Pass logicalName values to create_semantic_model, not displayName.";
        }

        return result;
    }

    private string BaseUrl()
    {
        return this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority) + ApiPath;
    }

    private static string RequiredString(JObject arguments, string name)
    {
        var value = arguments[name]?.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AskException("missing_argument", "The '" + name + "' argument is required.");
        }

        return value.Trim();
    }

    private async Task<JObject> SendAskAsync(HttpMethod method, string url, JObject payload)
    {
        return await SendJsonAsync(method, url, payload, true).ConfigureAwait(false);
    }

    private async Task<JObject> SendJsonAsync(HttpMethod method, string url, JObject payload, bool isAskApi)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = new HttpRequestMessage(method, url);

            if (this.Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (!isAskApi)
            {
                request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
                request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
            }

            if (payload != null)
            {
                request.Content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json");
            }

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

            var raw = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if ((int)response.StatusCode == 429 && attempt < MaxThrottleRetries)
            {
                var wait = RetryAfterOf(response);

                if (wait <= MaxThrottleWait)
                {
                    await Task.Delay(wait, this.CancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response, raw, isAskApi);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(raw);
            }
            catch (JsonReaderException)
            {
                throw new AskException("invalid_response",
                    "The service returned a response that is not JSON.");
            }
        }
    }

    private static TimeSpan RetryAfterOf(HttpResponseMessage response)
    {
        IEnumerable<string> values;

        if (response.Headers.TryGetValues("Retry-After", out values))
        {
            var header = values.FirstOrDefault();
            int seconds;

            if (int.TryParse(header, out seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return TimeSpan.FromSeconds(5);
    }

    private static AskException BuildException(HttpResponseMessage response, string raw, bool isAskApi)
    {
        var status = (int)response.StatusCode;
        var detail = ExtractMessage(raw);

        switch (status)
        {
            case 400:
                return isAskApi
                    ? new AskException("bad_request",
                        "The request was rejected. This is most often because Business Applications in Work IQ is not enabled for this environment, or because no table in the semantic model is one you are allowed to read. "
                        + detail)
                    : new AskException("bad_request",
                        "The table metadata request was rejected. " + detail);

            case 401:
                return new AskException("unauthorized",
                    "The access token is missing, expired, or was issued for a different resource. " + detail);

            case 403:
                return isAskApi
                    ? new AskException("forbidden",
                        "You lack a required privilege. Creating and deleting semantic models needs the Dataverse Search Role, Environment Maker, System Customizer or System Administrator role. " + detail)
                    : new AskException("forbidden",
                        "You lack permission to read table metadata in this environment. " + detail);

            case 404:
                return new AskException("not_found",
                    "No matching semantic model was found. Names and IDs are exact — call list_semantic_models to get the current values. " + detail);

            case 429:
                return new AskException("throttled",
                    "Service protection limits are in effect. The Ask API allows 30 requests per user per minute. Wait before retrying, and avoid sending several requests at once. " + detail);

            default:
                return new AskException("http_" + status,
                    "The service returned " + status + ". " + detail);
        }
    }

    private static string ExtractMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            var parsed = JObject.Parse(raw);

            var message = parsed["error"]?["message"]?.ToString()
                ?? parsed["message"]?.ToString()
                ?? parsed["Message"]?.ToString();

            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonReaderException)
        {
            // Not JSON; the raw text below is the best available detail.
        }

        return raw.Length > 400 ? raw.Substring(0, 400) : raw;
    }

    private HttpResponseMessage CreateToolResult(JToken requestId, JObject payload, bool isError)
    {
        var result = new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            },
            ["isError"] = isError
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (data != null)
        {
            error["data"] = data;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!APP_INSIGHTS_ENABLED
            || string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
        {
            return;
        }

        try
        {
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
                        properties = properties ?? new Dictionary<string, string>()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Telemetry must never fail a tool call.
        }
    }

    /// <summary>An error the agent can act on, carried back as a tool result rather than a protocol error.</summary>
    private sealed class AskException : Exception
    {
        public AskException(string code, string message, JArray validValues = null) : base(message)
        {
            this.Code = code;
            this.ValidValues = validValues;
        }

        public string Code { get; private set; }

        public JArray ValidValues { get; private set; }

        public JObject ToJson()
        {
            var payload = new JObject
            {
                ["error"] = this.Code,
                ["message"] = this.Message
            };

            if (this.ValidValues != null)
            {
                payload["validValues"] = this.ValidValues;
            }

            return payload;
        }
    }
}
