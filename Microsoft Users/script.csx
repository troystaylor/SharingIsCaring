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

    private static readonly string SERVER_NAME = "microsoft-users-mcp";
    private static readonly string SERVER_VERSION = "1.0.0";
    private static readonly string DEFAULT_PROTOCOL_VERSION = "2025-03-26";

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
                case "MCP":
                    response = await HandleMCPRequestAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetMyProfile":
                    response = await HandleGetMyProfileAsync().ConfigureAwait(false);
                    break;
                case "GetMyManager":
                    response = await HandleGetMyManagerAsync().ConfigureAwait(false);
                    break;
                case "GetMyDirectReports":
                    response = await HandleGetMyDirectReportsAsync().ConfigureAwait(false);
                    break;
                case "GetMyPhoto":
                    response = await HandleGetPhotoAsync("/me/photo/$value").ConfigureAwait(false);
                    break;
                case "GetMyPresence":
                    response = await HandleGetPresenceAsync("/me/presence").ConfigureAwait(false);
                    break;
                case "SearchPeople":
                    response = await HandleSearchPeopleAsync().ConfigureAwait(false);
                    break;
                case "ListUsers":
                    response = await HandleListUsersAsync().ConfigureAwait(false);
                    break;
                case "GetUserProfile":
                    response = await HandleGetUserProfileAsync().ConfigureAwait(false);
                    break;
                case "GetUsersManager":
                    response = await HandleGetUsersManagerAsync().ConfigureAwait(false);
                    break;
                case "GetDirectReports":
                    response = await HandleGetDirectReportsAsync().ConfigureAwait(false);
                    break;
                case "GetUserPhoto":
                    response = await HandleGetUserPhotoAsync().ConfigureAwait(false);
                    break;
                case "GetUserPresence":
                    response = await HandleGetUserPresenceAsync().ConfigureAwait(false);
                    break;
                case "GetBatchPresence":
                    response = await HandleGetBatchPresenceAsync().ConfigureAwait(false);
                    break;
                case "GetScheduleAvailability":
                    response = await HandleGetScheduleAvailabilityAsync().ConfigureAwait(false);
                    break;
                case "GetMyMemberships":
                    response = await HandleGetMyMembershipsAsync().ConfigureAwait(false);
                    break;
                case "GetUserMemberships":
                    response = await HandleGetUserMembershipsAsync().ConfigureAwait(false);
                    break;
                case "GetMyLicenses":
                    response = await HandleGetMyLicensesAsync().ConfigureAwait(false);
                    break;
                case "GetUserLicenses":
                    response = await HandleGetUserLicensesAsync().ConfigureAwait(false);
                    break;
                case "GetMailTips":
                    response = await HandleGetMailTipsAsync().ConfigureAwait(false);
                    break;
                case "GetMyJoinedTeams":
                    response = await HandleGetMyJoinedTeamsAsync().ConfigureAwait(false);
                    break;
                case "GetMyAuthMethods":
                    response = await HandleGetMyAuthMethodsAsync().ConfigureAwait(false);
                    break;
                case "GetMyOwnedObjects":
                    response = await HandleGetMyOwnedObjectsAsync().ConfigureAwait(false);
                    break;
                case "GetUserOwnedObjects":
                    response = await HandleGetUserOwnedObjectsAsync().ConfigureAwait(false);
                    break;
                // Profile API (Beta) handlers
                case "GetMyFullProfile":
                    response = await HandleGetMyFullProfileAsync().ConfigureAwait(false);
                    break;
                case "GetUserFullProfile":
                    response = await HandleGetUserFullProfileAsync().ConfigureAwait(false);
                    break;
                case "GetMySkills":
                    response = await CallGraphBetaAsync("GET", "/me/profile/skills").ConfigureAwait(false);
                    break;
                case "GetUserSkills":
                    response = await HandleGetUserProfileSectionAsync("skills").ConfigureAwait(false);
                    break;
                case "GetMyProjects":
                    response = await CallGraphBetaAsync("GET", "/me/profile/projects").ConfigureAwait(false);
                    break;
                case "GetUserProjects":
                    response = await HandleGetUserProfileSectionAsync("projects").ConfigureAwait(false);
                    break;
                case "GetMyCertifications":
                    response = await CallGraphBetaAsync("GET", "/me/profile/certifications").ConfigureAwait(false);
                    break;
                case "GetUserCertifications":
                    response = await HandleGetUserProfileSectionAsync("certifications").ConfigureAwait(false);
                    break;
                case "GetMyAwards":
                    response = await CallGraphBetaAsync("GET", "/me/profile/awards").ConfigureAwait(false);
                    break;
                case "GetUserAwards":
                    response = await HandleGetUserProfileSectionAsync("awards").ConfigureAwait(false);
                    break;
                case "GetMyLanguages":
                    response = await CallGraphBetaAsync("GET", "/me/profile/languages").ConfigureAwait(false);
                    break;
                case "GetUserLanguages":
                    response = await HandleGetUserProfileSectionAsync("languages").ConfigureAwait(false);
                    break;
                case "GetMyPositions":
                    response = await CallGraphBetaAsync("GET", "/me/profile/positions").ConfigureAwait(false);
                    break;
                case "GetUserPositions":
                    response = await HandleGetUserProfileSectionAsync("positions").ConfigureAwait(false);
                    break;
                case "GetMyEducation":
                    response = await CallGraphBetaAsync("GET", "/me/profile/educationalActivities").ConfigureAwait(false);
                    break;
                case "GetUserEducation":
                    response = await HandleGetUserProfileSectionAsync("educationalActivities").ConfigureAwait(false);
                    break;
                case "GetMyInterests":
                    response = await CallGraphBetaAsync("GET", "/me/profile/interests").ConfigureAwait(false);
                    break;
                case "GetUserInterests":
                    response = await HandleGetUserProfileSectionAsync("interests").ConfigureAwait(false);
                    break;
                case "GetMyWebAccounts":
                    response = await CallGraphBetaAsync("GET", "/me/profile/webAccounts").ConfigureAwait(false);
                    break;
                case "GetUserWebAccounts":
                    response = await HandleGetUserProfileSectionAsync("webAccounts").ConfigureAwait(false);
                    break;
                case "GetMyAddresses":
                    response = await CallGraphBetaAsync("GET", "/me/profile/addresses").ConfigureAwait(false);
                    break;
                case "GetUserAddresses":
                    response = await HandleGetUserProfileSectionAsync("addresses").ConfigureAwait(false);
                    break;
                case "GetMyWebsites":
                    response = await CallGraphBetaAsync("GET", "/me/profile/websites").ConfigureAwait(false);
                    break;
                case "GetUserWebsites":
                    response = await HandleGetUserProfileSectionAsync("websites").ConfigureAwait(false);
                    break;
                case "GetMyAnniversaries":
                    response = await CallGraphBetaAsync("GET", "/me/profile/anniversaries").ConfigureAwait(false);
                    break;
                case "GetUserAnniversaries":
                    response = await HandleGetUserProfileSectionAsync("anniversaries").ConfigureAwait(false);
                    break;
                case "GetMyNotes":
                    response = await CallGraphBetaAsync("GET", "/me/profile/notes").ConfigureAwait(false);
                    break;
                case "GetUserNotes":
                    response = await HandleGetUserProfileSectionAsync("notes").ConfigureAwait(false);
                    break;
                default:
                    response = await ForwardToGraphAsync().ConfigureAwait(false);
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

    #region REST Operation Handlers

    private async Task<HttpResponseMessage> HandleGetMyProfileAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyManagerAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/manager{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyDirectReportsAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/directReports{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetPresenceAsync(string path)
    {
        return await CallGraphAsync("GET", path).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetPhotoAsync(string path)
    {
        return await CallGraphAsync("GET", path).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleSearchPeopleAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/people{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListUsersAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        // Add ConsistencyLevel header for advanced queries
        return await CallGraphWithConsistencyAsync("GET", $"/users{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserProfileAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUsersManagerAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/manager{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetDirectReportsAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/directReports{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserPhotoAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/photo/$value").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserPresenceAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/presence").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetBatchPresenceAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return await CallGraphAsync("POST", "/communications/getPresencesByUserId", body).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetScheduleAvailabilityAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return await CallGraphAsync("POST", "/me/calendar/getSchedule", body).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyMembershipsAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/memberOf{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserMembershipsAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/memberOf{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyLicensesAsync()
    {
        return await CallGraphAsync("GET", "/me/licenseDetails").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserLicensesAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/licenseDetails").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMailTipsAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return await CallGraphAsync("POST", "/me/getMailTips", body).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyJoinedTeamsAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/joinedTeams{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyAuthMethodsAsync()
    {
        return await CallGraphAsync("GET", "/me/authentication/methods").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMyOwnedObjectsAsync()
    {
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/me/ownedObjects{query}").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserOwnedObjectsAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        var query = this.Context.Request.RequestUri.Query;
        return await CallGraphAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/ownedObjects{query}").ConfigureAwait(false);
    }

    // Profile API handlers (uses beta endpoint)
    private async Task<HttpResponseMessage> HandleGetMyFullProfileAsync()
    {
        return await CallGraphBetaAsync("GET", "/me/profile").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserFullProfileAsync()
    {
        var userId = ExtractPathParameter("userIdentifier");
        return await CallGraphBetaAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/profile").ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetUserProfileSectionAsync(string section)
    {
        var userId = ExtractPathParameter("userIdentifier");
        return await CallGraphBetaAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/profile/{section}").ConfigureAwait(false);
    }

    private string ExtractPathParameter(string paramName)
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var segments = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

        if (paramName == "userIdentifier" && segments.Length >= 2)
        {
            return segments[1];
        }
        return null;
    }

    private async Task<HttpResponseMessage> ForwardToGraphAsync()
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var query = this.Context.Request.RequestUri.Query;
        var method = this.Context.Request.Method.Method;

        string body = null;
        if (method == "POST" || method == "PATCH" || method == "PUT")
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        return await CallGraphAsync(method, $"{path}{query}", body).ConfigureAwait(false);
    }

    #endregion

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
                    return CreateMCPSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id, correlationId).ConfigureAwait(false);
                default:
                    await LogToAppInsights("MCPUnknownMethod", new { CorrelationId = correlationId, Method = method ?? "null" });
                    return CreateMCPError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("MCPParseError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32603, "Internal error", ex.Message);
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
                ["title"] = "Microsoft Users MCP",
                ["description"] = "Access user profiles, organizational hierarchy, presence status, and people discovery via Microsoft Graph. Enhanced alternative to Microsoft's first-party User Profile MCP Server."
            }
        };
        return CreateMCPSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        return CreateMCPSuccess(new JObject { ["tools"] = GetToolDefinitions() }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateMCPError(id, -32602, "Tool name required", "name parameter is required");

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
            // === MY PROFILE TOOLS (matches Microsoft 1P) ===
            CreateToolDef("get_my_profile", "Get the profile of the signed-in user. Returns displayName, mail, jobTitle, department, and other profile properties.",
                new JObject
                {
                    ["select"] = StrProp("Comma-separated list of properties to return (e.g., id,displayName,mail,jobTitle)"),
                    ["expand"] = StrProp("Expand related entity: 'manager' or 'directReports' (only one per request)")
                }),

            CreateToolDef("get_my_manager", "Get the manager of the signed-in user. Returns the manager's profile information.",
                new JObject
                {
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }),

            CreateToolDef("get_my_direct_reports", "Get the direct reports of the signed-in user.",
                new JObject
                {
                    ["select"] = StrProp("Comma-separated list of properties to return"),
                    ["top"] = IntProp("Number of direct reports to return")
                }),

            // === USER PROFILE TOOLS (matches Microsoft 1P) ===
            CreateToolDef("get_user_profile", "Get a user's profile by their ID or UPN. Do NOT use 'me' - use get_my_profile instead.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN). Do not use 'me'.", true),
                    ["select"] = StrProp("Comma-separated list of properties to return"),
                    ["expand"] = StrProp("Expand related entity: 'manager' or 'directReports'")
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_users_manager", "Get the manager of a specified user. Do NOT use 'me' - use get_my_manager instead.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_direct_reports", "Get the direct reports of a specified user. Do NOT use 'me'.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true),
                    ["select"] = StrProp("Comma-separated list of properties to return"),
                    ["top"] = IntProp("Number of direct reports to return")
                }, new[] { "userIdentifier" }),

            CreateToolDef("list_users", "List users in the organization. Supports filtering, searching, and sorting.",
                new JObject
                {
                    ["top"] = IntProp("Number of users to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return"),
                    ["filter"] = StrProp("OData filter (e.g., startswith(displayName,'A'), department eq 'Engineering')"),
                    ["orderby"] = StrProp("Property to sort by (e.g., displayName)"),
                    ["search"] = StrProp("Free-text search in format '\"property:value\"' (e.g., '\"displayName:John\"')")
                }),

            // === ENHANCED TOOLS (beyond Microsoft 1P) ===
            CreateToolDef("search_people", "Search for people relevant to the signed-in user. Returns relevance-ranked results based on communication patterns and collaboration.",
                new JObject
                {
                    ["query"] = StrProp("Search query (e.g., 'John' or 'Engineering')"),
                    ["top"] = IntProp("Number of results to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }),

            CreateToolDef("get_my_presence", "Get the presence (availability/activity) of the signed-in user. Shows if they're Available, Busy, Away, etc.",
                new JObject()),

            CreateToolDef("get_user_presence", "Get the presence (availability/activity) of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_batch_presence", "Get presence for multiple users at once (up to 650 users). Efficient for checking team availability.",
                new JObject
                {
                    ["userIds"] = ArrProp("Array of user IDs (GUIDs)", true)
                }, new[] { "userIds" }),

            CreateToolDef("get_my_photo_url", "Get the profile photo URL for the signed-in user. Returns a URL that can be used to display the photo.",
                new JObject()),

            CreateToolDef("get_user_photo_url", "Get the profile photo URL for a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            // === SCHEDULE & AVAILABILITY TOOLS ===
            CreateToolDef("get_schedule_availability", "Get free/busy availability for users over a time range. Perfect for finding meeting times.",
                new JObject
                {
                    ["schedules"] = ArrProp("Email addresses or UPNs of users to check availability for", true),
                    ["startDateTime"] = StrProp("Start date/time in ISO 8601 format (e.g., 2024-01-15T09:00:00)", true),
                    ["endDateTime"] = StrProp("End date/time in ISO 8601 format", true),
                    ["timeZone"] = StrProp("Time zone (e.g., 'Pacific Standard Time', 'UTC'). Defaults to UTC."),
                    ["intervalMinutes"] = IntProp("Duration of each time slot in minutes (default 30)")
                }, new[] { "schedules", "startDateTime", "endDateTime" }),

            // === GROUP & MEMBERSHIP TOOLS ===
            CreateToolDef("get_my_memberships", "Get the groups, teams, and directory roles that the signed-in user is a member of.",
                new JObject
                {
                    ["top"] = IntProp("Number of memberships to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }),

            CreateToolDef("get_user_memberships", "Get the groups, teams, and directory roles that a user is a member of.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true),
                    ["top"] = IntProp("Number of memberships to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_joined_teams", "Get the Microsoft Teams teams that the signed-in user is a member of.",
                new JObject
                {
                    ["top"] = IntProp("Number of teams to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }),

            // === LICENSE TOOLS ===
            CreateToolDef("get_my_licenses", "Get the license details (Microsoft 365, etc.) assigned to the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_licenses", "Get the license details assigned to a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            // === MAIL TIPS / OUT OF OFFICE ===
            CreateToolDef("get_mail_tips", "Get mail tips for recipients including out-of-office status, mailbox full, delivery restrictions.",
                new JObject
                {
                    ["emailAddresses"] = ArrProp("Email addresses to get mail tips for", true),
                    ["mailTipsOptions"] = StrProp("Comma-separated tips to get: automaticReplies,mailboxFullStatus,customMailTip,externalMemberCount,totalMemberCount,maxMessageSize,deliveryRestriction,moderationStatus")
                }, new[] { "emailAddresses" }),

            // === AUTHENTICATION & SECURITY ===
            CreateToolDef("get_my_auth_methods", "Get the authentication methods (phone, email, FIDO2, etc.) registered for the signed-in user.",
                new JObject()),

            // === OWNED OBJECTS ===
            CreateToolDef("get_my_owned_objects", "Get directory objects (apps, groups, service principals) owned by the signed-in user.",
                new JObject
                {
                    ["top"] = IntProp("Number of objects to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }),

            CreateToolDef("get_user_owned_objects", "Get directory objects owned by a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true),
                    ["top"] = IntProp("Number of objects to return"),
                    ["select"] = StrProp("Comma-separated list of properties to return")
                }, new[] { "userIdentifier" }),

            // === PROFILE API TOOLS (Beta) - Skills, Projects, Certifications ===
            CreateToolDef("get_my_full_profile", "Get the complete profile of the signed-in user including skills, projects, certifications, positions, education, and more. Uses beta API.",
                new JObject()),

            CreateToolDef("get_user_full_profile", "Get the complete profile of a specified user including skills, projects, certifications, positions, education, and more. Uses beta API.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_skills", "Get the professional skills of the signed-in user. Returns skill names and proficiency levels.",
                new JObject()),

            CreateToolDef("get_user_skills", "Get the professional skills of a specified user. Useful for finding expertise.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_projects", "Get projects the signed-in user has worked on. Includes project details, dates, and collaborators.",
                new JObject()),

            CreateToolDef("get_user_projects", "Get projects a specified user has worked on. Useful for understanding someone's work history.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_certifications", "Get professional certifications of the signed-in user. Includes certification names, IDs, and expiration dates.",
                new JObject()),

            CreateToolDef("get_user_certifications", "Get professional certifications of a specified user. Useful for verifying qualifications.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_awards", "Get awards and honors received by the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_awards", "Get awards and honors received by a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_languages", "Get languages spoken by the signed-in user with proficiency levels.",
                new JObject()),

            CreateToolDef("get_user_languages", "Get languages spoken by a specified user. Useful for finding multilingual team members.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_positions", "Get current and past positions/job history of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_positions", "Get current and past positions/job history of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_education", "Get educational activities and history of the signed-in user including degrees and institutions.",
                new JObject()),

            CreateToolDef("get_user_education", "Get educational activities and history of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_interests", "Get personal and professional interests of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_interests", "Get personal and professional interests of a specified user. Useful for finding people with shared interests.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_web_accounts", "Get web accounts (GitHub, LinkedIn, etc.) of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_web_accounts", "Get web accounts (GitHub, LinkedIn, etc.) of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_addresses", "Get physical addresses (home, work, other) of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_addresses", "Get physical addresses of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_websites", "Get websites of the signed-in user. Personal, professional, or blog sites.",
                new JObject()),

            CreateToolDef("get_user_websites", "Get websites of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_anniversaries", "Get anniversaries (birthday, work anniversary) of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_anniversaries", "Get anniversaries of a specified user. Useful for recognizing milestones.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" }),

            CreateToolDef("get_my_notes", "Get profile notes/annotations of the signed-in user.",
                new JObject()),

            CreateToolDef("get_user_notes", "Get profile notes/annotations of a specified user.",
                new JObject
                {
                    ["userIdentifier"] = StrProp("User's object ID (GUID) or userPrincipalName (UPN)", true)
                }, new[] { "userIdentifier" })
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
    private JObject BoolProp(string desc) => new JObject { ["type"] = "boolean", ["description"] = desc };
    private JObject ArrProp(string desc, bool req = false) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = desc };

    #endregion

    #region Tool Execution

    private async Task<HttpResponseMessage> ExecuteToolAsync(string toolName, JObject args, JToken id)
    {
        switch (toolName)
        {
            // My profile tools
            case "get_my_profile":
                return await ExecuteGetMyProfileAsync(args, id).ConfigureAwait(false);
            case "get_my_manager":
                return await ExecuteGetMyManagerAsync(args, id).ConfigureAwait(false);
            case "get_my_direct_reports":
                return await ExecuteGetMyDirectReportsAsync(args, id).ConfigureAwait(false);

            // User profile tools
            case "get_user_profile":
                return await ExecuteGetUserProfileAsync(args, id).ConfigureAwait(false);
            case "get_users_manager":
                return await ExecuteGetUsersManagerAsync(args, id).ConfigureAwait(false);
            case "get_direct_reports":
                return await ExecuteGetDirectReportsAsync(args, id).ConfigureAwait(false);
            case "list_users":
                return await ExecuteListUsersAsync(args, id).ConfigureAwait(false);

            // Enhanced tools
            case "search_people":
                return await ExecuteSearchPeopleAsync(args, id).ConfigureAwait(false);
            case "get_my_presence":
                return await CallGraphForToolAsync("GET", "/me/presence", null, id).ConfigureAwait(false);
            case "get_user_presence":
                return await ExecuteGetUserPresenceAsync(args, id).ConfigureAwait(false);
            case "get_batch_presence":
                return await ExecuteGetBatchPresenceAsync(args, id).ConfigureAwait(false);
            case "get_my_photo_url":
                return await ExecuteGetPhotoUrlAsync("/me", id).ConfigureAwait(false);
            case "get_user_photo_url":
                return await ExecuteGetUserPhotoUrlAsync(args, id).ConfigureAwait(false);

            // Schedule & availability
            case "get_schedule_availability":
                return await ExecuteGetScheduleAvailabilityAsync(args, id).ConfigureAwait(false);

            // Groups & memberships
            case "get_my_memberships":
                return await ExecuteGetMyMembershipsAsync(args, id).ConfigureAwait(false);
            case "get_user_memberships":
                return await ExecuteGetUserMembershipsAsync(args, id).ConfigureAwait(false);
            case "get_my_joined_teams":
                return await ExecuteGetMyJoinedTeamsAsync(args, id).ConfigureAwait(false);

            // Licenses
            case "get_my_licenses":
                return await CallGraphForToolAsync("GET", "/me/licenseDetails", null, id).ConfigureAwait(false);
            case "get_user_licenses":
                return await ExecuteGetUserLicensesAsync(args, id).ConfigureAwait(false);

            // Mail tips
            case "get_mail_tips":
                return await ExecuteGetMailTipsAsync(args, id).ConfigureAwait(false);

            // Auth methods
            case "get_my_auth_methods":
                return await CallGraphForToolAsync("GET", "/me/authentication/methods", null, id).ConfigureAwait(false);

            // Owned objects
            case "get_my_owned_objects":
                return await ExecuteGetMyOwnedObjectsAsync(args, id).ConfigureAwait(false);
            case "get_user_owned_objects":
                return await ExecuteGetUserOwnedObjectsAsync(args, id).ConfigureAwait(false);

            // Profile API tools (beta)
            case "get_my_full_profile":
                return await CallGraphBetaForToolAsync("GET", "/me/profile", null, id).ConfigureAwait(false);
            case "get_user_full_profile":
                return await ExecuteGetUserFullProfileToolAsync(args, id).ConfigureAwait(false);
            case "get_my_skills":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/skills", null, id).ConfigureAwait(false);
            case "get_user_skills":
                return await ExecuteGetUserProfileSectionToolAsync(args, "skills", id).ConfigureAwait(false);
            case "get_my_projects":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/projects", null, id).ConfigureAwait(false);
            case "get_user_projects":
                return await ExecuteGetUserProfileSectionToolAsync(args, "projects", id).ConfigureAwait(false);
            case "get_my_certifications":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/certifications", null, id).ConfigureAwait(false);
            case "get_user_certifications":
                return await ExecuteGetUserProfileSectionToolAsync(args, "certifications", id).ConfigureAwait(false);
            case "get_my_awards":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/awards", null, id).ConfigureAwait(false);
            case "get_user_awards":
                return await ExecuteGetUserProfileSectionToolAsync(args, "awards", id).ConfigureAwait(false);
            case "get_my_languages":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/languages", null, id).ConfigureAwait(false);
            case "get_user_languages":
                return await ExecuteGetUserProfileSectionToolAsync(args, "languages", id).ConfigureAwait(false);
            case "get_my_positions":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/positions", null, id).ConfigureAwait(false);
            case "get_user_positions":
                return await ExecuteGetUserProfileSectionToolAsync(args, "positions", id).ConfigureAwait(false);
            case "get_my_education":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/educationalActivities", null, id).ConfigureAwait(false);
            case "get_user_education":
                return await ExecuteGetUserProfileSectionToolAsync(args, "educationalActivities", id).ConfigureAwait(false);
            case "get_my_interests":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/interests", null, id).ConfigureAwait(false);
            case "get_user_interests":
                return await ExecuteGetUserProfileSectionToolAsync(args, "interests", id).ConfigureAwait(false);
            case "get_my_web_accounts":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/webAccounts", null, id).ConfigureAwait(false);
            case "get_user_web_accounts":
                return await ExecuteGetUserProfileSectionToolAsync(args, "webAccounts", id).ConfigureAwait(false);
            case "get_my_addresses":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/addresses", null, id).ConfigureAwait(false);
            case "get_user_addresses":
                return await ExecuteGetUserProfileSectionToolAsync(args, "addresses", id).ConfigureAwait(false);
            case "get_my_websites":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/websites", null, id).ConfigureAwait(false);
            case "get_user_websites":
                return await ExecuteGetUserProfileSectionToolAsync(args, "websites", id).ConfigureAwait(false);
            case "get_my_anniversaries":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/anniversaries", null, id).ConfigureAwait(false);
            case "get_user_anniversaries":
                return await ExecuteGetUserProfileSectionToolAsync(args, "anniversaries", id).ConfigureAwait(false);
            case "get_my_notes":
                return await CallGraphBetaForToolAsync("GET", "/me/profile/notes", null, id).ConfigureAwait(false);
            case "get_user_notes":
                return await ExecuteGetUserProfileSectionToolAsync(args, "notes", id).ConfigureAwait(false);

            default:
                return CreateMCPError(id, -32601, "Unknown tool", toolName);
        }
    }

    private async Task<HttpResponseMessage> ExecuteGetMyProfileAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var expand = ArgOpt(args, "expand");
        if (!string.IsNullOrEmpty(expand)) queryParts.Add($"$expand={Uri.EscapeDataString(expand)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMyManagerAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/manager{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMyDirectReportsAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/directReports{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserProfileAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        if (userId.ToLower() == "me") throw new ArgumentException("Do not use 'me'. Use get_my_profile instead.");

        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var expand = ArgOpt(args, "expand");
        if (!string.IsNullOrEmpty(expand)) queryParts.Add($"$expand={Uri.EscapeDataString(expand)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUsersManagerAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        if (userId.ToLower() == "me") throw new ArgumentException("Do not use 'me'. Use get_my_manager instead.");

        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/manager{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetDirectReportsAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        if (userId.ToLower() == "me") throw new ArgumentException("Do not use 'me'. Use get_my_direct_reports instead.");

        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/directReports{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteListUsersAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var filter = ArgOpt(args, "filter");
        if (!string.IsNullOrEmpty(filter)) queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");

        var orderby = ArgOpt(args, "orderby");
        if (!string.IsNullOrEmpty(orderby)) queryParts.Add($"$orderby={Uri.EscapeDataString(orderby)}");

        var search = ArgOpt(args, "search");
        if (!string.IsNullOrEmpty(search)) queryParts.Add($"$search={Uri.EscapeDataString(search)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        
        // Use consistency level for advanced queries
        return await CallGraphWithConsistencyForToolAsync("GET", $"/users{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteSearchPeopleAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var searchQuery = ArgOpt(args, "query");
        if (!string.IsNullOrEmpty(searchQuery)) queryParts.Add($"$search=\"{Uri.EscapeDataString(searchQuery)}\"");

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/people{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserPresenceAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/presence", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetBatchPresenceAsync(JObject args, JToken id)
    {
        var userIdsArr = args["userIds"] as JArray;
        if (userIdsArr == null || userIdsArr.Count == 0)
            throw new ArgumentException("userIds array is required");

        var body = new JObject
        {
            ["ids"] = userIdsArr
        };

        return await CallGraphForToolAsync("POST", "/communications/getPresencesByUserId", body.ToString(Newtonsoft.Json.Formatting.None), id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetPhotoUrlAsync(string userPath, JToken id)
    {
        // Return a constructed URL for the photo - the actual photo binary can be fetched from this URL
        var photoUrl = $"https://graph.microsoft.com/v1.0{userPath}/photo/$value";
        var result = new JObject
        {
            ["photoUrl"] = photoUrl,
            ["note"] = "Use this URL with appropriate authentication to fetch the actual photo binary"
        };
        return CreateToolResult(result.ToString(Newtonsoft.Json.Formatting.Indented), false, id);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserPhotoUrlAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        return await ExecuteGetPhotoUrlAsync($"/users/{Uri.EscapeDataString(userId)}", id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetScheduleAvailabilityAsync(JObject args, JToken id)
    {
        var schedulesArr = args["schedules"] as JArray;
        if (schedulesArr == null || schedulesArr.Count == 0)
            throw new ArgumentException("schedules array is required");

        var startDateTime = Arg(args, "startDateTime");
        var endDateTime = Arg(args, "endDateTime");
        var timeZone = ArgOpt(args, "timeZone") ?? "UTC";
        var intervalStr = ArgOpt(args, "intervalMinutes");
        var interval = string.IsNullOrEmpty(intervalStr) ? 30 : int.Parse(intervalStr);

        var body = new JObject
        {
            ["schedules"] = schedulesArr,
            ["startTime"] = new JObject
            {
                ["dateTime"] = startDateTime,
                ["timeZone"] = timeZone
            },
            ["endTime"] = new JObject
            {
                ["dateTime"] = endDateTime,
                ["timeZone"] = timeZone
            },
            ["availabilityViewInterval"] = interval
        };

        return await CallGraphForToolAsync("POST", "/me/calendar/getSchedule", body.ToString(Newtonsoft.Json.Formatting.None), id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMyMembershipsAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/memberOf{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserMembershipsAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");

        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/memberOf{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMyJoinedTeamsAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/joinedTeams{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserLicensesAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/licenseDetails", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMailTipsAsync(JObject args, JToken id)
    {
        var emailsArr = args["emailAddresses"] as JArray;
        if (emailsArr == null || emailsArr.Count == 0)
            throw new ArgumentException("emailAddresses array is required");

        var mailTipsOptions = ArgOpt(args, "mailTipsOptions") ?? "automaticReplies,mailboxFullStatus";

        var body = new JObject
        {
            ["emailAddresses"] = emailsArr,
            ["mailTipsOptions"] = mailTipsOptions
        };

        return await CallGraphForToolAsync("POST", "/me/getMailTips", body.ToString(Newtonsoft.Json.Formatting.None), id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetMyOwnedObjectsAsync(JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/me/ownedObjects{query}", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserOwnedObjectsAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");

        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        return await CallGraphForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/ownedObjects{query}", null, id).ConfigureAwait(false);
    }

    // Profile API tool execution helpers
    private async Task<HttpResponseMessage> ExecuteGetUserFullProfileToolAsync(JObject args, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        if (userId.ToLower() == "me") throw new ArgumentException("Do not use 'me'. Use get_my_full_profile instead.");
        return await CallGraphBetaForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/profile", null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetUserProfileSectionToolAsync(JObject args, string section, JToken id)
    {
        var userId = Arg(args, "userIdentifier");
        if (userId.ToLower() == "me") throw new ArgumentException($"Do not use 'me'. Use get_my_{section} instead.");
        return await CallGraphBetaForToolAsync("GET", $"/users/{Uri.EscapeDataString(userId)}/profile/{section}", null, id).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> CallGraphAsync(string method, string path, string body = null)
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

        if (!string.IsNullOrEmpty(body) && method != "GET" && method != "DELETE")
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> CallGraphWithConsistencyAsync(string method, string path, string body = null)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";

        var httpMethod = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            _ => HttpMethod.Get
        };

        var request = new HttpRequestMessage(httpMethod, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("ConsistencyLevel", "eventual");

        if (!string.IsNullOrEmpty(body) && method != "GET")
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> CallGraphForToolAsync(string method, string path, string body, JToken id)
    {
        var response = await CallGraphAsync(method, path, body).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;
        return CreateToolResult(textOut, !response.IsSuccessStatusCode, id);
    }

    private async Task<HttpResponseMessage> CallGraphWithConsistencyForToolAsync(string method, string path, string body, JToken id)
    {
        var response = await CallGraphWithConsistencyAsync(method, path, body).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;
        return CreateToolResult(textOut, !response.IsSuccessStatusCode, id);
    }

    // Graph Beta API helpers (for Profile API)
    private async Task<HttpResponseMessage> CallGraphBetaAsync(string method, string path, string body = null)
    {
        var url = $"https://graph.microsoft.com/beta{path}";

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

        if (!string.IsNullOrEmpty(body) && method != "GET" && method != "DELETE")
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> CallGraphBetaForToolAsync(string method, string path, string body, JToken id)
    {
        var response = await CallGraphBetaAsync(method, path, body).ConfigureAwait(false);
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

    #region MCP Response Helpers

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        return CreateMCPSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        }, id);
    }

    private HttpResponseMessage CreateMCPSuccess(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    private HttpResponseMessage CreateMCPError(JToken id, int code, string message, string data = null)
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
