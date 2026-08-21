using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  ORACLE FUSION CLOUD FINANCIALS — MCP CONNECTOR                              ║
// ║                                                                              ║
// ║  Dual-mode connector: exposes generic CRUD, resource actions, and ERP        ║
// ║  integration jobs across the whole Oracle Fusion Financials ADF REST surface ║
// ║  as MCP tools (for Copilot Studio) while the Swagger keeps typed operations  ║
// ║  (for Power Automate IntelliSense).                                          ║
// ║                                                                              ║
// ║  Every operation in the swagger is routed here, so the script can safely be   ║
// ║  enabled for all of them: the MCP endpoint and the designer resolvers are     ║
// ║  handled in-process, and the typed REST operations are forwarded to the pod.  ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Deployment Configuration ─────────────────────────────────────────
    //
    //   Replace POD_HOST with your Oracle Fusion pod host (no scheme, no path).
    //   It MUST match the `host` field in apiDefinition.swagger.json.
    //   Example: "myserver.fa.us2.oraclecloud.com"
    //
    private const string POD_HOST = "replace-pod-host.oraclecloud.com";

    /// <summary>REST resource collection base. Financials ships version 11.13.18.05.</summary>
    private const string API_BASE = "/fscmRestApi/resources/11.13.18.05";

    /// <summary>Content type Oracle requires when invoking a custom resource action.</summary>
    private const string ACTION_CONTENT_TYPE = "application/vnd.oracle.adf.action+json";

    /// <summary>App Insights connection string (leave empty to disable telemetry).</summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "oracle-fusion-financials",
            Version = "1.0.0",
            Title = "Oracle Fusion Cloud Financials",
            Description = "Generic CRUD, resource actions, and ERP integration jobs over the Oracle Fusion Cloud Financials REST API (ADF REST)."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities { Tools = true },
        Instructions =
            "Tools operate against Oracle Fusion Cloud Financials REST resources across General Ledger, " +
            "Payables, Receivables, Cash Management, Expenses, Tax, and Budgetary Control (e.g. invoices, " +
            "payablesPayments, receivablesInvoices, journalBatches, ledgerBalances, expenses, cashBankAccounts). " +
            "Use list_common_resources to see frequently used resource names, describe_resource to inspect a " +
            "resource's fields, then query_records / get_record / get_child_records / create_record / " +
            "update_record / delete_record. Business operations such as validating or cancelling an invoice are " +
            "resource actions: call list_resource_actions then invoke_resource_action. Long-running processes " +
            "(imports, posting, payment runs) are Enterprise Scheduler jobs: call submit_ess_job then poll " +
            "get_ess_job_status. Filters use the ADF 'q' RSQL syntax, e.g. \"InvoiceNumber='INV-1001'\" or " +
            "\"Supplier LIKE 'Zava%'\". Amounts and dates are returned in the ledger's own currency and calendar."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            // Designer resolvers — dynamic pickers and body schemas.
            case "ListResources": return await HandleListResourcesAsync().ConfigureAwait(false);
            case "ListResourceFields": return await HandleListResourceFieldsAsync().ConfigureAwait(false);
            case "ListChildResources": return await HandleListChildResourcesAsync().ConfigureAwait(false);
            case "ListResourceActions": return await HandleListResourceActionsAsync().ConfigureAwait(false);
            case "GetResourceSchema": return await HandleGetResourceSchemaAsync().ConfigureAwait(false);
            case "GetActionSchema": return await HandleGetActionSchemaAsync().ConfigureAwait(false);

            // MCP endpoint for Copilot Studio.
            case "InvokeMCP": return await HandleMcpAsync().ConfigureAwait(false);

            // Typed REST operations — forwarded to the Oracle pod.
            case "QueryRecords":
            case "GetRecord":
            case "CreateRecord":
            case "UpdateRecord":
            case "DeleteRecord":
            case "GetChildRecords":
            case "DescribeResource":
            case "InvokeResourceAction":
            case "SearchInvoices":
            case "SubmitErpIntegration":
            case "GetErpJobStatus":
                return await ForwardRequestAsync().ConfigureAwait(false);

            // Unknown operation ids are forwarded rather than treated as MCP traffic,
            // so adding a pass-through operation to the swagger needs no script change.
            default:
                return await ForwardRequestAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Forwards a typed operation to the Oracle pod unchanged. The platform has already resolved
    /// the request URI from the swagger, so only Oracle-specific media types need adjusting.
    /// </summary>
    private async Task<HttpResponseMessage> ForwardRequestAsync()
    {
        var request = this.Context.Request;

        // Oracle rejects resource actions unless they carry the ADF action media type.
        if (string.Equals(this.Context.OperationId, "InvokeResourceAction", StringComparison.OrdinalIgnoreCase)
            && request.Content != null)
        {
            var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            request.Content = new StringContent(body ?? "{}", Encoding.UTF8, ACTION_CONTENT_TYPE);
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var correlationId = Guid.NewGuid().ToString();

        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Dynamic Resolvers (designer dropdowns and schemas) ───────────────

    /// <summary>Populates the resource dropdown from the live catalog, falling back to a curated list.</summary>
    private async Task<HttpResponseMessage> HandleListResourcesAsync()
    {
        var values = new JArray();
        try
        {
            var catalog = await SendOracleAsync(HttpMethod.Get, API_BASE).ConfigureAwait(false);
            if (catalog["links"] is JArray links)
            {
                foreach (var link in links)
                {
                    if ((link.Value<string>("rel") ?? "") != "child") continue;
                    var name = link.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name))
                        values.Add(new JObject { ["name"] = name, ["title"] = name });
                }
            }
        }
        catch { /* fall back to curated list below */ }

        if (values.Count == 0 && CommonResources()["resources"] is JArray curated)
        {
            foreach (var r in curated)
            {
                var name = r.Value<string>("resource");
                var module = r.Value<string>("module");
                values.Add(new JObject
                {
                    ["name"] = name,
                    ["title"] = string.IsNullOrWhiteSpace(module) ? name : $"{name} ({module})"
                });
            }
        }

        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Populates a field picker (attributes) for the selected resource.</summary>
    private async Task<HttpResponseMessage> HandleListResourceFieldsAsync()
    {
        var values = new JArray();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            foreach (var attr in await GetAttributesAsync(resource).ConfigureAwait(false))
            {
                var name = attr.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                values.Add(new JObject { ["name"] = name, ["title"] = attr.Value<string>("title") ?? name });
            }
        }
        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Populates a child-resource picker (for expand and child reads) for the selected resource.</summary>
    private async Task<HttpResponseMessage> HandleListChildResourcesAsync()
    {
        var values = new JArray();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            var node = await GetResourceNodeAsync(resource).ConfigureAwait(false);
            if (node?["childResources"] is JObject children)
            {
                foreach (var p in children.Properties())
                    values.Add(new JObject { ["name"] = p.Name, ["title"] = p.Name });
            }
        }
        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Populates the action picker for the selected resource.</summary>
    private async Task<HttpResponseMessage> HandleListResourceActionsAsync()
    {
        var values = new JArray();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            foreach (var action in await GetActionsAsync(resource).ConfigureAwait(false))
            {
                var name = action.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                values.Add(new JObject { ["name"] = name, ["title"] = action.Value<string>("title") ?? name });
            }
        }
        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Builds a JSON Schema for the selected resource's writable attributes (dynamic body).</summary>
    private async Task<HttpResponseMessage> HandleGetResourceSchemaAsync()
    {
        var properties = new JObject();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            foreach (var attr in await GetAttributesAsync(resource).ConfigureAwait(false))
            {
                var name = attr.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = new JObject
                {
                    ["type"] = MapAdfType(attr.Value<string>("type")),
                    ["title"] = attr.Value<string>("title") ?? name
                };
                var description = attr.Value<string>("description");
                if (!string.IsNullOrWhiteSpace(description)) prop["description"] = description;
                properties[name] = prop;
            }
        }

        var schema = new JObject { ["type"] = "object", ["properties"] = properties };
        return JsonResponse(new JObject { ["schema"] = schema });
    }

    /// <summary>Builds a JSON Schema for the selected action's parameters (dynamic action body).</summary>
    private async Task<HttpResponseMessage> HandleGetActionSchemaAsync()
    {
        var properties = new JObject();
        var required = new JArray();

        var resource = GetQueryParam("resource");
        var actionName = GetQueryParam("action");

        if (!string.IsNullOrWhiteSpace(resource) && !string.IsNullOrWhiteSpace(actionName))
        {
            var action = (await GetActionsAsync(resource).ConfigureAwait(false))
                .FirstOrDefault(a => string.Equals(a.Value<string>("name"), actionName, StringComparison.OrdinalIgnoreCase));

            if (action?["parameters"] is JArray parameters)
            {
                foreach (var parameter in parameters.OfType<JObject>())
                {
                    var name = parameter.Value<string>("name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    properties[name] = new JObject
                    {
                        ["type"] = MapAdfType(parameter.Value<string>("type")),
                        ["title"] = parameter.Value<string>("title") ?? name
                    };
                    if (parameter.Value<bool?>("mandatory") == true) required.Add(name);
                }
            }
        }

        var schema = new JObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) schema["required"] = required;
        return JsonResponse(new JObject { ["schema"] = schema });
    }

    /// <summary>Returns the attributes array from a resource's describe document.</summary>
    private async Task<JArray> GetAttributesAsync(string resource)
    {
        var node = await GetResourceNodeAsync(resource).ConfigureAwait(false);
        return node?["attributes"] as JArray ?? new JArray();
    }

    /// <summary>
    /// Returns the custom actions published by a resource. Oracle's describe document represents actions
    /// either as an array or as a map keyed by action name, so both shapes are normalized here. When the
    /// describe call is unavailable, the documented action list for the resource is used instead.
    /// </summary>
    private async Task<JArray> GetActionsAsync(string resource)
    {
        var normalized = new JArray();
        try
        {
            var node = await GetResourceNodeAsync(resource).ConfigureAwait(false);
            var actions = node?["actions"];

            if (actions is JArray actionArray)
            {
                foreach (var action in actionArray.OfType<JObject>())
                    if (!string.IsNullOrWhiteSpace(action.Value<string>("name"))) normalized.Add(action);
            }
            else if (actions is JObject actionMap)
            {
                foreach (var p in actionMap.Properties())
                {
                    var action = p.Value as JObject ?? new JObject();
                    action["name"] = p.Name;
                    normalized.Add(action);
                }
            }
        }
        catch { /* fall back to the documented list below */ }

        if (normalized.Count == 0)
        {
            foreach (var name in KnownActions(TrimResource(resource)))
                normalized.Add(new JObject { ["name"] = name, ["title"] = name });
        }

        return normalized;
    }

    /// <summary>Returns the metadata node for a resource from its describe document.</summary>
    private async Task<JObject> GetResourceNodeAsync(string resource)
    {
        var describe = await SendOracleAsync(HttpMethod.Get, $"{API_BASE}/{TrimResource(resource)}/describe").ConfigureAwait(false);
        if (!(describe["Resources"] is JObject resources)) return null;
        return (resources[TrimResource(resource)] ?? resources.Properties().FirstOrDefault()?.Value) as JObject;
    }

    private static string MapAdfType(string adfType)
    {
        switch ((adfType ?? "").ToLowerInvariant())
        {
            case "integer":
            case "long":
            case "int": return "integer";
            case "number":
            case "decimal":
            case "double":
            case "float": return "number";
            case "boolean": return "boolean";
            default: return "string";
        }
    }

    private string GetQueryParam(string name)
    {
        var query = this.Context.Request.RequestUri?.Query ?? "";
        if (query.StartsWith("?")) query = query.Substring(1);
        foreach (var pair in query.Split('&'))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(pair.Substring(0, idx));
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair.Substring(idx + 1));
        }
        return null;
    }

    private static HttpResponseMessage JsonResponse(JToken token) => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(token.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
    };

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        handler.AddTool("list_common_resources",
            "Lists frequently used Oracle Fusion Financials REST resource names, grouped by module (General Ledger, " +
            "Payables, Receivables, Cash Management, Expenses, Tax, Budgetary Control, Intercompany, Integration). " +
            "Use the returned names as the 'resource' argument for the other tools.",
            schema: s => s.String("module", "Optional module filter, e.g. 'Payables' or 'General Ledger'."),
            handler: async (args, ct) =>
            {
                var module = GetArgument(args, "module");
                if (string.IsNullOrWhiteSpace(module)) return CommonResources();

                var all = CommonResources();
                var filtered = new JArray(((JArray)all["resources"])
                    .Where(r => (r.Value<string>("module") ?? "").IndexOf(module, StringComparison.OrdinalIgnoreCase) >= 0));
                return new JObject { ["note"] = all["note"], ["resources"] = filtered };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("describe_resource",
            "Returns the metadata (fields, actions, child resources) for a single Financials REST resource. " +
            "Call this before create/update to learn the required attributes.",
            schema: s => s.String("resource", "Resource collection name, e.g. 'invoices' or 'journalBatches'.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                return await SendOracleAsync(HttpMethod.Get, $"{API_BASE}/{TrimResource(resource)}/describe").ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("query_records",
            "Queries records from a Financials REST resource. Supports the ADF query grammar: 'q' (RSQL filter), " +
            "'finder' (named finder), 'fields' (comma-separated attributes), 'expand' (child resources), " +
            "'orderBy', 'limit', and 'offset'.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'invoices'.", required: true)
                .String("q", "RSQL filter, e.g. \"InvoiceNumber='INV-1001'\" or \"Supplier LIKE 'Zava%'\".")
                .String("finder", "Named finder with parameters, e.g. \"PrimaryKey;InvoiceId=300100012345678\".")
                .String("fields", "Comma-separated attributes to return, e.g. 'InvoiceId,InvoiceNumber,InvoiceAmount'.")
                .String("expand", "Child resources to expand, e.g. 'invoiceLines,invoiceInstallments'.")
                .String("orderBy", "Sort expression, e.g. 'InvoiceDate:desc'.")
                .Integer("limit", "Maximum number of records to return (default 25).", defaultValue: 25)
                .Integer("offset", "Zero-based index of the first record to return, for paging.")
                .Boolean("totalResults", "When true, includes the total count of matching records."),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var query = new Dictionary<string, string>
                {
                    ["onlyData"] = "true",
                    ["limit"] = (args.Value<int?>("limit") ?? 25).ToString()
                };
                AddIfPresent(query, "q", GetArgument(args, "q"));
                AddIfPresent(query, "finder", GetArgument(args, "finder"));
                AddIfPresent(query, "fields", GetArgument(args, "fields"));
                AddIfPresent(query, "expand", GetArgument(args, "expand"));
                AddIfPresent(query, "orderBy", GetArgument(args, "orderBy"));
                if (args.Value<int?>("offset").HasValue) query["offset"] = args.Value<int>("offset").ToString();
                if (args.Value<bool?>("totalResults") == true) query["totalResults"] = "true";

                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/{TrimResource(resource)}", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_record",
            "Retrieves a single Financials record by its resource key (the primary key value the API returns in 'links'/'self').",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'invoices'.", required: true)
                .String("id", "The record key, e.g. an InvoiceId or the composite key from the resource's self link.", required: true)
                .String("fields", "Comma-separated attributes to return.")
                .String("expand", "Child resources to expand."),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                var query = new Dictionary<string, string> { ["onlyData"] = "true" };
                AddIfPresent(query, "fields", GetArgument(args, "fields"));
                AddIfPresent(query, "expand", GetArgument(args, "expand"));
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_child_records",
            "Retrieves the child records of a parent record, e.g. the lines, distributions, or installments of an " +
            "invoice. Use describe_resource to discover the available child resource names.",
            schema: s => s
                .String("resource", "Parent resource collection name, e.g. 'invoices'.", required: true)
                .String("id", "The parent record key.", required: true)
                .String("child", "Child resource name, e.g. 'invoiceLines'.", required: true)
                .String("q", "RSQL filter applied to the child records.")
                .String("fields", "Comma-separated child attributes to return.")
                .Integer("limit", "Maximum number of child records to return (default 25).", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                var child = RequireArgument(args, "child");
                var query = new Dictionary<string, string>
                {
                    ["onlyData"] = "true",
                    ["limit"] = (args.Value<int?>("limit") ?? 25).ToString()
                };
                AddIfPresent(query, "q", GetArgument(args, "q"));
                AddIfPresent(query, "fields", GetArgument(args, "fields"));

                var path = $"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}/child/{TrimResource(child)}";
                return await SendOracleAsync(HttpMethod.Get, BuildPath(path, query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_record",
            "Creates a new record in a Financials REST resource. Provide the attributes as a JSON object string.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'invoices'.", required: true)
                .String("record", "The record attributes as a JSON object string, e.g. '{\"InvoiceNumber\":\"INV-1001\"}'.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var record = ParseRecord(RequireArgument(args, "record"));
                return await SendOracleAsync(HttpMethod.Post, $"{API_BASE}/{TrimResource(resource)}", record).ConfigureAwait(false);
            },
            annotations: a => { a["idempotentHint"] = false; });

        handler.AddTool("update_record",
            "Partially updates (PATCH) an existing Financials record. Provide only the attributes to change as a " +
            "JSON object string. Oracle does not cascade defaults, so review dependent attributes such as terms " +
            "date or due date after changing a key attribute.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'invoices'.", required: true)
                .String("id", "The record key.", required: true)
                .String("record", "The attributes to change as a JSON object string.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                var record = ParseRecord(RequireArgument(args, "record"));
                return await SendOracleAsync(new HttpMethod("PATCH"), $"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}", record).ConfigureAwait(false);
            });

        handler.AddTool("delete_record",
            "Deletes a Financials record by its resource key. Not all resources support delete; accounted or paid " +
            "documents generally must be cancelled with a resource action instead.",
            schema: s => s
                .String("resource", "Resource collection name.", required: true)
                .String("id", "The record key.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                return await SendOracleAsync(HttpMethod.Delete, $"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}").ConfigureAwait(false);
            },
            annotations: a => { a["destructiveHint"] = true; });

        handler.AddTool("list_resource_actions",
            "Lists the custom business actions a Financials resource publishes, e.g. validateInvoice, cancelInvoice, " +
            "calculateTax, or applyPrepayments on 'invoices'. Call this before invoke_resource_action.",
            schema: s => s.String("resource", "Resource collection name, e.g. 'invoices'.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                return new JObject
                {
                    ["resource"] = TrimResource(resource),
                    ["actions"] = await GetActionsAsync(resource).ConfigureAwait(false)
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("invoke_resource_action",
            "Invokes a custom business action on a Financials resource, e.g. validateInvoice or cancelInvoice on " +
            "'invoices'. Actions change financial documents, so confirm the target record before calling. Provide " +
            "the action parameters as a JSON object string.",
            schema: s => s
                .String("resource", "Resource collection name that publishes the action, e.g. 'invoices'.", required: true)
                .String("action", "The action name, e.g. 'validateInvoice'.", required: true)
                .String("parameters", "The action parameters as a JSON object string, e.g. '{\"invoiceId\":300100012345678}'. Use '{}' when the action takes none."),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var action = RequireArgument(args, "action");
                var payload = ParseRecord(GetArgument(args, "parameters", "{}"), "parameters");
                var path = $"{API_BASE}/{TrimResource(resource)}/action/{Uri.EscapeDataString(TrimResource(action))}";
                return await SendOracleAsync(HttpMethod.Post, path, payload, ACTION_CONTENT_TYPE).ConfigureAwait(false);
            },
            annotations: a => { a["idempotentHint"] = false; });

        handler.AddTool("search_invoices",
            "Convenience search over payables supplier invoices. Pass 'invoiceNumber', 'supplier', or " +
            "'businessUnit' for a simple lookup, or a raw 'q' RSQL filter for precise queries.",
            schema: s => s
                .String("invoiceNumber", "Exact supplier invoice number to look up.")
                .String("supplier", "Partial supplier name to search for (matched with LIKE).")
                .String("businessUnit", "Exact business unit name to restrict the search to.")
                .String("q", "Raw RSQL filter to use instead of the simple arguments.")
                .String("fields", "Comma-separated attributes to return.")
                .String("expand", "Child resources to expand, e.g. 'invoiceLines,invoiceInstallments'.")
                .String("orderBy", "Sort expression, e.g. 'InvoiceDate:desc'.")
                .Integer("limit", "Maximum number of invoices to return (default 25).", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var q = GetArgument(args, "q");
                if (string.IsNullOrWhiteSpace(q))
                {
                    var clauses = new List<string>();
                    var invoiceNumber = GetArgument(args, "invoiceNumber");
                    var supplier = GetArgument(args, "supplier");
                    var businessUnit = GetArgument(args, "businessUnit");

                    if (!string.IsNullOrWhiteSpace(invoiceNumber)) clauses.Add($"InvoiceNumber='{Escape(invoiceNumber)}'");
                    if (!string.IsNullOrWhiteSpace(supplier)) clauses.Add($"Supplier LIKE '%{Escape(supplier)}%'");
                    if (!string.IsNullOrWhiteSpace(businessUnit)) clauses.Add($"BusinessUnit='{Escape(businessUnit)}'");

                    if (clauses.Count > 0) q = string.Join(" and ", clauses);
                }

                var query = new Dictionary<string, string>
                {
                    ["onlyData"] = "true",
                    ["limit"] = (args.Value<int?>("limit") ?? 25).ToString()
                };
                AddIfPresent(query, "q", q);
                AddIfPresent(query, "fields", GetArgument(args, "fields"));
                AddIfPresent(query, "expand", GetArgument(args, "expand"));
                AddIfPresent(query, "orderBy", GetArgument(args, "orderBy"));
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/invoices", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("submit_ess_job",
            "Submits an Oracle Enterprise Scheduler job through the erpintegrations resource, e.g. Import Payables " +
            "Invoices or Post Journals. Returns the request identifier to poll with get_ess_job_status. Parameters " +
            "must be supplied as a comma-separated list in the exact order the job definition expects.",
            schema: s => s
                .String("jobPackageName", "The ESS job package, e.g. '/oracle/apps/ess/financials/payables/invoices/transactions'.", required: true)
                .String("jobDefName", "The ESS job definition, e.g. 'APXIIMPT' for Import Payables Invoices.", required: true)
                .String("essParameters", "Comma-separated job parameters, in the order the job definition expects them.")
                .String("callbackURL", "Optional callback URL to notify when the job completes.")
                .String("notificationCode", "Optional notification preference code for job completion."),
            handler: async (args, ct) =>
            {
                var payload = new JObject
                {
                    ["OperationName"] = "submitESSJobRequest",
                    ["JobPackageName"] = RequireArgument(args, "jobPackageName"),
                    ["JobDefName"] = RequireArgument(args, "jobDefName")
                };
                var essParameters = GetArgument(args, "essParameters");
                if (!string.IsNullOrWhiteSpace(essParameters)) payload["ESSParameters"] = essParameters;
                var callbackUrl = GetArgument(args, "callbackURL");
                if (!string.IsNullOrWhiteSpace(callbackUrl)) payload["CallbackURL"] = callbackUrl;
                var notificationCode = GetArgument(args, "notificationCode");
                if (!string.IsNullOrWhiteSpace(notificationCode)) payload["NotificationCode"] = notificationCode;

                return await SendOracleAsync(HttpMethod.Post, $"{API_BASE}/erpintegrations", payload).ConfigureAwait(false);
            },
            annotations: a => { a["idempotentHint"] = false; });

        handler.AddTool("get_ess_job_status",
            "Returns the status of a submitted Enterprise Scheduler job. Status values include WAIT, READY, RUNNING, " +
            "SUCCEEDED, WARNING, and ERROR.",
            schema: s => s.String("requestId", "The ESS request identifier returned by submit_ess_job.", required: true),
            handler: async (args, ct) =>
            {
                var requestId = RequireArgument(args, "requestId");
                var query = new Dictionary<string, string>
                {
                    ["finder"] = $"ESSJobStatusRF;requestId={requestId}",
                    ["onlyData"] = "true"
                };
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/erpintegrations", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── Oracle Request Helpers ───────────────────────────────────────────

    /// <summary>Send a request to the Oracle Fusion pod, forwarding the connector's OAuth token.</summary>
    private async Task<JObject> SendOracleAsync(HttpMethod method, string pathAndQuery, JObject body = null, string contentType = "application/json")
    {
        var url = $"https://{POD_HOST}{pathAndQuery}";
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, contentType);

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Oracle Financials request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["result"] = content }; }
    }

    /// <summary>Append a query string to a path, URL-encoding each value.</summary>
    private static string BuildPath(string path, IDictionary<string, string> query)
    {
        if (query == null || query.Count == 0) return path;
        var parts = query.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                         .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        return $"{path}?{string.Join("&", parts)}";
    }

    private static void AddIfPresent(IDictionary<string, string> dict, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) dict[key] = value;
    }

    /// <summary>Strip leading/trailing slashes so callers can pass 'invoices' or '/invoices'.</summary>
    private static string TrimResource(string resource) => resource.Trim().Trim('/');

    /// <summary>Escape single quotes so free-text input is safe inside an RSQL string literal.</summary>
    private static string Escape(string value) => value.Replace("'", "''");

    private static JObject ParseRecord(string json, string argumentName = "record")
    {
        try { return JObject.Parse(json); }
        catch (Exception ex) { throw new ArgumentException($"'{argumentName}' must be a JSON object: {ex.Message}"); }
    }

    /// <summary>
    /// Custom actions published by Financials resources, used when a live describe call is unavailable.
    /// Sourced from the Oracle Fusion Cloud Financials REST API reference (release 11.13.18.05).
    /// </summary>
    private static string[] KnownActions(string resource)
    {
        switch (resource)
        {
            case "invoices":
                return new[] { "applyPrepayments", "calculateTax", "cancelInvoice", "cancelLine",
                               "generateDistributions", "reverseDistributions", "unapplyPrepayments", "validateInvoice" };
            case "payablesPayments":
                return new[] { "initiateEscheat", "initiateStopPayment" };
            case "payablesInterfaceInvoices":
                return new[] { "submitImportInvoices" };
            case "expenses":
                return new[] { "notificationRequest", "sendEmailNotification" };
            case "intercompanyAgreements":
                return new[] { "approveTransferAuthorizations", "cancelTransferAuthorizationGrps", "generateAccountingLines",
                               "generateTransactions", "rejectTransferAuthorizations", "reverseTAGrpWithStandardCM",
                               "reverseTransferAuthorizationGrp" };
            case "intercompanyTransactionSourceDocuments":
                return new[] { "createTransferAuthorization" };
            case "buyerInitiatedEarlyPaymentOffers":
                return new[] { "getCampaignDetailsUsingVendorId", "getSupplierContacts", "sendEarlyPaymentOffers",
                               "updateSupplierAssignments" };
            case "workflowNotificationContents":
                return new[] { "addApprovers", "getApprovalActivities", "getApprovalSequence", "retrieveTaskId" };
            default:
                return new string[0];
        }
    }

    private static JObject CommonResources()
    {
        var list = new JArray
        {
            // General Ledger
            R("journalBatches", "General Ledger", "Journal batches, journals, and journal lines."),
            R("ledgerBalances", "General Ledger", "General ledger account balances by period."),
            R("ledgersLOV", "General Ledger", "Ledgers available to the signed-in user."),
            R("chartOfAccountsLOV", "General Ledger", "Chart of accounts structures."),
            R("accountCombinationsLOV", "General Ledger", "Valid account code combinations."),
            R("accountingPeriodsLOV", "General Ledger", "Accounting periods and their open or closed status."),
            R("journalSourcesLOV", "General Ledger", "Journal sources used to classify journals."),
            R("journalCategoriesLOV", "General Ledger", "Journal categories used to classify journals."),
            R("currencyRates", "General Ledger", "Daily currency conversion rates."),

            // Payables
            R("invoices", "Payables", "Supplier invoices with lines, distributions, installments, and attachments."),
            R("invoiceHolds", "Payables", "Holds placed on supplier invoices."),
            R("payablesPayments", "Payables", "Payments issued to suppliers."),
            R("paymentProcessRequests", "Payables", "Payment process requests (payment runs)."),
            R("payablesInterfaceInvoices", "Payables", "Staged invoices awaiting the Import Payables Invoices process."),
            R("payablesPaymentTerms", "Payables", "Payment terms definitions."),
            R("payablesOptions", "Payables", "Payables configuration options by business unit."),
            R("payablesDistributionSets", "Payables", "Distribution sets used to default invoice accounting."),
            R("interimDocumentsPayables", "Payables", "Interim payables documents awaiting processing."),

            // Receivables
            R("receivablesInvoices", "Receivables", "Customer invoices (transactions)."),
            R("receivablesCreditMemos", "Receivables", "Customer credit memos."),
            R("receivablesAdjustments", "Receivables", "Adjustments applied to receivables transactions."),
            R("standardReceipts", "Receivables", "Customer receipts and their applications."),
            R("receivablesDisputes", "Receivables", "Disputes raised against receivables transactions."),
            R("receivablesCustomerAccountActivities", "Receivables", "Customer account activity and balances."),
            R("autoInvoiceInterfaceLines", "Receivables", "Interface lines awaiting the AutoInvoice process."),
            R("collectionPromises", "Receivables", "Promises to pay recorded during collections."),
            R("collectionsDelinquencies", "Receivables", "Delinquent customer transactions."),

            // Cash Management
            R("cashBankAccounts", "Cash Management", "Internal bank accounts."),
            R("cashBanks", "Cash Management", "Banks."),
            R("cashBankBranches", "Cash Management", "Bank branches."),
            R("cashExternalTransactions", "Cash Management", "External cash transactions for reconciliation."),
            R("cashBankAccountTransfers", "Cash Management", "Transfers between internal bank accounts."),
            R("externalBankAccounts", "Cash Management", "Supplier and customer (external) bank accounts."),

            // Expenses
            R("expenses", "Expenses", "Individual expense items."),
            R("expenseReports", "Expenses", "Expense reports submitted for reimbursement."),
            R("expenseCashAdvances", "Expenses", "Cash advances issued to employees."),
            R("expenseCreditCardTransactions", "Expenses", "Corporate card transactions available to expense."),
            R("expenseTypes", "Expenses", "Configured expense types."),
            R("expenseTemplates", "Expenses", "Expense templates by business unit."),

            // Tax
            R("transactionTaxLines", "Tax", "Tax lines calculated on transactions."),
            R("withholdingTaxLines", "Tax", "Withholding tax lines on payables documents."),
            R("taxRatesLOV", "Tax", "Tax rates available for selection."),
            R("taxRegimesLOV", "Tax", "Tax regimes available for selection."),
            R("taxExemptions", "Tax", "Customer and product tax exemptions."),
            R("taxRegistrations", "Tax", "Party tax registrations."),

            // Budgetary Control
            R("budgetaryControlBudgetTransactions", "Budgetary Control", "Budget transactions entered into control budgets."),
            R("budgetaryControlResults", "Budgetary Control", "Funds check and funds reservation results."),
            R("controlBudgetsLOV", "Budgetary Control", "Control budgets available for selection."),

            // Intercompany and revenue
            R("intercompanyAgreements", "Intercompany", "Intercompany agreements and transfer authorizations."),
            R("intercompanyTransactionSourceDocuments", "Intercompany", "Source documents for intercompany transactions."),
            R("nettingAgreements", "Intercompany", "Payables and receivables netting agreements."),
            R("revenueContracts", "Revenue Management", "Revenue contracts and performance obligations."),

            // Integration
            R("erpintegrations", "Integration", "Submit Enterprise Scheduler jobs and import or export bulk data."),
            R("erpProcesses", "Integration", "Status of ERP integration processes."),
            R("erpBusinessEvents", "Integration", "ERP business events available for subscription.")
        };

        return new JObject
        {
            ["note"] = "This is a curated subset of roughly 300 Financials resources. The full resource catalog is " +
                       "available at /fscmRestApi/resources/11.13.18.05/. Use describe_resource for a resource's fields.",
            ["resources"] = list
        };
    }

    private static JObject R(string name, string module, string description) =>
        new JObject { ["resource"] = name, ["module"] = module, ["description"] = description };

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
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

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Suppress telemetry errors */ }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  MCP FRAMEWORK (spec 2025-11-25) — do not modify unless extending the framework.║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "array", ["description"] = description, ["items"] = itemSchema };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null)
        {
            annotationsObject = new JObject();
            annotations(annotationsObject);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
                if (jobj["structuredContent"] is JObject structured)
                    callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, ErrorResult($"Invalid arguments: {ex.Message}"));
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, ErrorResult($"Tool error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, ErrorResult($"Tool execution failed: {ex.Message}"));
        }
    }

    private static JObject ErrorResult(string message) => new JObject
    {
        ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = message } },
        ["isError"] = true
    };

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    private string SerializeSuccess(JToken id, JObject result) => new JObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    }.ToString(Newtonsoft.Json.Formatting.None);

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null) =>
        SerializeError(id, (int)code, message, data);

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}
