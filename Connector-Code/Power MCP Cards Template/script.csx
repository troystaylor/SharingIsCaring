using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  MCP Apps cards: interactive approval, form, table, and status views that  ║
// ║  an agent renders in chat in place of Adaptive Cards. Each view is a       ║
// ║  ui:// resource served by this script; tools link to a view through        ║
// ║  _meta.ui.resourceUri and return structuredContent for the view to render. ║
// ║                                                                            ║
// ║  RegisterCardViews  - the four ui:// resources                             ║
// ║  RegisterCardTools  - the tools that open each view, plus the app-only     ║
// ║                       tools the views call back into                       ║
// ║  CardViews          - the HTML, CSS, and JavaScript for each view          ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "power-mcp-cards",
            Version = "1.0.0",
            Title = "Power MCP Cards",
            Description = "Interactive approval, form, table, and status cards for agents, delivered as MCP Apps"
        },

        // Preferred revision. Legacy clients (Copilot Studio) that open with
        // initialize are detected and served under legacy semantics automatically.
        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            // Declares MCP Apps support so a host that negotiates the extension can see it.
            Extensions = new JObject
            {
                [McpKeys.ExtensionUi] = new JObject { ["mimeTypes"] = new JArray(McpKeys.UiMimeType) }
            }
            // Logging is deprecated as of 2026-07-28; leave off for new servers.
        },

        // Cache hints attached to list and read results (2026-07-28 CacheableResult).
        ListCacheTtlMs = 300000,      // 5 minutes
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,   // 1 minute
        ResourceCacheScope = "public", // View HTML is identical for every caller
        DiscoverCacheTtlMs = 3600000, // 1 hour

        Instructions =
            "Use show_approval, show_form, show_table, and show_status to present interactive cards instead of long text. " +
            "The card is shown to the user, so do not repeat its contents; reply briefly. " +
            "Hosts that cannot draw cards fall back to chat: if the user says they cannot see the card, or answers in chat, " +
            "present the details in text and record their answer with submit_decision or submit_form. " +
            "Do not record it again if the user's message already contains a confirmation ID, because the card did it. " +
            "Call refresh_status only when the user asks for an update."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register tools, resources, and prompts
        RegisterCapabilities(handler);

        // 3. Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 4. Handle the request — one line does everything
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 5. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────
    //
    //    Register tools, resources, prompts, and skills here.
    //    Tools:     handler.AddTool(name, description, schema, handler)
    //    Resources: handler.AddResource(uri, name, description, handler)
    //    Templates: handler.AddResourceTemplate(uriTemplate, name, description, handler)
    //    Prompts:   handler.AddPrompt(name, description, arguments, handler)
    //    Skills:    handler.AddSkill(name, description, instructions, resources)
    //

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        RegisterCardViews(handler);
        RegisterCardTools(handler);
    }

    // ── Card Views ─────────────────────────────────────────────────────

    private const string ApprovalUri = "ui://power-mcp-cards/approval";
    private const string FormUri = "ui://power-mcp-cards/form";
    private const string TableUri = "ui://power-mcp-cards/table";
    private const string StatusUri = "ui://power-mcp-cards/status";

    /// <summary>
    /// When true, submit_decision, submit_form, and refresh_status are listed to the model, so an
    /// agent on a host that cannot draw cards can still record the user's chat answer. The cards
    /// call them either way. Set to false to hide them from the model on hosts that honor
    /// _meta.ui.visibility, once your host renders cards reliably.
    /// </summary>
    private const bool AppToolsVisibleToModel = true;

    /// <summary>
    /// Register the ui:// resources. No csp is declared, so the host applies its restrictive
    /// default: no network access and no external scripts. Add a csp here only if a view
    /// needs to load assets or call an API directly.
    /// </summary>
    private void RegisterCardViews(McpRequestHandler handler)
    {
        handler.AddUiResource(ApprovalUri, "Approval card",
            "Approve or reject a request, with an optional comment.",
            CardViews.Approval, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(FormUri, "Form card",
            "Collect structured input with validation.",
            CardViews.Form, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(TableUri, "Table card",
            "Sortable, filterable table with optional row selection.",
            CardViews.Table, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(StatusUri, "Status card",
            "Progress and step status for a long-running job.",
            CardViews.Status, ui => ui["prefersBorder"] = false);
    }

    // ── Card Tools ─────────────────────────────────────────────────────
    //
    //    show_* tools open a card. Each returns a plain-text fallback in content for
    //    hosts that cannot render MCP Apps, and the data the card renders in
    //    structuredContent. submit_* and refresh_status are called by the cards.
    //    They echo their input so the template runs end to end; replace the TODO
    //    blocks with calls to your own system.

    private void RegisterCardTools(McpRequestHandler handler)
    {
        handler.AddTool("show_approval",
            "Show an approval card with the request details and Approve and Reject buttons. " +
            "Use when the user must approve, reject, or sign off on something. " +
            "The result tells you how to continue if the card is not shown.",
            schema: s => s
                .String("title", "What is being approved", required: true)
                .String("summary", "One or two sentences of context")
                .String("requester", "Who made the request")
                .String("requestId", "Identifier of the request. Generated when omitted.")
                .Array("facts", "Key details shown as label and value rows", ObjectSchema(new JObject
                {
                    ["label"] = StringProp("Row label"),
                    ["value"] = StringProp("Row value")
                }), required: false)
                .String("approveLabel", "Text for the approve button. Defaults to Approve.")
                .String("rejectLabel", "Text for the reject button. Defaults to Reject.")
                .Boolean("requireCommentOnReject", "Require a comment before the user can reject"),
            handler: (args, ct) =>
            {
                var title = RequireArgument(args, "title");
                var data = new JObject
                {
                    ["requestId"] = GetArgument(args, "requestId", Guid.NewGuid().ToString("N")),
                    ["title"] = title,
                    ["summary"] = GetArgument(args, "summary", ""),
                    ["requester"] = GetArgument(args, "requester", ""),
                    ["facts"] = GetArray(args, "facts"),
                    ["approveLabel"] = GetArgument(args, "approveLabel", "Approve"),
                    ["rejectLabel"] = GetArgument(args, "rejectLabel", "Reject"),
                    ["requireCommentOnReject"] = GetBool(args, "requireCommentOnReject", false)
                };

                var text = new StringBuilder();
                text.Append("Approval requested: ").Append(title).Append(". ");
                if (!string.IsNullOrWhiteSpace(data.Value<string>("summary"))) text.Append(data.Value<string>("summary")).Append(' ');
                foreach (var fact in data["facts"].OfType<JObject>())
                    text.Append(fact.Value<string>("label")).Append(": ").Append(fact.Value<string>("value")).Append("; ");
                text.Append("\nAn approval card with these details and the buttons is shown to the user, so keep your reply to one or two short sentences and do not repeat the details. ");
                text.Append("Add that if the card does not appear, they can reply approve or reject here");
                text.Append(data.Value<bool>("requireCommentOnReject") ? ", with a comment if they reject. " : ", with an optional comment. ");
                text.Append("If they answer in chat, record it by calling submit_decision with requestId '").Append(data.Value<string>("requestId"))
                    .Append("', their decision, and their comment. If their message already contains a confirmation ID, the card recorded it, so just acknowledge it.");

                return Task.FromResult(CardResult(text.ToString(), data));
            },
            annotations: a => { a["readOnlyHint"] = true; },
            title: "Show approval card",
            uiResourceUri: ApprovalUri);

        handler.AddTool("submit_decision",
            "Records the user's approval decision. Call it only after the user has clearly said approve or reject in chat, " +
            "with their comment if one was required. Do not call it if their message already contains a confirmation ID. " +
            "The approval card also calls this when its buttons are used.",
            schema: s => s
                .String("requestId", "Identifier of the request", required: true)
                .String("decision", "The user's decision", required: true, enumValues: new[] { "approved", "rejected" })
                .String("comment", "Optional comment from the user"),
            handler: (args, ct) =>
            {
                var requestId = RequireArgument(args, "requestId");
                var decision = RequireArgument(args, "decision");
                if (decision != "approved" && decision != "rejected")
                    throw new ArgumentException("'decision' must be approved or rejected");

                // TODO: Record the decision in your approval system, for example with
                // SendExternalRequestAsync(HttpMethod.Post, "https://api.example.com/approvals/" + requestId, body).
                var data = new JObject
                {
                    ["requestId"] = requestId,
                    ["decision"] = decision,
                    ["comment"] = GetArgument(args, "comment", ""),
                    ["confirmationId"] = "DEC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(),
                    ["recordedAt"] = DateTime.UtcNow.ToString("o")
                };

                return Task.FromResult(CardResult("Decision " + decision + " recorded for request " + requestId + ".", data));
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = false; a["idempotentHint"] = true; },
            title: "Submit approval decision",
            uiResourceUri: ApprovalUri,
            appOnly: !AppToolsVisibleToModel);

        handler.AddTool("show_form",
            "Show a form card that collects structured input from the user, with validation. " +
            "Use instead of asking several questions one at a time. " +
            "The result tells you how to continue if the card is not shown.",
            schema: s => s
                .String("title", "Form heading", required: true)
                .String("description", "Short instructions shown under the heading")
                .String("formId", "Identifier for this form. Generated when omitted.")
                .String("submitLabel", "Text for the submit button. Defaults to Submit.")
                .Array("fields", "The fields to show, in order", FormFieldSchema(), required: true),
            handler: (args, ct) =>
            {
                var title = RequireArgument(args, "title");
                var fields = GetArray(args, "fields");
                if (fields.Count == 0) throw new ArgumentException("'fields' must contain at least one field");

                var data = new JObject
                {
                    ["formId"] = GetArgument(args, "formId", Guid.NewGuid().ToString("N")),
                    ["title"] = title,
                    ["description"] = GetArgument(args, "description", ""),
                    ["submitLabel"] = GetArgument(args, "submitLabel", "Submit"),
                    ["fields"] = fields
                };

                var names = string.Join(", ", fields.OfType<JObject>().Select(f =>
                    (f.Value<string>("label") ?? f.Value<string>("name")) + (f.Value<bool?>("required") == true ? " (required)" : "")));
                return Task.FromResult(CardResult(
                    "Form: " + title + ". Fields: " + names + ".\nA form card with these fields is shown to the user, so keep your reply to one short sentence. " +
                    "Add that if the form does not appear, they can give the values here. " +
                    "If they answer in chat, ask again for any required field they skip, then call submit_form with formId '" + data.Value<string>("formId") +
                    "' and valuesJson, a JSON object keyed by each field's name. If their message already contains a confirmation ID, the card recorded it, so just acknowledge it.", data));
            },
            annotations: a => { a["readOnlyHint"] = true; },
            title: "Show form card",
            uiResourceUri: FormUri);

        handler.AddTool("submit_form",
            "Records the values the user submitted. Call it only after the user has given the values in chat. " +
            "Do not call it if their message already contains a confirmation ID. The form card also calls this when its button is used.",
            schema: s => s
                .String("formId", "Identifier of the form", required: true)
                .String("valuesJson", "The submitted values as a JSON object keyed by field name", required: true),
            handler: (args, ct) =>
            {
                var formId = RequireArgument(args, "formId");
                JObject values;
                try { values = JObject.Parse(RequireArgument(args, "valuesJson")); }
                catch (JsonException) { throw new ArgumentException("'valuesJson' must be a JSON object"); }

                // TODO: Persist the values or forward them to your system.
                var data = new JObject
                {
                    ["formId"] = formId,
                    ["values"] = values,
                    ["confirmationId"] = "FRM-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(),
                    ["recordedAt"] = DateTime.UtcNow.ToString("o")
                };

                return Task.FromResult(CardResult("Form " + formId + " submitted.", data));
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = false; a["idempotentHint"] = true; },
            title: "Submit form",
            uiResourceUri: FormUri,
            appOnly: !AppToolsVisibleToModel);

        handler.AddTool("show_table",
            "Show a table card with a short preview of the rows. The user can expand it to search, sort, and see every row, " +
            "and optionally select rows to send back. Use for lists of more than a few records.",
            schema: s => s
                .String("title", "Table heading")
                .Array("columns", "The columns, in display order", ObjectSchema(new JObject
                {
                    ["key"] = StringProp("Property name in each row"),
                    ["label"] = StringProp("Column heading")
                }), required: true)
                .Array("rows", "The rows. Each row is an object keyed by column key.",
                    new JObject { ["type"] = "object", ["additionalProperties"] = true }, required: true)
                .Boolean("selectable", "Let the user select rows and send them back")
                .Integer("pageSize", "Rows shown in the inline preview before the user expands the table. Defaults to 5.", defaultValue: 5),
            handler: (args, ct) =>
            {
                var columns = GetArray(args, "columns");
                var rows = GetArray(args, "rows");
                if (columns.Count == 0) throw new ArgumentException("'columns' must contain at least one column");
                if (rows.Count > MaxTableRows) throw new ArgumentException("'rows' is limited to " + MaxTableRows + " rows");

                var data = new JObject
                {
                    ["title"] = GetArgument(args, "title", "Results"),
                    ["columns"] = columns,
                    ["rows"] = rows,
                    ["selectable"] = GetBool(args, "selectable", false),
                    ["pageSize"] = Math.Max(1, Math.Min(100, args.Value<int?>("pageSize") ?? 5))
                };

                return Task.FromResult(CardResult(TableAsMarkdown(data), data));
            },
            annotations: a => { a["readOnlyHint"] = true; },
            title: "Show table card",
            uiResourceUri: TableUri);

        handler.AddTool("show_status",
            "Show a status card with a progress bar and step list for a long-running job. " +
            "The user can refresh it from the card, or ask you for an update.",
            schema: s => s
                .String("title", "What is running", required: true)
                .String("status", "Current state", required: true, enumValues: new[] { "queued", "running", "succeeded", "failed", "warning" })
                .Integer("percentComplete", "Progress from 0 to 100")
                .String("message", "Short status message")
                .String("statusId", "Identifier used to refresh this card")
                .Array("steps", "Steps of the job", ObjectSchema(new JObject
                {
                    ["label"] = StringProp("Step name"),
                    ["state"] = StringProp("pending, running, done, or failed")
                }), required: false)
                .Boolean("refreshable", "Show a Refresh button"),
            handler: (args, ct) =>
            {
                var title = RequireArgument(args, "title");
                var status = RequireArgument(args, "status");
                var percent = Math.Max(0, Math.Min(100, args.Value<int?>("percentComplete") ?? 0));

                var data = new JObject
                {
                    ["title"] = title,
                    ["status"] = status,
                    ["percentComplete"] = percent,
                    ["message"] = GetArgument(args, "message", ""),
                    ["statusId"] = GetArgument(args, "statusId", ""),
                    ["steps"] = GetArray(args, "steps"),
                    ["refreshable"] = GetBool(args, "refreshable", false)
                };

                var steps = string.Join("; ", data["steps"].OfType<JObject>().Select(s => s.Value<string>("label") + " (" + (s.Value<string>("state") ?? "pending") + ")"));
                var text = title + ": " + status + ", " + percent + "% complete." +
                    (string.IsNullOrWhiteSpace(data.Value<string>("message")) ? "" : " " + data.Value<string>("message")) +
                    (steps.Length == 0 ? "" : " Steps: " + steps + ".") +
                    (data.Value<bool>("refreshable") && !string.IsNullOrWhiteSpace(data.Value<string>("statusId"))
                        ? " To update it, call refresh_status with statusId '" + data.Value<string>("statusId") + "'." : "");
                return Task.FromResult(CardResult(text, data));
            },
            annotations: a => { a["readOnlyHint"] = true; },
            title: "Show status card",
            uiResourceUri: StatusUri);

        handler.AddTool("refresh_status",
            "Returns the latest status of a job. Call it when the user asks for an update on a status card's job. " +
            "The status card also calls this when its Refresh button is used.",
            schema: s => s
                .String("statusId", "Identifier of the job", required: true)
                .Integer("percentComplete", "Progress the card is currently showing"),
            handler: (args, ct) =>
            {
                var statusId = RequireArgument(args, "statusId");

                // TODO: Look up the job in your system. This demo advances progress by 25 per refresh.
                var percent = Math.Min(100, (args.Value<int?>("percentComplete") ?? 0) + 25);
                var data = new JObject
                {
                    ["statusId"] = statusId,
                    ["status"] = percent >= 100 ? "succeeded" : "running",
                    ["percentComplete"] = percent,
                    ["message"] = percent >= 100 ? "Completed." : "In progress.",
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                return Task.FromResult(CardResult("Job " + statusId + " is " + data.Value<string>("status") + " at " + percent + "%.", data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; },
            title: "Refresh status",
            uiResourceUri: StatusUri,
            appOnly: !AppToolsVisibleToModel);
    }

    // ── Card Helpers ────────────────────────────────────────────────────

    private const int MaxTableRows = 500;

    /// <summary>Tool result with a text fallback for the model and plain-text hosts, and data for the card.</summary>
    private static JObject CardResult(string fallbackText, JObject data)
    {
        return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(fallbackText) }, data);
    }

    /// <summary>
    /// Read an array argument. Accepts a JSON array or a string holding one, because some
    /// orchestrators pass array arguments as strings.
    /// </summary>
    private static JArray GetArray(JObject args, string name)
    {
        var token = args?[name];
        if (token is JArray array) return array;
        if (token != null && token.Type == JTokenType.String)
        {
            try { return JArray.Parse(token.ToString()); }
            catch (JsonException) { throw new ArgumentException("'" + name + "' must be an array"); }
        }
        return new JArray();
    }

    private static bool GetBool(JObject args, string name, bool defaultValue)
    {
        var raw = args?[name]?.ToString();
        bool parsed;
        return bool.TryParse(raw, out parsed) ? parsed : defaultValue;
    }

    /// <summary>Schema for an array item object. Pass each property as a name and a StringProp.</summary>
    private static JObject ObjectSchema(JObject properties)
    {
        return new JObject { ["type"] = "object", ["properties"] = properties };
    }

    private static JObject StringProp(string description)
    {
        return new JObject { ["type"] = "string", ["description"] = description };
    }

    private static JObject FormFieldSchema()
    {
        var props = new JObject
        {
            ["name"] = StringProp("Key used for this field in the submitted values"),
            ["label"] = StringProp("Label shown to the user"),
            ["type"] = StringProp("text, email, number, date, textarea, choice, or checkbox"),
            ["placeholder"] = StringProp("Hint shown in an empty field"),
            ["defaultValue"] = StringProp("Initial value. Use true for a checked checkbox."),
            ["required"] = new JObject { ["type"] = "boolean", ["description"] = "Whether the user must fill the field" },
            ["options"] = new JObject
            {
                ["type"] = "array",
                ["description"] = "Choices for a choice field",
                ["items"] = new JObject { ["type"] = "string" }
            }
        };

        var schema = ObjectSchema(props);
        schema["required"] = new JArray("name", "label");
        return schema;
    }

    private static string TableAsMarkdown(JObject data)
    {
        var columns = data["columns"].OfType<JObject>().ToList();
        var rows = data["rows"].OfType<JObject>().ToList();
        var sb = new StringBuilder();
        sb.Append(data.Value<string>("title")).Append(" (").Append(rows.Count).Append(" rows)\n\n");
        sb.Append("| ").Append(string.Join(" | ", columns.Select(c => MarkdownCell(c.Value<string>("label") ?? c.Value<string>("key"))))).Append(" |\n");
        sb.Append("|").Append(string.Concat(columns.Select(c => " --- |"))).Append("\n");
        foreach (var row in rows.Take(25))
            sb.Append("| ").Append(string.Join(" | ", columns.Select(c => MarkdownCell(row[c.Value<string>("key") ?? ""]?.ToString())))).Append(" |\n");
        if (rows.Count > 25) sb.Append("\n").Append(rows.Count - 25).Append(" more rows not shown.");
        return sb.ToString();
    }

    private static string MarkdownCell(string value)
    {
        return (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a request to an external API.
    /// Forwards the connector's Authorization header and handles response parsing.
    /// </summary>
    private async Task<JObject> SendExternalRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
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
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
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
        catch
        {
            // Suppress telemetry errors
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2026-07-28, dual-era                                   ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  This handler serves BOTH eras from one endpoint:                          ║
// ║    modern (2026-07-28)  server/discover, per-request _meta, resultType,    ║
// ║                         ttlMs/cacheScope, MRTR input_required              ║
// ║    legacy (2025-11-25-) initialize, ping, logging/setLevel,                ║
// ║                         resources/subscribe                                ║
// ║                                                                            ║
// ║  The era is chosen per request. Copilot Studio is a legacy client and is   ║
// ║  answered exactly as it was before this revision.                          ║
// ║                                                                            ║
// ║  Out of reach for a request/response connector (no open stream):           ║
// ║   - subscriptions/listen and every server-to-client notification           ║
// ║   - the io.modelcontextprotocol/tasks extension (needs durable state)      ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Protocol Constants ───────────────────────────────────────────────────────

/// <summary>Reserved _meta keys, extension identifiers, and URI schemes defined by MCP.</summary>
public static class McpKeys
{
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string LogLevel = "io.modelcontextprotocol/logLevel";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    // Official extensions, advertised through capabilities.extensions.
    public const string ExtensionTasks = "io.modelcontextprotocol/tasks";
    public const string ExtensionUi = "io.modelcontextprotocol/ui";

    /// <summary>MIME type that marks a resource as an MCP Apps view.</summary>
    public const string UiMimeType = "text/html;profile=mcp-app";

    // Agent Skills over MCP.
    public const string SkillScheme = "skill://";
    public const string SkillIndexUri = "skill://index.json";
}

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported by server/discover and in each result's _meta.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>Optional icons array, per the icons field in the MCP base spec.</summary>
    public JArray Icons { get; set; }
}

/// <summary>Capabilities advertised by server/discover and initialize.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Completions { get; set; }

    /// <summary>Deprecated in 2026-07-28. Advertised to legacy clients only.</summary>
    public bool Logging { get; set; }

    /// <summary>
    /// Optional extension map keyed by extension identifier, for example
    /// { "io.modelcontextprotocol/ui": { "mimeTypes": [ "text/html;profile=mcp-app" ] } }.
    /// </summary>
    public JObject Extensions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();

    /// <summary>Preferred revision. Listed first by server/discover.</summary>
    public string ProtocolVersion { get; set; } = "2026-07-28";

    /// <summary>Every revision this server accepts. Must contain ProtocolVersion.</summary>
    public List<string> SupportedProtocolVersions { get; set; } =
        new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" };

    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();

    /// <summary>Natural-language guidance for the model, returned by server/discover.</summary>
    public string Instructions { get; set; }

    // ── Cache hints (2026-07-28 CacheableResult) ──────────────────────────

    /// <summary>Freshness hint in milliseconds for tools/prompts/resources list results.</summary>
    public int ListCacheTtlMs { get; set; } = 300000;

    /// <summary>"public" when list results are identical for every caller, otherwise "private".</summary>
    public string ListCacheScope { get; set; } = "public";

    public int ResourceCacheTtlMs { get; set; } = 60000;

    /// <summary>"private" by default: resource content commonly varies by authenticated user.</summary>
    public string ResourceCacheScope { get; set; } = "private";

    public int DiscoverCacheTtlMs { get; set; } = 3600000;

    /// <summary>
    /// Compare the Mcp-Method and Mcp-Name headers against the request body and reject a
    /// mismatch with HeaderMismatch. Absent headers are ignored, because the Power Platform
    /// gateway makes no guarantee that it forwards them.
    /// </summary>
    public bool ValidateRequestHeaders { get; set; } = true;
}

// ── Transport Headers ────────────────────────────────────────────────────────

/// <summary>
/// The Streamable HTTP headers the framework cares about. Captured in the connector
/// entry point and handed to HandleAsync so the framework never touches ScriptBase.
/// </summary>
public class McpTransportHeaders
{
    public string ProtocolVersion { get; set; }
    public string Method { get; set; }
    public string Name { get; set; }

    public static McpTransportHeaders FromRequest(HttpRequestMessage request)
    {
        var headers = new McpTransportHeaders();
        if (request == null) return headers;

        headers.ProtocolVersion = FirstOrNull(request, "MCP-Protocol-Version");
        headers.Method = FirstOrNull(request, "Mcp-Method");
        headers.Name = FirstOrNull(request, "Mcp-Name");
        return headers;
    }

    private static string FirstOrNull(HttpRequestMessage request, string headerName)
    {
        IEnumerable<string> values;
        if (!request.Headers.TryGetValues(headerName, out values)) return null;
        foreach (var value in values) return value;
        return null;
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>JSON-RPC 2.0 and MCP error codes.</summary>
public enum McpErrorCode
{
    // JSON-RPC 2.0.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Legacy implementation-defined range (-32000 to -32019). 2026-07-28 forbids
    // allocating new codes here; this one predates the policy.
    RequestTimeout = -32000,

    // Reserved for the MCP specification (-32020 to -32099).
    HeaderMismatch = -32020,
    MissingRequiredClientCapability = -32021,
    UnsupportedProtocolVersion = -32022
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>
/// Fluent builder for the JSON Schema objects used in tool inputSchema and outputSchema.
///
/// Deliberately narrower than JSON Schema 2020-12 permits. Copilot Studio drops any tool
/// whose schema contains a reference type, and truncates schemas that use multi-type
/// arrays, so this builder never emits $ref, $defs, or "type": [...]. Nested objects are
/// inlined instead. If you hand-write a schema, keep to the same rules — and if you add
/// numeric bounds, note that Copilot Studio expects the draft-04 boolean form of
/// exclusiveMinimum, not the 2020-12 numeric form.
/// </summary>
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
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
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
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

// ── Request Context ──────────────────────────────────────────────────────────

/// <summary>
/// Everything the framework knows about one request. This replaces the connection
/// state that 2026-07-28 removed: era, negotiated version, client identity, and the
/// answers a client attaches when it retries a multi round-trip request.
/// </summary>
public class McpRequestContext
{
    public JToken Id { get; set; }
    public string Method { get; set; }

    /// <summary>True when the caller speaks 2026-07-28 or later.</summary>
    public bool IsModern { get; set; }

    public string ProtocolVersion { get; set; }
    public JObject ClientInfo { get; set; }
    public JObject ClientCapabilities { get; set; }
    public string LogLevel { get; set; }

    /// <summary>Answers supplied on an MRTR retry, keyed by input request name.</summary>
    public JObject InputResponses { get; set; }

    /// <summary>Opaque state handed back with a previous input_required result.</summary>
    public string RequestState { get; set; }

    /// <summary>True when the client declared the given extension identifier.</summary>
    public bool SupportsExtension(string extensionId)
    {
        var extensions = ClientCapabilities?["extensions"] as JObject;
        return extensions != null && extensions[extensionId] != null;
    }
}

// ── Agent Skills ─────────────────────────────────────────────────────────────

/// <summary>One resource file bundled with a skill.</summary>
internal class McpSkillResource
{
    public string Path { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public Func<string> Content { get; set; }
}

/// <summary>Fluent collector for a skill's sibling resource files.</summary>
public class McpSkillResourceBuilder
{
    internal readonly List<McpSkillResource> Resources = new List<McpSkillResource>();

    /// <summary>
    /// Add a file served alongside SKILL.md. Use the relative path the skill body
    /// references, for example "references/policy.md" or "assets/template.md".
    /// </summary>
    public McpSkillResourceBuilder Resource(string path, string description, Func<string> content, string mimeType = "text/markdown")
    {
        Resources.Add(new McpSkillResource
        {
            Path = path,
            Description = description,
            MimeType = mimeType,
            Content = content
        });
        return this;
    }
}

internal class McpSkillDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public string License { get; set; }
    public string Compatibility { get; set; }
    public JObject Metadata { get; set; }
    public List<McpSkillResource> Resources { get; set; } = new List<McpSkillResource>();

    public string SkillUri { get { return McpKeys.SkillScheme + Name + "/SKILL.md"; } }

    public string ResourceUri(string path) { return McpKeys.SkillScheme + Name + "/" + path; }

    /// <summary>Render SKILL.md: YAML frontmatter followed by the markdown body.</summary>
    public string RenderSkillMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name).Append('\n');
        sb.Append("description: ").Append(EscapeYaml(Description)).Append('\n');

        if (!string.IsNullOrWhiteSpace(License))
            sb.Append("license: ").Append(EscapeYaml(License)).Append('\n');

        if (!string.IsNullOrWhiteSpace(Compatibility))
            sb.Append("compatibility: ").Append(EscapeYaml(Compatibility)).Append('\n');

        if (Metadata != null && Metadata.Count > 0)
        {
            sb.Append("metadata:\n");
            foreach (var prop in Metadata.Properties())
                sb.Append("  ").Append(prop.Name).Append(": ").Append(EscapeYaml(prop.Value?.ToString())).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append(Instructions ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>Quote any scalar that would otherwise break the frontmatter block.</summary>
    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var flattened = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

// ── Internal Tool Registration ───────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── Internal Resource Registration ───────────────────────────────────────────

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── Internal Prompt Registration ─────────────────────────────────────────────

/// <summary>Describes a single prompt argument.</summary>
public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;

    // Dictionaries give O(1) dispatch. The parallel lists preserve registration order,
    // which 2026-07-28 asks for so clients can cache list results reliably.
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly List<McpToolDefinition> _toolOrder;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceDefinition> _resourceOrder;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;
    private readonly List<McpPromptDefinition> _promptOrder;
    private readonly List<McpSkillDefinition> _skills;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _toolOrder = new List<McpToolDefinition>();
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceOrder = new List<McpResourceDefinition>();
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
        _promptOrder = new List<McpPromptDefinition>();
        _skills = new List<McpSkillDefinition>();
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null,
        string uiResourceUri = null,
        bool appOnly = false)
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

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Meta = BuildToolUiMeta(uiResourceUri, appOnly),
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        McpToolDefinition previous;
        if (_tools.TryGetValue(name, out previous)) _toolOrder.Remove(previous);

        _tools[name] = definition;
        _toolOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Build the tool _meta that links a tool to its MCP Apps view. appOnly hides the tool
    /// from the model so only the view can call it (for example a card's submit action).
    /// The flat "ui/resourceUri" key is the pre-GA form some hosts still read.
    /// </summary>
    private static JObject BuildToolUiMeta(string uiResourceUri, bool appOnly)
    {
        if (string.IsNullOrWhiteSpace(uiResourceUri)) return null;

        var ui = new JObject { ["resourceUri"] = uiResourceUri };
        if (appOnly) ui["visibility"] = new JArray("app");

        return new JObject { ["ui"] = ui, ["ui/resourceUri"] = uiResourceUri };
    }

    // ── Resource Registration ─────────────────────────────────────────────

    /// <summary>
    /// Register an MCP Apps view. The HTML is served from resources/read with the
    /// text/html;profile=mcp-app MIME type, and uiMeta (csp, permissions, prefersBorder)
    /// is attached under _meta.ui. A view with no csp gets the host's restrictive default:
    /// no network access and no external scripts, which is what these views are written for.
    /// </summary>
    public McpRequestHandler AddUiResource(
        string uri,
        string name,
        string description,
        Func<string> html,
        Action<JObject> uiMeta = null)
    {
        var ui = new JObject();
        uiMeta?.Invoke(ui);

        return AddResource(
            uri, name, description,
            handler: ct => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = McpKeys.UiMimeType,
                    ["text"] = html()
                }
            }),
            mimeType: McpKeys.UiMimeType,
            meta: m => m["ui"] = ui);
    }

    /// <summary>
    /// Register a static resource. The handler returns the resource contents
    /// as a JArray of {uri, text, mimeType} or {uri, blob, mimeType} objects.
    /// </summary>
    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null,
        Action<JObject> meta = null)
    {
        JObject metaObject = null;
        if (meta != null)
        {
            metaObject = new JObject();
            meta(metaObject);
        }

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        var definition = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Meta = metaObject,
            Handler = handler
        };

        McpResourceDefinition previous;
        if (_resources.TryGetValue(uri, out previous)) _resourceOrder.Remove(previous);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Register a resource template. The handler receives the resolved URI
    /// and returns the resource contents as a JArray.
    /// </summary>
    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    /// <summary>
    /// Register a prompt. The handler receives the argument values as a JObject
    /// and returns a JArray of message objects ({role, content: {type, text}}).
    ///
    /// Copilot Studio does not consume MCP prompts. Register them for clients that do.
    /// </summary>
    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        var definition = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        McpPromptDefinition previous;
        if (_prompts.TryGetValue(name, out previous)) _promptOrder.Remove(previous);

        _prompts[name] = definition;
        _promptOrder.Add(definition);

        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP.
    ///
    /// The skill is exposed as ordinary MCP resources under the skill:// scheme, and a
    /// skill://index.json discovery document is generated from every registered skill.
    /// Agent Framework (.NET UseMcpSkills, Python MCPSkillsSource) and Microsoft Foundry
    /// Toolbox read that index and fetch SKILL.md on demand. Only the skill-md
    /// distribution type is produced; archives are not supported.
    /// </summary>
    /// <param name="name">Lowercase letters, digits, and single hyphens. Max 64 characters.</param>
    /// <param name="description">What the skill does and when to use it. Max 1024 characters.</param>
    /// <param name="instructions">The markdown body of SKILL.md. Keep it under 500 lines.</param>
    /// <param name="resources">Sibling files the instructions reference.</param>
    public McpRequestHandler AddSkill(
        string name,
        string description,
        string instructions,
        Action<McpSkillResourceBuilder> resources = null,
        string license = null,
        string compatibility = null,
        JObject metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name is required.");
        if (name.Length > 64)
            throw new ArgumentException($"Skill name '{name}' exceeds 64 characters.");
        if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException($"Skill name '{name}' must use lowercase letters, digits, and single hyphens, with no leading or trailing hyphen.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Skill '{name}' requires a description.");
        if (description.Length > 1024)
            throw new ArgumentException($"Skill '{name}' description exceeds 1024 characters.");
        if (!string.IsNullOrEmpty(compatibility) && compatibility.Length > 500)
            throw new ArgumentException($"Skill '{name}' compatibility exceeds 500 characters.");

        var resourceBuilder = new McpSkillResourceBuilder();
        resources?.Invoke(resourceBuilder);

        var skill = new McpSkillDefinition
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            License = license,
            Compatibility = compatibility,
            Metadata = metadata,
            Resources = resourceBuilder.Resources
        };

        _skills.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _skills.Add(skill);

        // The index handler runs at request time, so it always sees every skill
        // registered so far regardless of registration order.
        if (!_resources.ContainsKey(McpKeys.SkillIndexUri))
        {
            AddResource(McpKeys.SkillIndexUri, "Agent Skills index",
                "Discovery document listing the Agent Skills this server publishes.",
                handler: (ct) => Task.FromResult(BuildSkillIndexContents()));
        }

        AddResource(skill.SkillUri, name, description,
            handler: (ct) => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = skill.SkillUri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = skill.RenderSkillMarkdown()
                }
            }),
            mimeType: "text/markdown");

        foreach (var resource in skill.Resources)
        {
            var uri = skill.ResourceUri(resource.Path);
            var captured = resource;

            AddResource(uri, resource.Path, resource.Description ?? resource.Path,
                handler: (ct) => Task.FromResult(new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = captured.MimeType,
                        ["text"] = captured.Content != null ? captured.Content() : string.Empty
                    }
                }),
                mimeType: resource.MimeType);
        }

        return this;
    }

    /// <summary>Build the skill://index.json contents from every registered skill.</summary>
    private JArray BuildSkillIndexContents()
    {
        var entries = new JArray();

        foreach (var skill in _skills)
        {
            var entry = new JObject
            {
                ["type"] = "skill-md",
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["uri"] = skill.SkillUri
            };

            if (skill.Resources.Count > 0)
            {
                var resourceArray = new JArray();
                foreach (var resource in skill.Resources)
                {
                    resourceArray.Add(new JObject
                    {
                        ["path"] = resource.Path,
                        ["uri"] = skill.ResourceUri(resource.Path),
                        ["description"] = resource.Description,
                        ["mimeType"] = resource.MimeType
                    });
                }
                entry["resources"] = resourceArray;
            }

            entries.Add(entry);
        }

        var index = new JObject { ["skills"] = entries };

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = index.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
    public Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        return HandleAsync(body, null, cancellationToken);
    }

    /// <summary>
    /// Process a request, using the Streamable HTTP headers to detect the protocol era
    /// and to check Mcp-Method / Mcp-Name against the body.
    /// </summary>
    public async Task<string> HandleAsync(string body, McpTransportHeaders headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var ctx = BuildContext(request, headers);

        Log("McpRequestReceived", new
        {
            Method = ctx.Method,
            HasId = ctx.Id != null,
            Era = ctx.IsModern ? "modern" : "legacy",
            ProtocolVersion = ctx.ProtocolVersion
        });

        // 2026-07-28 negotiates per request rather than once per session.
        if (ctx.IsModern && !IsSupportedVersion(ctx.ProtocolVersion))
        {
            Log("McpUnsupportedProtocolVersion", new { Requested = ctx.ProtocolVersion });
            return SerializeError(ctx.Id, McpErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol version",
                new JObject
                {
                    ["supported"] = new JArray(_options.SupportedProtocolVersions),
                    ["requested"] = ctx.ProtocolVersion
                });
        }

        var headerError = ValidateHeaders(ctx, request, headers);
        if (headerError != null) return headerError;

        try
        {
            switch (ctx.Method)
            {
                // Discovery — servers MUST implement this in 2026-07-28.
                case "server/discover":
                    return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize":
                    return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                case "notifications/roots/list_changed":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and
                // resource subscriptions moved to the subscriptions/listen stream,
                // which a request/response connector cannot hold open.
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(ctx);

                case "tools/call":
                    return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Resources
                case "resources/list":
                    return HandleResourcesList(ctx);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(ctx);

                case "resources/read":
                    return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Prompts
                case "prompts/list":
                    return HandlePromptsList(ctx);

                case "prompts/get":
                    return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Completions
                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = ctx.Method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(ctx.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = ctx.Method, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    // ── Era Detection ────────────────────────────────────────────────────

    /// <summary>
    /// Decide which protocol era this request belongs to and capture its metadata.
    ///
    /// The spec picks the era from how the client opens the conversation, so an
    /// initialize-style request is always legacy no matter what else it carries — a client
    /// mid-upgrade might send MCP-Protocol-Version while still using the handshake, and
    /// rejecting its initialize would break the connection for no benefit.
    ///
    /// Otherwise a request is modern when it carries a protocol version in _meta or in the
    /// MCP-Protocol-Version header, or when it calls server/discover. Everything else,
    /// Copilot Studio included, is served under legacy semantics.
    /// </summary>
    private McpRequestContext BuildContext(JObject request, McpTransportHeaders headers)
    {
        var paramsObj = request["params"] as JObject;
        var meta = paramsObj?["_meta"] as JObject;
        var method = request.Value<string>("method") ?? string.Empty;

        var metaVersion = meta?[McpKeys.ProtocolVersion]?.ToString();
        var version = !string.IsNullOrWhiteSpace(metaVersion) ? metaVersion : headers?.ProtocolVersion;

        var opensLegacy = method == "initialize"
            || method == "initialized"
            || method == "notifications/initialized";

        var isModern = !opensLegacy
            && (!string.IsNullOrWhiteSpace(version) || method == "server/discover");

        return new McpRequestContext
        {
            Id = request["id"],
            Method = method,
            IsModern = isModern,
            ProtocolVersion = !string.IsNullOrWhiteSpace(version) ? version : _options.ProtocolVersion,
            ClientInfo = meta?[McpKeys.ClientInfo] as JObject,
            ClientCapabilities = meta?[McpKeys.ClientCapabilities] as JObject,
            LogLevel = meta?[McpKeys.LogLevel]?.ToString(),
            InputResponses = paramsObj?["inputResponses"] as JObject,
            RequestState = paramsObj?["requestState"]?.ToString()
        };
    }

    private bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;

        var supported = _options.SupportedProtocolVersions;
        if (supported == null || supported.Count == 0)
            return string.Equals(version, _options.ProtocolVersion, StringComparison.Ordinal);

        foreach (var candidate in supported)
            if (string.Equals(candidate, version, StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 2026-07-28 requires Mcp-Method and Mcp-Name on Streamable HTTP so gateways can route
    /// without parsing the body. A header that disagrees with the body is a HeaderMismatch.
    /// An absent header is ignored: the Power Platform gateway makes no guarantee that it
    /// forwards custom headers, and failing closed would break every deployment.
    /// </summary>
    private string ValidateHeaders(McpRequestContext ctx, JObject request, McpTransportHeaders headers)
    {
        if (!ctx.IsModern || !_options.ValidateRequestHeaders || headers == null) return null;

        if (!string.IsNullOrWhiteSpace(headers.Method) &&
            !string.Equals(headers.Method, ctx.Method, StringComparison.Ordinal))
        {
            Log("McpHeaderMismatch", new { Header = headers.Method, Body = ctx.Method });
            return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                "Mcp-Method header does not match the request method",
                new JObject { ["header"] = headers.Method, ["body"] = ctx.Method });
        }

        if (!string.IsNullOrWhiteSpace(headers.Name))
        {
            var bodyName = (request["params"] as JObject)?.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(bodyName) &&
                !string.Equals(headers.Name, bodyName, StringComparison.Ordinal))
            {
                Log("McpHeaderMismatch", new { Header = headers.Name, Body = bodyName });
                return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                    "Mcp-Name header does not match the request target",
                    new JObject { ["header"] = headers.Name, ["body"] = bodyName });
            }
        }

        return null;
    }

    /// <summary>Report a method that 2026-07-28 removed, rather than a bare not-found.</summary>
    private string MethodRemoved(McpRequestContext ctx, string method)
    {
        Log("McpMethodRemoved", new { Method = method, ProtocolVersion = ctx.ProtocolVersion });
        return SerializeError(ctx.Id, McpErrorCode.MethodNotFound,
            $"'{method}' was removed in MCP 2026-07-28", method);
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    /// <summary>
    /// server/discover — mandatory in 2026-07-28. Reports supported versions,
    /// capabilities, and identity in a single round trip.
    /// </summary>
    private string HandleDiscover(McpRequestContext ctx)
    {
        var result = new JObject
        {
            ["supportedVersions"] = new JArray(_options.SupportedProtocolVersions),
            ["capabilities"] = BuildCapabilities(isModern: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpDiscovered", new
        {
            Server = _options.ServerInfo.Name,
            Versions = _options.SupportedProtocolVersions.Count
        });

        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern)
            capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };

        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0)
            serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion)
            ? clientProtocolVersion
            : "2025-11-25";

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = negotiated
        });

        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
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
            if (tool.Meta != null && tool.Meta.Count > 0)
                toolObj["_meta"] = tool.Meta;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resourceOrder)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            if (res.Meta != null && res.Meta.Count > 0)
                obj["_meta"] = res.Meta;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = resourcesArray }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = templatesArray }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        // 1. Try exact match on registered static resources
        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);

                // The host reads csp and display hints from the content item, not the listing.
                if (resource.Meta != null)
                {
                    foreach (var item in contents.OfType<JObject>())
                        if (item["_meta"] == null) item["_meta"] = resource.Meta.DeepClone();
                }

                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        // 2. Try matching against registered resource templates
        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        // 2026-07-28 moved resource-not-found from -32002 to -32602 (Invalid Params).
        return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    /// <summary>
    /// Simple URI template matcher. Checks if a concrete URI matches a template
    /// with {param} placeholders (e.g., "data://records/{id}" matches "data://records/123").
    /// </summary>
    private static bool MatchesUriTemplate(string template, string uri)
    {
        // Split both on '/' and compare segments
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue; // wildcard
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract named parameters from a URI given a template pattern.
    /// E.g., template "data://records/{id}" with uri "data://records/123" returns { "id": "123" }.
    /// </summary>
    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _promptOrder)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = promptsArray }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName, IsRetry = ctx.InputResponses != null });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            // Multi round-trip request: the tool needs more input before it can finish.
            if (result is JObject marker && marker.Value<bool?>("__mcpInputRequired") == true)
                return SerializeInputRequired(ctx, toolName, marker);

            JObject callResult;

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (jobj["structuredContent"] != null)
                    callResult["structuredContent"] = jobj["structuredContent"];
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
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    /// <summary>
    /// Emit an input_required result. Legacy clients cannot interpret resultType, so they
    /// receive a tool error naming what the tool still needs rather than a silent failure.
    /// </summary>
    private string SerializeInputRequired(McpRequestContext ctx, string toolName, JObject marker)
    {
        var inputRequests = marker["inputRequests"] as JObject ?? new JObject();

        if (!ctx.IsModern)
        {
            var needed = string.Join(", ", inputRequests.Properties().Select(p => p.Name));
            Log("McpInputRequiredUnsupported", new { Tool = toolName, Needed = needed });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    TextContent($"'{toolName}' needs additional input ({needed}). That requires an MCP client on 2026-07-28 or later; supply the values as tool arguments instead.")
                },
                ["isError"] = true
            });
        }

        var result = new JObject { ["inputRequests"] = inputRequests };

        var requestState = marker.Value<string>("requestState");
        if (!string.IsNullOrWhiteSpace(requestState))
            result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });

        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    /// <summary>
    /// Create a resource link. Points the client at a resource it can fetch itself,
    /// instead of embedding the bytes in the tool result.
    /// </summary>
    public static JObject ResourceLinkContent(string uri, string name, string description = null, string mimeType = null)
    {
        var link = new JObject { ["type"] = "resource_link", ["uri"] = uri, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description)) link["description"] = description;
        if (!string.IsNullOrWhiteSpace(mimeType)) link["mimeType"] = mimeType;
        return link;
    }

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ──────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry.
    /// </summary>
    public static JObject InputRequired(JObject inputRequests, string requestState = null)
    {
        var result = new JObject
        {
            ["__mcpInputRequired"] = true,
            ["inputRequests"] = inputRequests ?? new JObject()
        };
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;
        return result;
    }

    /// <summary>Build a single elicitation entry for an InputRequired result.</summary>
    public static JObject ElicitationRequest(string message, Action<McpSchemaBuilder> schemaConfig, string mode = "form")
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        return new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = mode,
                ["message"] = message,
                ["requestedSchema"] = builder.Build()
            }
        };
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(McpRequestContext ctx, JObject result)
    {
        return SerializeSuccess(ctx, result, McpCacheKind.None, "complete");
    }

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache)
    {
        return SerializeSuccess(ctx, result, cache, "complete");
    }

    /// <summary>
    /// Envelope a successful result. Modern callers additionally get resultType, server
    /// identity in _meta, and cache hints. Legacy callers get exactly the shape they got
    /// before 2026-07-28, which is what keeps Copilot Studio working unchanged.
    /// </summary>
    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache, string resultType)
    {
        result = result ?? new JObject();

        if (ctx != null && ctx.IsModern)
        {
            result["resultType"] = resultType;

            var meta = result["_meta"] as JObject ?? new JObject();
            meta[McpKeys.ServerInfo] = BuildServerInfo(includeDetail: false);
            result["_meta"] = meta;

            // Only complete results are cacheable; input_required is interim.
            if (resultType == "complete") ApplyCacheHints(result, cache);
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ctx?.Id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void ApplyCacheHints(JObject result, McpCacheKind cache)
    {
        switch (cache)
        {
            case McpCacheKind.List:
                result["ttlMs"] = Math.Max(0, _options.ListCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;

            case McpCacheKind.Resource:
                result["ttlMs"] = Math.Max(0, _options.ResourceCacheTtlMs);
                result["cacheScope"] = _options.ResourceCacheScope ?? "private";
                break;

            case McpCacheKind.Discover:
                result["ttlMs"] = Math.Max(0, _options.DiscoverCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;
        }
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, JToken data = null)
    {
        var error = new JObject
        {
            ["code"] = (int)code,
            ["message"] = message
        };

        if (data != null && data.Type != JTokenType.Null)
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 3: CARD VIEWS                                                     ║
// ║                                                                            ║
// ║  The HTML for each ui:// resource. Every view is one self-contained        ║
// ║  document: no CDN, no external fonts, no network calls, so it runs under   ║
// ║  the host's default content security policy.                               ║
// ║                                                                            ║
// ║  The views follow Fluent 2 (the Copilot design system): Fluent web tokens  ║
// ║  for color, type, radius, and spacing in light and dark; Fluent-style      ║
// ║  Button, Input, Checkbox, MessageBar, Badge, ProgressBar, Table, and       ║
// ║  Skeleton; and the Copilot MCP app guidance of at most two actions per     ║
// ║  inline card, a short inline preview with the full view in the expanded    ║
// ║  display mode, and explicit loading, disabled, success, and error states.  ║
// ║                                                                            ║
// ║  The views talk to the host with a small JSON-RPC client over postMessage  ║
// ║  (CardViews.Bridge) instead of the @modelcontextprotocol/ext-apps SDK,     ║
// ║  which would have to be loaded from a CDN. The client implements ui/       ║
// ║  initialize, tool-input, tool-result, host-context-changed, size-changed,  ║
// ║  tools/call, ui/message, ui/update-model-context, and ui/open-link.        ║
// ║                                                                            ║
// ║  The strings use single quotes only, because they are C# verbatim strings. ║
// ║  Every value from tool data reaches the page through textContent, never    ║
// ║  innerHTML.                                                                ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

internal static class CardViews
{
    private const string Loading =
        "<div id='root'><div class='card' aria-busy='true' aria-label='Loading'>" +
        "<div class='sk' style='width:40%'></div><div class='sk' style='width:85%'></div><div class='sk' style='width:65%'></div></div></div>";

    public static string Approval() { return Page("Approval", ApprovalCss, Loading, ApprovalJs); }
    public static string Form() { return Page("Form", FormCss, Loading, FormJs); }
    public static string Table() { return Page("Table", TableCss, Loading, TableJs); }
    public static string Status() { return Page("Status", StatusCss, Loading, StatusJs); }

    private static string Page(string title, string css, string body, string script)
    {
        return "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>" +
            "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>" + title + "</title><style>" + BaseCss + css + "</style></head><body>" +
            body + "<script>" + Bridge + script + "</script></body></html>";
    }

    // ── Shared styles ───────────────────────────────────────────────────

    private const string BaseCss = @"
/* Fluent 2 web tokens (light, dark). The host's --color-text-primary, --color-background-primary,
   --color-border-primary and --font-sans win when it provides them, so a card follows its host. */
:root{color-scheme:light dark;
--fg1:var(--color-text-primary,light-dark(#242424,#ffffff));
--fg2:light-dark(#424242,#d6d6d6);
--fg3:light-dark(#616161,#adadad);
--fgd:light-dark(#bdbdbd,#5c5c5c);
--bg1:var(--color-background-primary,light-dark(#ffffff,#292929));
--bg1h:light-dark(#f5f5f5,#3d3d3d);
--bg1p:light-dark(#e0e0e0,#1f1f1f);
--bg3:light-dark(#f5f5f5,#1f1f1f);
--bgs:light-dark(#ebebeb,#333333);
--bg6:light-dark(#e6e6e6,#3d3d3d);
--bgd:light-dark(#f0f0f0,#141414);
--s1:var(--color-border-primary,light-dark(#d1d1d1,#666666));
--s1h:light-dark(#c7c7c7,#757575);
--s2:light-dark(#e0e0e0,#525252);
--sacc:light-dark(#616161,#adadad);
--sd:light-dark(#e0e0e0,#424242);
--brand:light-dark(#0f6cbd,#115ea3);
--brandh:light-dark(#115ea3,#0f6cbd);
--brandp:#0c3b5e;
--brandfg:light-dark(#0f6cbd,#479ef5);
--focus:light-dark(#000000,#ffffff);
--red-fg:light-dark(#b10e1c,#dc626d);--red-bg:light-dark(#fdf3f4,#3f1011);--red-bd:light-dark(#eeacb2,#751d1f);
--grn-fg:light-dark(#0e700e,#54b054);--grn-bg:light-dark(#f1faf1,#052505);--grn-bd:light-dark(#9fd89f,#0e4d0e);
--org-fg:light-dark(#bc4b09,#f98845);--org-bg:light-dark(#fdf6f3,#411200);--org-bd:light-dark(#f4bfab,#7a3405);
--r-m:4px;--r-xl:8px;
--font:var(--font-sans,'Segoe UI','Segoe UI Web (West European)',-apple-system,BlinkMacSystemFont,Roboto,'Helvetica Neue',sans-serif);}
*{box-sizing:border-box}
html,body{margin:0}
body{padding:2px;background:transparent;color:var(--fg1);font-family:var(--font);font-size:14px;line-height:20px;font-weight:400;-webkit-font-smoothing:antialiased}
.card{background:var(--bg1);border:1px solid var(--s2);border-radius:var(--r-xl);padding:24px;max-width:720px}
html[data-mode=fullscreen] body{padding:0}
html[data-mode=fullscreen] .card{max-width:none;min-height:100vh;border:0;border-radius:0;padding:24px}
h1{margin:0;font-size:16px;line-height:22px;font-weight:600}
p{margin:0}
.stack>*+*{margin-top:12px}
.stack>div:empty{display:none}
.cap{font-size:12px;line-height:16px;color:var(--fg3)}
.row{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.row.between{justify-content:space-between}
.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
.btn{display:inline-flex;align-items:center;justify-content:center;gap:6px;min-width:96px;height:32px;padding:0 12px;font:inherit;font-weight:600;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-radius:var(--r-m);cursor:pointer}
.btn:hover:not(:disabled){background:var(--bg1h);border-color:var(--s1h)}
.btn:active:not(:disabled){background:var(--bg1p)}
.btn.primary{color:#ffffff;background:var(--brand);border-color:transparent}
.btn.primary:hover:not(:disabled){background:var(--brandh);border-color:transparent}
.btn.primary:active:not(:disabled){background:var(--brandp)}
.btn:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
.btn.sm{height:24px;min-width:0;padding:0 8px;font-size:12px}
.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px}
input,select,textarea{font:inherit;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-bottom-color:var(--sacc);border-radius:var(--r-m);padding:0 10px;height:32px;width:100%}
textarea{height:auto;min-height:60px;padding:6px 10px;resize:vertical}
input:hover:not(:disabled),select:hover:not(:disabled),textarea:hover:not(:disabled){border-color:var(--s1h);border-bottom-color:var(--sacc)}
input:focus,select:focus,textarea:focus{outline:none;border-bottom-color:var(--brandfg);box-shadow:0 1px 0 0 var(--brandfg)}
input:disabled,select:disabled,textarea:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
input::placeholder,textarea::placeholder{color:var(--fg3)}
input[type=checkbox]{width:16px;height:16px;padding:0;accent-color:var(--brand);cursor:pointer}
[aria-invalid=true]{border-color:var(--red-fg)}
.btn:focus-visible,th button:focus-visible,input[type=checkbox]:focus-visible{outline:2px solid var(--focus);outline-offset:1px}
label.lbl{display:block;margin-bottom:4px}
.req{color:var(--red-fg)}
.ic{flex:none;width:16px;height:16px}
.ic .mk{fill:none;stroke:var(--bg1);stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.ic .ln{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.rot{animation:rot 1s linear infinite}
.spin{width:16px;height:16px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;animation:rot .8s linear infinite}
@keyframes rot{to{transform:rotate(360deg)}}
.msg{display:flex;gap:8px;align-items:flex-start;padding:8px 12px;border:1px solid var(--s1);border-radius:var(--r-m);background:var(--bg3)}
.msg .body{flex:1;min-width:0}
.msg .t{font-weight:600}
.msg.error{background:var(--red-bg);border-color:var(--red-bd)}.msg.error>.ic{color:var(--red-fg)}
.msg.success{background:var(--grn-bg);border-color:var(--grn-bd)}.msg.success>.ic{color:var(--grn-fg)}
.msg.warning{background:var(--org-bg);border-color:var(--org-bd)}.msg.warning>.ic{color:var(--org-fg)}
.msg.info>.ic{color:var(--brandfg)}
.badge{display:inline-flex;align-items:center;height:20px;padding:0 8px;border:1px solid transparent;border-radius:999px;font-size:12px;line-height:16px;font-weight:600;text-transform:capitalize}
.sk{height:12px;border-radius:var(--r-m);background:linear-gradient(90deg,var(--bg6) 25%,var(--bg3) 50%,var(--bg6) 75%);background-size:300% 100%;animation:sh 1.5s ease-in-out infinite}
.sk+.sk{margin-top:12px}
@keyframes sh{from{background-position:100% 0}to{background-position:0 0}}
@media (prefers-reduced-motion:reduce){.sk,.rot,.spin{animation-duration:4s}}
";

    // ── Shared host bridge ──────────────────────────────────────────────

    private const string Bridge = @"
var App = (function () {
  var PROTOCOL = '2026-01-26';
  var nextId = 1, pending = {}, handlers = {}, caps = {}, mode = 'inline', modeListeners = [];
  var standalone = window.parent === window;
  var resizeTimer = null, lastHeight = 0;

  function post(message) { window.parent.postMessage(message, '*'); }

  function request(method, params) {
    if (standalone) return Promise.reject(new Error('Not running inside a host'));
    return new Promise(function (resolve, reject) {
      var id = nextId++;
      pending[id] = { resolve: resolve, reject: reject };
      post({ jsonrpc: '2.0', id: id, method: method, params: params || {} });
    });
  }

  function notify(method, params) {
    if (!standalone) post({ jsonrpc: '2.0', method: method, params: params || {} });
  }

  function applyContext(context) {
    if (!context) return;
    var root = document.documentElement;
    if (context.theme) {
      root.setAttribute('data-theme', context.theme);
      root.style.colorScheme = context.theme;
    }
    var vars = context.styles && context.styles.variables;
    if (vars) for (var name in vars) if (vars[name]) root.style.setProperty(name, vars[name]);
    var fonts = context.styles && context.styles.css && context.styles.css.fonts;
    if (fonts) { var style = document.createElement('style'); style.textContent = fonts; document.head.appendChild(style); }
    if (context.displayMode) setMode(context.displayMode);
  }

  function setMode(next) {
    if (next === mode) return;
    mode = next;
    document.documentElement.setAttribute('data-mode', mode);
    modeListeners.forEach(function (fn) { fn(mode); });
    resize();
  }

  function requestDisplayMode(next) {
    if (standalone) { setMode(next); return Promise.resolve(mode); }
    return request('ui/request-display-mode', { mode: next }).then(function (result) {
      if (result && result.mode) setMode(result.mode);
      return mode;
    });
  }

  window.addEventListener('message', function (event) {
    if (event.source !== window.parent) return;
    var message = event.data;
    if (!message || message.jsonrpc !== '2.0') return;

    if (message.method) {
      if (message.id !== undefined) {
        if (message.method === 'ping' || message.method === 'ui/resource-teardown') post({ jsonrpc: '2.0', id: message.id, result: {} });
        else post({ jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'Method not found' } });
      }
      var handler = handlers[message.method];
      if (handler) handler(message.params || {});
      return;
    }

    var waiting = pending[message.id];
    if (!waiting) return;
    delete pending[message.id];
    if (message.error) waiting.reject(new Error(message.error.message || 'Request failed'));
    else waiting.resolve(message.result);
  });

  handlers['ui/notifications/host-context-changed'] = applyContext;

  function measure() {
    var html = document.documentElement, previous = html.style.height;
    html.style.height = 'max-content';
    var height = Math.ceil(html.getBoundingClientRect().height);
    html.style.height = previous;
    return height;
  }

  function resize() {
    if (standalone || mode === 'fullscreen') return;
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      var height = measure();
      if (height !== lastHeight) { lastHeight = height; notify('ui/notifications/size-changed', { height: height }); }
    }, 30);
  }

  function connect(name, modes) {
    if (standalone) return Promise.resolve(null);
    return request('ui/initialize', {
      protocolVersion: PROTOCOL,
      appInfo: { name: name, version: '1.0.0' },
      appCapabilities: { availableDisplayModes: modes || ['inline'] }
    }).then(function (result) {
      caps = (result && result.hostCapabilities) || {};
      applyContext(result && result.hostContext);
      notify('ui/notifications/initialized');
      if (window.ResizeObserver) new ResizeObserver(resize).observe(document.body);
      resize();
      return result;
    });
  }

  return {
    isStandalone: standalone,
    connect: connect,
    resize: resize,
    mode: function () { return mode; },
    onMode: function (fn) { modeListeners.push(fn); },
    requestDisplayMode: requestDisplayMode,
    onToolResult: function (fn) { handlers['ui/notifications/tool-result'] = fn; },
    onToolCancelled: function (fn) { handlers['ui/notifications/tool-cancelled'] = fn; },
    canCallTools: function () { return !!caps.serverTools; },
    callTool: function (name, args) { return request('tools/call', { name: name, arguments: args || {} }); },
    sendMessage: function (text) { return request('ui/message', { role: 'user', content: [{ type: 'text', text: text }] }); },
    updateModelContext: function (text, data) { return request('ui/update-model-context', { content: [{ type: 'text', text: text }], structuredContent: data }); },
    openLink: function (url) { return request('ui/open-link', { url: url }); }
  };
})();

function h(tag, attrs) {
  var el = document.createElement(tag);
  if (attrs) for (var key in attrs) {
    var value = attrs[key];
    if (value === null || value === undefined || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'text') el.textContent = value;
    else if (key.indexOf('on') === 0) el.addEventListener(key.slice(2), value);
    else if (key === 'value' || key === 'checked' || key === 'disabled' || key === 'selected') el[key] = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (var i = 2; i < arguments.length; i++) add(el, arguments[i]);
  return el;
}

function add(el, child) {
  if (child === null || child === undefined || child === false) return;
  if (Array.isArray(child)) child.forEach(function (c) { add(el, c); });
  else el.appendChild(typeof child === 'object' ? child : document.createTextNode(String(child)));
}

function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); }

function text(value) { return value === null || value === undefined ? '' : String(value); }

var FLUENT_ICONS = {
    check: 'M1 8a7 7 0 1 1 14 0A7 7 0 0 1 1 8m9.85-1.15a.5.5 0 0 0-.7-.7l-2.9 2.9-1.4-1.4a.5.5 0 1 0-.7.7L6.9 10.1c.2.2.5.2.7 0z',
    error: 'M8 1a7 7 0 1 1 0 14A7 7 0 0 1 8 1m0 9a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5m0-5.5a.5.5 0 0 0-.5.5v3.59a.5.5 0 0 0 1-.09V4.91A.5.5 0 0 0 8 4.5',
    info: 'M8 1a7 7 0 1 1 0 14A7 7 0 0 1 8 1m0 5.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5m.5 1.25a.5.5 0 0 0-1 0v3a.5.5 0 0 0 1 0z',
    warning: 'M5.82 2.28a2.5 2.5 0 0 1 4.36 0l4.5 8A2.5 2.5 0 0 1 12.5 14h-9a2.5 2.5 0 0 1-2.18-3.72zM8 9.5A.75.75 0 1 0 8 11a.75.75 0 0 0 0-1.5M8 5a.5.5 0 0 0-.5.5V8a.5.5 0 0 0 1 0V5.5A.5.5 0 0 0 8 5',
    pending: 'M8 1a7 7 0 1 1 0 14A7 7 0 0 1 8 1m0 1a6 6 0 1 0 0 12A6 6 0 0 0 8 2',
    up: 'M3.15 10.35c.2.2.5.2.7 0L8 6.21l4.15 4.14a.5.5 0 0 0 .7-.7l-4.5-4.5a.5.5 0 0 0-.7 0l-4.5 4.5a.5.5 0 0 0 0 .7',
    down: 'M3.15 5.65c.2-.2.5-.2.7 0L8 9.79l4.15-4.14a.5.5 0 0 1 .7.7l-4.5 4.5a.5.5 0 0 1-.7 0l-4.5-4.5a.5.5 0 0 1 0-.7',
    expand: 'M12.4 3c.33 0 .6.27.6.6v4.9a.5.5 0 0 1-1 0V4.7L4.7 12h3.8a.5.5 0 0 1 0 1H3.6a.6.6 0 0 1-.6-.6V7.5a.5.5 0 0 1 1 0v3.8L11.3 4H7.5a.5.5 0 0 1 0-1z',
    collapse: 'M13.85 2.85a.5.5 0 0 0-.7-.7L10 5.29V3.5a.5.5 0 0 0-1 0v2.9c0 .33.27.6.6.6h2.9a.5.5 0 0 0 0-1h-1.8zM6.5 13a.5.5 0 0 1-.5-.5v-1.8l-3.15 3.15a.5.5 0 0 1-.7-.7L5.29 10H3.5a.5.5 0 0 1 0-1h2.9c.33 0 .6.27.6.6v2.9a.5.5 0 0 1-.5.5'
};

// Fluent System Icons (MIT), 16 px: filled status icons, outline circle, chevrons, and expand and collapse.
function icon(name) {
  var NS = 'http://www.w3.org/2000/svg';
  var svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('class', 'ic' + (name === 'running' ? ' rot' : ''));
  if (name === 'running') {
    var arc = document.createElementNS(NS, 'path');
    arc.setAttribute('d', 'M8 1.75a6.25 6.25 0 1 1-6.25 6.25');
    arc.setAttribute('class', 'ln');
    svg.appendChild(arc);
    return svg;
  }
  (FLUENT_ICONS[name] || FLUENT_ICONS.pending).split('|').forEach(function (d) {
    var p = document.createElementNS(NS, 'path');
    p.setAttribute('d', d);
    p.setAttribute('fill', 'currentColor');
    svg.appendChild(p);
  });
  return svg;
}

// Fluent MessageBar. intent is error, success, warning, or info.
function msg(intent, title, body, action) {
  var icons = { error: 'error', success: 'check', warning: 'warning', info: 'info' };
  return h('div', { class: 'msg ' + intent, role: intent === 'error' ? 'alert' : 'status' },
    icon(icons[intent]),
    h('div', { class: 'body' }, title ? h('span', { class: 't', text: title + ' ' }) : null, body ? h('span', { text: body }) : null),
    action || null);
}

// Fluent button loading state: a spinner ahead of the label.
function setBusy(button, busy) {
  var spinner = button.querySelector('.spin');
  if (busy && !spinner) button.insertBefore(h('span', { class: 'spin', 'aria-hidden': 'true' }), button.firstChild);
  if (!busy && spinner) button.removeChild(spinner);
}

function showError(message) {
  var root = document.getElementById('root');
  clear(root);
  root.appendChild(h('div', { class: 'card' }, msg('error', 'Something went wrong.', message)));
  App.resize();
}

function start(name, sample, render, modes) {
  App.onToolResult(function (result) {
    if (result && result.isError) { showError('The card could not be loaded.'); return; }
    var data = result && result.structuredContent;
    if (data) render(data); else showError('The tool returned no data for this card.');
  });
  App.onToolCancelled(function () { showError('The request was cancelled.'); });
  if (App.isStandalone) { render(sample); return; }
  App.connect(name, modes).catch(function () { showError('This card needs a host that supports MCP Apps.'); });
}
";

    // ── Approval ────────────────────────────────────────────────────────

    private const string ApprovalCss = @"
.facts{margin-bottom:0;border-top:1px solid var(--s2)}
.facts div{display:grid;grid-template-columns:minmax(80px,30%) 1fr;gap:12px;padding:8px 0;border-bottom:1px solid var(--s2)}
.facts dt{color:var(--fg3)}
.facts dd{margin:0;overflow-wrap:anywhere}
";

    private const string ApprovalJs = @"
var SAMPLE = { requestId: 'REQ-1042', title: 'Purchase order PO-7731', requester: 'Avery Chen',
  summary: 'Renew the annual analytics subscription.',
  facts: [{ label: 'Amount', value: '$12,400' }, { label: 'Vendor', value: 'Contoso Analytics' }, { label: 'Cost center', value: 'CC-210' }],
  approveLabel: 'Approve', rejectLabel: 'Reject', requireCommentOnReject: true };

function render(d) {
  var root = document.getElementById('root');
  clear(root);

  var comment = h('textarea', { id: 'comment', rows: '2', 'aria-label': 'Comment',
    placeholder: d.requireCommentOnReject ? 'Add a comment (required to reject)' : 'Add a comment (optional)' });
  var note = h('div', { 'aria-live': 'polite' });
  var approve = h('button', { class: 'btn primary', type: 'button', onclick: function () { act('approved', approve); } }, text(d.approveLabel) || 'Approve');
  var reject = h('button', { class: 'btn', type: 'button', onclick: function () { act('rejected', reject); } }, text(d.rejectLabel) || 'Reject');

  function setNote(element) {
    clear(note);
    if (element) note.appendChild(element);
    App.resize();
  }

  function lock(on) { approve.disabled = on; reject.disabled = on; comment.disabled = on; }

  function finish(decision, record) {
    var label = decision === 'approved' ? 'Approved' : 'Rejected';
    var typed = comment.value.trim();
    clear(root);
    root.appendChild(h('div', { class: 'card stack' },
      h('h1', { text: text(d.title) }),
      msg('success', label + '.', record && record.confirmationId ? 'Confirmation ' + record.confirmationId : null),
      typed ? h('p', { class: 'cap', text: 'Comment: ' + typed }) : null));
    App.resize();
  }

  function act(decision, button) {
    var typed = comment.value.trim();
    if (decision === 'rejected' && d.requireCommentOnReject && !typed) {
      comment.setAttribute('aria-invalid', 'true');
      setNote(msg('error', 'Add a comment to reject.', null));
      comment.focus();
      return;
    }
    comment.removeAttribute('aria-invalid');
    setNote(null);
    lock(true);
    setBusy(button, true);

    var args = { requestId: d.requestId, decision: decision, comment: typed };
    var recorded = App.canCallTools()
      ? App.callTool('submit_decision', args).then(function (r) {
          if (r && r.isError) throw new Error('The decision could not be recorded.');
          return r && r.structuredContent;
        })
      : Promise.resolve(null);

    recorded.then(function (record) {
      finish(decision, record);
      var message = 'I ' + decision + ' ' + text(d.title) + ' (request ' + d.requestId + ').' +
        (typed ? ' Comment: ' + typed + '.' : '') +
        (record ? ' The decision is recorded as ' + record.confirmationId + '.'
                : ' Record it with the submit_decision tool.');
      return App.sendMessage(message).catch(function () {});
    }).catch(function (e) {
      lock(false);
      setBusy(button, false);
      setNote(msg('error', 'Your decision was not recorded.', 'Select ' + text(button.textContent) + ' to try again.'));
    });
  }

  var facts = (d.facts || []).map(function (f) { return h('div', null, h('dt', { text: text(f.label) }), h('dd', { text: text(f.value) })); });

  root.appendChild(h('div', { class: 'card stack' },
    h('div', null,
      h('h1', { text: text(d.title) }),
      d.requester ? h('p', { class: 'cap', text: 'Requested by ' + text(d.requester) }) : null),
    d.summary ? h('p', { text: text(d.summary) }) : null,
    facts.length ? h('dl', { class: 'facts' }, facts) : null,
    comment,
    note,
    h('div', { class: 'actions' }, approve, reject)));
  App.resize();
}

start('approval-card', SAMPLE, render);
";

    // ── Form ────────────────────────────────────────────────────────────

    private const string FormCss = @"
.field .err{display:flex;gap:4px;align-items:center;margin-top:4px;color:var(--red-fg);font-size:12px;line-height:16px}
.field .err:empty{display:none}
.field.check label{display:flex;gap:8px;align-items:center}
";

    private const string FormJs = @"
var SAMPLE = { formId: 'FRM-1', title: 'New support request', description: 'Tell us what you need help with.', submitLabel: 'Send request',
  fields: [
    { name: 'summary', label: 'Summary', type: 'text', required: true, placeholder: 'One line description' },
    { name: 'email', label: 'Contact email', type: 'email', required: true },
    { name: 'priority', label: 'Priority', type: 'choice', options: ['Low', 'Normal', 'High'], defaultValue: 'Normal' },
    { name: 'details', label: 'Details', type: 'textarea' },
    { name: 'notify', label: 'Email me updates', type: 'checkbox', defaultValue: 'true' }] };

var EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

function render(d) {
  var root = document.getElementById('root');
  clear(root);

  var fields = (d.fields || []).filter(function (f) { return f && f.name; });
  var inputs = {}, errors = {};
  var note = h('div', { 'aria-live': 'polite' });
  var submit = h('button', { class: 'btn primary', type: 'submit' }, text(d.submitLabel) || 'Submit');

  function control(f, id) {
    var type = f.type || 'text';
    if (type === 'textarea') return h('textarea', { id: id, rows: '3', placeholder: f.placeholder, value: text(f.defaultValue) });
    if (type === 'choice') {
      var select = h('select', { id: id }, h('option', { value: '', text: 'Select...' }));
      (f.options || []).forEach(function (o) { select.appendChild(h('option', { value: text(o), text: text(o) })); });
      select.value = text(f.defaultValue);
      return select;
    }
    if (type === 'checkbox') return h('input', { id: id, type: 'checkbox', checked: text(f.defaultValue) === 'true' });
    return h('input', { id: id, type: ['email', 'number', 'date'].indexOf(type) >= 0 ? type : 'text', placeholder: f.placeholder, value: text(f.defaultValue) });
  }

  var rows = fields.map(function (f, i) {
    var id = 'f' + i;
    var input = control(f, id);
    var err = h('div', { class: 'err', id: id + '-err', role: 'alert' });
    input.setAttribute('aria-describedby', id + '-err');
    inputs[f.name] = input;
    errors[f.name] = err;
    var label = h('label', { class: 'lbl', for: id }, text(f.label || f.name), f.required ? h('span', { class: 'req', text: ' *' }) : null);
    if (f.type === 'checkbox') return h('div', { class: 'field check' }, h('label', { for: id }, input, text(f.label || f.name)), err);
    return h('div', { class: 'field' }, label, input, err);
  });

  function valueOf(f) {
    var el = inputs[f.name];
    return f.type === 'checkbox' ? el.checked : el.value.trim();
  }

  function validate() {
    var ok = true;
    fields.forEach(function (f) {
      var el = inputs[f.name], value = valueOf(f), message = '';
      if (f.required && (value === '' || value === false)) message = 'This field is required';
      else if (value !== '' && f.type === 'email' && !EMAIL.test(value)) message = 'Enter a valid email address';
      else if (value !== '' && f.type === 'number' && isNaN(Number(value))) message = 'Enter a number';
      errors[f.name].textContent = message;
      el.setAttribute('aria-invalid', message ? 'true' : 'false');
      if (message && ok) { ok = false; el.focus(); }
    });
    return ok;
  }

  function setNote(element) {
    clear(note);
    if (element) note.appendChild(element);
    App.resize();
  }

  function lock(on) {
    submit.disabled = on;
    fields.forEach(function (f) { inputs[f.name].disabled = on; });
    setBusy(submit, on);
  }

  function collect() {
    var values = {};
    fields.forEach(function (f) { values[f.name] = valueOf(f); });
    return values;
  }

  function onSubmit(event) {
    event.preventDefault();
    if (!validate()) { setNote(null); return; }
    var values = collect();
    setNote(null);
    lock(true);

    var recorded = App.canCallTools()
      ? App.callTool('submit_form', { formId: d.formId, valuesJson: JSON.stringify(values) }).then(function (r) {
          if (r && r.isError) throw new Error('The form could not be submitted.');
          return r && r.structuredContent;
        })
      : Promise.resolve(null);

    recorded.then(function (record) {
      clear(root);
      root.appendChild(h('div', { class: 'card stack' },
        h('h1', { text: text(d.title) }),
        msg('success', 'Submitted.', record && record.confirmationId ? 'Confirmation ' + record.confirmationId : null)));
      App.resize();

      var lines = fields.map(function (f) { return text(f.label || f.name) + ': ' + text(values[f.name]); });
      var message = 'I submitted the form ' + text(d.title) + ' (' + d.formId + '). ' + lines.join('; ') + '. ' +
        (record ? 'It is recorded as ' + record.confirmationId + '.' : 'Record it with the submit_form tool.');
      return App.sendMessage(message).catch(function () {});
    }).catch(function (e) {
      lock(false);
      setNote(msg('error', 'The form was not submitted.', 'Check your entries and select ' + text(submit.textContent) + ' to try again.'));
    });
  }

  root.appendChild(h('form', { class: 'card stack', novalidate: true, onsubmit: onSubmit },
    h('div', null,
      h('h1', { text: text(d.title) }),
      d.description ? h('p', { class: 'cap', text: text(d.description) }) : null),
    h('div', { class: 'stack' }, rows),
    note,
    h('div', { class: 'actions' }, submit)));
  App.resize();
}

start('form-card', SAMPLE, render);
";

    // ── Table ───────────────────────────────────────────────────────────

    private const string TableCss = @"
.scroll.capped{max-height:360px;overflow:auto}
.wrap{overflow-x:auto}
table{border-collapse:collapse;width:100%}
th,td{height:40px;padding:0 12px;text-align:left;border-bottom:1px solid var(--s2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:260px}
th{position:sticky;top:0;background:var(--bg1);font-weight:600}
th button{all:unset;display:inline-flex;gap:4px;align-items:center;cursor:pointer}
tbody tr:hover td{background:var(--bg1h)}
tbody tr.sel td{background:var(--bgs)}
td.chk,th.chk{width:40px;max-width:40px;padding:0 0 0 12px}
.search{max-width:320px}
";

    private const string TableJs = @"
var SAMPLE = { title: 'Open opportunities', selectable: true, pageSize: 5,
  columns: [{ key: 'name', label: 'Opportunity' }, { key: 'owner', label: 'Owner' }, { key: 'stage', label: 'Stage' }, { key: 'amount', label: 'Amount' }],
  rows: [
    { name: 'Fabrikam renewal', owner: 'Avery', stage: 'Negotiate', amount: 48000 },
    { name: 'Contoso expansion', owner: 'Jordan', stage: 'Propose', amount: 125000 },
    { name: 'Northwind pilot', owner: 'Sam', stage: 'Qualify', amount: 9000 },
    { name: 'Adventure Works', owner: 'Riley', stage: 'Close', amount: 72000 },
    { name: 'Tailspin upgrade', owner: 'Avery', stage: 'Propose', amount: 31000 },
    { name: 'Wingtip rollout', owner: 'Jordan', stage: 'Qualify', amount: 18000 }] };

function compare(a, b) {
  var na = Number(a), nb = Number(b);
  var numeric = a !== '' && b !== '' && a !== null && b !== null && a !== undefined && b !== undefined && !isNaN(na) && !isNaN(nb);
  if (numeric) return na - nb;
  return text(a).localeCompare(text(b), undefined, { numeric: true, sensitivity: 'base' });
}

var TS = null, redraw = null;
App.onMode(function () { if (redraw) redraw(); });

function render(d) {
  var root = document.getElementById('root');
  clear(root);

  var columns = (d.columns || []).filter(function (c) { return c && c.key; });
  var rows = d.rows || [];
  var pageSize = Math.max(1, Number(d.pageSize) || 5);
  var key = JSON.stringify([d.title, columns.length, rows.length, rows[0]]);
  if (!TS || TS.key !== key) TS = { key: key, sort: null, dir: 1, filter: '', selected: {}, inlineOpen: false };
  var state = TS;

  var search = h('input', { class: 'search', type: 'search', placeholder: 'Search', 'aria-label': 'Search rows', value: state.filter,
    oninput: function () { state.filter = search.value; draw(); } });
  var searchRow = h('div', null, search);
  var holder = h('div');
  var caption = h('p', { class: 'cap', 'aria-live': 'polite' });
  var note = h('div', { 'aria-live': 'polite' });
  var actions = h('div', { class: 'actions' });

  // Inline shows a short preview. Sorting, search, and the full list open in the expanded view.
  function isOpen() { return App.mode() === 'fullscreen' || state.inlineOpen; }

  function visible() {
    var query = state.filter.toLowerCase();
    var list = rows.map(function (row, index) { return { index: index, row: row }; });
    if (query) list = list.filter(function (x) {
      return columns.some(function (c) { return text(x.row[c.key]).toLowerCase().indexOf(query) >= 0; });
    });
    if (state.sort) list.sort(function (a, b) { return compare(a.row[state.sort], b.row[state.sort]) * state.dir; });
    return list;
  }

  function selectedRows() {
    return rows.filter(function (row, index) { return state.selected[index]; });
  }

  function syncSelection() {
    var picked = selectedRows();
    if (d.selectable) {
      App.updateModelContext('The user has selected ' + picked.length + ' row(s) in the table ' + text(d.title) + '.', { selectedRows: picked }).catch(function () {});
    }
  }

  function setNote(element) {
    clear(note);
    if (element) note.appendChild(element);
    App.resize();
  }

  function expand() {
    App.requestDisplayMode('fullscreen')
      .then(function (mode) { if (mode !== 'fullscreen') { state.inlineOpen = true; draw(); } })
      .catch(function () { state.inlineOpen = true; draw(); });
  }

  function collapse() {
    if (state.inlineOpen) { state.inlineOpen = false; draw(); }
    if (App.mode() === 'fullscreen') App.requestDisplayMode('inline').catch(function () {});
  }

  function sendSelection(button) {
    var picked = selectedRows();
    var summary = picked.map(function (row) {
      return columns.map(function (c) { return text(row[c.key]); }).join(', ');
    }).join('; ');
    setNote(null);
    button.disabled = true;
    setBusy(button, true);
    App.sendMessage('I selected ' + picked.length + ' row(s) from ' + text(d.title) + ': ' + summary + '.')
      .then(function () { draw(); setNote(msg('success', 'Sent to the agent.', null)); })
      .catch(function () {
        draw();
        setNote(msg('error', 'The selection was not sent.', 'Select Send selection to try again.'));
      });
  }

  function draw() {
    var open = isOpen();
    var list = open ? visible() : rows.map(function (row, index) { return { index: index, row: row }; }).slice(0, pageSize);
    searchRow.style.display = open ? '' : 'none';

    var headerCells = columns.map(function (c) {
      var label = text(c.label || c.key);
      if (!open) return h('th', { scope: 'col', text: label });
      var active = state.sort === c.key;
      return h('th', { scope: 'col', 'aria-sort': active ? (state.dir === 1 ? 'ascending' : 'descending') : 'none' },
        h('button', { type: 'button', onclick: function () {
          if (state.sort === c.key) state.dir = -state.dir; else { state.sort = c.key; state.dir = 1; }
          draw();
        } }, label, active ? icon(state.dir === 1 ? 'up' : 'down') : null));
    });

    if (d.selectable) {
      var allOn = open && list.length > 0 && list.every(function (x) { return state.selected[x.index]; });
      headerCells.unshift(h('th', { class: 'chk', scope: 'col' }, open
        ? h('input', { type: 'checkbox', checked: allOn, 'aria-label': 'Select all rows', onchange: function (e) {
            list.forEach(function (x) { state.selected[x.index] = e.target.checked; });
            syncSelection(); draw();
          } })
        : h('span', { class: 'sr', text: 'Select' })));
    }

    var body = list.map(function (x) {
      var cells = columns.map(function (c) { return h('td', { text: text(x.row[c.key]), title: text(x.row[c.key]) }); });
      if (d.selectable) cells.unshift(h('td', { class: 'chk' }, h('input', { type: 'checkbox', checked: !!state.selected[x.index], 'aria-label': 'Select row', onchange: function (e) {
        state.selected[x.index] = e.target.checked;
        syncSelection(); draw();
      } })));
      return h('tr', { class: state.selected[x.index] ? 'sel' : '' }, cells);
    });
    if (!body.length) body.push(h('tr', null, h('td', { colspan: String(columns.length + (d.selectable ? 1 : 0)), class: 'cap', text: 'No rows match your search.' })));

    var capped = state.inlineOpen && App.mode() !== 'fullscreen';
    clear(holder);
    holder.appendChild(h('div', { class: 'wrap scroll' + (capped ? ' capped' : '') },
      h('table', null, h('thead', null, h('tr', null, headerCells)), h('tbody', null, body))));

    var picked = selectedRows().length;
    caption.textContent = (open
      ? list.length + ' of ' + rows.length + ' rows'
      : (rows.length > pageSize ? 'Showing ' + list.length + ' of ' + rows.length + ' rows' : rows.length + ' rows')) +
      (d.selectable ? ', ' + picked + ' selected' : '');

    clear(actions);
    if (open) actions.appendChild(h('button', { class: 'btn', type: 'button', onclick: collapse }, icon('collapse'), 'Collapse'));
    else if (rows.length > pageSize) actions.appendChild(h('button', { class: 'btn', type: 'button', onclick: expand }, icon('expand'), 'Expand'));
    if (d.selectable) {
      var send = h('button', { class: 'btn primary', type: 'button', disabled: picked === 0, onclick: function () { sendSelection(send); } }, 'Send selection');
      actions.appendChild(send);
    }
    App.resize();
  }

  redraw = draw;
  root.appendChild(h('div', { class: 'card stack' },
    h('h1', { text: text(d.title) || 'Results' }),
    searchRow,
    holder,
    caption,
    note,
    actions));
  draw();
}

start('table-card', SAMPLE, render, ['inline', 'fullscreen']);
";

    // ── Status ──────────────────────────────────────────────────────────

    private const string StatusCss = @"
.head{display:flex;gap:8px;align-items:center;justify-content:space-between}
.b-succeeded{color:var(--grn-fg);background:var(--grn-bg);border-color:var(--grn-bd)}
.b-failed{color:var(--red-fg);background:var(--red-bg);border-color:var(--red-bd)}
.b-warning{color:var(--org-fg);background:var(--org-bg);border-color:var(--org-bd)}
.b-running{color:var(--brandfg);background:var(--bg3);border-color:var(--s2)}
.b-queued{color:var(--fg2);background:var(--bg3);border-color:var(--s2)}
.track{height:4px;border-radius:999px;background:var(--bg6);overflow:hidden}
.track>div{height:100%;border-radius:999px;background:var(--brandfg);transition:width .3s}
.track.t-succeeded>div{background:var(--grn-fg)}.track.t-failed>div{background:var(--red-fg)}.track.t-warning>div{background:var(--org-fg)}.track.t-queued>div{background:var(--fg3)}
ul.steps{list-style:none;margin:0;padding:0}
ul.steps li{display:flex;gap:8px;align-items:center;min-height:28px}
ul.steps .s-done{color:var(--grn-fg)}ul.steps .s-running{color:var(--brandfg)}ul.steps .s-failed{color:var(--red-fg)}ul.steps .s-pending{color:var(--fg3)}
ul.steps .grow{flex:1}
";

    private const string StatusJs = @"
var SAMPLE = { title: 'Nightly data sync', status: 'running', percentComplete: 50, statusId: 'JOB-88', refreshable: true,
  message: 'Copying tables to the warehouse.',
  steps: [{ label: 'Export from source', state: 'done' }, { label: 'Transform', state: 'done' }, { label: 'Load to warehouse', state: 'running' }, { label: 'Verify row counts', state: 'pending' }] };

var STEP_ICONS = { done: 'check', running: 'running', failed: 'error', pending: 'pending' };

function render(d) {
  var root = document.getElementById('root');
  clear(root);

  var note = h('div', { 'aria-live': 'polite' });
  var refresh = h('button', { class: 'btn', type: 'button', onclick: onRefresh }, 'Refresh');

  function setNote(element) {
    clear(note);
    if (element) note.appendChild(element);
    App.resize();
  }

  function onRefresh() {
    setNote(null);
    refresh.disabled = true;
    setBusy(refresh, true);
    App.callTool('refresh_status', { statusId: d.statusId, percentComplete: d.percentComplete || 0 })
      .then(function (r) {
        var latest = r && r.structuredContent;
        if (!latest || (r && r.isError)) throw new Error('No update was returned.');
        render({ title: d.title, steps: d.steps, statusId: d.statusId, refreshable: d.refreshable,
          status: latest.status, percentComplete: latest.percentComplete, message: latest.message });
      })
      .catch(function (e) {
        refresh.disabled = false;
        setBusy(refresh, false);
        setNote(msg('error', 'The status was not refreshed.', 'Select Refresh to try again.'));
      });
  }

  var percent = Math.max(0, Math.min(100, Number(d.percentComplete) || 0));
  var status = text(d.status) || 'queued';
  var steps = (d.steps || []).map(function (s) {
    var state = status === 'succeeded' ? 'done' : (s.state || 'pending');
    return h('li', null,
      h('span', { class: 's-' + state }, icon(STEP_ICONS[state] || 'pending')),
      h('span', { class: 'grow', text: text(s.label) }),
      h('span', { class: 'cap', text: state }));
  });

  var track = h('div', { class: 'track t-' + status, role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(percent), 'aria-label': 'Progress' },
    h('div', { style: 'width:' + percent + '%' }));

  root.appendChild(h('div', { class: 'card stack' },
    h('div', { class: 'head' }, h('h1', { text: text(d.title) }), h('span', { class: 'badge b-' + status, text: status })),
    h('div', null, track,
      h('p', { class: 'cap', style: 'margin-top:4px', text: percent + '% complete' + (d.message ? ' - ' + text(d.message) : '') })),
    steps.length ? h('ul', { class: 'steps' }, steps) : null,
    note,
    d.refreshable && d.statusId && !App.isStandalone ? h('div', { class: 'actions' }, refresh) : null));
  App.resize();
}

start('status-card', SAMPLE, render);
";
}
