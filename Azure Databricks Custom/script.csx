using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Azure Databricks Custom.
///
/// The workspace host comes from the Set Host URL policy in apiProperties.json (an environment variable),
/// so the connection needs no Server Hostname. The SQL warehouse comes from a second policy that sends the
/// environment's HTTP path in the x-databricks-http-path header, or is chosen here when none is configured,
/// so the connection needs no HTTP Path either. Two operations run through this script:
///   InvokeMCP         - MCP endpoint for Copilot Studio (JSON-RPC 2.0)
///   ExecuteStatement  - fills in warehouse_id when it is missing, then forwards the request
/// Every other operation is forwarded by the platform without this script.
/// </summary>
public class Script : ScriptBase
{
    // ── Configuration ────────────────────────────────────────────────────

    private const string CONNECTOR_NAME = "Azure Databricks Custom";
    private const string SERVER_VERSION = "1.0.0";

    // Header set by the policy in apiProperties.json from the environment variable. It holds a SQL warehouse
    // HTTP path (/sql/1.0/warehouses/<id>), a bare warehouse ID, or "auto".
    private const string HTTP_PATH_HEADER = "x-databricks-http-path";

    // Optional fallback used when the header is missing or "auto". Leave empty to pick a warehouse
    // automatically (running first, then starting, then serverless).
    private const string DEFAULT_WAREHOUSE_ID = "";

    private const int MCP_DEFAULT_ROW_LIMIT = 100;
    private const int MCP_MAX_ROW_LIMIT = 1000;
    private const int MAX_TEXT_LENGTH = 300;
    private const int MAX_OUTPUT_LENGTH = 4000;
    private const int POLL_BUDGET_SECONDS = 80;

    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ── Entry point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMcpAsync().ConfigureAwait(false);
                case "ExecuteStatement":
                    return await HandleExecuteStatementAsync().ConfigureAwait(false);
                default:
                    return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (DatabricksException ex)
        {
            await LogExceptionAsync(ex, this.Context.OperationId).ConfigureAwait(false);
            return CreateErrorResponse((HttpStatusCode)ex.StatusCode, "DatabricksError", ex.Message);
        }
        catch (Exception ex)
        {
            await LogExceptionAsync(ex, this.Context.OperationId).ConfigureAwait(false);
            return CreateErrorResponse(HttpStatusCode.InternalServerError, "InternalError", ex.Message);
        }
    }

    // ── ExecuteStatement: default the SQL warehouse ──────────────────────

    private async Task<HttpResponseMessage> HandleExecuteStatementAsync()
    {
        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);

        if (string.IsNullOrWhiteSpace((string)body["warehouse_id"]))
        {
            body["warehouse_id"] = await ResolveWarehouseIdAsync().ConfigureAwait(false);
        }

        this.Context.Request.Content = new StringContent(
            body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        this.Context.Request.Headers.Remove(HTTP_PATH_HEADER);

        await LogEventAsync("ExecuteStatement", null).ConfigureAwait(false);
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the warehouse configured for this environment, or the best available warehouse for the signed-in user.
    /// </summary>
    private async Task<string> ResolveWarehouseIdAsync()
    {
        var configured = ParseWarehouseId(ReadHeader(HTTP_PATH_HEADER)) ?? ParseWarehouseId(DEFAULT_WAREHOUSE_ID);
        if (configured != null)
        {
            return configured;
        }

        var response = await CallAsync(HttpMethod.Get, "/api/2.0/sql/warehouses", null).ConfigureAwait(false);
        var warehouses = (response["warehouses"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

        if (warehouses.Count == 0)
        {
            throw new DatabricksException(404,
                "No SQL warehouse is available to you in this workspace. Ask a workspace admin for CAN USE access to a SQL warehouse, or specify a warehouse ID.");
        }

        Func<JObject, string, bool> inState = (w, state) =>
            string.Equals((string)w["state"], state, StringComparison.OrdinalIgnoreCase);

        var pick = warehouses.FirstOrDefault(w => inState(w, "RUNNING"))
            ?? warehouses.FirstOrDefault(w => inState(w, "STARTING"))
            ?? warehouses.FirstOrDefault(w => (bool?)w["enable_serverless_compute"] == true)
            ?? warehouses[0];

        return (string)pick["id"];
    }

    private string ReadHeader(string name)
    {
        IEnumerable<string> values;
        return this.Context.Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null;
    }

    /// <summary>
    /// Accepts a SQL warehouse HTTP path such as /sql/1.0/warehouses/abc123, or a bare warehouse ID.
    /// Returns null for an empty value or "auto", which means choose a warehouse automatically.
    /// </summary>
    private static string ParseWarehouseId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = value.Trim().TrimEnd('/');
        var slash = id.LastIndexOf('/');
        if (slash >= 0)
        {
            id = id.Substring(slash + 1);
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9]+$"))
        {
            throw new DatabricksException(400,
                "The configured HTTP path is not valid. Use a SQL warehouse HTTP path such as /sql/1.0/warehouses/<id>, a warehouse ID, or auto.");
        }
        return id;
    }

    // ── MCP ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return JsonRpcError(null, -32700, "Parse error", null);
        }

        var method = (string)request["method"];
        var id = request["id"];
        var parameters = request["params"] as JObject ?? new JObject();

        await LogEventAsync("McpRequest", new Dictionary<string, string> { { "method", method ?? "" } }).ConfigureAwait(false);

        switch (method)
        {
            case "initialize":
                return JsonRpcResult(id, new JObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JObject { ["name"] = "azure-databricks-custom", ["version"] = SERVER_VERSION }
                });

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
            case "ping":
                return JsonRpcResult(id, new JObject());

            case "tools/list":
                return JsonRpcResult(id, new JObject { ["tools"] = BuildToolList() });

            case "resources/list":
                return JsonRpcResult(id, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return JsonRpcResult(id, new JObject { ["prompts"] = new JArray() });

            case "tools/call":
                return await HandleToolsCallAsync(id, parameters).ConfigureAwait(false);

            default:
                return JsonRpcError(id, -32601, "Method not found", method);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JToken id, JObject parameters)
    {
        var toolName = (string)parameters["name"];
        var args = parameters["arguments"] as JObject ?? new JObject();

        try
        {
            var result = await RunToolAsync(toolName, args).ConfigureAwait(false);
            await LogEventAsync("McpToolSucceeded", new Dictionary<string, string> { { "tool", toolName } }).ConfigureAwait(false);
            return ToolResult(id, result.ToString(Newtonsoft.Json.Formatting.None), false);
        }
        catch (UnknownToolException)
        {
            return JsonRpcError(id, -32602, "Unknown tool", toolName);
        }
        catch (Exception ex)
        {
            await LogExceptionAsync(ex, toolName).ConfigureAwait(false);
            return ToolResult(id, "Tool execution failed: " + ex.Message, true);
        }
    }

    private async Task<JToken> RunToolAsync(string tool, JObject a)
    {
        switch (tool)
        {
            case "execute_sql": return await ToolExecuteSqlAsync(a).ConfigureAwait(false);
            case "get_statement": return await ToolGetStatementAsync(a).ConfigureAwait(false);
            case "cancel_statement":
                await CallAsync(HttpMethod.Post, "/api/2.0/sql/statements/" + Esc(Require(a, "statement_id")) + "/cancel", null).ConfigureAwait(false);
                return new JObject { ["cancel_requested"] = true };
            case "list_warehouses": return await ToolListWarehousesAsync().ConfigureAwait(false);
            case "manage_warehouse": return await ToolManageWarehouseAsync(a).ConfigureAwait(false);
            case "browse_catalog": return await ToolBrowseCatalogAsync(a).ConfigureAwait(false);
            case "find_tables": return await ToolFindTablesAsync(a).ConfigureAwait(false);
            case "describe_table": return await ToolDescribeTableAsync(a).ConfigureAwait(false);
            case "list_jobs": return await ToolListJobsAsync(a).ConfigureAwait(false);
            case "run_job": return await ToolRunJobAsync(a).ConfigureAwait(false);
            case "get_job_run": return await ToolGetJobRunAsync(a).ConfigureAwait(false);
            case "get_job_run_output": return await ToolGetJobRunOutputAsync(a).ConfigureAwait(false);
            case "cancel_job_run":
                await CallAsync(HttpMethod.Post, "/api/2.2/jobs/runs/cancel", new JObject { ["run_id"] = GetLong(a, "run_id") }).ConfigureAwait(false);
                return new JObject { ["cancel_requested"] = true };
            case "list_genie_spaces": return await ToolListGenieSpacesAsync().ConfigureAwait(false);
            case "ask_genie": return await ToolAskGenieAsync(a).ConfigureAwait(false);
            default: throw new UnknownToolException();
        }
    }

    // ── Tool definitions ─────────────────────────────────────────────────

    private static JArray BuildToolList()
    {
        return new JArray
        {
            Tool("execute_sql",
                "Run a SQL statement on a Databricks SQL warehouse and return the rows. Waits for the result. If warehouse_id is omitted, a warehouse is chosen automatically. Use catalog and schema to avoid fully qualifying table names.",
                new JObject
                {
                    ["statement"] = Prop("string", "The SQL statement to run."),
                    ["warehouse_id"] = Prop("string", "Optional SQL warehouse ID. Leave out to use the default warehouse."),
                    ["catalog"] = Prop("string", "Optional default catalog."),
                    ["schema"] = Prop("string", "Optional default schema."),
                    ["row_limit"] = Prop("integer", "Maximum rows to return. Default 100, maximum 1000.")
                }, "statement"),
            Tool("get_statement",
                "Get the status and rows of a SQL statement started earlier. Use when execute_sql returned a state of PENDING or RUNNING, or to read a later chunk of a large result.",
                new JObject
                {
                    ["statement_id"] = Prop("string", "The statement ID."),
                    ["chunk_index"] = Prop("integer", "Optional chunk index for large results. Use next_chunk_index from the previous result.")
                }, "statement_id"),
            Tool("cancel_statement", "Cancel a running SQL statement.",
                new JObject { ["statement_id"] = Prop("string", "The statement ID.") }, "statement_id"),
            Tool("list_warehouses", "List the SQL warehouses in the workspace with their state, size, and HTTP path.", new JObject()),
            Tool("manage_warehouse", "Start or stop a SQL warehouse. Ask the user first, because a running warehouse costs money and stopping one interrupts running queries.",
                new JObject
                {
                    ["warehouse_id"] = Prop("string", "The SQL warehouse ID."),
                    ["action"] = new JObject { ["type"] = "string", ["description"] = "start or stop.", ["enum"] = new JArray("start", "stop") }
                }, "warehouse_id", "action"),
            Tool("browse_catalog", "Browse Unity Catalog one level at a time. With no arguments, lists catalogs. With catalog_name, lists its schemas. With catalog_name and schema_name, lists its tables and views.",
                new JObject
                {
                    ["catalog_name"] = Prop("string", "Optional catalog. Omit to list catalogs."),
                    ["schema_name"] = Prop("string", "Optional schema. Needs catalog_name. Omit to list schemas."),
                    ["max_results"] = Prop("integer", "Maximum items to return. Default 50."),
                    ["page_token"] = Prop("string", "Token from a previous table listing for the next page.")
                }),
            Tool("find_tables", "Find tables and views by name across catalogs. Use when the user does not know where a table lives. Patterns are SQL LIKE patterns, where % matches any text, such as %customer%.",
                new JObject
                {
                    ["table_name_pattern"] = Prop("string", "SQL LIKE pattern for the table name, for example %orders%."),
                    ["schema_name_pattern"] = Prop("string", "Optional SQL LIKE pattern for the schema name."),
                    ["catalog_name"] = Prop("string", "Optional catalog to search. Omit to search every catalog except system."),
                    ["max_results"] = Prop("integer", "Maximum tables to return. Default 25.")
                }, "table_name_pattern"),
            Tool("describe_table", "Get a table's columns, types, and description. Use before writing SQL against an unfamiliar table.",
                new JObject { ["full_name"] = Prop("string", "Three-part name: catalog.schema.table.") }, "full_name"),
            Tool("list_jobs", "List Databricks jobs. The name filter must match a job name exactly, ignoring case. To search by part of a name, list without a filter.",
                new JObject
                {
                    ["name"] = Prop("string", "Optional exact job name, ignoring case."),
                    ["limit"] = Prop("integer", "Maximum jobs to return. Default 25, maximum 100."),
                    ["page_token"] = Prop("string", "Token from a previous result for the next page.")
                }),
            Tool("run_job", "Start a run of a job. This starts compute and can cost money, so confirm with the user first.",
                new JObject
                {
                    ["job_id"] = Prop("integer", "The job ID."),
                    ["job_parameters"] = new JObject { ["type"] = "object", ["description"] = "Optional job parameters as name and value pairs.", ["additionalProperties"] = new JObject { ["type"] = "string" } }
                }, "job_id"),
            Tool("get_job_run", "Get the status of a job run and its tasks.",
                new JObject { ["run_id"] = Prop("integer", "The run ID.") }, "run_id"),
            Tool("get_job_run_output", "Get the output of a finished job run: the value a notebook returned with dbutils.notebook.exit, SQL output, logs, and any error. For a job with several tasks, pass a task's run_id from get_job_run.",
                new JObject { ["run_id"] = Prop("integer", "The run ID. For multi-task jobs, use a task run ID.") }, "run_id"),
            Tool("cancel_job_run", "Cancel a job run.",
                new JObject { ["run_id"] = Prop("integer", "The run ID.") }, "run_id"),
            Tool("list_genie_spaces", "List the Genie spaces you can access. Genie answers data questions in plain language.", new JObject()),
            Tool("ask_genie",
                "Ask a Genie space a question in plain language. Waits for the answer and returns the text, the SQL Genie wrote, and the rows. Pass conversation_id from an earlier answer to ask a follow-up.",
                new JObject
                {
                    ["space_id"] = Prop("string", "The Genie space ID from list_genie_spaces."),
                    ["question"] = Prop("string", "The question in plain language. Not needed when resuming with message_id."),
                    ["conversation_id"] = Prop("string", "Optional conversation ID for a follow-up question."),
                    ["message_id"] = Prop("string", "Optional. With conversation_id, resumes waiting for a message that was already sent instead of asking again.")
                }, "space_id")
        };
    }

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
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

    private static JObject Prop(string type, string description)
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    // ── SQL tools ────────────────────────────────────────────────────────

    private async Task<JToken> ToolExecuteSqlAsync(JObject a)
    {
        var statement = Require(a, "statement");
        var rowLimit = Math.Max(1, Math.Min(GetInt(a, "row_limit", MCP_DEFAULT_ROW_LIMIT), MCP_MAX_ROW_LIMIT));
        var warehouseId = Str(a, "warehouse_id") ?? await ResolveWarehouseIdAsync().ConfigureAwait(false);

        var body = new JObject
        {
            ["statement"] = statement,
            ["warehouse_id"] = warehouseId,
            ["wait_timeout"] = "30s",
            ["on_wait_timeout"] = "CONTINUE",
            ["disposition"] = "INLINE",
            ["format"] = "JSON_ARRAY",
            ["row_limit"] = rowLimit
        };
        if (Str(a, "catalog") != null) body["catalog"] = Str(a, "catalog");
        if (Str(a, "schema") != null) body["schema"] = Str(a, "schema");

        var started = DateTime.UtcNow;
        var response = await CallAsync(HttpMethod.Post, "/api/2.0/sql/statements", body).ConfigureAwait(false);
        response = await PollStatementAsync(response, started).ConfigureAwait(false);

        var shaped = ShapeStatement(response, rowLimit, null);
        shaped["warehouse_id"] = warehouseId;
        return shaped;
    }

    private async Task<JToken> ToolGetStatementAsync(JObject a)
    {
        var statementId = Require(a, "statement_id");
        var statement = await CallAsync(HttpMethod.Get, "/api/2.0/sql/statements/" + Esc(statementId), null).ConfigureAwait(false);

        if (a["chunk_index"] == null)
        {
            return ShapeStatement(statement, MCP_MAX_ROW_LIMIT, null);
        }

        var chunk = await CallAsync(HttpMethod.Get,
            "/api/2.0/sql/statements/" + Esc(statementId) + "/result/chunks/" + GetInt(a, "chunk_index", 0), null).ConfigureAwait(false);
        statement["result"] = chunk;
        return ShapeStatement(statement, MCP_MAX_ROW_LIMIT, null);
    }

    private async Task<JObject> PollStatementAsync(JObject response, DateTime started)
    {
        while (IsRunning(response) && (DateTime.UtcNow - started).TotalSeconds < POLL_BUDGET_SECONDS)
        {
            await Task.Delay(2000, this.CancellationToken).ConfigureAwait(false);
            response = await CallAsync(HttpMethod.Get, "/api/2.0/sql/statements/" + Esc((string)response["statement_id"]), null).ConfigureAwait(false);
        }
        return response;
    }

    private static bool IsRunning(JObject response)
    {
        var state = (string)response.SelectToken("status.state");
        return state == "PENDING" || state == "RUNNING";
    }

    /// <summary>
    /// Turns a statement response into compact JSON: rows become objects keyed by column name.
    /// </summary>
    private static JObject ShapeStatement(JObject response, int rowLimit, JArray columnsOverride)
    {
        var state = (string)response.SelectToken("status.state");
        var shaped = new JObject
        {
            ["statement_id"] = response["statement_id"],
            ["state"] = state
        };

        var error = response.SelectToken("status.error");
        if (error != null)
        {
            shaped["error"] = error;
        }

        if (state == "PENDING" || state == "RUNNING")
        {
            shaped["note"] = "Still running. Call get_statement with this statement_id to check again.";
            return shaped;
        }

        var columns = columnsOverride ?? response.SelectToken("manifest.schema.columns") as JArray;
        var names = new List<string>();
        if (columns != null)
        {
            var columnInfo = new JArray();
            foreach (var column in columns)
            {
                var name = (string)column["name"];
                var unique = name;
                var suffix = 1;
                while (names.Contains(unique))
                {
                    unique = name + "_" + (++suffix);
                }
                names.Add(unique);
                columnInfo.Add(new JObject { ["name"] = unique, ["type"] = column["type_text"] ?? column["type_name"] });
            }
            shaped["columns"] = columnInfo;
        }

        var data = response.SelectToken("result.data_array") as JArray;
        if (data != null)
        {
            var rows = new JArray();
            foreach (var row in data.Take(rowLimit))
            {
                var cells = row as JArray;
                if (cells == null || names.Count == 0)
                {
                    rows.Add(row);
                    continue;
                }
                var obj = new JObject();
                for (var i = 0; i < cells.Count && i < names.Count; i++)
                {
                    obj[names[i]] = cells[i];
                }
                rows.Add(obj);
            }
            shaped["rows"] = rows;
            shaped["rows_returned"] = rows.Count;
            if (data.Count > rowLimit)
            {
                shaped["rows_cut_by_limit"] = true;
            }
        }

        if (response.SelectToken("manifest.total_row_count") != null) shaped["total_row_count"] = response.SelectToken("manifest.total_row_count");
        if (response.SelectToken("manifest.truncated") != null) shaped["truncated"] = response.SelectToken("manifest.truncated");
        if (response.SelectToken("result.next_chunk_index") != null) shaped["next_chunk_index"] = response.SelectToken("result.next_chunk_index");

        return shaped;
    }

    private async Task<JToken> ToolListWarehousesAsync()
    {
        var response = await CallAsync(HttpMethod.Get, "/api/2.0/sql/warehouses", null).ConfigureAwait(false);
        var list = new JArray();
        foreach (var w in (response["warehouses"] as JArray) ?? new JArray())
        {
            list.Add(new JObject
            {
                ["id"] = w["id"],
                ["name"] = w["name"],
                ["state"] = w["state"],
                ["cluster_size"] = w["cluster_size"],
                ["serverless"] = w["enable_serverless_compute"],
                ["http_path"] = w.SelectToken("odbc_params.path")
            });
        }
        return new JObject { ["warehouses"] = list };
    }

    // ── Unity Catalog tools ──────────────────────────────────────────────

    private async Task<JToken> ToolManageWarehouseAsync(JObject a)
    {
        var action = Require(a, "action").ToLowerInvariant();
        if (action != "start" && action != "stop")
        {
            throw new ArgumentException("'action' must be start or stop.");
        }
        await CallAsync(HttpMethod.Post, "/api/2.0/sql/warehouses/" + Esc(Require(a, "warehouse_id")) + "/" + action, null).ConfigureAwait(false);
        return new JObject
        {
            [action == "start" ? "start_requested" : "stop_requested"] = true,
            ["note"] = "This takes a few minutes. Use list_warehouses to check the state."
        };
    }

    private async Task<JToken> ToolBrowseCatalogAsync(JObject a)
    {
        var maxResults = Math.Max(1, Math.Min(GetInt(a, "max_results", 50), 50)).ToString();
        var catalog = Str(a, "catalog_name");
        var schema = Str(a, "schema_name");

        if (catalog == null)
        {
            var catalogs = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/catalogs" + BuildQuery(new Dictionary<string, string> { { "max_results", maxResults } }), null).ConfigureAwait(false);
            return PickList(catalogs, "catalogs", "name", "comment", "owner");
        }

        if (schema == null)
        {
            var schemas = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/schemas" + BuildQuery(new Dictionary<string, string>
            {
                { "catalog_name", catalog },
                { "max_results", maxResults }
            }), null).ConfigureAwait(false);
            return PickList(schemas, "schemas", "name", "full_name", "comment");
        }

        var tables = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/tables" + BuildQuery(new Dictionary<string, string>
        {
            { "catalog_name", catalog },
            { "schema_name", schema },
            { "max_results", maxResults },
            { "page_token", Str(a, "page_token") },
            { "omit_columns", "true" },
            { "omit_properties", "true" }
        }), null).ConfigureAwait(false);
        return PickList(tables, "tables", "name", "full_name", "table_type", "comment");
    }

    private async Task<JToken> ToolFindTablesAsync(JObject a)
    {
        var tablePattern = Require(a, "table_name_pattern");
        var maxResults = Math.Max(1, Math.Min(GetInt(a, "max_results", 25), 100));
        var catalogs = new List<string>();

        if (Str(a, "catalog_name") != null)
        {
            catalogs.Add(Str(a, "catalog_name"));
        }
        else
        {
            var list = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/catalogs", null).ConfigureAwait(false);
            foreach (var c in (list["catalogs"] as JArray) ?? new JArray())
            {
                var name = (string)c["name"];
                if (!string.IsNullOrEmpty(name) && name != "system" && !name.StartsWith("__"))
                {
                    catalogs.Add(name);
                }
            }
        }

        var found = new JArray();
        foreach (var catalog in catalogs)
        {
            if (found.Count >= maxResults) break;
            var summaries = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/table-summaries" + BuildQuery(new Dictionary<string, string>
            {
                { "catalog_name", catalog },
                { "table_name_pattern", tablePattern },
                { "schema_name_pattern", Str(a, "schema_name_pattern") },
                { "max_results", (maxResults - found.Count).ToString() }
            }), null).ConfigureAwait(false);

            foreach (var t in (summaries["tables"] as JArray) ?? new JArray())
            {
                if (found.Count >= maxResults) break;
                found.Add(PickProps(t, "full_name", "table_type"));
            }
        }

        var result = new JObject { ["tables"] = found, ["catalogs_searched"] = catalogs.Count };
        if (found.Count >= maxResults) result["note"] = "Result limit reached. Narrow the pattern or raise max_results to see more.";
        return result;
    }

    private async Task<JToken> ToolDescribeTableAsync(JObject a)
    {
        var table = await CallAsync(HttpMethod.Get, "/api/2.1/unity-catalog/tables/" + Esc(Require(a, "full_name")), null).ConfigureAwait(false);
        var result = PickProps(table, "full_name", "table_type", "data_source_format", "comment", "owner");
        var columns = new JArray();
        foreach (var column in (table["columns"] as JArray) ?? new JArray())
        {
            columns.Add(PickProps(column, "name", "type_text", "nullable", "comment"));
        }
        result["columns"] = columns;
        return result;
    }

    private static JObject PickList(JObject response, string collection, params string[] properties)
    {
        var items = new JArray();
        foreach (var item in (response[collection] as JArray) ?? new JArray())
        {
            items.Add(PickProps(item, properties));
        }
        var result = new JObject { [collection] = items };
        if (response["next_page_token"] != null) result["next_page_token"] = response["next_page_token"];
        return result;
    }

    private static JObject PickProps(JToken source, params string[] properties)
    {
        var picked = new JObject();
        foreach (var name in properties)
        {
            var value = source[name];
            if (value != null && value.Type != JTokenType.Null)
            {
                var text = value.Type == JTokenType.String ? (string)value : null;
                picked[name] = text != null && text.Length > MAX_TEXT_LENGTH ? text.Substring(0, MAX_TEXT_LENGTH) + "..." : value;
            }
        }
        return picked;
    }

    // ── Job tools ────────────────────────────────────────────────────────

    private async Task<JToken> ToolListJobsAsync(JObject a)
    {
        var query = BuildQuery(new Dictionary<string, string>
        {
            { "name", Str(a, "name") },
            { "limit", Math.Max(1, Math.Min(GetInt(a, "limit", 25), 100)).ToString() },
            { "page_token", Str(a, "page_token") }
        });
        var response = await CallAsync(HttpMethod.Get, "/api/2.2/jobs/list" + query, null).ConfigureAwait(false);

        var jobs = new JArray();
        foreach (var job in (response["jobs"] as JArray) ?? new JArray())
        {
            jobs.Add(new JObject
            {
                ["job_id"] = job["job_id"],
                ["name"] = job.SelectToken("settings.name"),
                ["creator"] = job["creator_user_name"]
            });
        }
        var result = new JObject { ["jobs"] = jobs, ["has_more"] = response["has_more"] ?? false };
        if (response["next_page_token"] != null) result["next_page_token"] = response["next_page_token"];
        return result;
    }

    private async Task<JToken> ToolRunJobAsync(JObject a)
    {
        var body = new JObject { ["job_id"] = GetLong(a, "job_id") };
        if (a["job_parameters"] is JObject) body["job_parameters"] = a["job_parameters"];
        return await CallAsync(HttpMethod.Post, "/api/2.2/jobs/run-now", body).ConfigureAwait(false);
    }

    private async Task<JToken> ToolGetJobRunAsync(JObject a)
    {
        var run = await CallAsync(HttpMethod.Get, "/api/2.2/jobs/runs/get?run_id=" + GetLong(a, "run_id"), null).ConfigureAwait(false);
        var result = PickProps(run, "run_id", "job_id", "run_name", "run_page_url", "start_time", "end_time");
        result["state"] = run["state"];

        var tasks = new JArray();
        foreach (var task in (run["tasks"] as JArray) ?? new JArray())
        {
            tasks.Add(new JObject { ["task_key"] = task["task_key"], ["state"] = task["state"] });
        }
        result["tasks"] = tasks;
        return result;
    }

    // ── Genie tools ──────────────────────────────────────────────────────

    private async Task<JToken> ToolGetJobRunOutputAsync(JObject a)
    {
        var output = await CallAsync(HttpMethod.Get, "/api/2.2/jobs/runs/get-output?run_id=" + GetLong(a, "run_id"), null).ConfigureAwait(false);
        var result = new JObject();
        foreach (var name in new[] { "notebook_output", "sql_output", "run_job_output", "error", "error_trace", "logs", "logs_truncated" })
        {
            var value = output[name];
            if (value == null || value.Type == JTokenType.Null) continue;

            var text = value.Type == JTokenType.String ? (string)value : null;
            result[name] = text != null && text.Length > MAX_OUTPUT_LENGTH ? text.Substring(0, MAX_OUTPUT_LENGTH) + "..." : value;
        }
        var state = output.SelectToken("metadata.state");
        if (state != null) result["state"] = state;
        return result;
    }

    private async Task<JToken> ToolListGenieSpacesAsync()
    {
        var response = await CallAsync(HttpMethod.Get, "/api/2.0/genie/spaces?page_size=100", null).ConfigureAwait(false);
        return PickList(response, "spaces", "space_id", "title", "description");
    }

    private async Task<JToken> ToolAskGenieAsync(JObject a)
    {
        var spaceId = Require(a, "space_id");
        var conversationId = Str(a, "conversation_id");
        var spacePath = "/api/2.0/genie/spaces/" + Esc(spaceId);
        var started = DateTime.UtcNow;

        var messageId = Str(a, "message_id");
        if (messageId != null && conversationId != null)
        {
            // Resume waiting on a message that was already sent; do not ask again.
        }
        else if (conversationId == null)
        {
            var start = await CallAsync(HttpMethod.Post, spacePath + "/start-conversation", new JObject { ["content"] = Require(a, "question") }).ConfigureAwait(false);
            conversationId = (string)start["conversation_id"] ?? (string)start.SelectToken("message.conversation_id");
            messageId = (string)start["message_id"] ?? (string)start.SelectToken("message.message_id") ?? (string)start.SelectToken("message.id");
        }
        else
        {
            var message = await CallAsync(HttpMethod.Post, spacePath + "/conversations/" + Esc(conversationId) + "/messages", new JObject { ["content"] = Require(a, "question") }).ConfigureAwait(false);
            messageId = (string)message["message_id"] ?? (string)message["id"];
        }

        if (string.IsNullOrEmpty(conversationId) || string.IsNullOrEmpty(messageId))
        {
            throw new DatabricksException(502, "Genie did not return a conversation and message ID.");
        }

        var messagePath = spacePath + "/conversations/" + Esc(conversationId) + "/messages/" + Esc(messageId);
        JObject current;
        string status;
        do
        {
            await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
            current = await CallAsync(HttpMethod.Get, messagePath, null).ConfigureAwait(false);
            status = (string)current["status"];
        }
        while (status != "COMPLETED" && status != "FAILED" && status != "CANCELLED" && status != "QUERY_RESULT_EXPIRED"
               && (DateTime.UtcNow - started).TotalSeconds < POLL_BUDGET_SECONDS);

        var result = new JObject
        {
            ["conversation_id"] = conversationId,
            ["message_id"] = messageId,
            ["status"] = status
        };

        if (current["error"] != null) result["error"] = current["error"];

        if (status != "COMPLETED")
        {
            result["note"] = "Genie has not finished. Call ask_genie again with the same space_id, conversation_id, and message_id to keep waiting without asking the question again.";
            return result;
        }

        var texts = new JArray();
        var queries = new JArray();
        foreach (var attachment in (current["attachments"] as JArray) ?? new JArray())
        {
            var text = (string)attachment.SelectToken("text.content");
            if (!string.IsNullOrEmpty(text)) texts.Add(text);

            var query = attachment["query"];
            if (query == null) continue;

            var entry = new JObject
            {
                ["title"] = query["title"],
                ["description"] = query["description"],
                ["sql"] = query["query"]
            };

            try
            {
                var rows = await CallAsync(HttpMethod.Get, messagePath + "/attachments/" + Esc((string)attachment["attachment_id"]) + "/query-result", null).ConfigureAwait(false);
                var statement = rows["statement_response"] as JObject;
                if (statement != null)
                {
                    entry["result"] = ShapeStatement(statement, MCP_DEFAULT_ROW_LIMIT, null);
                }
            }
            catch (DatabricksException ex)
            {
                entry["result_error"] = ex.Message;
            }
            queries.Add(entry);
        }

        result["answer"] = texts;
        result["queries"] = queries;
        return result;
    }

    // ── Databricks REST helper ───────────────────────────────────────────

    /// <summary>
    /// Calls the Databricks REST API on the workspace host from the swagger, with the caller's token.
    /// </summary>
    private async Task<JObject> CallAsync(HttpMethod method, string pathAndQuery, JObject body)
    {
        var uri = new Uri("https://" + this.Context.Request.RequestUri.Host + pathAndQuery);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DatabricksException((int)response.StatusCode, DescribeError((int)response.StatusCode, text));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new JObject();
        }

        try { return JObject.Parse(text); }
        catch (JsonException) { return new JObject { ["text"] = text }; }
    }

    private static string DescribeError(int status, string text)
    {
        string message = null;
        try { message = (string)JObject.Parse(text)["message"]; }
        catch (Exception) { }

        var detail = string.IsNullOrWhiteSpace(message) ? text : message;
        var hint = status == 401 || status == 403
            ? " Check that you can sign in to this workspace and have access to this resource."
            : "";
        return "Databricks returned " + status + ": " + detail + hint;
    }

    // ── Small helpers ────────────────────────────────────────────────────

    private static string Esc(string value)
    {
        return Uri.EscapeDataString(value ?? "");
    }

    private static string BuildQuery(Dictionary<string, string> values)
    {
        var parts = values
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))
            .ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static string Str(JObject args, string name)
    {
        var value = args[name];
        if (value == null || value.Type == JTokenType.Null) return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Require(JObject args, string name)
    {
        var value = Str(args, name);
        if (value == null) throw new ArgumentException("'" + name + "' is required.");
        return value;
    }

    private static int GetInt(JObject args, string name, int defaultValue)
    {
        int parsed;
        return int.TryParse(Str(args, name), out parsed) ? parsed : defaultValue;
    }

    private static long GetLong(JObject args, string name)
    {
        long parsed;
        if (!long.TryParse(Str(args, name), out parsed)) throw new ArgumentException("'" + name + "' is required and must be a number.");
        return parsed;
    }

    // ── JSON-RPC and error responses ─────────────────────────────────────

    private static HttpResponseMessage JsonRpcResult(JToken id, JObject result)
    {
        var envelope = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id ?? JValue.CreateNull() };
        return Json(HttpStatusCode.OK, envelope);
    }

    private static HttpResponseMessage JsonRpcError(JToken id, int code, string message, string data)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;
        var envelope = new JObject { ["jsonrpc"] = "2.0", ["error"] = error, ["id"] = id ?? JValue.CreateNull() };
        return Json(HttpStatusCode.OK, envelope);
    }

    private static HttpResponseMessage ToolResult(JToken id, string text, bool isError)
    {
        return JsonRpcResult(id, new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        });
    }

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode status, string code, string message)
    {
        return Json(status, new JObject { ["error"] = new JObject { ["code"] = code, ["message"] = message } });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JObject body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ── Application Insights ─────────────────────────────────────────────

    private bool TelemetryConfigured()
    {
        return APP_INSIGHTS_ENABLED
            && !string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            && !APP_INSIGHTS_KEY.Contains("INSERT_YOUR");
    }

    private async Task LogEventAsync(string eventName, IDictionary<string, string> properties)
    {
        if (!TelemetryConfigured()) return;

        var props = new Dictionary<string, string>(properties ?? new Dictionary<string, string>())
        {
            ["connector"] = CONNECTOR_NAME,
            ["operationId"] = this.Context.OperationId ?? ""
        };

        await SendTelemetryAsync("Microsoft.ApplicationInsights.Event", new
        {
            baseType = "EventData",
            baseData = new { ver = 2, name = eventName, properties = props }
        }).ConfigureAwait(false);
    }

    private async Task LogExceptionAsync(Exception ex, string context)
    {
        if (!TelemetryConfigured()) return;

        await SendTelemetryAsync("Microsoft.ApplicationInsights.Exception", new
        {
            baseType = "ExceptionData",
            baseData = new
            {
                ver = 2,
                exceptions = new[]
                {
                    new { typeName = ex.GetType().Name, message = ex.Message, hasFullStack = true, stack = ex.StackTrace ?? "" }
                },
                properties = new Dictionary<string, string>
                {
                    { "connector", CONNECTOR_NAME },
                    { "context", context ?? "" }
                }
            }
        }).ConfigureAwait(false);
    }

    private async Task SendTelemetryAsync(string name, object data)
    {
        try
        {
            var envelope = new
            {
                name = name,
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = data
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(JsonConvert.SerializeObject(envelope), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception) { /* Telemetry must never break an operation */ }
    }
}

public class DatabricksException : Exception
{
    public int StatusCode { get; private set; }

    public DatabricksException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class UnknownToolException : Exception
{
}
