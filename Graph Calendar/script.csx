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
    // Application Insights connection string (leave empty to disable telemetry)
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    private static readonly string SERVER_NAME = "graph-calendar-mcp";
    private static readonly string SERVER_VERSION = "1.0.0";
    private static readonly string DEFAULT_PROTOCOL_VERSION = "2025-12-01";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = operationId });

            HttpResponseMessage response;

            if (operationId == "InvokeMCP")
            {
                response = await HandleMCPRequestAsync(correlationId).ConfigureAwait(false);
            }
            else
            {
                response = await HandleRESTRequestAsync(operationId).ConfigureAwait(false);
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

    #region MCP Protocol Handlers

    private async Task<HttpResponseMessage> HandleMCPRequestAsync(string correlationId)
    {
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);
            if (!request.ContainsKey("jsonrpc")) request["jsonrpc"] = "2.0";

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject;

            await LogToAppInsights("MCPMethod", new { CorrelationId = correlationId, Method = method });

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(@params, id);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id, correlationId).ConfigureAwait(false);
                default:
                    await LogToAppInsights("MCPUnknownMethod", new { CorrelationId = correlationId, Method = method ?? "null" });
                    return CreateError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("MCPParseError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateError(null, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken id)
    {
        var protocolVersion = @params?["protocolVersion"]?.ToString() ?? DEFAULT_PROTOCOL_VERSION;
        var result = new JObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Microsoft Graph Calendar MCP",
                ["description"] = "Enterprise calendar management tools via Microsoft Graph - view calendars, find meeting times, schedule meetings across users"
            }
        };
        return CreateSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        return CreateSuccess(new JObject { ["tools"] = GetToolDefinitions() }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateError(id, -32602, "Tool name required", "name parameter is required");

        var toolStartTime = DateTime.UtcNow;
        try
        {
            await LogToAppInsights("ToolExecuting", new { CorrelationId = correlationId, Tool = toolName });
            var result = await ExecuteToolAsync(toolName, args, id).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            await LogToAppInsights("ToolExecuted", new { CorrelationId = correlationId, Tool = toolName, DurationMs = toolDuration.TotalMilliseconds, Success = true });
            return result;
        }
        catch (ArgumentException ex)
        {
            await LogToAppInsights("ToolError", new { CorrelationId = correlationId, Tool = toolName, Error = ex.Message, ErrorType = "ArgumentException" });
            return CreateToolResult(ex.Message, true, id);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("ToolError", new { CorrelationId = correlationId, Tool = toolName, Error = ex.Message, ErrorType = ex.GetType().Name });
            return CreateToolResult($"Tool error: {ex.Message}", true, id);
        }
    }

    #endregion

    #region Tool Definitions

    private JArray GetToolDefinitions()
    {
        return new JArray
        {
            // === CALENDAR VIEW TOOLS ===
            CreateToolDef("getMyCalendarView", "Get my calendar events within a date range. Use this to see what's on my schedule.",
                new JObject { 
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601, e.g., 2026-01-08T00:00:00)", true), 
                    ["endDateTime"] = StrProp("End date/time (ISO 8601, e.g., 2026-01-08T23:59:59)", true),
                    ["top"] = IntProp("Max number of events to return (default 50)")
                }, new[] { "startDateTime", "endDateTime" }),
            
            CreateToolDef("getUserCalendarView", "Get calendar events for a specific user within a date range. Use this to check another person's schedule.",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601)", true), 
                    ["endDateTime"] = StrProp("End date/time (ISO 8601)", true),
                    ["top"] = IntProp("Max number of events to return")
                }, new[] { "userId", "startDateTime", "endDateTime" }),

            CreateToolDef("getMultipleCalendarViews", "Get calendar events for multiple users within a date range. Use for viewing leadership team calendars.",
                new JObject { 
                    ["userEmails"] = ArrProp("Array of user email addresses", true),
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601)", true), 
                    ["endDateTime"] = StrProp("End date/time (ISO 8601)", true),
                    ["top"] = IntProp("Max events per user (default 20)")
                }, new[] { "userEmails", "startDateTime", "endDateTime" }),

            // === AVAILABILITY TOOLS ===
            CreateToolDef("getSchedule", "Get free/busy availability for multiple users. Returns availability status for each time slot.",
                new JObject { 
                    ["schedules"] = ArrProp("Array of email addresses to check availability", true),
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601)", true), 
                    ["endDateTime"] = StrProp("End date/time (ISO 8601)", true),
                    ["timeZone"] = StrProp("Time zone (e.g., Pacific Standard Time, default UTC)"),
                    ["availabilityViewInterval"] = IntProp("Slot duration in minutes (default 30)")
                }, new[] { "schedules", "startDateTime", "endDateTime" }),

            CreateToolDef("findMeetingTimes", "Find optimal meeting times when multiple attendees are available. Returns suggested time slots ranked by availability.",
                new JObject { 
                    ["attendees"] = ArrProp("Array of attendee email addresses", true),
                    ["meetingDuration"] = StrProp("Duration in ISO 8601 format (e.g., PT1H for 1 hour, PT30M for 30 min)", true),
                    ["startDateTime"] = StrProp("Earliest possible start time (ISO 8601)", true),
                    ["endDateTime"] = StrProp("Latest possible end time (ISO 8601)", true),
                    ["timeZone"] = StrProp("Time zone (default UTC)"),
                    ["maxCandidates"] = IntProp("Max number of suggestions (default 5)"),
                    ["minimumAttendeePercentage"] = NumProp("Min percentage of attendees required (0-100, default 100)"),
                    ["isOrganizerOptional"] = BoolProp("Whether organizer attendance is optional")
                }, new[] { "attendees", "meetingDuration", "startDateTime", "endDateTime" }),

            // === EVENT MANAGEMENT TOOLS ===
            CreateToolDef("createEvent", "Create a new calendar event/meeting. Can include attendees and create Teams meetings.",
                new JObject { 
                    ["subject"] = StrProp("Event title/subject", true),
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601)", true),
                    ["endDateTime"] = StrProp("End date/time (ISO 8601)", true),
                    ["timeZone"] = StrProp("Time zone (default UTC)"),
                    ["attendees"] = ArrProp("Array of attendee email addresses"),
                    ["body"] = StrProp("Event description/body (HTML or text)"),
                    ["location"] = StrProp("Location name"),
                    ["isOnlineMeeting"] = BoolProp("Create Teams meeting (default false)"),
                    ["isAllDay"] = BoolProp("All-day event (default false)"),
                    ["reminderMinutes"] = IntProp("Reminder before event in minutes"),
                    ["importance"] = StrProp("Importance: low, normal, high"),
                    ["showAs"] = StrProp("Show as: free, tentative, busy, oof, workingElsewhere")
                }, new[] { "subject", "startDateTime", "endDateTime" }),

            CreateToolDef("createEventForUser", "Create a calendar event on behalf of another user (requires delegate permissions).",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["subject"] = StrProp("Event title/subject", true),
                    ["startDateTime"] = StrProp("Start date/time (ISO 8601)", true),
                    ["endDateTime"] = StrProp("End date/time (ISO 8601)", true),
                    ["timeZone"] = StrProp("Time zone (default UTC)"),
                    ["attendees"] = ArrProp("Array of attendee email addresses"),
                    ["body"] = StrProp("Event description"),
                    ["location"] = StrProp("Location name"),
                    ["isOnlineMeeting"] = BoolProp("Create Teams meeting")
                }, new[] { "userId", "subject", "startDateTime", "endDateTime" }),

            CreateToolDef("updateEvent", "Update an existing calendar event in my calendar.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID", true),
                    ["subject"] = StrProp("New subject/title"),
                    ["startDateTime"] = StrProp("New start date/time (ISO 8601)"),
                    ["endDateTime"] = StrProp("New end date/time (ISO 8601)"),
                    ["timeZone"] = StrProp("Time zone"),
                    ["body"] = StrProp("New description"),
                    ["location"] = StrProp("New location"),
                    ["isOnlineMeeting"] = BoolProp("Add/remove Teams meeting")
                }, new[] { "eventId" }),

            CreateToolDef("updateUserEvent", "Update an event in another user's calendar (requires delegate permissions).",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["eventId"] = StrProp("Event ID", true),
                    ["subject"] = StrProp("New subject/title"),
                    ["startDateTime"] = StrProp("New start date/time (ISO 8601)"),
                    ["endDateTime"] = StrProp("New end date/time (ISO 8601)"),
                    ["timeZone"] = StrProp("Time zone"),
                    ["body"] = StrProp("New description"),
                    ["location"] = StrProp("New location")
                }, new[] { "userId", "eventId" }),

            CreateToolDef("deleteEvent", "Delete a calendar event from my calendar.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID to delete", true)
                }, new[] { "eventId" }),

            CreateToolDef("deleteUserEvent", "Delete a calendar event from another user's calendar (requires delegate permissions).",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["eventId"] = StrProp("Event ID to delete", true)
                }, new[] { "userId", "eventId" }),

            CreateToolDef("cancelEvent", "Cancel a meeting and notify attendees with an optional message.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID to cancel", true),
                    ["comment"] = StrProp("Cancellation message to send to attendees")
                }, new[] { "eventId" }),

            // === EVENT RESPONSE TOOLS ===
            CreateToolDef("acceptEvent", "Accept a meeting invitation.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID", true),
                    ["comment"] = StrProp("Optional response message"),
                    ["sendResponse"] = BoolProp("Send response to organizer (default true)")
                }, new[] { "eventId" }),

            CreateToolDef("declineEvent", "Decline a meeting invitation.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID", true),
                    ["comment"] = StrProp("Optional response message"),
                    ["sendResponse"] = BoolProp("Send response to organizer (default true)")
                }, new[] { "eventId" }),

            CreateToolDef("tentativelyAcceptEvent", "Tentatively accept a meeting invitation.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID", true),
                    ["comment"] = StrProp("Optional response message"),
                    ["sendResponse"] = BoolProp("Send response to organizer (default true)")
                }, new[] { "eventId" }),

            // === EVENT RETRIEVAL TOOLS ===
            CreateToolDef("getEvent", "Get details of a specific calendar event from my calendar.",
                new JObject { 
                    ["eventId"] = StrProp("Event ID", true)
                }, new[] { "eventId" }),

            CreateToolDef("getUserEvent", "Get details of a specific calendar event from another user's calendar.",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["eventId"] = StrProp("Event ID", true)
                }, new[] { "userId", "eventId" }),

            CreateToolDef("listMyEvents", "List my upcoming calendar events.",
                new JObject { 
                    ["top"] = IntProp("Number of events to return (default 25)"),
                    ["filter"] = StrProp("OData filter (e.g., subject eq 'Team Meeting')"),
                    ["orderby"] = StrProp("Order by property (default start/dateTime)")
                }),

            CreateToolDef("listUserEvents", "List calendar events from another user's calendar.",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true),
                    ["top"] = IntProp("Number of events to return (default 25)"),
                    ["filter"] = StrProp("OData filter expression"),
                    ["orderby"] = StrProp("Order by property (default start/dateTime)")
                }, new[] { "userId" }),

            // === USER & GROUP TOOLS ===
            CreateToolDef("listUsers", "List users in the organization. Filter by department to find leadership team.",
                new JObject { 
                    ["filter"] = StrProp("OData filter (e.g., department eq 'Leadership' or startsWith(displayName,'John'))"),
                    ["search"] = StrProp("Search by name or email"),
                    ["top"] = IntProp("Number of users to return (default 25)"),
                    ["select"] = StrProp("Properties to return (default: id,displayName,mail,jobTitle,department)")
                }),

            CreateToolDef("getUser", "Get details of a specific user.",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true)
                }, new[] { "userId" }),

            CreateToolDef("listGroups", "List groups in the organization. Use to find leadership team groups.",
                new JObject { 
                    ["filter"] = StrProp("OData filter (e.g., displayName eq 'US Leadership Team')"),
                    ["search"] = StrProp("Search by group name"),
                    ["top"] = IntProp("Number of groups to return")
                }),

            CreateToolDef("listGroupMembers", "Get members of a group. Use to get all leadership team members.",
                new JObject { 
                    ["groupId"] = StrProp("Group ID", true),
                    ["top"] = IntProp("Number of members to return")
                }, new[] { "groupId" }),

            // === CALENDAR TOOLS ===
            CreateToolDef("listMyCalendars", "List all calendars for the current user.",
                new JObject {}),

            CreateToolDef("listUserCalendars", "List all calendars for a specific user.",
                new JObject { 
                    ["userId"] = StrProp("User email address or ID", true)
                }, new[] { "userId" })
        };
    }

    private JObject CreateToolDef(string name, string desc, JObject props, string[] required = null)
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = props };
        if (required != null && required.Length > 0) schema["required"] = new JArray(required);
        return new JObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };
    }
    private JObject StrProp(string desc, bool req = false) => new JObject { ["type"] = "string", ["description"] = desc };
    private JObject IntProp(string desc) => new JObject { ["type"] = "integer", ["description"] = desc };
    private JObject NumProp(string desc) => new JObject { ["type"] = "number", ["description"] = desc };
    private JObject BoolProp(string desc) => new JObject { ["type"] = "boolean", ["description"] = desc };
    private JObject ArrProp(string desc, bool req = false) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = desc };

    #endregion

    #region Tool Execution

    private async Task<HttpResponseMessage> ExecuteToolAsync(string toolName, JObject args, JToken id)
    {
        switch (toolName)
        {
            // Calendar View Tools
            case "getMyCalendarView":
                return await GetCalendarViewAsync("/me/calendarView", args, id);
            case "getUserCalendarView":
                return await GetCalendarViewAsync($"/users/{Arg(args, "userId")}/calendarView", args, id);
            case "getMultipleCalendarViews":
                return await GetMultipleCalendarViewsAsync(args, id);

            // Availability Tools
            case "getSchedule":
                return await GetScheduleAsync(args, id);
            case "findMeetingTimes":
                return await FindMeetingTimesAsync(args, id);

            // Event Management Tools
            case "createEvent":
                return await CreateEventAsync("/me/calendar/events", args, id);
            case "createEventForUser":
                return await CreateEventAsync($"/users/{Arg(args, "userId")}/calendar/events", args, id);
            case "updateEvent":
                return await UpdateEventAsync("/me/calendar/events", args, id);
            case "updateUserEvent":
                return await UpdateEventAsync($"/users/{Arg(args, "userId")}/calendar/events", args, id);
            case "deleteEvent":
                return await CallGraphAsync("DELETE", $"/me/calendar/events/{Arg(args, "eventId")}", null, id);
            case "deleteUserEvent":
                return await CallGraphAsync("DELETE", $"/users/{Arg(args, "userId")}/calendar/events/{Arg(args, "eventId")}", null, id);
            case "cancelEvent":
                return await CancelEventAsync(args, id);

            // Event Response Tools
            case "acceptEvent":
                return await RespondToEventAsync("accept", args, id);
            case "declineEvent":
                return await RespondToEventAsync("decline", args, id);
            case "tentativelyAcceptEvent":
                return await RespondToEventAsync("tentativelyAccept", args, id);

            // Event Retrieval Tools
            case "getEvent":
                return await CallGraphAsync("GET", $"/me/calendar/events/{Arg(args, "eventId")}", null, id);
            case "getUserEvent":
                return await CallGraphAsync("GET", $"/users/{Arg(args, "userId")}/calendar/events/{Arg(args, "eventId")}", null, id);
            case "listMyEvents":
                return await ListEventsAsync("/me/calendar/events", args, id);
            case "listUserEvents":
                return await ListEventsAsync($"/users/{Arg(args, "userId")}/calendar/events", args, id);

            // User & Group Tools
            case "listUsers":
                return await ListUsersAsync(args, id);
            case "getUser":
                return await CallGraphAsync("GET", $"/users/{Arg(args, "userId")}?$select=id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation", null, id);
            case "listGroups":
                return await ListGroupsAsync(args, id);
            case "listGroupMembers":
                return await CallGraphAsync("GET", $"/groups/{Arg(args, "groupId")}/members?$top={ArgOpt(args, "top") ?? "50"}", null, id);

            // Calendar Tools
            case "listMyCalendars":
                return await CallGraphAsync("GET", "/me/calendars", null, id);
            case "listUserCalendars":
                return await CallGraphAsync("GET", $"/users/{Arg(args, "userId")}/calendars", null, id);

            default:
                return CreateError(id, -32601, "Unknown tool", toolName);
        }
    }

    #endregion

    #region Tool Implementations

    private async Task<HttpResponseMessage> GetCalendarViewAsync(string basePath, JObject args, JToken id)
    {
        var start = Uri.EscapeDataString(Arg(args, "startDateTime"));
        var end = Uri.EscapeDataString(Arg(args, "endDateTime"));
        var top = ArgOpt(args, "top") ?? "50";
        var path = $"{basePath}?startDateTime={start}&endDateTime={end}&$top={top}&$orderby=start/dateTime&$select=id,subject,start,end,location,organizer,attendees,isOnlineMeeting,onlineMeetingUrl,showAs,importance,isAllDay";
        return await CallGraphAsync("GET", path, null, id);
    }

    private async Task<HttpResponseMessage> GetMultipleCalendarViewsAsync(JObject args, JToken id)
    {
        var emails = args["userEmails"] as JArray;
        if (emails == null || emails.Count == 0)
            throw new ArgumentException("userEmails is required and must be a non-empty array");

        var start = Arg(args, "startDateTime");
        var end = Arg(args, "endDateTime");
        var top = ArgOpt(args, "top") ?? "20";
        
        var results = new JArray();
        foreach (var email in emails)
        {
            var emailStr = email.ToString();
            try
            {
                var path = $"/users/{Uri.EscapeDataString(emailStr)}/calendarView?startDateTime={Uri.EscapeDataString(start)}&endDateTime={Uri.EscapeDataString(end)}&$top={top}&$orderby=start/dateTime&$select=id,subject,start,end,location,organizer,isOnlineMeeting,showAs";
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0{path}");
                request.Headers.Add("Accept", "application/json");
                var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                
                results.Add(new JObject
                {
                    ["user"] = emailStr,
                    ["success"] = response.IsSuccessStatusCode,
                    ["events"] = response.IsSuccessStatusCode ? JObject.Parse(content)["value"] : null,
                    ["error"] = response.IsSuccessStatusCode ? null : content
                });
            }
            catch (Exception ex)
            {
                results.Add(new JObject
                {
                    ["user"] = emailStr,
                    ["success"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        return CreateToolResult(results.ToString(Newtonsoft.Json.Formatting.Indented), false, id);
    }

    private async Task<HttpResponseMessage> GetScheduleAsync(JObject args, JToken id)
    {
        var schedules = args["schedules"] as JArray;
        if (schedules == null || schedules.Count == 0)
            throw new ArgumentException("schedules is required and must be a non-empty array");

        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        var interval = ArgOpt(args, "availabilityViewInterval") ?? "30";

        var body = new JObject
        {
            ["schedules"] = schedules,
            ["startTime"] = new JObject { ["dateTime"] = Arg(args, "startDateTime"), ["timeZone"] = tz },
            ["endTime"] = new JObject { ["dateTime"] = Arg(args, "endDateTime"), ["timeZone"] = tz },
            ["availabilityViewInterval"] = int.Parse(interval)
        };

        return await CallGraphAsync("POST", "/me/calendar/getSchedule", body, id);
    }

    private async Task<HttpResponseMessage> FindMeetingTimesAsync(JObject args, JToken id)
    {
        var attendeeEmails = args["attendees"] as JArray;
        if (attendeeEmails == null || attendeeEmails.Count == 0)
            throw new ArgumentException("attendees is required and must be a non-empty array");

        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        var attendees = new JArray();
        foreach (var email in attendeeEmails)
        {
            attendees.Add(new JObject
            {
                ["emailAddress"] = new JObject { ["address"] = email.ToString() },
                ["type"] = "required"
            });
        }

        var body = new JObject
        {
            ["attendees"] = attendees,
            ["meetingDuration"] = Arg(args, "meetingDuration"),
            ["timeConstraint"] = new JObject
            {
                ["activityDomain"] = "work",
                ["timeSlots"] = new JArray
                {
                    new JObject
                    {
                        ["start"] = new JObject { ["dateTime"] = Arg(args, "startDateTime"), ["timeZone"] = tz },
                        ["end"] = new JObject { ["dateTime"] = Arg(args, "endDateTime"), ["timeZone"] = tz }
                    }
                }
            },
            ["returnSuggestionReasons"] = true
        };

        var maxCandidates = ArgOpt(args, "maxCandidates");
        if (!string.IsNullOrEmpty(maxCandidates)) body["maxCandidates"] = int.Parse(maxCandidates);

        var minPercent = ArgOpt(args, "minimumAttendeePercentage");
        if (!string.IsNullOrEmpty(minPercent)) body["minimumAttendeePercentage"] = double.Parse(minPercent);

        var orgOptional = args["isOrganizerOptional"];
        if (orgOptional != null) body["isOrganizerOptional"] = orgOptional;

        return await CallGraphAsync("POST", "/me/findMeetingTimes", body, id);
    }

    private async Task<HttpResponseMessage> CreateEventAsync(string path, JObject args, JToken id)
    {
        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        var body = new JObject
        {
            ["subject"] = Arg(args, "subject"),
            ["start"] = new JObject { ["dateTime"] = Arg(args, "startDateTime"), ["timeZone"] = tz },
            ["end"] = new JObject { ["dateTime"] = Arg(args, "endDateTime"), ["timeZone"] = tz }
        };

        // Optional: Attendees
        var attendeeEmails = args["attendees"] as JArray;
        if (attendeeEmails != null && attendeeEmails.Count > 0)
        {
            var attendees = new JArray();
            foreach (var email in attendeeEmails)
            {
                attendees.Add(new JObject
                {
                    ["emailAddress"] = new JObject { ["address"] = email.ToString() },
                    ["type"] = "required"
                });
            }
            body["attendees"] = attendees;
        }

        // Optional: Body
        var bodyText = ArgOpt(args, "body");
        if (!string.IsNullOrEmpty(bodyText))
        {
            body["body"] = new JObject
            {
                ["contentType"] = bodyText.Contains("<") ? "html" : "text",
                ["content"] = bodyText
            };
        }

        // Optional: Location
        var location = ArgOpt(args, "location");
        if (!string.IsNullOrEmpty(location))
        {
            body["location"] = new JObject { ["displayName"] = location };
        }

        // Optional: Online meeting
        var isOnline = args["isOnlineMeeting"];
        if (isOnline != null && isOnline.Type == JTokenType.Boolean && (bool)isOnline)
        {
            body["isOnlineMeeting"] = true;
            body["onlineMeetingProvider"] = "teamsForBusiness";
        }

        // Optional: All day
        var isAllDay = args["isAllDay"];
        if (isAllDay != null) body["isAllDay"] = isAllDay;

        // Optional: Reminder
        var reminder = ArgOpt(args, "reminderMinutes");
        if (!string.IsNullOrEmpty(reminder))
        {
            body["reminderMinutesBeforeStart"] = int.Parse(reminder);
            body["isReminderOn"] = true;
        }

        // Optional: Importance
        var importance = ArgOpt(args, "importance");
        if (!string.IsNullOrEmpty(importance)) body["importance"] = importance;

        // Optional: Show as
        var showAs = ArgOpt(args, "showAs");
        if (!string.IsNullOrEmpty(showAs)) body["showAs"] = showAs;

        return await CallGraphAsync("POST", path, body, id);
    }

    private async Task<HttpResponseMessage> UpdateEventAsync(string basePath, JObject args, JToken id)
    {
        var eventId = Arg(args, "eventId");
        var body = new JObject();

        var subject = ArgOpt(args, "subject");
        if (!string.IsNullOrEmpty(subject)) body["subject"] = subject;

        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        var start = ArgOpt(args, "startDateTime");
        if (!string.IsNullOrEmpty(start)) body["start"] = new JObject { ["dateTime"] = start, ["timeZone"] = tz };

        var end = ArgOpt(args, "endDateTime");
        if (!string.IsNullOrEmpty(end)) body["end"] = new JObject { ["dateTime"] = end, ["timeZone"] = tz };

        var bodyText = ArgOpt(args, "body");
        if (!string.IsNullOrEmpty(bodyText))
        {
            body["body"] = new JObject
            {
                ["contentType"] = bodyText.Contains("<") ? "html" : "text",
                ["content"] = bodyText
            };
        }

        var location = ArgOpt(args, "location");
        if (!string.IsNullOrEmpty(location)) body["location"] = new JObject { ["displayName"] = location };

        var isOnline = args["isOnlineMeeting"];
        if (isOnline != null)
        {
            body["isOnlineMeeting"] = isOnline;
            if (isOnline.Type == JTokenType.Boolean && (bool)isOnline)
                body["onlineMeetingProvider"] = "teamsForBusiness";
        }

        return await CallGraphAsync("PATCH", $"{basePath}/{eventId}", body, id);
    }

    private async Task<HttpResponseMessage> CancelEventAsync(JObject args, JToken id)
    {
        var eventId = Arg(args, "eventId");
        var body = new JObject();
        var comment = ArgOpt(args, "comment");
        if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;

        return await CallGraphAsync("POST", $"/me/calendar/events/{eventId}/cancel", body, id);
    }

    private async Task<HttpResponseMessage> RespondToEventAsync(string action, JObject args, JToken id)
    {
        var eventId = Arg(args, "eventId");
        var body = new JObject();
        
        var comment = ArgOpt(args, "comment");
        if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;

        var sendResponse = args["sendResponse"];
        body["sendResponse"] = sendResponse ?? true;

        return await CallGraphAsync("POST", $"/me/calendar/events/{eventId}/{action}", body, id);
    }

    private async Task<HttpResponseMessage> ListEventsAsync(string basePath, JObject args, JToken id)
    {
        var queryParts = new List<string>();
        
        var top = ArgOpt(args, "top") ?? "25";
        queryParts.Add($"$top={top}");

        var filter = ArgOpt(args, "filter");
        if (!string.IsNullOrEmpty(filter)) queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");

        var orderby = ArgOpt(args, "orderby") ?? "start/dateTime";
        queryParts.Add($"$orderby={Uri.EscapeDataString(orderby)}");

        queryParts.Add("$select=id,subject,start,end,location,organizer,attendees,isOnlineMeeting,onlineMeetingUrl,showAs,importance");

        var path = $"{basePath}?{string.Join("&", queryParts)}";
        return await CallGraphAsync("GET", path, null, id);
    }

    private async Task<HttpResponseMessage> ListUsersAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();
        
        var top = ArgOpt(args, "top") ?? "25";
        queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select") ?? "id,displayName,mail,userPrincipalName,jobTitle,department";
        queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var filter = ArgOpt(args, "filter");
        if (!string.IsNullOrEmpty(filter)) queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");

        var search = ArgOpt(args, "search");
        if (!string.IsNullOrEmpty(search))
        {
            queryParts.Add($"$search=\"displayName:{search}\" OR \"mail:{search}\"");
        }

        var path = $"/users?{string.Join("&", queryParts)}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0{path}");
        request.Headers.Add("Accept", "application/json");
        if (!string.IsNullOrEmpty(search)) request.Headers.Add("ConsistencyLevel", "eventual");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return CreateToolResult(content, !response.IsSuccessStatusCode, id);
    }

    private async Task<HttpResponseMessage> ListGroupsAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();
        
        var top = ArgOpt(args, "top") ?? "25";
        queryParts.Add($"$top={top}");
        queryParts.Add("$select=id,displayName,description,mail");

        var filter = ArgOpt(args, "filter");
        if (!string.IsNullOrEmpty(filter)) queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");

        var search = ArgOpt(args, "search");
        if (!string.IsNullOrEmpty(search))
        {
            queryParts.Add($"$search=\"displayName:{search}\"");
        }

        var path = $"/groups?{string.Join("&", queryParts)}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0{path}");
        request.Headers.Add("Accept", "application/json");
        if (!string.IsNullOrEmpty(search)) request.Headers.Add("ConsistencyLevel", "eventual");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return CreateToolResult(content, !response.IsSuccessStatusCode, id);
    }

    #endregion

    #region REST Operation Handlers

    private async Task<HttpResponseMessage> HandleRESTRequestAsync(string operationId)
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var query = this.Context.Request.RequestUri.Query;
        var method = this.Context.Request.Method;
        var url = $"https://graph.microsoft.com{path}{query}";

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Accept", "application/json");

        if (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH")
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }

    #endregion

    #region Graph API Helpers

    private string Arg(JObject args, string key)
    {
        var val = args?[key]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{key} is required");
        return val;
    }

    private string ArgOpt(JObject args, string key) => args?[key]?.ToString();

    private async Task<HttpResponseMessage> CallGraphAsync(string method, string path, JObject body, JToken id)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";

        var httpMethod = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => new HttpMethod("PATCH"),
            "PUT" => HttpMethod.Put,
            _ => HttpMethod.Get
        };

        var request = new HttpRequestMessage(httpMethod, url);
        request.Headers.Add("Accept", "application/json");

        if (body != null && method != "GET" && method != "DELETE")
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;
        return CreateToolResult(textOut, !response.IsSuccessStatusCode, id);
    }

    #endregion

    #region Application Insights Telemetry

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return;

            var propsDict = new Dictionary<string, string>();
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
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion

    #region Response Helpers

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        }, id);
    }

    private HttpResponseMessage CreateSuccess(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    private HttpResponseMessage CreateError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrEmpty(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    #endregion
}
