using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string PROTOCOL_VERSION = "2025-03-26";
    private const string SERVER_NAME = "epic-fhir-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string FHIR_JSON = "application/fhir+json";
    private const int DEFAULT_MAX_TEXT_CHARS = 40000;

    // Telemetry records operation and tool names, status codes, and durations only. Never add patient data.
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private static readonly Regex FhirIdPattern = new Regex(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.Compiled);

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (this.Context.OperationId == "InvokeMCP")
        {
            return await HandleMcpRequestAsync().ConfigureAwait(false);
        }

        return await HandleDirectOperationAsync(this.Context.OperationId).ConfigureAwait(false);
    }

    // ── Direct operations (Power Automate / Power Apps) ──────────────────

    private async Task<HttpResponseMessage> HandleDirectOperationAsync(string operationId)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var includeDecodedText = false;

            switch (operationId)
            {
                case "SearchDocumentReferences":
                case "SearchEncounters":
                    RewriteQuery(query => ExpandDateRange(query));
                    break;

                case "GetBinary":
                    RewriteQuery(query =>
                    {
                        var flag = query.FirstOrDefault(kv => kv.Key == "includeDecodedText");
                        includeDecodedText = string.Equals(Uri.UnescapeDataString(flag.Value ?? ""), "true", StringComparison.OrdinalIgnoreCase);
                        return query.Where(kv => kv.Key != "includeDecodedText").ToList();
                    });
                    break;

                case "SearchPatients":
                case "GetPatient":
                case "GetDocumentReference":
                case "GetEncounter":
                case "SearchCoverages":
                case "GetCoverage":
                    break;

                default:
                    return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject
                    {
                        ["error"] = new JObject { ["message"] = $"Unknown operation: {operationId}" }
                    });
            }

            this.Context.Request.Headers.Accept.Clear();
            this.Context.Request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(FHIR_JSON));

            var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            var body = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : "";

            await LogToAppInsightsAsync("DirectOperation", new Dictionary<string, string>
            {
                ["Operation"] = operationId,
                ["StatusCode"] = ((int)response.StatusCode).ToString(),
                ["DurationMs"] = ((int)(DateTime.UtcNow - startTime).TotalMilliseconds).ToString()
            }).ConfigureAwait(false);

            if (!LooksLikeJson(body))
            {
                return response;
            }

            // Epic returns application/fhir+json; Power Platform parses action outputs reliably only as application/json.
            var binary = operationId == "GetBinary" && includeDecodedText && response.IsSuccessStatusCode ? ParseJson(body) as JObject : null;
            if (binary != null)
            {
                var decoded = DecodeBinaryText(binary["contentType"]?.ToString(), binary["data"]?.ToString());
                if (decoded != null)
                {
                    binary["decodedText"] = decoded;
                }
                body = binary.ToString(Newtonsoft.Json.Formatting.None);
            }

            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("DirectOperationError", new Dictionary<string, string>
            {
                ["Operation"] = operationId,
                ["ErrorType"] = ex.GetType().Name
            }).ConfigureAwait(false);

            return CreateJsonResponse(HttpStatusCode.BadGateway, new JObject
            {
                ["error"] = new JObject { ["message"] = ex.Message }
            });
        }
    }

    private void RewriteQuery(Func<List<KeyValuePair<string, string>>, List<KeyValuePair<string, string>>> transform)
    {
        var uri = this.Context.Request.RequestUri;
        var pairs = ParseRawQuery(uri.Query);
        var rewritten = transform(pairs);
        var query = string.Join("&", rewritten.Select(kv => kv.Value == null ? kv.Key : kv.Key + "=" + kv.Value));
        var builder = new UriBuilder(uri) { Query = query };
        this.Context.Request.RequestUri = builder.Uri;
    }

    // Values stay URL-encoded exactly as the platform sent them.
    private static List<KeyValuePair<string, string>> ParseRawQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var part in query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            result.Add(idx < 0
                ? new KeyValuePair<string, string>(part, null)
                : new KeyValuePair<string, string>(part.Substring(0, idx), part.Substring(idx + 1)));
        }
        return result;
    }

    // FHIR expresses a date range as two "date" parameters, which a single OpenAPI parameter list cannot represent.
    private static List<KeyValuePair<string, string>> ExpandDateRange(List<KeyValuePair<string, string>> query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var kv in query)
        {
            if (kv.Key == "dateFrom" && !string.IsNullOrWhiteSpace(kv.Value))
                result.Add(new KeyValuePair<string, string>("date", "ge" + kv.Value));
            else if (kv.Key == "dateTo" && !string.IsNullOrWhiteSpace(kv.Value))
                result.Add(new KeyValuePair<string, string>("date", "le" + kv.Value));
            else if (kv.Key != "dateFrom" && kv.Key != "dateTo")
                result.Add(kv);
        }
        return result;
    }

    // ── MCP JSON-RPC handler (Copilot Studio) ────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var startTime = DateTime.UtcNow;
        JToken requestId = null;
        string method = null;

        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject request;
            try
            {
                request = ParseJson(body) as JObject;
                if (request == null) throw new JsonReaderException("The request body must be a JSON-RPC object.");
            }
            catch (JsonException ex)
            {
                return CreateJsonRpcError(null, -32700, "Parse error", ex.Message);
            }

            method = request["method"]?.ToString() ?? "";
            requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            HttpResponseMessage result;
            switch (method)
            {
                case "initialize":
                    result = HandleInitialize(@params, requestId);
                    break;

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    result = CreateJsonRpcSuccess(requestId, new JObject());
                    break;

                case "tools/list":
                    result = CreateJsonRpcSuccess(requestId, new JObject { ["tools"] = GetToolDefinitions() });
                    break;

                case "tools/call":
                    result = await HandleToolsCallAsync(@params, requestId).ConfigureAwait(false);
                    break;

                case "resources/list":
                    result = CreateJsonRpcSuccess(requestId, new JObject { ["resources"] = new JArray() });
                    break;

                case "prompts/list":
                    result = CreateJsonRpcSuccess(requestId, new JObject { ["prompts"] = new JArray() });
                    break;

                default:
                    result = CreateJsonRpcError(requestId, -32601, "Method not found", method);
                    break;
            }

            await LogToAppInsightsAsync("McpRequest", new Dictionary<string, string>
            {
                ["Method"] = method,
                ["DurationMs"] = ((int)(DateTime.UtcNow - startTime).TotalMilliseconds).ToString()
            }).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("McpRequestError", new Dictionary<string, string>
            {
                ["Method"] = method ?? "",
                ["ErrorType"] = ex.GetType().Name
            }).ConfigureAwait(false);

            return CreateJsonRpcError(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken requestId)
    {
        var requestedVersion = @params["protocolVersion"]?.ToString();

        return CreateJsonRpcSuccess(requestId, new JObject
        {
            ["protocolVersion"] = string.IsNullOrEmpty(requestedVersion) ? PROTOCOL_VERSION : requestedVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION
            },
            ["instructions"] = "Read-only access to Epic FHIR R4 as the signed-in user. Start with search_patients to find a patient's FHIR ID, then use it with the other tools. " +
                "Patient search can return several patients: never assume a match is the right person; confirm with the user using identifiers, name, and birth date. " +
                "For clinical notes, call search_document_references, then pass a content[].attachment.url value (Binary/{id}) to get_binary to read the note text. " +
                "If a search result includes nextPageUrl, call get_next_page to retrieve more results."
        });
    }

    private JArray GetToolDefinitions()
    {
        var countProp = Prop("integer", "Maximum results per page.");
        var patientIdProp = Prop("string", "Patient FHIR ID, from search_patients.");

        return new JArray
        {
            Tool("search_patients",
                "Search Epic patients by demographics or identifier. Epic requires one of these minimum combinations by default: identifier (MRN or other ID); given + family + birthdate; or given + family + legal_sex + telecom. " +
                "Can return several patients. Confirm the right patient with the user before using a result.",
                new JObject
                {
                    ["family"] = Prop("string", "Family (last) name."),
                    ["given"] = Prop("string", "Given (first or middle) name."),
                    ["birthdate"] = Prop("string", "Date of birth, YYYY-MM-DD."),
                    ["identifier"] = Prop("string", "Patient identifier as system|value or just value, such as an MRN."),
                    ["legal_sex"] = Prop("string", "Legal sex.", "male", "female", "other", "unknown"),
                    ["telecom"] = Prop("string", "Phone number or email address."),
                    ["name"] = Prop("string", "Any part of the name. Ignored when family or given is provided."),
                    ["address_city"] = Prop("string", "City of the home address."),
                    ["address_state"] = Prop("string", "State of the home address."),
                    ["address_postalcode"] = Prop("string", "Postal code of the home address."),
                    ["count"] = countProp
                }),

            Tool("get_patient",
                "Get a patient's demographics by FHIR ID: names, birth date, gender, identifiers such as MRN, addresses, phone numbers, contacts, language, and primary care provider.",
                new JObject { ["patient_id"] = patientIdProp },
                "patient_id"),

            Tool("search_document_references",
                "Search a patient's document references, such as clinical notes. Each result's content[].attachment.url holds a Binary/{id} reference in text/html or text/rtf format; " +
                "pass it to get_binary to read the note. Filter by note type, creation date range, status, or encounter.",
                new JObject
                {
                    ["patient_id"] = patientIdProp,
                    ["category"] = Prop("string", "Document category. Defaults to clinical-note."),
                    ["type"] = Prop("string", "Note type as system|code. LOINC examples: http://loinc.org|11506-3 (progress note), http://loinc.org|18842-5 (discharge summary), http://loinc.org|11488-4 (consultation), http://loinc.org|34117-2 (history and physical)."),
                    ["date_from"] = Prop("string", "Earliest creation date, YYYY-MM-DD or ISO 8601 date-time."),
                    ["date_to"] = Prop("string", "Latest creation date, YYYY-MM-DD or ISO 8601 date-time."),
                    ["docstatus"] = Prop("string", "Note status.", "preliminary", "final", "amended", "entered-in-error"),
                    ["encounter_id"] = Prop("string", "Encounter FHIR ID associated with the note."),
                    ["count"] = countProp
                },
                "patient_id"),

            Tool("get_document_reference",
                "Get a document reference, such as a clinical note's metadata, by FHIR ID: type, author, date, status, encounter, and Binary content references.",
                new JObject { ["document_reference_id"] = Prop("string", "DocumentReference FHIR ID.") },
                "document_reference_id"),

            Tool("get_binary",
                "Read document content, such as the text of a clinical note, by Binary ID. Accepts a bare ID or a Binary/{id} reference from a document reference. " +
                "By default, HTML and RTF content is converted to plain text. Prefer the text/html attachment when a note has both.",
                new JObject
                {
                    ["binary_id"] = Prop("string", "Binary FHIR ID or Binary/{id} reference."),
                    ["format"] = Prop("string", "text (default) converts HTML and RTF to plain text; raw returns decoded content without conversion.", "text", "raw"),
                    ["max_chars"] = Prop("integer", $"Maximum characters of text to return. Defaults to {DEFAULT_MAX_TEXT_CHARS}.")
                },
                "binary_id"),

            Tool("search_encounters",
                "Search a patient's encounters: inpatient stays and checked-in outpatient, emergency, and virtual visits. Future appointments are not returned. Filter by date range, status, class, or type.",
                new JObject
                {
                    ["patient_id"] = patientIdProp,
                    ["date_from"] = Prop("string", "Earliest encounter date, YYYY-MM-DD or ISO 8601 date-time."),
                    ["date_to"] = Prop("string", "Latest encounter date, YYYY-MM-DD or ISO 8601 date-time."),
                    ["status"] = Prop("string", "Encounter status, such as finished or in-progress."),
                    ["class"] = Prop("string", "Encounter class as system|code. Codes are defined by the Epic organization."),
                    ["type"] = Prop("string", "Encounter type, such as contact type or visit type."),
                    ["count"] = countProp
                },
                "patient_id"),

            Tool("get_encounter",
                "Get an encounter by FHIR ID: status, class, type, period, participants, locations, diagnoses, and hospitalization details.",
                new JObject { ["encounter_id"] = Prop("string", "Encounter FHIR ID.") },
                "encounter_id"),

            Tool("search_coverages",
                "Search a patient's insurance coverages: payer, plan, group, subscriber, status, and filing order. Returns every coverage that could apply to the patient, not only those billed for a specific visit.",
                new JObject
                {
                    ["patient_id"] = patientIdProp,
                    ["status"] = Prop("string", "Coverage status, such as active."),
                    ["count"] = countProp
                },
                "patient_id"),

            Tool("get_coverage",
                "Get an insurance coverage by FHIR ID: payer, plan and group, subscriber, beneficiary, relationship, period, and filing order.",
                new JObject { ["coverage_id"] = Prop("string", "Coverage FHIR ID.") },
                "coverage_id"),

            Tool("get_next_page",
                "Get the next page of results from a previous search, using the nextPageUrl value it returned.",
                new JObject { ["next_page_url"] = Prop("string", "The nextPageUrl value from a previous search result.") },
                "next_page_url")
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var args = @params["arguments"] as JObject ?? new JObject();
        var startTime = DateTime.UtcNow;

        try
        {
            JToken output;
            switch (toolName)
            {
                case "search_patients":
                    output = await SearchPatientsAsync(args).ConfigureAwait(false);
                    break;
                case "get_patient":
                    output = await ReadResourceAsync("Patient", RequireId(args, "patient_id", "Patient")).ConfigureAwait(false);
                    break;
                case "search_document_references":
                    output = await SearchDocumentReferencesAsync(args).ConfigureAwait(false);
                    break;
                case "get_document_reference":
                    output = await ReadResourceAsync("DocumentReference", RequireId(args, "document_reference_id", "DocumentReference")).ConfigureAwait(false);
                    break;
                case "get_binary":
                    output = await GetBinaryAsync(args).ConfigureAwait(false);
                    break;
                case "search_encounters":
                    output = await SearchEncountersAsync(args).ConfigureAwait(false);
                    break;
                case "get_encounter":
                    output = await ReadResourceAsync("Encounter", RequireId(args, "encounter_id", "Encounter")).ConfigureAwait(false);
                    break;
                case "search_coverages":
                    output = await SearchCoveragesAsync(args).ConfigureAwait(false);
                    break;
                case "get_coverage":
                    output = await ReadResourceAsync("Coverage", RequireId(args, "coverage_id", "Coverage")).ConfigureAwait(false);
                    break;
                case "get_next_page":
                    output = await GetNextPageAsync(args).ConfigureAwait(false);
                    break;
                default:
                    return CreateJsonRpcError(requestId, -32602, "Unknown tool", toolName ?? "");
            }

            await LogToolAsync(toolName, true, startTime).ConfigureAwait(false);
            return CreateToolResult(requestId, output.ToString(Newtonsoft.Json.Formatting.Indented), false);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FhirRequestException)
        {
            await LogToolAsync(toolName, false, startTime).ConfigureAwait(false);
            return CreateToolResult(requestId, ex.Message, true);
        }
    }

    // ── MCP tool implementations ─────────────────────────────────────────

    private async Task<JToken> SearchPatientsAsync(JObject args)
    {
        var query = new List<KeyValuePair<string, string>>();
        AddArg(query, args, "family", "family");
        AddArg(query, args, "given", "given");
        AddArg(query, args, "birthdate", "birthdate");
        AddArg(query, args, "identifier", "identifier");
        AddArg(query, args, "legal_sex", "legal-sex");
        AddArg(query, args, "telecom", "telecom");
        AddArg(query, args, "name", "name");
        AddArg(query, args, "address_city", "address-city");
        AddArg(query, args, "address_state", "address-state");
        AddArg(query, args, "address_postalcode", "address-postalcode");

        if (query.Count == 0)
            throw new ArgumentException("Provide at least one search criterion, such as identifier, or given + family + birthdate.");

        AddArg(query, args, "count", "_count");
        return await SearchAsync("Patient", query).ConfigureAwait(false);
    }

    private async Task<JToken> SearchDocumentReferencesAsync(JObject args)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("patient", RequireId(args, "patient_id", "Patient"))
        };

        var category = args["category"]?.ToString();
        query.Add(new KeyValuePair<string, string>("category", string.IsNullOrWhiteSpace(category) ? "clinical-note" : category.Trim()));
        AddArg(query, args, "type", "type");
        AddDateRange(query, args);
        AddArg(query, args, "docstatus", "docstatus");

        var encounterId = args["encounter_id"]?.ToString();
        if (!string.IsNullOrWhiteSpace(encounterId))
            query.Add(new KeyValuePair<string, string>("encounter", NormalizeId(encounterId, "Encounter", "encounter_id")));

        AddArg(query, args, "count", "_count");
        return await SearchAsync("DocumentReference", query).ConfigureAwait(false);
    }

    private async Task<JToken> SearchEncountersAsync(JObject args)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("patient", RequireId(args, "patient_id", "Patient"))
        };
        AddDateRange(query, args);
        AddArg(query, args, "status", "status");
        AddArg(query, args, "class", "class");
        AddArg(query, args, "type", "type");
        AddArg(query, args, "count", "_count");
        return await SearchAsync("Encounter", query).ConfigureAwait(false);
    }

    private async Task<JToken> SearchCoveragesAsync(JObject args)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("patient", RequireId(args, "patient_id", "Patient"))
        };
        AddArg(query, args, "status", "status");
        AddArg(query, args, "count", "_count");
        return await SearchAsync("Coverage", query).ConfigureAwait(false);
    }

    private async Task<JToken> GetBinaryAsync(JObject args)
    {
        var binaryId = RequireId(args, "binary_id", "Binary");
        var format = (args["format"]?.ToString() ?? "text").Trim().ToLowerInvariant();
        var maxChars = DEFAULT_MAX_TEXT_CHARS;
        int parsedMax;
        if (args["max_chars"] != null && int.TryParse(args["max_chars"].ToString(), out parsedMax) && parsedMax > 0)
            maxChars = parsedMax;

        var binary = (JObject)await FhirGetAsync(FhirBaseUrl + "/Binary/" + Uri.EscapeDataString(binaryId)).ConfigureAwait(false);
        var contentType = binary["contentType"]?.ToString() ?? "";
        var decoded = DecodeBinaryText(contentType, binary["data"]?.ToString());

        var result = new JObject
        {
            ["id"] = binary["id"],
            ["contentType"] = contentType
        };

        if (decoded == null)
        {
            result["note"] = "The content is not text, so it was not decoded. Use Get binary content in Power Automate to retrieve the Base64 data.";
            return result;
        }

        var text = decoded;
        if (format != "raw")
        {
            var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
            if (mediaType == "text/html" || mediaType == "application/xhtml+xml")
                text = HtmlToText(decoded);
            else if (mediaType == "text/rtf" || mediaType == "application/rtf")
                text = RtfToText(decoded);
        }

        result["truncated"] = text.Length > maxChars;
        result["text"] = text.Length > maxChars ? text.Substring(0, maxChars) : text;
        return result;
    }

    private async Task<JToken> GetNextPageAsync(JObject args)
    {
        var url = args["next_page_url"]?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Required parameter 'next_page_url' is missing.");

        Uri nextUri;
        var baseUri = new Uri(FhirBaseUrl);
        // The bearer token is attached to this request, so it may only go to the configured Epic FHIR server.
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out nextUri)
            || nextUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(nextUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || !nextUri.AbsolutePath.StartsWith(baseUri.AbsolutePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("next_page_url must be a nextPageUrl value returned by a previous search on this Epic FHIR server.");
        }

        var bundle = await FhirGetAsync(nextUri.AbsoluteUri).ConfigureAwait(false);
        return CompactBundle(bundle as JObject);
    }

    private async Task<JToken> ReadResourceAsync(string resourceType, string id)
    {
        var resource = await FhirGetAsync(FhirBaseUrl + "/" + resourceType + "/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
        return StripNarrative(resource as JObject);
    }

    private async Task<JToken> SearchAsync(string resourceType, List<KeyValuePair<string, string>> query)
    {
        var qs = string.Join("&", query.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
        var bundle = await FhirGetAsync(FhirBaseUrl + "/" + resourceType + "?" + qs).ConfigureAwait(false);
        return CompactBundle(bundle as JObject);
    }

    // ── FHIR HTTP helpers ────────────────────────────────────────────────

    // Derived from the connector's host and basePath, so the OpenAPI definition is the only place the FHIR base URL is set.
    private string FhirBaseUrl
    {
        get { return this.Context.Request.RequestUri.GetLeftPart(UriPartial.Path).TrimEnd('/'); }
    }

    private async Task<JToken> FhirGetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(FHIR_JSON));
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var body = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : "";

        if (!response.IsSuccessStatusCode)
            throw new FhirRequestException(DescribeFhirError(response.StatusCode, body));

        if (!LooksLikeJson(body))
            throw new FhirRequestException("Epic returned a non-JSON response. Confirm the connector host and base path point to an Epic FHIR R4 endpoint.");

        return ParseJson(body);
    }

    private static string DescribeFhirError(HttpStatusCode status, string body)
    {
        var code = (int)status;
        string hint;
        switch (code)
        {
            case 401: hint = "The access token is missing, expired, or invalid. Reconnect the Epic FHIR connection."; break;
            case 403: hint = "The signed-in user or the app registration is not authorized for this resource."; break;
            case 404: hint = "The resource was not found. Check the FHIR ID."; break;
            default: hint = "Epic returned an error."; break;
        }

        var details = new List<string>();
        if (LooksLikeJson(body))
        {
            try
            {
                var outcome = ParseJson(body) as JObject;
                foreach (var issue in outcome?["issue"] as JArray ?? new JArray())
                {
                    var text = issue["details"]?["text"]?.ToString() ?? issue["diagnostics"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) details.Add(text);
                }
            }
            catch (JsonException) { }
        }

        return $"HTTP {code}: {hint}" + (details.Count > 0 ? " Details: " + string.Join("; ", details) : "");
    }

    private static JObject CompactBundle(JObject bundle)
    {
        if (bundle == null)
            throw new FhirRequestException("Epic returned an empty response.");

        var results = new JArray();
        var warnings = new JArray();

        foreach (var entry in bundle["entry"] as JArray ?? new JArray())
        {
            var resource = entry["resource"] as JObject;
            if (resource == null) continue;

            if (resource["resourceType"]?.ToString() == "OperationOutcome")
            {
                foreach (var issue in resource["issue"] as JArray ?? new JArray())
                {
                    warnings.Add(new JObject
                    {
                        ["severity"] = issue["severity"],
                        ["message"] = issue["details"]?["text"] ?? issue["diagnostics"]
                    });
                }
                continue;
            }

            results.Add(StripNarrative(resource));
        }

        var next = (bundle["link"] as JArray ?? new JArray())
            .FirstOrDefault(l => l["relation"]?.ToString() == "next")?["url"]?.ToString();

        var compact = new JObject
        {
            ["total"] = bundle["total"] ?? results.Count,
            ["returned"] = results.Count
        };
        if (!string.IsNullOrEmpty(next)) compact["nextPageUrl"] = next;
        if (warnings.Count > 0) compact["warnings"] = warnings;
        compact["results"] = results;
        return compact;
    }

    // The generated XHTML narrative duplicates the structured data and is expensive for an agent to read.
    private static JObject StripNarrative(JObject resource)
    {
        if (resource == null) return new JObject();
        resource.Remove("text");
        return resource;
    }

    // ── Content conversion ───────────────────────────────────────────────

    private static string DecodeBinaryText(string contentType, string base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;

        var mediaType = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        var isText = mediaType.StartsWith("text/")
            || mediaType == "application/rtf"
            || mediaType == "application/json"
            || mediaType == "application/fhir+json"
            || mediaType == "application/xml"
            || mediaType == "application/fhir+xml"
            || mediaType == "application/xhtml+xml"
            || mediaType.EndsWith("+xml");
        if (!isText) return null;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(p|div|tr|li|h[1-6]|table|ul|ol|blockquote|pre)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        return NormalizeWhitespace(text);
    }

    private static readonly HashSet<string> RtfIgnoredDestinations = new HashSet<string>
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "themedata", "colorschememapping",
        "latentstyles", "datastore", "xmlnstbl", "listtable", "listoverridetable", "rsidtbl", "generator",
        "filetbl", "revtbl", "header", "headerl", "headerr", "headerf", "footer", "footerl", "footerr",
        "footerf", "fldinst", "pgdsctbl", "mmathPr", "wgrffmtfilter", "nonshppict"
    };

    private static string RtfToText(string rtf)
    {
        var sb = new StringBuilder();
        var stack = new Stack<KeyValuePair<bool, int>>();
        var ignorable = false;
        var ucSkip = 1;
        var pendingSkip = 0;
        var i = 0;

        Action<string> emit = s =>
        {
            if (ignorable) return;
            foreach (var ch in s)
            {
                if (pendingSkip > 0) { pendingSkip--; continue; }
                sb.Append(ch);
            }
        };

        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '{')
            {
                stack.Push(new KeyValuePair<bool, int>(ignorable, ucSkip));
                i++;
            }
            else if (c == '}')
            {
                if (stack.Count > 0)
                {
                    var state = stack.Pop();
                    ignorable = state.Key;
                    ucSkip = state.Value;
                }
                i++;
            }
            else if (c == '\\')
            {
                i++;
                if (i >= rtf.Length) break;
                var d = rtf[i];

                if (d == '\\' || d == '{' || d == '}')
                {
                    emit(d.ToString());
                    i++;
                }
                else if (d == '*')
                {
                    ignorable = true;
                    i++;
                }
                else if (d == '\'')
                {
                    if (i + 2 < rtf.Length)
                    {
                        int code;
                        if (int.TryParse(rtf.Substring(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out code))
                            emit(((char)code).ToString());
                    }
                    i += 3;
                }
                else if (d == '~') { emit(" "); i++; }
                else if (d == '_') { emit("-"); i++; }
                else if (d == '\r' || d == '\n') { emit("\n"); i++; }
                else if (char.IsLetter(d))
                {
                    var start = i;
                    while (i < rtf.Length && char.IsLetter(rtf[i])) i++;
                    var word = rtf.Substring(start, i - start);

                    var numStart = i;
                    if (i < rtf.Length && rtf[i] == '-') i++;
                    while (i < rtf.Length && char.IsDigit(rtf[i])) i++;
                    int? number = null;
                    int parsed;
                    if (i > numStart && int.TryParse(rtf.Substring(numStart, i - numStart), out parsed)) number = parsed;

                    if (i < rtf.Length && rtf[i] == ' ') i++;

                    if (RtfIgnoredDestinations.Contains(word)) { ignorable = true; continue; }

                    switch (word)
                    {
                        case "par":
                        case "line":
                        case "sect":
                        case "page":
                        case "row":
                            emit("\n"); break;
                        case "tab":
                        case "cell":
                            emit("\t"); break;
                        case "emdash": emit("\u2014"); break;
                        case "endash": emit("\u2013"); break;
                        case "bullet": emit("\u2022"); break;
                        case "lquote": emit("\u2018"); break;
                        case "rquote": emit("\u2019"); break;
                        case "ldblquote": emit("\u201C"); break;
                        case "rdblquote": emit("\u201D"); break;
                        case "uc":
                            ucSkip = number ?? 1; break;
                        case "u":
                            if (number.HasValue)
                            {
                                var value = number.Value < 0 ? number.Value + 65536 : number.Value;
                                emit(((char)value).ToString());
                                pendingSkip = ignorable ? 0 : ucSkip;
                            }
                            break;
                    }
                }
                else
                {
                    i++;
                }
            }
            else if (c == '\r' || c == '\n')
            {
                i++;
            }
            else
            {
                emit(c.ToString());
                i++;
            }
        }

        // Epic RTF notes can carry literal <BR> tags inside the text runs.
        var text = Regex.Replace(sb.ToString(), @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        return NormalizeWhitespace(text);
    }

    private static string NormalizeWhitespace(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    // ── Argument helpers ─────────────────────────────────────────────────

    private static void AddArg(List<KeyValuePair<string, string>> query, JObject args, string argName, string fhirName)
    {
        var value = args[argName]?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
            query.Add(new KeyValuePair<string, string>(fhirName, value.Trim()));
    }

    private static void AddDateRange(List<KeyValuePair<string, string>> query, JObject args)
    {
        var from = args["date_from"]?.ToString();
        var to = args["date_to"]?.ToString();
        if (!string.IsNullOrWhiteSpace(from)) query.Add(new KeyValuePair<string, string>("date", "ge" + from.Trim()));
        if (!string.IsNullOrWhiteSpace(to)) query.Add(new KeyValuePair<string, string>("date", "le" + to.Trim()));
    }

    private static string RequireId(JObject args, string argName, string resourceType)
    {
        var value = args[argName]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required parameter '{argName}' is missing.");
        return NormalizeId(value, resourceType, argName);
    }

    // Accepts a bare ID, a relative reference (Binary/{id}), or an absolute URL ending in {resourceType}/{id}.
    private static string NormalizeId(string value, string resourceType, string argName)
    {
        var id = value.Trim();
        var marker = resourceType + "/";
        var idx = id.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) id = id.Substring(idx + marker.Length);
        id = id.Split('?', '#')[0].TrimEnd('/');

        if (!FhirIdPattern.IsMatch(id))
            throw new ArgumentException($"'{argName}' is not a valid {resourceType} FHIR ID.");
        return id;
    }

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required.Length > 0) schema["required"] = new JArray(required);

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema,
            ["annotations"] = new JObject
            {
                ["readOnlyHint"] = true,
                ["destructiveHint"] = false,
                ["idempotentHint"] = true,
                ["openWorldHint"] = false
            }
        };
    }

    private static JObject Prop(string type, string description, params string[] enumValues)
    {
        var prop = new JObject
        {
            ["type"] = type,
            ["description"] = description
        };
        if (enumValues.Length > 0) prop["enum"] = new JArray(enumValues);
        return prop;
    }

    // Default parsing converts ISO date strings to DateTime, which rewrites FHIR timestamps and date-time arguments.
    private static JToken ParseJson(string json)
    {
        using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None })
        {
            return JToken.Load(reader);
        }
    }

    private static bool LooksLikeJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var first = body.TrimStart()[0];
        // '\u007B' is the opening curly brace, escaped because ppcv counts braces inside literals.
        return first == '\u007B' || first == '[';
    }

    // ── Response helpers ─────────────────────────────────────────────────

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode status, JObject body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateToolResult(JToken requestId, string text, bool isError)
    {
        return CreateJsonRpcSuccess(requestId, new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        });
    }

    private static HttpResponseMessage CreateJsonRpcSuccess(JToken id, JObject result)
    {
        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        });
    }

    private static HttpResponseMessage CreateJsonRpcError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        });
    }

    // ── Application Insights ─────────────────────────────────────────────

    private Task LogToolAsync(string toolName, bool success, DateTime startTime)
    {
        return LogToAppInsightsAsync("McpToolCall", new Dictionary<string, string>
        {
            ["Tool"] = toolName ?? "",
            ["Success"] = success.ToString(),
            ["DurationMs"] = ((int)(DateTime.UtcNow - startTime).TotalMilliseconds).ToString()
        });
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
            var props = properties ?? new Dictionary<string, string>();
            props["Connector"] = "Epic FHIR";
            props["CorrelationId"] = this.Context.CorrelationId ?? "";

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
                        properties = props
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(JsonConvert.SerializeObject(telemetryData), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Telemetry failures must never affect the operation.
        }
    }

    private class FhirRequestException : Exception
    {
        public FhirRequestException(string message) : base(message) { }
    }
}
