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
// ║  SECTION 1: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. Spec coverage: MCP 2025-11-25.                                  ║
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

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
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
        Action<McpSchemaBuilder> schemaConfig,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotationsConfig = null,
        string title = null)
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            Annotations = annotations,
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

        Log("McpRequest", new { Method = method });

        try
        {
            switch (method)
            {
                case "initialize": return HandleInitialize(id, request);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());
                case "ping": return SerializeSuccess(id, new JObject());
                case "tools/list": return HandleToolsList(id);
                case "tools/call": return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/list": return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });
                case "resources/templates/list": return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });
                case "resources/read": return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");
                case "prompts/list": return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });
                case "prompts/get": return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");
                case "completion/complete": return SerializeSuccess(id, new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } });
                case "logging/setLevel": return SerializeSuccess(id, new JObject());
                default: return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex) { return SerializeError(id, ex.Code, ex.Message); }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;
        var caps = new JObject();
        if (_options.Capabilities.Tools) caps["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources) caps["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts) caps["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging) caps["logging"] = new JObject();
        if (_options.Capabilities.Completions) caps["completions"] = new JObject();

        var si = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) si["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) si["description"] = _options.ServerInfo.Description;

        var result = new JObject { ["protocolVersion"] = clientVersion, ["capabilities"] = caps, ["serverInfo"] = si };
        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;
        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var arr = new JArray();
        foreach (var t in _tools.Values)
        {
            var obj = new JObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema };
            if (!string.IsNullOrWhiteSpace(t.Title)) obj["title"] = t.Title;
            if (t.Annotations != null && t.Annotations.Count > 0) obj["annotations"] = t.Annotations;
            arr.Add(obj);
        }
        return SerializeSuccess(id, new JObject { ["tools"] = arr });
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var p = request["params"] as JObject;
        var toolName = p?.Value<string>("name");
        var args = p?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");
        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        try
        {
            var result = await tool.Handler(args, ct).ConfigureAwait(false);
            string text;
            if (result is JObject jo) text = jo.ToString(Newtonsoft.Json.Formatting.Indented);
            else if (result is string s) text = s;
            else text = result == null ? "{}" : JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    public static JObject TextContent(string text) => new JObject { ["type"] = "text", ["text"] = text };
    public static JObject ToolResult(JArray content, bool isError = false) => new JObject { ["content"] = content, ["isError"] = isError };

    private string SerializeSuccess(JToken id, JObject result) =>
        new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None);

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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: CANVAS LMS MCP CONNECTOR                                      ║
// ║                                                                            ║
// ║  Teacher-focused tools: courses, assignments, submissions, grades,         ║
// ║  enrollments, modules, discussions, announcements, analytics, and users.   ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Configuration ────────────────────────────────────────────────────

    private const string CANVAS_API_BASE = "https://canvas.instructure.com/api/v1";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "canvas-lms-mcp",
            Version = "1.0.0",
            Title = "Canvas LMS MCP",
            Description = "Canvas LMS connector for course management, assignments, grading, enrollments, and analytics"
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities { Tools = true },
        Instructions = "Canvas LMS teacher tools. Use list_courses first to discover course IDs, then use other tools with those IDs. All IDs are strings. Pagination is automatic (returns up to per_page results, default 10)."
    };

    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var handler = new McpRequestHandler(Options);
        handler.OnLog = (name, data) => LogToAppInsights(name, data);

        RegisterTools(handler);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(result, Encoding.UTF8, "application/json");
        return response;
    }

    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ── Courses ──

        handler.AddTool("list_courses",
            "List the current user's courses. Returns course ID, name, code, term, and enrollment info. Use enrollment_type to filter (teacher, student, ta, observer, designer).",
            s => {
                s.String("enrollment_type", "Filter by enrollment type: teacher, student, ta, observer, designer");
                s.String("enrollment_state", "Filter by enrollment state: active, invited_or_pending, completed");
                s.String("state", "Course state filter: unpublished, available, completed, deleted");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet("/courses", args, ct, new[] {
                "enrollment_type", "enrollment_state", "state[]", "per_page"
            }, "include[]=total_students&include[]=term&include[]=teachers").ConfigureAwait(false));

        handler.AddTool("get_course",
            "Get details for a single course including settings, term, and teacher info.",
            s => s.String("course_id", "The course ID", required: true),
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}", null, ct, null,
                "include[]=total_students&include[]=term&include[]=teachers&include[]=needs_grading_count").ConfigureAwait(false));

        // ── Users in Course ──

        handler.AddTool("list_course_users",
            "List users enrolled in a course. Filter by enrollment type (student, teacher, ta, observer, designer).",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("enrollment_type", "Filter: teacher, student, student_view, ta, observer, designer");
                s.String("search_term", "Search by name or ID (min 3 characters)");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/users", args, ct, new[] {
                "enrollment_type[]", "search_term", "per_page"
            }, "include[]=enrollments&include[]=email&include[]=avatar_url").ConfigureAwait(false));

        // ── Assignments ──

        handler.AddTool("list_assignments",
            "List assignments for a course. Filter by bucket (past, overdue, undated, ungraded, unsubmitted, upcoming, future) or search by name.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search assignment names");
                s.String("bucket", "Filter: past, overdue, undated, ungraded, unsubmitted, upcoming, future");
                s.String("order_by", "Sort: position, name, due_at");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/assignments", args, ct, new[] {
                "search_term", "bucket", "order_by", "per_page"
            }, "include[]=submission&include[]=score_statistics").ConfigureAwait(false));

        handler.AddTool("get_assignment",
            "Get detailed information about a single assignment including rubric, due dates, and submission types.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/assignments/{Required(args, "assignment_id")}", null, ct, null,
                "include[]=submission&include[]=score_statistics&include[]=overrides&all_dates=true").ConfigureAwait(false));

        // ── Submissions & Grading ──

        handler.AddTool("list_submissions",
            "List all submissions for an assignment. Shows student name, score, grade, submission time, and workflow state.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/assignments/{Required(args, "assignment_id")}/submissions", args, ct, new[] {
                "per_page"
            }, "include[]=user&include[]=submission_comments").ConfigureAwait(false));

        handler.AddTool("get_submission",
            "Get a single student's submission for an assignment, including comments, score, and submission history.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.String("user_id", "The student's user ID", required: true);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/assignments/{Required(args, "assignment_id")}/submissions/{Required(args, "user_id")}", null, ct, null,
                "include[]=submission_comments&include[]=rubric_assessment&include[]=submission_history").ConfigureAwait(false));

        handler.AddTool("grade_submission",
            "Grade a student's submission. Set a score or grade, and optionally add a comment.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.String("user_id", "The student's user ID", required: true);
                s.String("grade", "The grade to assign (e.g., '95', 'A', 'pass')");
                s.String("score", "Numeric score to assign");
                s.String("comment", "Text comment to add with the grade");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var assignmentId = Required(args, "assignment_id");
                var userId = Required(args, "user_id");

                var submission = new JObject();
                if (args["grade"] != null) submission["posted_grade"] = args.Value<string>("grade");
                if (args["score"] != null) submission["posted_grade"] = args.Value<string>("score");

                var body = new JObject { ["submission"] = submission };

                var comment = args.Value<string>("comment");
                if (!string.IsNullOrWhiteSpace(comment))
                    body["comment"] = new JObject { ["text_comment"] = comment };

                return await CanvasPut($"/courses/{courseId}/assignments/{assignmentId}/submissions/{userId}", body, ct).ConfigureAwait(false);
            });

        // ── Enrollments ──

        handler.AddTool("list_enrollments",
            "List enrollments for a course. Shows user, role, enrollment state, grades, and last activity.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("type", "Filter: StudentEnrollment, TeacherEnrollment, TaEnrollment, ObserverEnrollment, DesignerEnrollment");
                s.String("state", "Filter: active, invited, creation_pending, deleted, rejected, completed, inactive");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/enrollments", args, ct, new[] {
                "type[]", "state[]", "per_page"
            }, "include[]=avatar_url").ConfigureAwait(false));

        // ── Modules ──

        handler.AddTool("list_modules",
            "List modules in a course. Modules organize content into sections.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search module names");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/modules", args, ct, new[] {
                "search_term", "per_page"
            }, "include[]=items&include[]=content_details").ConfigureAwait(false));

        handler.AddTool("list_module_items",
            "List items within a specific module.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("module_id", "The module ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/modules/{Required(args, "module_id")}/items", args, ct, new[] {
                "per_page"
            }, "include[]=content_details").ConfigureAwait(false));

        // ── Announcements ──

        handler.AddTool("list_announcements",
            "List announcements for one or more courses.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("start_date", "Only return announcements posted after this date (ISO 8601)");
                s.String("end_date", "Only return announcements posted before this date (ISO 8601)");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var extraParams = $"context_codes[]=course_{courseId}";
                if (args["start_date"] != null) extraParams += $"&start_date={args.Value<string>("start_date")}";
                if (args["end_date"] != null) extraParams += $"&end_date={args.Value<string>("end_date")}";
                var perPage = args.Value<int?>("per_page") ?? 10;
                extraParams += $"&per_page={perPage}";
                return await CanvasGet("/announcements", null, ct, null, extraParams).ConfigureAwait(false);
            });

        // ── Discussion Topics ──

        handler.AddTool("list_discussions",
            "List discussion topics for a course.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search discussion titles");
                s.String("order_by", "Sort: position or recent_activity");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/discussion_topics", args, ct, new[] {
                "search_term", "order_by", "per_page"
            }).ConfigureAwait(false));

        // ── Grades / Analytics ──

        handler.AddTool("get_course_analytics",
            "Get course-level analytics including assignment statistics and student summaries.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("type", "Analytics type: assignments or students", required: true,
                    enumValues: new[] { "assignments", "students" });
                s.String("sort_column", "For students: name, score, participation, page_views");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var type = Required(args, "type");
                var path = $"/courses/{courseId}/analytics/{type}";
                var extra = "";
                if (type == "students" && args["sort_column"] != null)
                    extra = $"sort_column={args.Value<string>("sort_column")}";
                return await CanvasGet(path, null, ct, null, extra).ConfigureAwait(false);
            });

        handler.AddTool("get_needs_grading",
            "Get the current user's TODO items — assignments needing grading and submissions needing review.",
            s => s.Integer("per_page", "Results per page (1-100)", defaultValue: 10),
            async (args, ct) => await CanvasGet("/users/self/todo", args, ct, new[] { "per_page" }).ConfigureAwait(false));

        // ── User Profile ──

        handler.AddTool("get_my_profile",
            "Get the current authenticated user's profile (name, email, avatar, locale).",
            s => { },
            async (args, ct) => await CanvasGet("/users/self/profile", null, ct).ConfigureAwait(false));

        // ── Calendar Events ──

        handler.AddTool("list_upcoming_events",
            "List the current user's upcoming assignments and calendar events.",
            s => { },
            async (args, ct) => await CanvasGet("/users/self/upcoming_events", null, ct).ConfigureAwait(false));

        // ── Course Pages (Wiki) ──

        handler.AddTool("list_pages",
            "List wiki pages in a course.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search page titles");
                s.String("sort", "Sort: title, created_at, updated_at");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/pages", args, ct, new[] {
                "search_term", "sort", "per_page"
            }).ConfigureAwait(false));

        // ── Sections ──

        handler.AddTool("list_sections",
            "List sections in a course.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/sections", args, ct, new[] {
                "per_page"
            }, "include[]=students&include[]=total_students").ConfigureAwait(false));

        // ══════════════════════════════════════════════════════════════════
        // HIGH VALUE — WRITE OPERATIONS
        // ══════════════════════════════════════════════════════════════════

        // ── Create Assignment ──

        handler.AddTool("create_assignment",
            "Create a new assignment in a course. Specify name, description, due date, points, submission types, and more.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("name", "Assignment name", required: true);
                s.String("description", "Assignment description (supports HTML)");
                s.String("due_at", "Due date in ISO 8601 format (e.g., 2026-09-15T23:59:00Z)");
                s.String("unlock_at", "Date to unlock the assignment (ISO 8601)");
                s.String("lock_at", "Date to lock the assignment (ISO 8601)");
                s.String("points_possible", "Maximum points (e.g., '100')");
                s.String("submission_types", "Comma-separated: online_text_entry, online_upload, online_url, on_paper, none, external_tool");
                s.String("grading_type", "Grading strategy: points, pass_fail, percent, letter_grade, gpa_scale, not_graded");
                s.Boolean("published", "Whether to publish immediately (default false)");
                s.String("assignment_group_id", "Assignment group ID to categorize the assignment");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var assignment = new JObject
                {
                    ["name"] = Required(args, "name")
                };
                if (args["description"] != null) assignment["description"] = args.Value<string>("description");
                if (args["due_at"] != null) assignment["due_at"] = args.Value<string>("due_at");
                if (args["unlock_at"] != null) assignment["unlock_at"] = args.Value<string>("unlock_at");
                if (args["lock_at"] != null) assignment["lock_at"] = args.Value<string>("lock_at");
                if (args["points_possible"] != null) assignment["points_possible"] = double.Parse(args.Value<string>("points_possible"));
                if (args["grading_type"] != null) assignment["grading_type"] = args.Value<string>("grading_type");
                if (args["published"] != null) assignment["published"] = args.Value<bool>("published");
                if (args["assignment_group_id"] != null) assignment["assignment_group_id"] = args.Value<string>("assignment_group_id");
                if (args["submission_types"] != null)
                {
                    var types = args.Value<string>("submission_types").Split(',');
                    assignment["submission_types"] = new JArray(types.Select(t => t.Trim()));
                }
                return await CanvasPost($"/courses/{courseId}/assignments", new JObject { ["assignment"] = assignment }, ct).ConfigureAwait(false);
            });

        // ── Update Assignment ──

        handler.AddTool("update_assignment",
            "Update an existing assignment. Change name, due date, points, description, published status, etc.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.String("name", "New assignment name");
                s.String("description", "New description (supports HTML)");
                s.String("due_at", "New due date (ISO 8601). Pass empty string to clear.");
                s.String("unlock_at", "New unlock date (ISO 8601). Pass empty string to clear.");
                s.String("lock_at", "New lock date (ISO 8601). Pass empty string to clear.");
                s.String("points_possible", "New max points");
                s.String("grading_type", "Grading strategy: points, pass_fail, percent, letter_grade, gpa_scale, not_graded");
                s.Boolean("published", "Set published state");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var assignmentId = Required(args, "assignment_id");
                var assignment = new JObject();
                if (args["name"] != null) assignment["name"] = args.Value<string>("name");
                if (args["description"] != null) assignment["description"] = args.Value<string>("description");
                if (args["due_at"] != null) assignment["due_at"] = args.Value<string>("due_at") == "" ? null : args.Value<string>("due_at");
                if (args["unlock_at"] != null) assignment["unlock_at"] = args.Value<string>("unlock_at") == "" ? null : args.Value<string>("unlock_at");
                if (args["lock_at"] != null) assignment["lock_at"] = args.Value<string>("lock_at") == "" ? null : args.Value<string>("lock_at");
                if (args["points_possible"] != null) assignment["points_possible"] = double.Parse(args.Value<string>("points_possible"));
                if (args["grading_type"] != null) assignment["grading_type"] = args.Value<string>("grading_type");
                if (args["published"] != null) assignment["published"] = args.Value<bool>("published");
                return await CanvasPut($"/courses/{courseId}/assignments/{assignmentId}", new JObject { ["assignment"] = assignment }, ct).ConfigureAwait(false);
            });

        // ── Create Announcement ──

        handler.AddTool("create_announcement",
            "Post an announcement to a course. Visible to all enrolled users.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("title", "Announcement title", required: true);
                s.String("message", "Announcement body (supports HTML)", required: true);
                s.Boolean("is_published", "Whether to publish immediately (default true)");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var body = new JObject
                {
                    ["title"] = Required(args, "title"),
                    ["message"] = Required(args, "message"),
                    ["is_announcement"] = true,
                    ["published"] = args.Value<bool?>("is_published") ?? true
                };
                return await CanvasPost($"/courses/{courseId}/discussion_topics", body, ct).ConfigureAwait(false);
            });

        // ── Create Discussion ──

        handler.AddTool("create_discussion",
            "Create a discussion topic in a course.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("title", "Discussion title", required: true);
                s.String("message", "Discussion body (supports HTML)", required: true);
                s.String("discussion_type", "Type: side_comment (default) or threaded");
                s.Boolean("published", "Whether to publish immediately (default true)");
                s.Boolean("require_initial_post", "Require students to post before seeing replies");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var body = new JObject
                {
                    ["title"] = Required(args, "title"),
                    ["message"] = Required(args, "message")
                };
                if (args["discussion_type"] != null) body["discussion_type"] = args.Value<string>("discussion_type");
                if (args["published"] != null) body["published"] = args.Value<bool>("published");
                if (args["require_initial_post"] != null) body["require_initial_post"] = args.Value<bool>("require_initial_post");
                return await CanvasPost($"/courses/{courseId}/discussion_topics", body, ct).ConfigureAwait(false);
            });

        // ── Post Comment (no grade) ──

        handler.AddTool("post_comment",
            "Add a comment to a student's submission without changing their grade. Useful for feedback.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.String("user_id", "The student's user ID", required: true);
                s.String("comment", "The comment text", required: true);
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var assignmentId = Required(args, "assignment_id");
                var userId = Required(args, "user_id");
                var body = new JObject
                {
                    ["comment"] = new JObject { ["text_comment"] = Required(args, "comment") }
                };
                return await CanvasPut($"/courses/{courseId}/assignments/{assignmentId}/submissions/{userId}", body, ct).ConfigureAwait(false);
            });

        // ── Send Message ──

        handler.AddTool("send_message",
            "Send a message (conversation) to one or more users. Recipients can be user IDs or course-scoped IDs like 'course_123_students'.",
            s => {
                s.String("recipients", "Comma-separated recipient IDs (user IDs or group codes like 'course_123_students')", required: true);
                s.String("subject", "Message subject", required: true);
                s.String("body", "Message body (supports HTML)", required: true);
                s.String("context_code", "Context for the message (e.g., 'course_123')");
                s.Boolean("force_new", "Force a new conversation instead of replying to existing");
            },
            async (args, ct) =>
            {
                var recipientList = Required(args, "recipients").Split(',').Select(r => r.Trim()).ToArray();
                var body = new JObject
                {
                    ["recipients"] = new JArray(recipientList),
                    ["subject"] = Required(args, "subject"),
                    ["body"] = Required(args, "body")
                };
                if (args["context_code"] != null) body["context_code"] = args.Value<string>("context_code");
                if (args["force_new"] != null) body["force_new"] = args.Value<bool>("force_new");
                return await CanvasPost("/conversations", body, ct).ConfigureAwait(false);
            });

        // ══════════════════════════════════════════════════════════════════
        // HIGH VALUE — READ OPERATIONS
        // ══════════════════════════════════════════════════════════════════

        // ── Quizzes ──

        handler.AddTool("list_quizzes",
            "List quizzes in a course. Quizzes are separate from assignments in Canvas.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search quiz titles");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/quizzes", args, ct, new[] {
                "search_term", "per_page"
            }).ConfigureAwait(false));

        // ── Student Summary (per-student analytics) ──

        handler.AddTool("get_student_summary",
            "Get per-student assignment analytics: scores, submission status, and performance across all assignments in a course.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("user_id", "The student's user ID", required: true);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/analytics/users/{Required(args, "user_id")}/assignments", null, ct).ConfigureAwait(false));

        // ── Missing Submissions ──

        handler.AddTool("list_missing_submissions",
            "List past-due assignments a student has not submitted. Essential for identifying students falling behind.",
            s => {
                s.String("user_id", "The student's user ID", required: true);
                s.String("course_ids", "Comma-separated course IDs to filter by");
                s.String("filter", "Filter: submittable (only assignments student can submit) or current_grading_period");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) =>
            {
                var userId = Required(args, "user_id");
                var extra = "include[]=course";
                if (args["filter"] != null) extra += $"&filter[]={args.Value<string>("filter")}";
                if (args["course_ids"] != null)
                {
                    foreach (var id in args.Value<string>("course_ids").Split(','))
                        extra += $"&course_ids[]={id.Trim()}";
                }
                var perPage = args.Value<int?>("per_page") ?? 10;
                extra += $"&per_page={perPage}";
                return await CanvasGet($"/users/{userId}/missing_submissions", null, ct, null, extra).ConfigureAwait(false);
            });

        // ── Rubrics ──

        handler.AddTool("list_rubrics",
            "List rubrics in a course. Shows criteria, ratings, and point values for grading.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/rubrics", args, ct, new[] {
                "per_page"
            }).ConfigureAwait(false));

        // ── Files ──

        handler.AddTool("list_files",
            "List files in a course. Shows name, size, content type, and download URL.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("search_term", "Search file names");
                s.String("sort", "Sort: name, size, created_at, updated_at, content_type");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/files", args, ct, new[] {
                "search_term", "sort", "per_page"
            }).ConfigureAwait(false));

        // ── Groups ──

        handler.AddTool("list_groups",
            "List student groups in a course. Useful for group assignments and collaboration.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/groups", args, ct, new[] {
                "per_page"
            }, "include[]=users").ConfigureAwait(false));

        // ══════════════════════════════════════════════════════════════════
        // MEDIUM VALUE
        // ══════════════════════════════════════════════════════════════════

        // ── Assignment Groups ──

        handler.AddTool("list_assignment_groups",
            "List assignment groups (categories like Homework, Exams, Participation) and their weight toward the final grade.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) => await CanvasGet($"/courses/{Required(args, "course_id")}/assignment_groups", args, ct, new[] {
                "per_page"
            }, "include[]=assignments&include[]=submission").ConfigureAwait(false));

        // ── Create Module ──

        handler.AddTool("create_module",
            "Create a new module in a course to organize content.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("name", "Module name", required: true);
                s.String("unlock_at", "Date to unlock the module (ISO 8601)");
                s.Integer("position", "Position in the module list");
                s.Boolean("require_sequential_progress", "Require items be completed in order");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var module = new JObject { ["name"] = Required(args, "name") };
                if (args["unlock_at"] != null) module["unlock_at"] = args.Value<string>("unlock_at");
                if (args["position"] != null) module["position"] = args.Value<int>("position");
                if (args["require_sequential_progress"] != null) module["require_sequential_progress"] = args.Value<bool>("require_sequential_progress");
                return await CanvasPost($"/courses/{courseId}/modules", new JObject { ["module"] = module }, ct).ConfigureAwait(false);
            });

        // ── Update Course ──

        handler.AddTool("update_course",
            "Update course settings: name, start/end dates, syllabus, default view, and more.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("name", "New course name");
                s.String("course_code", "New course code");
                s.String("start_at", "Course start date (ISO 8601)");
                s.String("end_at", "Course end date (ISO 8601)");
                s.String("syllabus_body", "Syllabus content (HTML)");
                s.String("default_view", "Landing page: feed, wiki, modules, syllabus, assignments");
                s.String("time_zone", "Course time zone (IANA format)");
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var course = new JObject();
                if (args["name"] != null) course["name"] = args.Value<string>("name");
                if (args["course_code"] != null) course["course_code"] = args.Value<string>("course_code");
                if (args["start_at"] != null) course["start_at"] = args.Value<string>("start_at");
                if (args["end_at"] != null) course["end_at"] = args.Value<string>("end_at");
                if (args["syllabus_body"] != null) course["syllabus_body"] = args.Value<string>("syllabus_body");
                if (args["default_view"] != null) course["default_view"] = args.Value<string>("default_view");
                if (args["time_zone"] != null) course["time_zone"] = args.Value<string>("time_zone");
                return await CanvasPut($"/courses/{courseId}", new JObject { ["course"] = course }, ct).ConfigureAwait(false);
            });

        // ── Calendar Events ──

        handler.AddTool("list_calendar_events",
            "List calendar events within a date range. Broader than upcoming_events — supports arbitrary date queries.",
            s => {
                s.String("context_codes", "Comma-separated context codes (e.g., 'course_123,user_456')", required: true);
                s.String("start_date", "Start date (ISO 8601)");
                s.String("end_date", "End date (ISO 8601)");
                s.String("type", "Event type: event or assignment");
                s.Integer("per_page", "Results per page (1-100)", defaultValue: 10);
            },
            async (args, ct) =>
            {
                var codes = Required(args, "context_codes").Split(',');
                var extra = string.Join("&", codes.Select(c => $"context_codes[]={c.Trim()}"));
                if (args["start_date"] != null) extra += $"&start_date={args.Value<string>("start_date")}";
                if (args["end_date"] != null) extra += $"&end_date={args.Value<string>("end_date")}";
                if (args["type"] != null) extra += $"&type={args.Value<string>("type")}";
                var perPage = args.Value<int?>("per_page") ?? 10;
                extra += $"&per_page={perPage}";
                return await CanvasGet("/calendar_events", null, ct, null, extra).ConfigureAwait(false);
            });

        // ── Publish/Unpublish Assignment ──

        handler.AddTool("publish_assignment",
            "Publish or unpublish an assignment. Published assignments are visible to students.",
            s => {
                s.String("course_id", "The course ID", required: true);
                s.String("assignment_id", "The assignment ID", required: true);
                s.Boolean("published", "true to publish, false to unpublish", required: true);
            },
            async (args, ct) =>
            {
                var courseId = Required(args, "course_id");
                var assignmentId = Required(args, "assignment_id");
                var assignment = new JObject { ["published"] = args.Value<bool>("published") };
                return await CanvasPut($"/courses/{courseId}/assignments/{assignmentId}", new JObject { ["assignment"] = assignment }, ct).ConfigureAwait(false);
            });
    }

    // ── Canvas API Helpers ───────────────────────────────────────────────

    private string BuildCanvasUrl(string path, JObject args, string[] queryParams, string extraQuery)
    {
        var url = CANVAS_API_BASE + path;
        var queryParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(extraQuery))
            queryParts.Add(extraQuery);

        if (queryParams != null && args != null)
        {
            foreach (var param in queryParams)
            {
                var cleanParam = param.Replace("[]", "");
                var val = args.Value<string>(cleanParam);
                if (!string.IsNullOrWhiteSpace(val))
                    queryParts.Add($"{param}={Uri.EscapeDataString(val)}");
            }
        }

        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        return url;
    }

    private async Task<JObject> ParseCanvasResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["error"] = true,
                ["status_code"] = (int)response.StatusCode,
                ["message"] = content
            };
        }

        try
        {
            var parsed = JToken.Parse(content);

            if (parsed is JArray arr)
            {
                return new JObject
                {
                    ["count"] = arr.Count,
                    ["results"] = arr
                };
            }

            return parsed as JObject ?? new JObject { ["data"] = parsed };
        }
        catch
        {
            return new JObject { ["raw"] = content };
        }
    }

    private async Task<JObject> CanvasGet(string path, JObject args, CancellationToken ct,
        string[] queryParams = null, string extraQuery = null)
    {
        var url = BuildCanvasUrl(path, args, queryParams, extraQuery);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseCanvasResponse(response).ConfigureAwait(false);
    }

    private async Task<JObject> CanvasPost(string path, JObject body, CancellationToken ct)
    {
        var url = CANVAS_API_BASE + path;

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new StringContent(
            body.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json");

        var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseCanvasResponse(response).ConfigureAwait(false);
    }

    private async Task<JObject> CanvasPut(string path, JObject body, CancellationToken ct)
    {
        var url = CANVAS_API_BASE + path;

        var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new StringContent(
            body.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json");

        var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
        return await ParseCanvasResponse(response).ConfigureAwait(false);
    }

    private static string Required(JObject args, string name)
    {
        var val = args?.Value<string>(name);
        if (string.IsNullOrWhiteSpace(val))
            throw new ArgumentException($"'{name}' is required");
        return val;
    }

    // ── Application Insights ─────────────────────────────────────────────

    private void LogToAppInsights(string eventName, object properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        try
        {
            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = properties != null
                            ? JObject.FromObject(properties)
                            : new JObject()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            request.Content = new StringContent(
                telemetry.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");

            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
