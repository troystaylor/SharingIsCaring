using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // ===========================================================================
    // SmartRecruiters dual-mode connector
    // - MCP endpoint (POST /mcp): 80+ tools exposed to Copilot Studio
    // - Typed REST ops: ~165 operations forwarded with Bearer token attached
    // Auth: OAuth 2.0 General Partner Integration (Client Credentials grant).
    //   - Connection params: client_id + client_secret
    //   - Power Platform injects them as X-SR-Client-Id / X-SR-Client-Secret via setheader policies
    //   - Script exchanges them for a Bearer token at POST /identity/oauth/token (cached in-process)
    // ===========================================================================

    /// <summary>
    /// Application Insights connection string. Leave empty to disable telemetry.
    /// Format: "InstrumentationKey=...;IngestionEndpoint=https://..."
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private const string ServerName = "smartrecruiters-mcp";
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "SmartRecruiters MCP";
    private const string ServerDescription = "SmartRecruiters MCP tools for jobs, candidates, applications, users, configuration, approvals, messages, interviews, reports, and webhooks.";
    private const string ProtocolVersion = "2025-11-25";

    private const string SmartRecruitersApiBase = "https://api.smartrecruiters.com";
    private const string TokenEndpoint = "https://api.smartrecruiters.com/identity/oauth/token";
    private const string ClientIdHeader = "X-SR-Client-Id";
    private const string ClientSecretHeader = "X-SR-Client-Secret";

    // Process-wide token cache keyed by client_id. Power Platform reuses Script instances across calls,
    // so this avoids hitting /identity/oauth/token on every request. Each entry also stores an expiry
    // (with a 2-minute safety margin).
    private static readonly Dictionary<string, (string Token, DateTime ExpiresAtUtc)> _tokenCache
        = new Dictionary<string, (string, DateTime)>();
    private static readonly object _tokenCacheLock = new object();

    /// <summary>
    /// Entry point. Resolves a Bearer token from the connection's client_id + client_secret,
    /// then either dispatches the MCP handler or forwards the typed REST request upstream.
    /// </summary>
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            // Pull connection params out of the request headers (Power Platform's setheader policies put them there).
            var clientId = ReadAndRemoveHeader(ClientIdHeader);
            var clientSecret = ReadAndRemoveHeader(ClientSecretHeader);

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return CreateErrorResponse(
                    "SmartRecruiters connection is missing client_id or client_secret. Edit the connection and provide both values.",
                    HttpStatusCode.Unauthorized);
            }

            string bearerToken;
            try
            {
                bearerToken = await GetBearerTokenAsync(clientId, clientSecret).ConfigureAwait(false);
            }
            catch (Exception tokenEx)
            {
                await LogToAppInsights("TokenExchangeFailed", new
                {
                    CorrelationId = correlationId,
                    ErrorMessage = tokenEx.Message
                });
                return CreateErrorResponse("OAuth token exchange failed: " + tokenEx.Message, HttpStatusCode.Unauthorized);
            }

            // Install Bearer token on the incoming request so downstream forwarders (MCP tool executors
            // and the REST passthrough) can read it from Context.Request.Headers.Authorization.
            this.Context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            if (this.Context.OperationId == "InvokeMCP")
            {
                return await HandleMcpAsync(correlationId, startTime).ConfigureAwait(false);
            }

            return await ForwardTypedOperationAsync(bearerToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                Operation = this.Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            return CreateErrorResponse(ex.Message, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                Operation = this.Context.OperationId,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
        }
    }

    // ========================================
    // OAUTH 2.0 CLIENT CREDENTIALS
    // ========================================

    /// <summary>
    /// Exchange master- or customer-level client credentials for a Bearer token.
    /// Cached in-process by client_id; refreshed automatically when the cached token is within 2 minutes of expiry.
    /// </summary>
    private async Task<string> GetBearerTokenAsync(string clientId, string clientSecret)
    {
        lock (_tokenCacheLock)
        {
            if (_tokenCache.TryGetValue(clientId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2))
            {
                return cached.Token;
            }
        }

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        });

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint)) { Content = form };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("SmartRecruiters /identity/oauth/token returned " + (int)response.StatusCode + ": " + body);
        }

        JObject json;
        try { json = JObject.Parse(body); }
        catch (Exception ex) { throw new InvalidOperationException("Unexpected token response: " + body, ex); }

        var token = json.Value<string>("access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token response did not include access_token: " + body);
        }

        var expiresIn = json.Value<int?>("expires_in") ?? 3600;
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

        lock (_tokenCacheLock)
        {
            _tokenCache[clientId] = (token, expiresAt);
        }

        return token;
    }

    /// <summary>
    /// Read a header from the incoming Power Platform request and remove it so it never leaks upstream.
    /// Returns null when the header is absent.
    /// </summary>
    private string ReadAndRemoveHeader(string headerName)
    {
        if (!this.Context.Request.Headers.TryGetValues(headerName, out var values))
        {
            return null;
        }
        var value = values.FirstOrDefault();
        this.Context.Request.Headers.Remove(headerName);
        return value;
    }

    /// <summary>
    /// Forward a typed REST operation upstream after stripping the connection-param headers and
    /// installing the freshly-fetched Bearer token.
    /// </summary>
    private async Task<HttpResponseMessage> ForwardTypedOperationAsync(string bearerToken)
    {
        // The incoming Context.Request is already targeted at api.smartrecruiters.com (per the swagger host),
        // so we just need to attach Bearer auth and send it as-is.
        this.Context.Request.Headers.Remove("Authorization");
        this.Context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    // ========================================
    // MCP DISPATCH
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId, DateTime startTime)
    {
        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var requestId = request["id"];

        await LogToAppInsights("McpRequestReceived", new { CorrelationId = correlationId, Method = method });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(request, requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = ServerTitle,
                ["description"] = ServerDescription
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // ===== Discovery / read =====
            Tool("sr_search_jobs", "Search jobs by free-text and filters. Returns a ListResult page (default 10).", null,
                P("q","string","Free-text query"), P("status","string","DRAFT, SOURCING, INTERVIEW, OFFER, HIRED, ON_HOLD, CANCELLED, FILLED"),
                P("postingStatus","string","PUBLIC, INTERNAL, PRIVATE, NOT_PUBLISHED"), P("department","string","Department id"),
                P("location","string","Location"), P("limit","integer","Page size (max 100)"), P("offset","integer","Page offset")),

            Tool("sr_get_job", DescribeWithDeps("Get a job by id.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_get_job_status_history", DescribeWithDeps("Get status history for a job.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_get_job_publications", DescribeWithDeps("List publications for a job.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_get_hiring_team", DescribeWithDeps("Get a job's hiring team members.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_get_job_note", DescribeWithDeps("Get the note for a job.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_get_latest_job_approval", DescribeWithDeps("Get the latest approval request for a job.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_search_candidates", "Search candidates by free-text and filters. Only candidates with at least one job application are returned.", null,
                P("q","string","Free-text query"), P("jobId","string","Filter by job"), P("tag","string","Filter by tag"),
                P("status","string","NEW, IN_REVIEW, INTERVIEW, OFFERED, HIRED, ARCHIVED"),
                P("limit","integer","Page size (max 100)"), P("offset","integer","Page offset")),

            Tool("sr_get_candidate", DescribeWithDeps("Get details for a candidate by id.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_get_application", DescribeWithDeps("Get the candidate's application to a specific job.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_get_application_status_history", DescribeWithDeps("Get status change history for a candidate's job application.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_get_application_properties", DescribeWithDeps("Get candidate property values for the candidate's job application.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_get_candidate_tags", DescribeWithDeps("Get all tags assigned to a candidate.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_list_candidate_attachments", DescribeWithDeps("List candidate-level attachments.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_list_application_attachments", DescribeWithDeps("List application-level attachments.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_get_screening_answers", DescribeWithDeps("Get screening question answers for a candidate's job application.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_get_candidate_consent_status", DescribeWithDeps("Get candidate consent status.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_get_candidate_offers", DescribeWithDeps("Get a candidate's offers.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_get_job_application", "Get a job application by application id.", Req("id"),
                P("id","string","Job Application ID")),

            // ===== Configuration / lookup =====
            Tool("sr_get_company_info", "Get information about your SmartRecruiters company.", null),

            Tool("sr_list_departments", "List configured departments.", null),

            Tool("sr_list_hiring_processes", "List hiring processes configured for the company.", null),

            Tool("sr_list_source_types", "List candidate source types with subtypes. Use values from this to populate sr_update_application_source.", null),

            Tool("sr_list_source_values", DescribeWithDeps("List source values for a given source type.", "sr_list_source_types"), Req("type"),
                P("type","string","Source type id")),

            Tool("sr_list_rejection_reasons", "List rejection reasons configured for the company.", null),

            Tool("sr_list_withdrawal_reasons", "List withdrawal reasons configured for the company.", null),

            Tool("sr_list_job_properties", "List available job properties (custom fields on jobs).", null),

            Tool("sr_list_candidate_properties", "List available candidate properties (custom fields on candidates).", null),

            Tool("sr_list_candidate_property_values", DescribeWithDeps("List values for a SINGLE_SELECT candidate property.", "sr_list_candidate_properties"), Req("id"),
                P("id","string","Candidate property id")),

            Tool("sr_list_career_sites", "List career site configurations.", null),

            Tool("sr_list_predefined_locations", "List predefined locations.", null,
                P("limit","integer","Page size"), P("offset","integer","Page offset")),

            Tool("sr_list_system_roles", "List system roles available in the company.", null),

            Tool("sr_list_access_groups", "List access groups configured in the company.", null),

            // ===== Public Posting API =====
            Tool("sr_list_public_postings", "Public Posting API — list active postings published by a company (no auth required).", Req("companyIdentifier"),
                P("companyIdentifier","string","Company identifier (from career site URL)"),
                P("q","string","Free-text query"), P("limit","integer","Page size (max 100)"), P("offset","integer","Offset"),
                P("country","string","Country filter"), P("city","string","City filter")),

            Tool("sr_get_public_posting", DescribeWithDeps("Public Posting API — get a posting by id or uuid.", "sr_list_public_postings"), Req("companyIdentifier","postingId"),
                P("companyIdentifier","string","Company identifier"), P("postingId","string","Posting id or uuid")),

            Tool("sr_list_public_departments", "Public Posting API — list departments for a company.", Req("companyIdentifier"),
                P("companyIdentifier","string","Company identifier")),

            // ===== Application API (apply on behalf of candidate) =====
            Tool("sr_post_application", DescribeWithDeps("Create a new candidate application against a public posting (Application API).", "sr_list_public_postings"), Req("uuid","firstName","lastName","email"),
                P("uuid","string","Posting UUID"),
                P("firstName","string","Candidate first name"), P("lastName","string","Candidate last name"),
                P("email","string","Candidate email"), P("phoneNumber","string","Phone (optional)"),
                P("resumeFileName","string","Resume file name (e.g. resume.pdf)"),
                P("resumeMimeType","string","Resume mime type (e.g. application/pdf)"),
                P("resumeContentBase64","string","Base64-encoded resume content"),
                P("messageToHiringManager","string","Optional cover message"),
                P("sourceTypeId","string","Source type id"), P("sourceSubTypeId","string","Source subtype id"), P("sourceId","string","Source id")),

            Tool("sr_get_application_configuration", DescribeWithDeps("Get application configuration (screening questions, privacy policies) for a posting.", "sr_list_public_postings"), Req("uuid"),
                P("uuid","string","Posting UUID"),
                P("conditionalsIncluded","boolean","Include conditional questions"),
                P("language","string","Language code (default 'en')")),

            Tool("sr_get_candidate_application_status", DescribeWithDeps("Get the status of a submitted application.", "sr_post_application"), Req("uuid","candidateId"),
                P("uuid","string","Posting UUID"), P("candidateId","string","Candidate ID returned by sr_post_application")),

            // ===== Jobs write =====
            Tool("sr_create_job", "Create a new job.", Req("title"),
                P("title","string","Job title"), P("refNumber","string","Reference number"),
                P("departmentId","string","Department id"), P("industryId","string","Industry id"),
                P("functionId","string","Function id"), P("experienceLevelId","string","Experience level id"),
                P("typeOfEmploymentId","string","Type of employment id"),
                P("city","string","City"), P("countryCode","string","Country code (ISO alpha-2, lowercase)"),
                P("regionCode","string","Region code (uppercase, US only)"),
                P("creatorId","string","Creator user id"),
                P("compensationCurrency","string","Compensation currency (ISO 4217)"),
                P("compensationMin","number","Compensation min"), P("compensationMax","number","Compensation max"),
                P("compensationPeriod","string","HOURLY, DAILY, WEEKLY, MONTHLY, YEARLY")),

            Tool("sr_update_job_status", DescribeWithDeps("Update a job's status.", "sr_search_jobs"), Req("id","status"),
                P("id","string","Job ID"),
                P("status","string","DRAFT, SOURCING, ON_HOLD, INTERVIEW, OFFER, HIRED, CANCELLED, FILLED")),

            Tool("sr_update_job_headcount", DescribeWithDeps("Set a job's target hiring count.", "sr_search_jobs"), Req("id","targetHiringCount"),
                P("id","string","Job ID"), P("targetHiringCount","integer","New headcount")),

            Tool("sr_update_job_note", DescribeWithDeps("Update the note on a job.", "sr_search_jobs"), Req("id","content"),
                P("id","string","Job ID"), P("content","string","Note text")),

            Tool("sr_publish_job", DescribeWithDeps("Publish a job's default ad to internal sources and free aggregators. Throttled at 2 req/sec.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_unpublish_job", DescribeWithDeps("Unpublish a job from all sources. Throttled at 2 req/sec.", "sr_search_jobs"), Req("id"),
                P("id","string","Job ID")),

            Tool("sr_add_hiring_team_member", DescribeWithDeps("Add a user to a job's hiring team in a specific role.", "sr_search_jobs", "sr_list_users"), Req("id","userId","roleId"),
                P("id","string","Job ID"), P("userId","string","User ID"), P("roleId","string","Hiring team role id")),

            Tool("sr_remove_hiring_team_member", DescribeWithDeps("Remove a user from a job's hiring team.", "sr_get_hiring_team"), Req("id","userId"),
                P("id","string","Job ID"), P("userId","string","User ID")),

            Tool("sr_publish_job_ad", DescribeWithDeps("Publish a specific job ad.", "sr_search_jobs"), Req("id","jobAdId"),
                P("id","string","Job ID"), P("jobAdId","string","Job Ad ID")),

            Tool("sr_unpublish_job_ad", DescribeWithDeps("Unpublish a specific job ad.", "sr_search_jobs"), Req("id","jobAdId"),
                P("id","string","Job ID"), P("jobAdId","string","Job Ad ID")),

            // ===== Candidates write =====
            Tool("sr_create_candidate", "Create a candidate in the talent pool (or assign to a job if jobId provided).", Req("firstName","lastName","email"),
                P("firstName","string","First name"), P("lastName","string","Last name"), P("email","string","Email"),
                P("phoneNumber","string","Phone"),
                P("jobId","string","Optional job id to assign candidate to a job"),
                P("city","string","City"), P("countryCode","string","Country code"), P("regionCode","string","Region code"),
                P("sourceTypeId","string","Source type id"), P("sourceSubTypeId","string","Source subtype id"), P("sourceId","string","Source id"),
                P("tags","array","Tags to assign (array of strings)")),

            Tool("sr_update_candidate", DescribeWithDeps("Patch candidate personal information.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID"),
                P("firstName","string"), P("lastName","string"), P("email","string"), P("phoneNumber","string"),
                P("city","string"), P("countryCode","string"), P("regionCode","string")),

            Tool("sr_delete_candidate", DescribeWithDeps("Delete a candidate.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_parse_resume", "Parse a resume, create a candidate, and add to talent pool.", Req("fileName","mimeType","contentBase64"),
                P("fileName","string","Resume file name"), P("mimeType","string","Mime type (e.g. application/pdf)"),
                P("contentBase64","string","Base64-encoded file content"),
                P("sourceTypeId","string","Source type id"), P("sourceSubTypeId","string","Source subtype id"), P("sourceId","string","Source id")),

            Tool("sr_parse_resume_to_job", DescribeWithDeps("Parse a resume and assign new candidate to a job.", "sr_search_jobs"), Req("jobId","fileName","mimeType","contentBase64"),
                P("jobId","string","Job ID"),
                P("fileName","string","Resume file name"), P("mimeType","string","Mime type"),
                P("contentBase64","string","Base64-encoded file content"),
                P("sourceTypeId","string","Source type id"), P("sourceSubTypeId","string","Source subtype id"), P("sourceId","string","Source id")),

            Tool("sr_update_application_status", DescribeWithDeps("Update the status of a candidate's job application. Use reasonId when status is REJECTED or WITHDRAWN.", "sr_search_candidates", "sr_search_jobs", "sr_list_rejection_reasons"), Req("id","jobId","status"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID"),
                P("status","string","NEW, IN_REVIEW, INTERVIEW, OFFERED, HIRED, REJECTED, WITHDRAWN_BY_APPLICANT"),
                P("reasonId","string","Rejection / withdrawal reason id (required when archiving)")),

            Tool("sr_update_application_source", DescribeWithDeps("Update a candidate's source on a specific application.", "sr_search_candidates", "sr_list_source_types"), Req("id","jobId","sourceTypeId","sourceId"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID"),
                P("sourceTypeId","string","Source type id"), P("sourceSubTypeId","string","Source subtype id"), P("sourceId","string","Source value id")),

            Tool("sr_add_candidate_tags", DescribeWithDeps("Add tags to a candidate (preserves existing tags).", "sr_search_candidates"), Req("id","tags"),
                P("id","string","Candidate ID"), P("tags","array","Tags to add (array of strings)")),

            Tool("sr_replace_candidate_tags", DescribeWithDeps("Replace ALL tags on a candidate.", "sr_search_candidates"), Req("id","tags"),
                P("id","string","Candidate ID"), P("tags","array","New tags (array of strings)")),

            Tool("sr_delete_candidate_tags", DescribeWithDeps("Remove all tags from a candidate.", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_update_application_properties", DescribeWithDeps("Bulk set candidate property values on a candidate's job application.", "sr_search_candidates", "sr_search_jobs", "sr_list_candidate_properties"), Req("id","jobId","properties"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID"),
                P("properties","array","Array of {id, value} objects")),

            Tool("sr_request_candidate_consent", DescribeWithDeps("Request consent from a candidate (GDPR).", "sr_search_candidates"), Req("id"),
                P("id","string","Candidate ID")),

            Tool("sr_update_onboarding_status", DescribeWithDeps("Set onboarding status for a candidate on a specific job.", "sr_search_candidates", "sr_search_jobs"), Req("id","jobId","status"),
                P("id","string","Candidate ID"), P("jobId","string","Job ID"),
                P("status","string","Onboarding status value id")),

            // ===== Users =====
            Tool("sr_list_users", "List users in the company.", null,
                P("q","string","Free-text query"), P("active","boolean","Only active users"),
                P("limit","integer","Page size"), P("offset","integer","Page offset")),

            Tool("sr_get_user", DescribeWithDeps("Get a user by id.", "sr_list_users"), Req("id"),
                P("id","string","User ID")),

            Tool("sr_get_my_user", "Get details of the authenticated user.", null),

            Tool("sr_create_user", "Create a new user (inactive until activated).", Req("firstName","lastName","email","systemRoleId"),
                P("firstName","string"), P("lastName","string"), P("email","string"),
                P("systemRoleId","string","System role id (ADMINISTRATOR, EXTENDED, STANDARD, RESTRICTED, EMPLOYEE)"),
                P("languageCode","string","Language code (default 'en')"),
                P("city","string"), P("countryCode","string"),
                P("title","string"), P("departmentId","string")),

            Tool("sr_update_user", DescribeWithDeps("Update a user's fields.", "sr_list_users"), Req("id"),
                P("id","string","User ID"),
                P("firstName","string"), P("lastName","string"), P("title","string"),
                P("systemRoleId","string"), P("departmentId","string"), P("languageCode","string")),

            Tool("sr_activate_user", DescribeWithDeps("Activate a user.", "sr_list_users"), Req("id"),
                P("id","string","User ID")),

            Tool("sr_deactivate_user", DescribeWithDeps("Deactivate a user.", "sr_list_users"), Req("id"),
                P("id","string","User ID")),

            Tool("sr_send_activation_email", DescribeWithDeps("Send activation email to a user.", "sr_list_users"), Req("id"),
                P("id","string","User ID")),

            Tool("sr_send_password_reset", DescribeWithDeps("Send password reset email to a user.", "sr_list_users"), Req("id"),
                P("id","string","User ID")),

            // ===== Approvals =====
            Tool("sr_list_pending_approvals", "List pending approval requests where you are an approver (max 100).", null),

            Tool("sr_get_approval", DescribeWithDeps("Get an approval request by id.", "sr_list_pending_approvals"), Req("id"),
                P("id","string","Approval request ID")),

            Tool("sr_create_approval_request", "Create a new approval request based on an existing one.", Req("baseId"),
                P("baseId","string","Existing approval request id to base on")),

            Tool("sr_approve_request", DescribeWithDeps("Approve an approval request.", "sr_list_pending_approvals"), Req("id"),
                P("id","string","Approval request ID"), P("comment","string","Optional comment")),

            Tool("sr_reject_approval", DescribeWithDeps("Reject an approval request.", "sr_list_pending_approvals"), Req("id"),
                P("id","string","Approval request ID"), P("comment","string","Optional comment"), P("reasonId","string","Optional reason id")),

            Tool("sr_get_approval_comments", DescribeWithDeps("Get comments for an approval request.", "sr_list_pending_approvals"), Req("id"),
                P("id","string","Approval request ID")),

            Tool("sr_add_approval_comment", DescribeWithDeps("Add a comment to an approval request.", "sr_list_pending_approvals"), Req("id","text"),
                P("id","string","Approval request ID"), P("text","string","Comment text")),

            // ===== Notes / messages =====
            Tool("sr_create_note", "Share a note (Hireloop update). Use @[USER:id] to mention users, #[CANDIDATE:id] to tag candidates.", Req("content"),
                P("content","string","Note text"),
                P("candidateId","string","Candidate to attach the note to"),
                P("jobId","string","Job context (optional)"),
                P("shareWithUsers","array","Array of user ids to share with"),
                P("shareWithHiringTeamOfJobIds","array","Array of job ids whose hiring teams should receive the note"),
                P("everyone","boolean","Share with everyone in the company"),
                P("openNote","boolean","Share with everyone with access to the candidate")),

            Tool("sr_delete_note", "Delete a previously created note.", Req("id"),
                P("id","string","Note ID")),

            Tool("sr_fetch_messages", DescribeWithDeps("Search messages for a candidate (ADMINISTRATOR role required).", "sr_search_candidates"), Req("candidateId"),
                P("candidateId","string","Candidate ID"), P("jobId","string","Optional job filter"), P("limit","integer","Page size")),

            // ===== Interviews =====
            Tool("sr_list_interviews", "List interviews. Filter by candidate or application.", null,
                P("candidateId","string","Candidate id"), P("applicationId","string","Application id"),
                P("limit","integer","Page size"), P("offset","integer","Page offset")),

            Tool("sr_get_interview", DescribeWithDeps("Get an interview by id.", "sr_list_interviews"), Req("id"),
                P("id","string","Interview ID")),

            Tool("sr_create_interview", "Create an interview (Public API).", null,
                P("body","object","Full interview payload — pass the request body as an object")),

            Tool("sr_delete_interview", DescribeWithDeps("Delete an interview (only Public API interviews).", "sr_list_interviews"), Req("id"),
                P("id","string","Interview ID")),

            Tool("sr_list_interview_types", "List configured interview types.", null),

            // ===== Reports =====
            Tool("sr_list_reports", "List reports configured in Report Builder.", null,
                P("page","string","Opaque pagination cursor")),

            Tool("sr_get_report", DescribeWithDeps("Get details of a report.", "sr_list_reports"), Req("reportId"),
                P("reportId","string","Report ID")),

            Tool("sr_list_report_files", DescribeWithDeps("List files generated for a report.", "sr_list_reports"), Req("reportId"),
                P("reportId","string","Report ID"), P("page","string","Opaque pagination cursor")),

            Tool("sr_get_most_recent_report_file", DescribeWithDeps("Get metadata for the most recent file of a report.", "sr_list_reports"), Req("reportId"),
                P("reportId","string","Report ID")),

            Tool("sr_generate_ad_hoc_report", DescribeWithDeps("Trigger generation of an ad-hoc report file. Poll sr_get_most_recent_report_file until status is COMPLETED.", "sr_list_reports"), Req("reportId"),
                P("reportId","string","Report ID")),

            // ===== Webhooks =====
            Tool("sr_list_subscriptions", "List webhook subscriptions.", null),

            Tool("sr_create_subscription", "Create a webhook subscription. Must be activated via sr_activate_subscription before events flow.", Req("callbackUrl","events"),
                P("callbackUrl","string","URL that will receive callbacks"),
                P("events","array","Event names (e.g. candidate.created, application.created)"),
                P("alertingEmail","string","Optional alerting email")),

            Tool("sr_get_subscription", DescribeWithDeps("Get a webhook subscription.", "sr_list_subscriptions"), Req("id"),
                P("id","string","Subscription ID")),

            Tool("sr_delete_subscription", DescribeWithDeps("Delete a webhook subscription.", "sr_list_subscriptions"), Req("id"),
                P("id","string","Subscription ID")),

            Tool("sr_activate_subscription", DescribeWithDeps("Activate a webhook subscription. Your callback URL must echo back the X-Hook-Secret header within 20 seconds.", "sr_list_subscriptions"), Req("id"),
                P("id","string","Subscription ID")),

            // ===== Reviews =====
            Tool("sr_list_reviews", DescribeWithDeps("List reviews for a candidate's job application.", "sr_search_candidates", "sr_search_jobs"), Req("candidateId","jobId"),
                P("candidateId","string","Candidate ID"), P("jobId","string","Job ID")),

            Tool("sr_create_review", DescribeWithDeps("Create a review for a candidate's job application.", "sr_search_candidates", "sr_search_jobs", "sr_get_scorecard_criteria_by_job"), Req("candidateId","jobId","rating"),
                P("candidateId","string","Candidate ID"), P("jobId","string","Job ID"),
                P("rating","integer","Rating 1-5"), P("comment","string","Comment")),

            Tool("sr_get_scorecard_criteria_by_job", DescribeWithDeps("Get configured scorecard criteria for a job. Use the returned criterion ids when scoring a review.", "sr_search_jobs"), Req("jobId"),
                P("jobId","string","Job ID")),

            // ===== Candidate offers (detail) =====
            Tool("sr_search_offers", "Search offers across candidates by criteria. Sorted by lastUpdateDate descending.", null,
                P("q","string","Free-text query"),
                P("status","string","DRAFT, PENDING_APPROVAL, APPROVED, SENT, ACCEPTED, DECLINED, REVOKED, EXPIRED"),
                P("limit","integer","Page size"), P("offset","integer","Page offset")),

            Tool("sr_find_candidate_offers", DescribeWithDeps("Search a candidate's offers. Requires Extended or Admin role.", "sr_search_candidates"), Req("candidateId"),
                P("candidateId","string","Candidate ID"),
                P("status","string","Offer status filter"),
                P("limit","integer","Page size")),

            Tool("sr_get_candidate_offer", DescribeWithDeps("Get a specific offer for a candidate.", "sr_get_candidate_offers"), Req("candidateId","offerId"),
                P("candidateId","string","Candidate ID"), P("offerId","string","Offer ID")),

            Tool("sr_list_offer_documents", DescribeWithDeps("List documents attached to a sent offer.", "sr_get_candidate_offer"), Req("offerId"),
                P("offerId","string","Offer ID")),

            // ===== Self-scheduling extras =====
            Tool("sr_search_self_schedules", "Search self-scheduling instances by filter.", null,
                P("applicationId","string","Filter by application UUID"),
                P("status","string","Self-schedule status filter"),
                P("limit","integer","Page size"), P("offset","integer","Page offset")),

            Tool("sr_get_application_self_schedule", "Retrieve application-related details for a self-scheduling instance.", Req("applicationId"),
                P("applicationId","string","Application UUID")),

            Tool("sr_request_self_reschedule", DescribeWithDeps("Request an automated self-reschedule for a candidate. Cancels the existing interview and updates the self-schedule. The original self-schedule link remains valid.", "sr_search_self_schedules"), Req("id","applicationId"),
                P("id","string","Self-schedule ID"),
                P("applicationId","string","Application UUID"),
                P("earliestDate","string","Earliest date offered (ISO 8601)"),
                P("latestDate","string","Latest date offered (ISO 8601)"),
                P("interviewerIds","array","Interviewer user ids")),

            Tool("sr_update_self_schedule_invite", DescribeWithDeps("Update an automated self-schedule invite. The interview must NOT already exist; use sr_request_self_reschedule if it does.", "sr_search_self_schedules"), Req("id"),
                P("id","string","Self-schedule ID"),
                P("earliestDate","string","Earliest date offered (ISO 8601)"),
                P("latestDate","string","Latest date offered (ISO 8601)"),
                P("interviewerIds","array","Updated interviewer user ids")),

            // ===== Interview statuses + no-show =====
            Tool("sr_update_interview_candidate_status", DescribeWithDeps("Change a candidate's status on an interview (Public API interviews only).", "sr_list_interviews"), Req("interviewId","status"),
                P("interviewId","string","Interview ID"),
                P("status","string","ACCEPTED, DECLINED, NEEDS_RESCHEDULING, TENTATIVE, NO_RESPONSE"),
                P("comment","string","Optional comment")),

            Tool("sr_update_timeslot_interviewer_status", DescribeWithDeps("Change an interviewer's status on a timeslot.", "sr_get_interview"), Req("interviewId","slotId","interviewerId","status"),
                P("interviewId","string","Interview ID"),
                P("slotId","string","Timeslot ID"),
                P("interviewerId","string","Interviewer user id"),
                P("status","string","ACCEPTED, DECLINED, TENTATIVE, NO_RESPONSE"),
                P("comment","string","Optional comment")),

            Tool("sr_update_timeslot_candidate_status", DescribeWithDeps("Change a candidate's status on a specific timeslot.", "sr_get_interview"), Req("interviewId","slotId","status"),
                P("interviewId","string","Interview ID"),
                P("slotId","string","Timeslot ID"),
                P("status","string","ACCEPTED, DECLINED, NEEDS_RESCHEDULING, TENTATIVE, NO_RESPONSE"),
                P("comment","string","Optional comment")),

            Tool("sr_update_timeslot_noshow", DescribeWithDeps("Mark a timeslot as no-show (or clear the flag).", "sr_get_interview"), Req("interviewId","slotId","noShow"),
                P("interviewId","string","Interview ID"),
                P("slotId","string","Timeslot ID"),
                P("noShow","boolean","True to mark no-show, false to clear"),
                P("comment","string","Optional comment")),

            // ===== Onboarding processes detail =====
            Tool("sr_get_onboarding_process", "Return details of a single SmartOnboard onboarding process.", Req("id"),
                P("id","string","Onboarding Process ID")),

            Tool("sr_get_onboarding_assignments", DescribeWithDeps("Return all activity assignments associated with an onboarding process.", "sr_get_onboarding_process"), Req("id"),
                P("id","string","Onboarding Process ID")),

            Tool("sr_get_web_form_answers", DescribeWithDeps("Return answers submitted for a web-form assignment.", "sr_get_onboarding_assignments"), Req("assignmentId"),
                P("assignmentId","string","Web Form Assignment ID")),

            Tool("sr_get_new_hire", "Return details for a single new hire in SmartOnboard.", Req("id"),
                P("id","string","New Hire ID")),

            // ===== Audit =====
            Tool("sr_list_audit_events", "List audit events (ADMINISTRATOR role required). Defaults to last 7 days if no range specified.", null,
                P("eventDateAfter","string","ISO 8601 lower bound"), P("eventDateBefore","string","ISO 8601 upper bound"),
                P("entityType","string","USER, JOB, CANDIDATE, APPLICATION, etc."),
                P("entityId","string","Entity id"), P("eventName","string","Event name filter"),
                P("limit","integer","Page size"))
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        await LogToAppInsights("McpToolCallStarted", new { CorrelationId = correlationId, ToolName = toolName });

        try
        {
            var toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            await LogToAppInsights("McpToolCallCompleted", new { CorrelationId = correlationId, ToolName = toolName, IsError = false });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = "Invalid arguments: " + ex.Message } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("McpToolCallError", new { CorrelationId = correlationId, ToolName = toolName, ErrorMessage = ex.Message });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = "Tool execution failed: " + ex.Message } },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // TOOL EXECUTION (dispatch)
    // ========================================

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // ===== Discovery / read =====
            case "sr_search_jobs":
                return await CallSrAsync(HttpMethod.Get, "/jobs", null, BuildQuery(args, "q","status","postingStatus","department","location","limit","offset")).ConfigureAwait(false);

            case "sr_get_job":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_get_job_status_history":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id") + "/status/history", null, null).ConfigureAwait(false);

            case "sr_get_job_publications":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id") + "/publication", null, null).ConfigureAwait(false);

            case "sr_get_hiring_team":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id") + "/hiring-team", null, null).ConfigureAwait(false);

            case "sr_get_job_note":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id") + "/note", null, null).ConfigureAwait(false);

            case "sr_get_latest_job_approval":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "id") + "/approvals/latest", null, null).ConfigureAwait(false);

            case "sr_search_candidates":
                return await CallSrAsync(HttpMethod.Get, "/candidates", null, BuildQuery(args, "q","jobId","tag","status","limit","offset")).ConfigureAwait(false);

            case "sr_get_candidate":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_get_application":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId"), null, null).ConfigureAwait(false);

            case "sr_get_application_status_history":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/status/history", null, null).ConfigureAwait(false);

            case "sr_get_application_properties":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/properties", null, null).ConfigureAwait(false);

            case "sr_get_candidate_tags":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/tags", null, null).ConfigureAwait(false);

            case "sr_list_candidate_attachments":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/attachments", null, null).ConfigureAwait(false);

            case "sr_list_application_attachments":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/attachments", null, null).ConfigureAwait(false);

            case "sr_get_screening_answers":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/screening-answers", null, null).ConfigureAwait(false);

            case "sr_get_candidate_consent_status":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/consent/status", null, null).ConfigureAwait(false);

            case "sr_get_candidate_offers":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "id") + "/offers", null, null).ConfigureAwait(false);

            case "sr_get_job_application":
                return await CallSrAsync(HttpMethod.Get, "/job-applications/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            // ===== Configuration =====
            case "sr_get_company_info":
                return await CallSrAsync(HttpMethod.Get, "/configuration/company/my", null, null).ConfigureAwait(false);

            case "sr_list_departments":
                return await CallSrAsync(HttpMethod.Get, "/configuration/departments", null, null).ConfigureAwait(false);

            case "sr_list_hiring_processes":
                return await CallSrAsync(HttpMethod.Get, "/configuration/hiring-processes", null, null).ConfigureAwait(false);

            case "sr_list_source_types":
                return await CallSrAsync(HttpMethod.Get, "/configuration/sources", null, null).ConfigureAwait(false);

            case "sr_list_source_values":
                return await CallSrAsync(HttpMethod.Get, "/configuration/sources/" + EscapeRequired(args, "type") + "/values", null, null).ConfigureAwait(false);

            case "sr_list_rejection_reasons":
                return await CallSrAsync(HttpMethod.Get, "/configuration/reasons/rejection", null, null).ConfigureAwait(false);

            case "sr_list_withdrawal_reasons":
                return await CallSrAsync(HttpMethod.Get, "/configuration/reasons/withdrawal", null, null).ConfigureAwait(false);

            case "sr_list_job_properties":
                return await CallSrAsync(HttpMethod.Get, "/configuration/job-properties", null, null).ConfigureAwait(false);

            case "sr_list_candidate_properties":
                return await CallSrAsync(HttpMethod.Get, "/configuration/candidate-properties", null, null).ConfigureAwait(false);

            case "sr_list_candidate_property_values":
                return await CallSrAsync(HttpMethod.Get, "/configuration/candidate-properties/" + EscapeRequired(args, "id") + "/values", null, null).ConfigureAwait(false);

            case "sr_list_career_sites":
                return await CallSrAsync(HttpMethod.Get, "/configuration/career-sites", null, null).ConfigureAwait(false);

            case "sr_list_predefined_locations":
                return await CallSrAsync(HttpMethod.Get, "/configuration/predefined-locations", null, BuildQuery(args, "limit","offset")).ConfigureAwait(false);

            case "sr_list_system_roles":
                return await CallSrAsync(HttpMethod.Get, "/system-roles", null, null).ConfigureAwait(false);

            case "sr_list_access_groups":
                return await CallSrAsync(HttpMethod.Get, "/access-groups", null, null).ConfigureAwait(false);

            // ===== Public Posting API =====
            case "sr_list_public_postings":
                {
                    var company = EscapeRequired(args, "companyIdentifier");
                    return await CallSrAsync(HttpMethod.Get, "/v1/companies/" + company + "/postings", null, BuildQuery(args, "q","limit","offset","country","city","department","language","region")).ConfigureAwait(false);
                }

            case "sr_get_public_posting":
                return await CallSrAsync(HttpMethod.Get, "/v1/companies/" + EscapeRequired(args, "companyIdentifier") + "/postings/" + EscapeRequired(args, "postingId"), null, null).ConfigureAwait(false);

            case "sr_list_public_departments":
                return await CallSrAsync(HttpMethod.Get, "/v1/companies/" + EscapeRequired(args, "companyIdentifier") + "/departments", null, null).ConfigureAwait(false);

            // ===== Application API =====
            case "sr_post_application":
                return await ExecutePostApplicationAsync(args).ConfigureAwait(false);

            case "sr_get_application_configuration":
                {
                    var query = BuildQuery(args, "conditionalsIncluded");
                    var headers = new Dictionary<string, string>();
                    var lang = args.Value<string>("language");
                    if (!string.IsNullOrWhiteSpace(lang)) headers["Accept-Language"] = lang;
                    return await CallSrAsync(HttpMethod.Get, "/postings/" + EscapeRequired(args, "uuid") + "/configuration", null, query, headers).ConfigureAwait(false);
                }

            case "sr_get_candidate_application_status":
                return await CallSrAsync(HttpMethod.Get, "/postings/" + EscapeRequired(args, "uuid") + "/candidates/" + EscapeRequired(args, "candidateId") + "/status", null, null).ConfigureAwait(false);

            // ===== Jobs write =====
            case "sr_create_job":
                return await CallSrAsync(HttpMethod.Post, "/jobs", BuildJobCreateBody(args), null).ConfigureAwait(false);

            case "sr_update_job_status":
                return await CallSrAsync(HttpMethod.Put, "/jobs/" + EscapeRequired(args, "id") + "/status",
                    new JObject { ["status"] = RequireArgument(args, "status") }, null).ConfigureAwait(false);

            case "sr_update_job_headcount":
                {
                    var hc = args["targetHiringCount"];
                    if (hc == null) throw new ArgumentException("targetHiringCount is required");
                    return await CallSrAsync(HttpMethod.Put, "/jobs/" + EscapeRequired(args, "id") + "/headcount",
                        new JObject { ["targetHiringCount"] = hc }, null).ConfigureAwait(false);
                }

            case "sr_update_job_note":
                return await CallSrAsync(HttpMethod.Put, "/jobs/" + EscapeRequired(args, "id") + "/note",
                    new JObject { ["content"] = RequireArgument(args, "content") }, null).ConfigureAwait(false);

            case "sr_publish_job":
                return await CallSrAsync(HttpMethod.Post, "/jobs/" + EscapeRequired(args, "id") + "/publication", new JObject(), null).ConfigureAwait(false);

            case "sr_unpublish_job":
                return await CallSrAsync(HttpMethod.Delete, "/jobs/" + EscapeRequired(args, "id") + "/publication", null, null).ConfigureAwait(false);

            case "sr_add_hiring_team_member":
                return await CallSrAsync(HttpMethod.Post, "/jobs/" + EscapeRequired(args, "id") + "/hiring-team",
                    new JObject { ["userId"] = RequireArgument(args, "userId"), ["roleId"] = RequireArgument(args, "roleId") }, null).ConfigureAwait(false);

            case "sr_remove_hiring_team_member":
                return await CallSrAsync(HttpMethod.Delete, "/jobs/" + EscapeRequired(args, "id") + "/hiring-team/" + EscapeRequired(args, "userId"), null, null).ConfigureAwait(false);

            case "sr_publish_job_ad":
                return await CallSrAsync(HttpMethod.Post, "/jobs/" + EscapeRequired(args, "id") + "/jobads/" + EscapeRequired(args, "jobAdId") + "/postings", new JObject(), null).ConfigureAwait(false);

            case "sr_unpublish_job_ad":
                return await CallSrAsync(HttpMethod.Delete, "/jobs/" + EscapeRequired(args, "id") + "/jobads/" + EscapeRequired(args, "jobAdId") + "/postings", null, null).ConfigureAwait(false);

            // ===== Candidates write =====
            case "sr_create_candidate":
                {
                    var body = BuildCandidateCreateBody(args);
                    var query = string.Empty;
                    var jobId = args.Value<string>("jobId");
                    if (!string.IsNullOrWhiteSpace(jobId)) query = "?jobId=" + Uri.EscapeDataString(jobId);
                    return await CallSrAsync(HttpMethod.Post, "/candidates", body, query).ConfigureAwait(false);
                }

            case "sr_update_candidate":
                return await CallSrAsync(new HttpMethod("PATCH"), "/candidates/" + EscapeRequired(args, "id"), BuildCandidateUpdateBody(args), null).ConfigureAwait(false);

            case "sr_delete_candidate":
                return await CallSrAsync(HttpMethod.Delete, "/candidates/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_parse_resume":
                return await CallSrAsync(HttpMethod.Post, "/candidates/resume", BuildResumeBody(args), null).ConfigureAwait(false);

            case "sr_parse_resume_to_job":
                return await CallSrAsync(HttpMethod.Post, "/candidates/resume/jobs/" + EscapeRequired(args, "jobId"), BuildResumeBody(args), null).ConfigureAwait(false);

            case "sr_update_application_status":
                {
                    var body = new JObject { ["status"] = RequireArgument(args, "status") };
                    var reasonId = args.Value<string>("reasonId");
                    if (!string.IsNullOrWhiteSpace(reasonId)) body["reasonId"] = reasonId;
                    return await CallSrAsync(HttpMethod.Put, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/status", body, null).ConfigureAwait(false);
                }

            case "sr_update_application_source":
                {
                    var body = new JObject
                    {
                        ["sourceTypeId"] = RequireArgument(args, "sourceTypeId"),
                        ["sourceId"] = RequireArgument(args, "sourceId")
                    };
                    var subType = args.Value<string>("sourceSubTypeId");
                    if (!string.IsNullOrWhiteSpace(subType)) body["sourceSubTypeId"] = subType;
                    return await CallSrAsync(HttpMethod.Put, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/source", body, null).ConfigureAwait(false);
                }

            case "sr_add_candidate_tags":
                return await CallSrAsync(HttpMethod.Post, "/candidates/" + EscapeRequired(args, "id") + "/tags",
                    new JObject { ["tags"] = RequireArrayArgument(args, "tags") }, null).ConfigureAwait(false);

            case "sr_replace_candidate_tags":
                return await CallSrAsync(HttpMethod.Put, "/candidates/" + EscapeRequired(args, "id") + "/tags",
                    new JObject { ["tags"] = RequireArrayArgument(args, "tags") }, null).ConfigureAwait(false);

            case "sr_delete_candidate_tags":
                return await CallSrAsync(HttpMethod.Delete, "/candidates/" + EscapeRequired(args, "id") + "/tags", null, null).ConfigureAwait(false);

            case "sr_update_application_properties":
                {
                    var props = args["properties"] as JArray;
                    if (props == null) throw new ArgumentException("properties array is required");
                    // SR /properties endpoint expects raw array body
                    return await CallSrRawAsync(HttpMethod.Put, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/properties",
                        props.ToString(Newtonsoft.Json.Formatting.None), null, null).ConfigureAwait(false);
                }

            case "sr_request_candidate_consent":
                return await CallSrAsync(HttpMethod.Post, "/candidates/" + EscapeRequired(args, "id") + "/consent/request", new JObject(), null).ConfigureAwait(false);

            case "sr_update_onboarding_status":
                return await CallSrAsync(HttpMethod.Put, "/candidates/" + EscapeRequired(args, "id") + "/jobs/" + EscapeRequired(args, "jobId") + "/onboardingStatus",
                    new JObject { ["status"] = RequireArgument(args, "status") }, null).ConfigureAwait(false);

            // ===== Users =====
            case "sr_list_users":
                return await CallSrAsync(HttpMethod.Get, "/users", null, BuildQuery(args, "q","active","limit","offset")).ConfigureAwait(false);

            case "sr_get_user":
                return await CallSrAsync(HttpMethod.Get, "/users/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_get_my_user":
                return await CallSrAsync(HttpMethod.Get, "/users/me", null, null).ConfigureAwait(false);

            case "sr_create_user":
                return await CallSrAsync(HttpMethod.Post, "/users", BuildUserCreateBody(args), null).ConfigureAwait(false);

            case "sr_update_user":
                return await CallSrAsync(HttpMethod.Put, "/users/" + EscapeRequired(args, "id"), BuildUserUpdateBody(args), null).ConfigureAwait(false);

            case "sr_activate_user":
                return await CallSrAsync(HttpMethod.Put, "/users/" + EscapeRequired(args, "id") + "/activation", null, null).ConfigureAwait(false);

            case "sr_deactivate_user":
                return await CallSrAsync(HttpMethod.Delete, "/users/" + EscapeRequired(args, "id") + "/activation", null, null).ConfigureAwait(false);

            case "sr_send_activation_email":
                return await CallSrAsync(HttpMethod.Post, "/users/" + EscapeRequired(args, "id") + "/activation/email", new JObject(), null).ConfigureAwait(false);

            case "sr_send_password_reset":
                return await CallSrAsync(HttpMethod.Post, "/users/" + EscapeRequired(args, "id") + "/password/reset", new JObject(), null).ConfigureAwait(false);

            // ===== Approvals =====
            case "sr_list_pending_approvals":
                return await CallSrAsync(HttpMethod.Get, "/approvals", null, null).ConfigureAwait(false);

            case "sr_get_approval":
                return await CallSrAsync(HttpMethod.Get, "/approvals/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_create_approval_request":
                return await CallSrAsync(HttpMethod.Post, "/approvals",
                    new JObject { ["baseId"] = RequireArgument(args, "baseId") }, null).ConfigureAwait(false);

            case "sr_approve_request":
                {
                    var body = new JObject();
                    var comment = args.Value<string>("comment");
                    if (!string.IsNullOrWhiteSpace(comment)) body["comment"] = comment;
                    return await CallSrAsync(HttpMethod.Post, "/approvals/" + EscapeRequired(args, "id") + "/approve", body, null).ConfigureAwait(false);
                }

            case "sr_reject_approval":
                {
                    var body = new JObject();
                    var comment = args.Value<string>("comment");
                    var reasonId = args.Value<string>("reasonId");
                    if (!string.IsNullOrWhiteSpace(comment)) body["comment"] = comment;
                    if (!string.IsNullOrWhiteSpace(reasonId)) body["reasonId"] = reasonId;
                    return await CallSrAsync(HttpMethod.Post, "/approvals/" + EscapeRequired(args, "id") + "/reject", body, null).ConfigureAwait(false);
                }

            case "sr_get_approval_comments":
                return await CallSrAsync(HttpMethod.Get, "/approvals/" + EscapeRequired(args, "id") + "/comments", null, null).ConfigureAwait(false);

            case "sr_add_approval_comment":
                return await CallSrAsync(HttpMethod.Post, "/approvals/" + EscapeRequired(args, "id") + "/comments",
                    new JObject { ["text"] = RequireArgument(args, "text") }, null).ConfigureAwait(false);

            // ===== Notes / messages =====
            case "sr_create_note":
                return await CallSrAsync(HttpMethod.Post, "/messages/shares", BuildNoteBody(args), null).ConfigureAwait(false);

            case "sr_delete_note":
                return await CallSrAsync(HttpMethod.Delete, "/messages/shares/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_fetch_messages":
                {
                    var q = new Dictionary<string, string> { ["candidateId"] = RequireArgument(args, "candidateId") };
                    var jobId = args.Value<string>("jobId");
                    if (!string.IsNullOrWhiteSpace(jobId)) q["jobId"] = jobId;
                    var limit = args["limit"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(limit)) q["limit"] = limit;
                    return await CallSrAsync(HttpMethod.Get, "/messages/fetch", null, BuildQueryFromDict(q)).ConfigureAwait(false);
                }

            // ===== Interviews =====
            case "sr_list_interviews":
                return await CallSrAsync(HttpMethod.Get, "/public-api/interviews", null, BuildQuery(args, "candidateId","applicationId","limit","offset")).ConfigureAwait(false);

            case "sr_get_interview":
                return await CallSrAsync(HttpMethod.Get, "/public-api/interviews/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_create_interview":
                {
                    var body = args["body"] as JObject ?? new JObject();
                    return await CallSrAsync(HttpMethod.Post, "/public-api/interviews", body, null).ConfigureAwait(false);
                }

            case "sr_delete_interview":
                return await CallSrAsync(HttpMethod.Delete, "/public-api/interviews/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_list_interview_types":
                return await CallSrAsync(HttpMethod.Get, "/public-api/types", null, null).ConfigureAwait(false);

            // ===== Reports =====
            case "sr_list_reports":
                return await CallSrAsync(HttpMethod.Get, "/reporting-api/v201804/reports", null, BuildQuery(args, "page")).ConfigureAwait(false);

            case "sr_get_report":
                return await CallSrAsync(HttpMethod.Get, "/reporting-api/v201804/reports/" + EscapeRequired(args, "reportId"), null, null).ConfigureAwait(false);

            case "sr_list_report_files":
                return await CallSrAsync(HttpMethod.Get, "/reporting-api/v201804/reports/" + EscapeRequired(args, "reportId") + "/files", null, BuildQuery(args, "page")).ConfigureAwait(false);

            case "sr_get_most_recent_report_file":
                return await CallSrAsync(HttpMethod.Get, "/reporting-api/v201804/reports/" + EscapeRequired(args, "reportId") + "/files/recent", null, null).ConfigureAwait(false);

            case "sr_generate_ad_hoc_report":
                return await CallSrAsync(HttpMethod.Post, "/reporting-api/v201804/reports/" + EscapeRequired(args, "reportId") + "/files", new JObject(), null).ConfigureAwait(false);

            // ===== Webhooks =====
            case "sr_list_subscriptions":
                return await CallSrAsync(HttpMethod.Get, "/subscriptions", null, null).ConfigureAwait(false);

            case "sr_create_subscription":
                {
                    var body = new JObject
                    {
                        ["callbackUrl"] = RequireArgument(args, "callbackUrl"),
                        ["events"] = RequireArrayArgument(args, "events")
                    };
                    var alerting = args.Value<string>("alertingEmail");
                    if (!string.IsNullOrWhiteSpace(alerting)) body["alertingEmail"] = alerting;
                    return await CallSrAsync(HttpMethod.Post, "/subscriptions", body, null).ConfigureAwait(false);
                }

            case "sr_get_subscription":
                return await CallSrAsync(HttpMethod.Get, "/subscriptions/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_delete_subscription":
                return await CallSrAsync(HttpMethod.Delete, "/subscriptions/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_activate_subscription":
                return await CallSrAsync(HttpMethod.Post, "/subscriptions/" + EscapeRequired(args, "id") + "/activation", new JObject(), null).ConfigureAwait(false);

            // ===== Reviews =====
            case "sr_list_reviews":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "candidateId") + "/jobs/" + EscapeRequired(args, "jobId") + "/reviews", null, null).ConfigureAwait(false);

            case "sr_create_review":
                {
                    var rating = args["rating"];
                    if (rating == null) throw new ArgumentException("rating is required");
                    var body = new JObject { ["rating"] = rating };
                    var comment = args.Value<string>("comment");
                    if (!string.IsNullOrWhiteSpace(comment)) body["comment"] = comment;
                    return await CallSrAsync(HttpMethod.Post, "/candidates/" + EscapeRequired(args, "candidateId") + "/jobs/" + EscapeRequired(args, "jobId") + "/reviews", body, null).ConfigureAwait(false);
                }

            case "sr_get_scorecard_criteria_by_job":
                return await CallSrAsync(HttpMethod.Get, "/jobs/" + EscapeRequired(args, "jobId") + "/scorecards/criteria", null, null).ConfigureAwait(false);

            // ===== Candidate offers (detail) =====
            case "sr_search_offers":
                return await CallSrAsync(HttpMethod.Get, "/offers", null, BuildQuery(args, "q","status","limit","offset")).ConfigureAwait(false);

            case "sr_find_candidate_offers":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "candidateId") + "/offers/find", null, BuildQuery(args, "status","limit")).ConfigureAwait(false);

            case "sr_get_candidate_offer":
                return await CallSrAsync(HttpMethod.Get, "/candidates/" + EscapeRequired(args, "candidateId") + "/offers/" + EscapeRequired(args, "offerId"), null, null).ConfigureAwait(false);

            case "sr_list_offer_documents":
                return await CallSrAsync(HttpMethod.Get, "/offers/" + EscapeRequired(args, "offerId") + "/documents", null, null).ConfigureAwait(false);

            // ===== Self-scheduling extras =====
            case "sr_search_self_schedules":
                return await CallSrAsync(HttpMethod.Get, "/public-api/self-schedules", null, BuildQuery(args, "applicationId","status","limit","offset")).ConfigureAwait(false);

            case "sr_get_application_self_schedule":
                return await CallSrAsync(HttpMethod.Get, "/public-api/applications/" + EscapeRequired(args, "applicationId") + "/self-schedule", null, null).ConfigureAwait(false);

            case "sr_request_self_reschedule":
                {
                    var body = new JObject { ["applicationId"] = RequireArgument(args, "applicationId") };
                    AddIfPresent(body, args, "earliestDate");
                    AddIfPresent(body, args, "latestDate");
                    var interviewers = args["interviewerIds"] as JArray;
                    if (interviewers != null && interviewers.Count > 0) body["interviewerIds"] = interviewers;
                    return await CallSrAsync(HttpMethod.Post, "/public-api/self-schedules/" + EscapeRequired(args, "id") + "/reschedule", body, null).ConfigureAwait(false);
                }

            case "sr_update_self_schedule_invite":
                {
                    var body = new JObject();
                    AddIfPresent(body, args, "earliestDate");
                    AddIfPresent(body, args, "latestDate");
                    var interviewers = args["interviewerIds"] as JArray;
                    if (interviewers != null && interviewers.Count > 0) body["interviewerIds"] = interviewers;
                    return await CallSrAsync(HttpMethod.Put, "/public-api/self-schedules/" + EscapeRequired(args, "id") + "/invite", body, null).ConfigureAwait(false);
                }

            // ===== Interview statuses + no-show =====
            case "sr_update_interview_candidate_status":
                {
                    var body = new JObject { ["status"] = RequireArgument(args, "status") };
                    AddIfPresent(body, args, "comment");
                    return await CallSrAsync(HttpMethod.Put, "/public-api/interviews/" + EscapeRequired(args, "interviewId") + "/statuses/candidate", body, null).ConfigureAwait(false);
                }

            case "sr_update_timeslot_interviewer_status":
                {
                    var body = new JObject
                    {
                        ["interviewerId"] = RequireArgument(args, "interviewerId"),
                        ["status"] = RequireArgument(args, "status")
                    };
                    AddIfPresent(body, args, "comment");
                    return await CallSrAsync(HttpMethod.Put, "/public-api/interviews/" + EscapeRequired(args, "interviewId") + "/timeslots/" + EscapeRequired(args, "slotId") + "/statuses/interviewer", body, null).ConfigureAwait(false);
                }

            case "sr_update_timeslot_candidate_status":
                {
                    var body = new JObject { ["status"] = RequireArgument(args, "status") };
                    AddIfPresent(body, args, "comment");
                    return await CallSrAsync(HttpMethod.Put, "/public-api/interviews/" + EscapeRequired(args, "interviewId") + "/timeslots/" + EscapeRequired(args, "slotId") + "/statuses/candidate", body, null).ConfigureAwait(false);
                }

            case "sr_update_timeslot_noshow":
                {
                    var noShow = args["noShow"];
                    if (noShow == null || noShow.Type == JTokenType.Null) throw new ArgumentException("noShow is required");
                    var body = new JObject { ["noShow"] = noShow };
                    AddIfPresent(body, args, "comment");
                    return await CallSrAsync(new HttpMethod("PATCH"), "/public-api/interviews/" + EscapeRequired(args, "interviewId") + "/timeslots/" + EscapeRequired(args, "slotId") + "/noshow", body, null).ConfigureAwait(false);
                }

            // ===== Onboarding processes detail =====
            case "sr_get_onboarding_process":
                return await CallSrAsync(HttpMethod.Get, "/onboarding-processes/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            case "sr_get_onboarding_assignments":
                return await CallSrAsync(HttpMethod.Get, "/onboarding-processes/" + EscapeRequired(args, "id") + "/assignments", null, null).ConfigureAwait(false);

            case "sr_get_web_form_answers":
                return await CallSrAsync(HttpMethod.Get, "/web-form-assignments/" + EscapeRequired(args, "assignmentId") + "/form-answers", null, null).ConfigureAwait(false);

            case "sr_get_new_hire":
                return await CallSrAsync(HttpMethod.Get, "/new-hires/" + EscapeRequired(args, "id"), null, null).ConfigureAwait(false);

            // ===== Audit =====
            case "sr_list_audit_events":
                return await CallSrAsync(HttpMethod.Get, "/audit/events", null, BuildQuery(args, "eventDateAfter","eventDateBefore","entityType","entityId","eventName","limit")).ConfigureAwait(false);

            default:
                throw new ArgumentException("Unknown tool: " + toolName);
        }
    }

    // ========================================
    // REQUEST BODY BUILDERS
    // ========================================

    private static JObject BuildPostApplicationBody(JObject a)
    {
        var body = new JObject
        {
            ["firstName"] = RequireArgument(a, "firstName"),
            ["lastName"] = RequireArgument(a, "lastName"),
            ["email"] = RequireArgument(a, "email")
        };
        var phone = a.Value<string>("phoneNumber");
        if (!string.IsNullOrWhiteSpace(phone)) body["phoneNumber"] = phone;
        var message = a.Value<string>("messageToHiringManager");
        if (!string.IsNullOrWhiteSpace(message)) body["messageToHiringManager"] = message;

        var resumeName = a.Value<string>("resumeFileName");
        var resumeMime = a.Value<string>("resumeMimeType");
        var resumeContent = a.Value<string>("resumeContentBase64");
        if (!string.IsNullOrWhiteSpace(resumeName) && !string.IsNullOrWhiteSpace(resumeContent))
        {
            body["resume"] = new JObject
            {
                ["fileName"] = resumeName,
                ["mimeType"] = string.IsNullOrWhiteSpace(resumeMime) ? "application/pdf" : resumeMime,
                ["fileContent"] = resumeContent
            };
        }

        var source = BuildSourceDetails(a);
        if (source != null) body["sourceDetails"] = source;

        return body;
    }

    private static JObject BuildCandidateCreateBody(JObject a)
    {
        var body = new JObject
        {
            ["firstName"] = RequireArgument(a, "firstName"),
            ["lastName"] = RequireArgument(a, "lastName"),
            ["email"] = RequireArgument(a, "email")
        };
        var phone = a.Value<string>("phoneNumber");
        if (!string.IsNullOrWhiteSpace(phone)) body["phoneNumber"] = phone;
        var location = BuildLocation(a);
        if (location != null) body["location"] = location;
        var source = BuildSourceDetails(a);
        if (source != null) body["sourceDetails"] = source;
        var tags = a["tags"] as JArray;
        if (tags != null && tags.Count > 0) body["tags"] = tags;
        return body;
    }

    private static JObject BuildCandidateUpdateBody(JObject a)
    {
        var body = new JObject();
        AddIfPresent(body, a, "firstName");
        AddIfPresent(body, a, "lastName");
        AddIfPresent(body, a, "email");
        AddIfPresent(body, a, "phoneNumber");
        var location = BuildLocation(a);
        if (location != null) body["location"] = location;
        return body;
    }

    private static JObject BuildResumeBody(JObject a)
    {
        var body = new JObject
        {
            ["resume"] = new JObject
            {
                ["fileName"] = RequireArgument(a, "fileName"),
                ["mimeType"] = RequireArgument(a, "mimeType"),
                ["fileContent"] = RequireArgument(a, "contentBase64")
            }
        };
        var source = BuildSourceDetails(a);
        if (source != null) body["sourceDetails"] = source;
        return body;
    }

    private static JObject BuildJobCreateBody(JObject a)
    {
        var body = new JObject { ["title"] = RequireArgument(a, "title") };
        AddIfPresent(body, a, "refNumber");

        AddIdObjectIfPresent(body, "department", a.Value<string>("departmentId"));
        AddIdObjectIfPresent(body, "industry", a.Value<string>("industryId"));
        AddIdObjectIfPresent(body, "function", a.Value<string>("functionId"));
        AddIdObjectIfPresent(body, "experienceLevel", a.Value<string>("experienceLevelId"));
        AddIdObjectIfPresent(body, "typeOfEmployment", a.Value<string>("typeOfEmploymentId"));
        AddIdObjectIfPresent(body, "creator", a.Value<string>("creatorId"));

        var loc = BuildLocation(a);
        if (loc != null) body["location"] = loc;

        var comp = new JObject();
        AddIfPresent(comp, a, "compensationCurrency", "currency");
        var min = a["compensationMin"]; if (min != null && min.Type != JTokenType.Null) comp["min"] = min;
        var max = a["compensationMax"]; if (max != null && max.Type != JTokenType.Null) comp["max"] = max;
        AddIfPresent(comp, a, "compensationPeriod", "period");
        if (comp.HasValues) body["compensation"] = comp;

        return body;
    }

    private static JObject BuildUserCreateBody(JObject a)
    {
        var body = new JObject
        {
            ["firstName"] = RequireArgument(a, "firstName"),
            ["lastName"] = RequireArgument(a, "lastName"),
            ["email"] = RequireArgument(a, "email"),
            ["systemRole"] = new JObject { ["id"] = RequireArgument(a, "systemRoleId") }
        };
        var langCode = a.Value<string>("languageCode");
        body["language"] = new JObject { ["code"] = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode };

        var loc = BuildLocation(a);
        if (loc != null) body["location"] = loc;
        AddIfPresent(body, a, "title");
        AddIdObjectIfPresent(body, "department", a.Value<string>("departmentId"));
        return body;
    }

    private static JObject BuildUserUpdateBody(JObject a)
    {
        var body = new JObject();
        AddIfPresent(body, a, "firstName");
        AddIfPresent(body, a, "lastName");
        AddIfPresent(body, a, "title");
        var roleId = a.Value<string>("systemRoleId");
        if (!string.IsNullOrWhiteSpace(roleId)) body["systemRole"] = new JObject { ["id"] = roleId };
        var langCode = a.Value<string>("languageCode");
        if (!string.IsNullOrWhiteSpace(langCode)) body["language"] = new JObject { ["code"] = langCode };
        AddIdObjectIfPresent(body, "department", a.Value<string>("departmentId"));
        return body;
    }

    private static JObject BuildNoteBody(JObject a)
    {
        var body = new JObject { ["content"] = RequireArgument(a, "content") };
        AddIfPresent(body, a, "candidateId");
        AddIfPresent(body, a, "jobId");

        var share = new JObject();
        var users = a["shareWithUsers"] as JArray;
        if (users != null && users.Count > 0) share["users"] = users;
        var teams = a["shareWithHiringTeamOfJobIds"] as JArray;
        if (teams != null && teams.Count > 0) share["hiringTeamOf"] = teams;
        var everyone = a["everyone"]; if (everyone != null && everyone.Type != JTokenType.Null) share["everyone"] = everyone;
        var openNote = a["openNote"]; if (openNote != null && openNote.Type != JTokenType.Null) share["openNote"] = openNote;
        if (share.HasValues) body["shareWith"] = share;

        return body;
    }

    private static JObject BuildLocation(JObject a)
    {
        var loc = new JObject();
        AddIfPresent(loc, a, "city");
        AddIfPresent(loc, a, "countryCode");
        AddIfPresent(loc, a, "regionCode");
        return loc.HasValues ? loc : null;
    }

    private static JObject BuildSourceDetails(JObject a)
    {
        var typeId = a.Value<string>("sourceTypeId");
        var sourceId = a.Value<string>("sourceId");
        if (string.IsNullOrWhiteSpace(typeId) && string.IsNullOrWhiteSpace(sourceId)) return null;
        var src = new JObject();
        if (!string.IsNullOrWhiteSpace(typeId)) src["sourceTypeId"] = typeId;
        if (!string.IsNullOrWhiteSpace(sourceId)) src["sourceId"] = sourceId;
        var sub = a.Value<string>("sourceSubTypeId");
        if (!string.IsNullOrWhiteSpace(sub)) src["sourceSubTypeId"] = sub;
        return src;
    }

    private static void AddIfPresent(JObject target, JObject source, string sourceKey, string targetKey = null)
    {
        var value = source.Value<string>(sourceKey);
        if (!string.IsNullOrWhiteSpace(value)) target[targetKey ?? sourceKey] = value;
    }

    private static void AddIdObjectIfPresent(JObject target, string key, string id)
    {
        if (!string.IsNullOrWhiteSpace(id)) target[key] = new JObject { ["id"] = id };
    }

    private async Task<JObject> ExecutePostApplicationAsync(JObject args)
    {
        var body = BuildPostApplicationBody(args);
        return await CallSrAsync(HttpMethod.Post, "/postings/" + EscapeRequired(args, "uuid") + "/candidates", body, null).ConfigureAwait(false);
    }

    // ========================================
    // SMARTRECRUITERS HTTP HELPERS
    // ========================================

    private async Task<JObject> CallSrAsync(HttpMethod method, string path, JObject body, string queryOrParams, IDictionary<string, string> extraHeaders = null)
    {
        return await CallSrRawAsync(method, path, body?.ToString(Newtonsoft.Json.Formatting.None), queryOrParams, extraHeaders).ConfigureAwait(false);
    }

    private async Task<JObject> CallSrRawAsync(HttpMethod method, string path, string rawBody, string queryOrParams, IDictionary<string, string> extraHeaders)
    {
        var url = SmartRecruitersApiBase + path;
        if (!string.IsNullOrWhiteSpace(queryOrParams))
        {
            url += queryOrParams.StartsWith("?") ? queryOrParams : ("?" + queryOrParams);
        }

        var request = new HttpRequestMessage(method, new Uri(url));

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        }

        // Forward Bearer token (installed onto Context.Request.Headers.Authorization by ExecuteAsync
        // after exchanging client_id + client_secret at the SmartRecruiters token endpoint).
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                this.Context.Request.Headers.Authorization.Scheme,
                this.Context.Request.Headers.Authorization.Parameter);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (extraHeaders != null)
        {
            foreach (var kv in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var result = new JObject { ["statusCode"] = (int)response.StatusCode };

        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        try
        {
            var token = JToken.Parse(payload);
            result["data"] = token;
        }
        catch
        {
            result["raw"] = payload;
        }

        if (!response.IsSuccessStatusCode)
        {
            result["isError"] = true;
        }

        return result;
    }

    // ========================================
    // ARGUMENT / QUERY HELPERS
    // ========================================

    private static string RequireArgument(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(name + " is required");
        }
        return value;
    }

    private static JArray RequireArrayArgument(JObject args, string name)
    {
        var arr = args[name] as JArray;
        if (arr == null || arr.Count == 0)
        {
            throw new ArgumentException(name + " is required and must be a non-empty array");
        }
        return arr;
    }

    private static string EscapeRequired(JObject args, string name)
    {
        return Uri.EscapeDataString(RequireArgument(args, name));
    }

    private static string BuildQuery(JObject args, params string[] keys)
    {
        var parts = new List<string>();
        foreach (var key in keys)
        {
            var token = args[key];
            if (token == null || token.Type == JTokenType.Null) continue;
            var value = token.ToString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            parts.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
        }
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string BuildQueryFromDict(IDictionary<string, string> values)
    {
        var parts = new List<string>();
        foreach (var kv in values)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value));
        }
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string DescribeWithDeps(string baseDescription, params string[] dependencies)
    {
        if (dependencies == null || dependencies.Length == 0) return baseDescription;
        var deps = string.Join(" or ", dependencies.Select(d => "`" + d + "`"));
        return baseDescription + " Call " + deps + " first to discover the required identifier(s).";
    }

    // ========================================
    // TOOL DEFINITION HELPERS (compact)
    // ========================================

    private static JObject Tool(string name, string description, string[] required, params (string Name, string Type, string Description)[] props)
    {
        var properties = new JObject();
        foreach (var p in props)
        {
            var propDef = new JObject { ["type"] = p.Type };
            if (!string.IsNullOrWhiteSpace(p.Description)) propDef["description"] = p.Description;
            if (p.Type == "array")
            {
                propDef["items"] = new JObject { ["type"] = "string" };
            }
            properties[p.Name] = propDef;
        }

        var requiredArr = new JArray();
        if (required != null)
        {
            foreach (var r in required) requiredArr.Add(r);
        }

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = requiredArr
            }
        };
    }

    private static (string, string, string) P(string name, string type, string description = null) => (name, type, description);

    private static string[] Req(params string[] names) => names;

    // ========================================
    // JSON-RPC RESPONSE HELPERS
    // ========================================

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
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

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

    private HttpResponseMessage CreateErrorResponse(string message, HttpStatusCode statusCode)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = (int)statusCode
            }
        };
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(error.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey)) return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                }
            }

            var telemetryData = new
            {
                name = "Microsoft.ApplicationInsights." + instrumentationKey + ".Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring(prefix.Length);
            }
        }
        return key == "IngestionEndpoint" ? "https://dc.services.visualstudio.com/" : null;
    }
}
