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
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Hybrid connector: serves MCP requests via InvokeMCP, wraps VTT transcript ║
// ║  content for REST operations, and passes all other operations through to    ║
// ║  Microsoft Graph.                                                          ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "graph-meeting-transcripts-mcp",
            Version = "1.0.0",
            Title = "Graph Meeting Transcripts MCP",
            Description = "Microsoft Graph tools for Teams meeting transcripts, recordings, attendance, AI insights, and virtual events (webinars, town halls, sessions, presenters, registrations)."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities { Tools = true }
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = operationId });

            HttpResponseMessage response;

            switch (operationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetMyTranscriptContent":
                case "GetUserTranscriptContent":
                case "GetMyTranscriptMetadata":
                case "GetUserTranscriptMetadata":
                    response = await HandleTranscriptContentAsync().ConfigureAwait(false);
                    break;
                case "GetMyRecordingContent":
                case "GetUserRecordingContent":
                    response = await HandleRecordingContentAsync().ConfigureAwait(false);
                    break;
                default:
                    response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
                    break;
            }

            var duration = DateTime.UtcNow - startTime;
            await LogToAppInsights("RequestCompleted", new { CorrelationId = correlationId, OperationId = operationId, DurationMs = duration.TotalMilliseconds, StatusCode = (int)response.StatusCode });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new { CorrelationId = correlationId, OperationId = operationId, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name });
            throw;
        }
    }

    // ── MCP Handler ──────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        var handler = new McpRequestHandler(Options);
        RegisterTools(handler);

        handler.OnLog = (eventName, data) =>
        {
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ── Meeting Operations ───────────────────────────────────────────

        handler.AddTool("find_meeting",
            "Find an online meeting by its Teams join URL. Returns meeting details including the meeting ID needed for transcript, recording, and attendance operations.",
            schema: s => s
                .String("join_url", "The Teams meeting join URL", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var joinUrl = RequireArgument(args, "join_url");
                var userId = GetArgument(args, "user_id");
                var filter = $"JoinWebUrl eq '{joinUrl.Replace("'", "''")}'";
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings?$filter={Uri.EscapeDataString(filter)}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_meeting",
            "Get details of an online meeting by its ID.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_meeting",
            "Create a new online meeting with a subject and time range.",
            schema: s => s
                .String("subject", "Meeting subject", required: true)
                .String("start_time", "Start date/time in ISO 8601 format (e.g. 2026-03-15T09:00:00)", required: true)
                .String("end_time", "End date/time in ISO 8601 format (e.g. 2026-03-15T10:00:00)", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var userId = GetArgument(args, "user_id");
                var body = new JObject
                {
                    ["subject"] = RequireArgument(args, "subject"),
                    ["startDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "start_time"),
                        ["timeZone"] = "UTC"
                    },
                    ["endDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "end_time"),
                        ["timeZone"] = "UTC"
                    }
                };
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"{GraphBase(userId)}/onlineMeetings", body);
            });

        handler.AddTool("update_meeting",
            "Update properties of an existing online meeting.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("subject", "New meeting subject")
                .String("start_time", "New start date/time in ISO 8601 format")
                .String("end_time", "New end date/time in ISO 8601 format")
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                var body = new JObject();
                var subject = GetArgument(args, "subject");
                var startTime = GetArgument(args, "start_time");
                var endTime = GetArgument(args, "end_time");
                if (subject != null) body["subject"] = subject;
                if (startTime != null) body["startDateTime"] = new JObject { ["dateTime"] = startTime, ["timeZone"] = "UTC" };
                if (endTime != null) body["endDateTime"] = new JObject { ["dateTime"] = endTime, ["timeZone"] = "UTC" };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"),
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}", body);
            });

        handler.AddTool("delete_meeting",
            "Delete an online meeting.",
            schema: s => s
                .String("meeting_id", "The online meeting ID to delete", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Delete,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}");
            },
            annotations: a => { a["destructiveHint"] = true; });

        // ── Transcript Operations ────────────────────────────────────────

        handler.AddTool("list_transcripts",
            "List all transcripts for a meeting.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/transcripts");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_transcript_content",
            "Get the text content of a meeting transcript in VTT format.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("transcript_id", "The transcript ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var transcriptId = RequireArgument(args, "transcript_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphTextRequestAsync(
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content?$format=text/vtt");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── Recording Operations ─────────────────────────────────────────

        handler.AddTool("list_recordings",
            "List all recordings for a meeting.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/recordings");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── Attendance Operations ────────────────────────────────────────

        handler.AddTool("list_attendance_reports",
            "List attendance reports for a meeting.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/attendanceReports");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_attendance_records",
            "Get individual attendance records for a specific attendance report.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("report_id", "The attendance report ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var reportId = RequireArgument(args, "report_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/attendanceReports/{reportId}/attendanceRecords");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── AI Insights ──────────────────────────────────────────────────

        handler.AddTool("list_ai_insights",
            "List AI-generated insights for a meeting, including summaries and action items.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/aiInsights");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_ai_insight",
            "Get a specific AI insight with detailed content.",
            schema: s => s
                .String("meeting_id", "The online meeting ID", required: true)
                .String("insight_id", "The AI insight ID", required: true)
                .String("user_id", "User ID or email (omit for signed-in user)"),
            handler: async (args, ct) =>
            {
                var meetingId = RequireArgument(args, "meeting_id");
                var insightId = RequireArgument(args, "insight_id");
                var userId = GetArgument(args, "user_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"{GraphBase(userId)}/onlineMeetings/{meetingId}/aiInsights/{insightId}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── Webinar Operations ───────────────────────────────────────────

        handler.AddTool("list_webinars",
            "List webinars. Optionally filter by user role.",
            schema: s => s
                .String("role", "Filter by role: organizer, coOrganizer")
                .Integer("top", "Maximum number of results"),
            handler: async (args, ct) =>
            {
                var role = GetArgument(args, "role");
                var top = args.Value<int?>("top");
                var url = "https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars";
                var query = new List<string>();
                if (role != null) query.Add($"role={Uri.EscapeDataString(role)}");
                if (top.HasValue) query.Add($"$top={top.Value}");
                if (query.Count > 0) url += "?" + string.Join("&", query);
                return await SendGraphRequestAsync(HttpMethod.Get, url);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_webinar",
            "Create a new webinar with a display name and time range.",
            schema: s => s
                .String("display_name", "Webinar display name", required: true)
                .String("start_time", "Start date/time in ISO 8601 format", required: true)
                .String("end_time", "End date/time in ISO 8601 format", required: true)
                .String("description", "Webinar description"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["displayName"] = RequireArgument(args, "display_name"),
                    ["startDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "start_time"),
                        ["timeZone"] = "UTC"
                    },
                    ["endDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "end_time"),
                        ["timeZone"] = "UTC"
                    }
                };
                var desc = GetArgument(args, "description");
                if (desc != null) body["description"] = desc;
                return await SendGraphRequestAsync(HttpMethod.Post,
                    "https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars", body);
            });

        handler.AddTool("get_webinar",
            "Get details of a webinar.",
            schema: s => s.String("webinar_id", "The webinar ID", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("publish_webinar",
            "Publish a draft webinar to make it visible to attendees.",
            schema: s => s.String("webinar_id", "The webinar ID", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}/publish");
            });

        handler.AddTool("cancel_webinar",
            "Cancel a webinar.",
            schema: s => s.String("webinar_id", "The webinar ID", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}/cancel");
            },
            annotations: a => { a["destructiveHint"] = true; });

        // ── Town Hall Operations ─────────────────────────────────────────

        handler.AddTool("list_townhalls",
            "List town halls. Optionally filter by user role.",
            schema: s => s
                .String("role", "Filter by role: organizer, coOrganizer")
                .Integer("top", "Maximum number of results"),
            handler: async (args, ct) =>
            {
                var role = GetArgument(args, "role");
                var top = args.Value<int?>("top");
                var url = "https://graph.microsoft.com/v1.0/solutions/virtualEvents/townhalls";
                var query = new List<string>();
                if (role != null) query.Add($"role={Uri.EscapeDataString(role)}");
                if (top.HasValue) query.Add($"$top={top.Value}");
                if (query.Count > 0) url += "?" + string.Join("&", query);
                return await SendGraphRequestAsync(HttpMethod.Get, url);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_townhall",
            "Create a new town hall with a display name and time range.",
            schema: s => s
                .String("display_name", "Town hall display name", required: true)
                .String("start_time", "Start date/time in ISO 8601 format", required: true)
                .String("end_time", "End date/time in ISO 8601 format", required: true)
                .String("description", "Town hall description"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["displayName"] = RequireArgument(args, "display_name"),
                    ["startDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "start_time"),
                        ["timeZone"] = "UTC"
                    },
                    ["endDateTime"] = new JObject
                    {
                        ["dateTime"] = RequireArgument(args, "end_time"),
                        ["timeZone"] = "UTC"
                    }
                };
                var desc = GetArgument(args, "description");
                if (desc != null) body["description"] = desc;
                return await SendGraphRequestAsync(HttpMethod.Post,
                    "https://graph.microsoft.com/v1.0/solutions/virtualEvents/townhalls", body);
            });

        handler.AddTool("get_townhall",
            "Get details of a town hall.",
            schema: s => s.String("townhall_id", "The town hall ID", required: true),
            handler: async (args, ct) =>
            {
                var townhallId = RequireArgument(args, "townhall_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/townhalls/{townhallId}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("publish_townhall",
            "Publish a draft town hall.",
            schema: s => s.String("townhall_id", "The town hall ID", required: true),
            handler: async (args, ct) =>
            {
                var townhallId = RequireArgument(args, "townhall_id");
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/townhalls/{townhallId}/publish");
            });

        handler.AddTool("cancel_townhall",
            "Cancel a town hall.",
            schema: s => s.String("townhall_id", "The town hall ID", required: true),
            handler: async (args, ct) =>
            {
                var townhallId = RequireArgument(args, "townhall_id");
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/townhalls/{townhallId}/cancel");
            },
            annotations: a => { a["destructiveHint"] = true; });

        // ── Session Operations ───────────────────────────────────────────

        handler.AddTool("list_sessions",
            "List sessions for a webinar or town hall.",
            schema: s => s
                .String("event_type", "Event type: webinars or townhalls", required: true, enumValues: new[] { "webinars", "townhalls" })
                .String("event_id", "The webinar or town hall ID", required: true),
            handler: async (args, ct) =>
            {
                var eventType = RequireArgument(args, "event_type");
                var eventId = RequireArgument(args, "event_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/{eventType}/{eventId}/sessions");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_session",
            "Get details of a specific session for a webinar or town hall.",
            schema: s => s
                .String("event_type", "Event type: webinars or townhalls", required: true, enumValues: new[] { "webinars", "townhalls" })
                .String("event_id", "The webinar or town hall ID", required: true)
                .String("session_id", "The session ID", required: true),
            handler: async (args, ct) =>
            {
                var eventType = RequireArgument(args, "event_type");
                var eventId = RequireArgument(args, "event_id");
                var sessionId = RequireArgument(args, "session_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/{eventType}/{eventId}/sessions/{sessionId}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── Presenter Operations ─────────────────────────────────────────

        handler.AddTool("list_presenters",
            "List presenters for a webinar or town hall.",
            schema: s => s
                .String("event_type", "Event type: webinars or townhalls", required: true, enumValues: new[] { "webinars", "townhalls" })
                .String("event_id", "The webinar or town hall ID", required: true),
            handler: async (args, ct) =>
            {
                var eventType = RequireArgument(args, "event_type");
                var eventId = RequireArgument(args, "event_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/{eventType}/{eventId}/presenters");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("add_presenter",
            "Add a presenter to a webinar or town hall.",
            schema: s => s
                .String("event_type", "Event type: webinars or townhalls", required: true, enumValues: new[] { "webinars", "townhalls" })
                .String("event_id", "The webinar or town hall ID", required: true)
                .String("presenter_user_id", "Azure AD user ID of the presenter", required: true)
                .String("presenter_name", "Display name of the presenter"),
            handler: async (args, ct) =>
            {
                var eventType = RequireArgument(args, "event_type");
                var eventId = RequireArgument(args, "event_id");
                var body = new JObject
                {
                    ["identity"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.communicationsUserIdentity",
                        ["id"] = RequireArgument(args, "presenter_user_id"),
                        ["displayName"] = GetArgument(args, "presenter_name", "")
                    }
                };
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/{eventType}/{eventId}/presenters", body);
            });

        handler.AddTool("remove_presenter",
            "Remove a presenter from a webinar or town hall.",
            schema: s => s
                .String("event_type", "Event type: webinars or townhalls", required: true, enumValues: new[] { "webinars", "townhalls" })
                .String("event_id", "The webinar or town hall ID", required: true)
                .String("presenter_id", "The presenter ID to remove", required: true),
            handler: async (args, ct) =>
            {
                var eventType = RequireArgument(args, "event_type");
                var eventId = RequireArgument(args, "event_id");
                var presenterId = RequireArgument(args, "presenter_id");
                return await SendGraphRequestAsync(HttpMethod.Delete,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/{eventType}/{eventId}/presenters/{presenterId}");
            },
            annotations: a => { a["destructiveHint"] = true; });

        // ── Webinar Registration Operations ──────────────────────────────

        handler.AddTool("list_registrations",
            "List registrations for a webinar.",
            schema: s => s.String("webinar_id", "The webinar ID", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                return await SendGraphRequestAsync(HttpMethod.Get,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}/registrations");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_registration",
            "Register a user for a webinar.",
            schema: s => s
                .String("webinar_id", "The webinar ID", required: true)
                .String("first_name", "Registrant first name", required: true)
                .String("last_name", "Registrant last name", required: true)
                .String("email", "Registrant email address", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                var body = new JObject
                {
                    ["firstName"] = RequireArgument(args, "first_name"),
                    ["lastName"] = RequireArgument(args, "last_name"),
                    ["email"] = RequireArgument(args, "email")
                };
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}/registrations", body);
            });

        handler.AddTool("cancel_registration",
            "Cancel a webinar registration.",
            schema: s => s
                .String("webinar_id", "The webinar ID", required: true)
                .String("registration_id", "The registration ID to cancel", required: true),
            handler: async (args, ct) =>
            {
                var webinarId = RequireArgument(args, "webinar_id");
                var registrationId = RequireArgument(args, "registration_id");
                return await SendGraphRequestAsync(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/solutions/virtualEvents/webinars/{webinarId}/registrations/{registrationId}/cancel");
            },
            annotations: a => { a["destructiveHint"] = true; });
    }

    // ── REST Transcript Content Handler ──────────────────────────────────

    private async Task<HttpResponseMessage> HandleTranscriptContentAsync()
    {
        var response = await this.Context.SendAsync(
            this.Context.Request,
            this.CancellationToken
        ).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var result = new JObject
            {
                ["content"] = content,
                ["contentType"] = response.Content.Headers.ContentType?.ToString() ?? "text/vtt"
            };

            response.Content = new StringContent(
                result.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
        }

        return response;
    }

    // ── REST Recording Content Handler ────────────────────────────────────

    private async Task<HttpResponseMessage> HandleRecordingContentAsync()
    {
        // Use Context.SendAsync — Power Platform does not allow new HttpClient instances.
        // The runtime follows redirects automatically, so we get the final content directly.
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var result = new JObject
            {
                ["content"] = Convert.ToBase64String(bytes),
                ["contentType"] = contentType,
                ["encoding"] = "base64"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    result.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
        }

        return response;
    }

    // ── Graph API Helpers ────────────────────────────────────────────────

    private static string GraphBase(string userId = null) =>
        userId != null
            ? $"https://graph.microsoft.com/v1.0/users/{userId}"
            : "https://graph.microsoft.com/v1.0/me";

    private async Task<JObject> SendGraphRequestAsync(HttpMethod method, string url, JObject body = null)
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
            throw new Exception($"Graph API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private async Task<JObject> SendGraphTextRequestAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Graph API error ({(int)response.StatusCode}): {content}");

        return new JObject
        {
            ["content"] = content,
            ["contentType"] = response.Content.Headers.ContentType?.ToString() ?? "text/vtt"
        };
    }

    // ── Utility Helpers ──────────────────────────────────────────────────

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

    private async Task LogToAppInsights(string eventName, object properties, string correlationId = null)
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
                ["ServerVersion"] = Options.ServerInfo.Version
            };

            if (correlationId != null)
                propsDict["CorrelationId"] = correlationId;

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
            // Suppress telemetry errors — never fail a request due to logging
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
// ║  SECTION 2: MCP FRAMEWORK                                                    ║
// ║                                                                              ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power         ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section    ║
// ║  becomes a using statement instead of inline code.                           ║
// ║                                                                              ║
// ║  Spec coverage: MCP 2025-11-25                                               ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,                 ║
// ║           completion/complete, logging/setLevel, all notifications           ║
// ║                                                                              ║
// ║  Stateless limitations (Power Platform cannot send async notifications):     ║
// ║   - Tasks (experimental, requires persistent state between requests)         ║
// ║   - Server→client requests (sampling, elicitation, roots/list)               ║
// ║   - Server→client notifications (progress, logging/message, list_changed)    ║
// ║                                                                              ║
// ║  Do not modify unless extending the framework itself.                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported in initialize response.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

/// <summary>Capabilities declared during initialization.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>Standard JSON-RPC 2.0 error codes used by MCP.</summary>
public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
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

/// <summary>Fluent builder for JSON Schema objects used in tool inputSchema.</summary>
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
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
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
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// </summary>
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

        JObject annotationsObj = null;
        if (annotations != null)
        {
            annotationsObj = new JObject();
            annotations(annotationsObj);
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
            Annotations = annotationsObj,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// </summary>
    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
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

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

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

    // ── Protocol Handlers ────────────────────────────────────────────────

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

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

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = clientProtocolVersion
        });

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
            return SerializeSuccess(id, new JObject
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
            return SerializeSuccess(id, new JObject
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

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ── Content Helpers ──────────────────────────────────────────────────

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
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
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
