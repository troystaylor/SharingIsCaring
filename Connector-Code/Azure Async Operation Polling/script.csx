// ─────────────────────────────────────────────────────────────────────────────
//  Async Operation Polling — reusable template
//
//  Custom connector scripts are capped at two minutes. Long-running operations
//  routinely exceed that, so this helper never waits for completion:
//
//    Start -> fires the request and, the moment it detects a long-running
//             operation, returns an opaque handle. It does not poll even once.
//    Poll  -> takes the handle and performs EXACTLY ONE status check.
//
//  The retry loop belongs in the calling flow (Do-Until on isComplete with a
//  Delay of retryAfterSeconds), not in the script. The flow owns the waiting,
//  so the script always returns in seconds.
//
//  TO ADAPT THIS TEMPLATE:
//    1. Set START_OPERATIONS and POLL_OPERATION to your operationIds.
//    2. Review NormalizeStatus for your service's status vocabulary.
//    3. Review NormalizeAzureError for your service's error shape.
//  See readme.md for per-service differences.
// ─────────────────────────────────────────────────────────────────────────────

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
    // ─── Configure for your connector ─────────────────────────────────────────────

    /// <summary>Operations that begin a long-running operation.</summary>
    private static readonly HashSet<string> START_OPERATIONS = new HashSet<string>(StringComparer.Ordinal)
    {
        "StartLongRunningOperation"
    };

    /// <summary>The operation that performs a single status check.</summary>
    private const string POLL_OPERATION = "GetOperationStatus";

    /// <summary>Query parameter carrying the handle on the poll operation.</summary>
    private const string HANDLE_PARAMETER = "operationHandle";

    private const int DEFAULT_RETRY_AFTER_SECONDS = 10;
    private const int MAX_RETRY_AFTER_SECONDS = 300;
    private const int HANDLE_VERSION = 1;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = ResolveOperationId();

        try
        {
            if (START_OPERATIONS.Contains(operationId))
            {
                return await HandleStartAsync().ConfigureAwait(false);
            }

            if (string.Equals(operationId, POLL_OPERATION, StringComparison.Ordinal))
            {
                return await HandlePollAsync().ConfigureAwait(false);
            }

            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Json(Envelope(
                status: "Failed",
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: new JObject
                {
                    ["code"] = "ConnectorScriptError",
                    ["message"] = ex.Message,
                    ["httpStatus"] = 500
                }));
        }
    }

    // ─── Start ────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleStartAsync()
    {
        var forwarded = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        var rawBody = forwarded.Content == null
            ? string.Empty
            : await forwarded.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!forwarded.IsSuccessStatusCode)
        {
            return Json(Envelope(
                status: "Failed",
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: NormalizeError(rawBody, (int)forwarded.StatusCode)));
        }

        // An async header is authoritative even on 200/201. Some services return
        // 201 Created with Azure-AsyncOperation while the resource is still
        // provisioning, so resolve the poll target BEFORE deciding this finished.
        var poll = ResolvePollTarget(forwarded);

        if (poll != null)
        {
            return Json(Envelope(
                status: "Running",
                isComplete: false,
                handle: EncodeHandle(poll),
                retryAfterSeconds: ReadRetryAfter(forwarded),
                result: null,
                error: null));
        }

        if (forwarded.StatusCode == HttpStatusCode.Accepted)
        {
            // 202 with no pollable header. Surfacing this is better than silently
            // reporting success for an operation whose outcome is unknown.
            return Json(Envelope(
                status: "Failed",
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: new JObject
                {
                    ["code"] = "NoPollingLocation",
                    ["message"] = "Service returned 202 Accepted without an Azure-AsyncOperation, Operation-Location, or Location header, so the operation cannot be tracked.",
                    ["httpStatus"] = 202
                }));
        }

        // No async header at all: the service genuinely completed inline.
        return Json(Envelope(
            status: "Succeeded",
            isComplete: true,
            handle: null,
            retryAfterSeconds: 0,
            result: ParseLoose(rawBody),
            error: null));
    }

    // ─── Poll ─────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandlePollAsync()
    {
        JObject handle;
        try
        {
            handle = DecodeHandle(ReadHandleFromRequest());
        }
        catch (Exception ex)
        {
            return Json(Envelope(
                status: "Failed",
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: new JObject
                {
                    ["code"] = "InvalidOperationHandle",
                    ["message"] = ex.Message,
                    ["httpStatus"] = 400
                }));
        }

        var pollUrl = handle.Value<string>("u");
        var kind = handle.Value<string>("k");

        using (var request = new HttpRequestMessage(HttpMethod.Get, pollUrl))
        {
            if (this.Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            var response = await this.Context.SendAsync(request, this.CancellationToken)
                .ConfigureAwait(false);

            var rawBody = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var retryAfter = ReadRetryAfter(response);

            if (!response.IsSuccessStatusCode)
            {
                return Json(Envelope(
                    status: "Failed",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: null,
                    error: NormalizeError(rawBody, (int)response.StatusCode)));
            }

            // Location style: 202 means still running; a 200/201 body IS the result.
            if (string.Equals(kind, "location", StringComparison.OrdinalIgnoreCase))
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    var next = ResolvePollTarget(response) ?? handle;
                    return Json(Envelope(
                        status: "Running",
                        isComplete: false,
                        handle: EncodeHandle(next),
                        retryAfterSeconds: retryAfter,
                        result: null,
                        error: null));
                }

                return Json(Envelope(
                    status: "Succeeded",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: ParseLoose(rawBody),
                    error: null));
            }

            // Status-document style: a 200 carries a status field to inspect.
            var doc = ParseLoose(rawBody) as JObject ?? new JObject();
            var reported = doc.Value<string>("status");
            if (string.IsNullOrWhiteSpace(reported))
            {
                var provisioning = doc.SelectToken("properties.provisioningState");
                if (provisioning != null) reported = provisioning.ToString();
            }

            var normalized = NormalizeStatus(reported);

            if (string.Equals(normalized, "Running", StringComparison.Ordinal))
            {
                return Json(Envelope(
                    status: "Running",
                    isComplete: false,
                    handle: EncodeHandle(handle),
                    retryAfterSeconds: retryAfter,
                    result: null,
                    error: null));
            }

            if (string.Equals(normalized, "Succeeded", StringComparison.Ordinal))
            {
                return Json(Envelope(
                    status: "Succeeded",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: doc,
                    error: null));
            }

            return Json(Envelope(
                status: normalized,
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: NormalizeError(rawBody, (int)response.StatusCode)));
        }
    }

    // ─── Poll target resolution ───────────────────────────────────────────────────
    // Azure-AsyncOperation wins when several headers are present. Operation-Location
    // is the Cognitive Services / Foundry spelling. Both are unambiguous long-running
    // operation signals at any status code.
    //
    // Location is different: it only means "poll me" on a 202. On a 201 it usually
    // points at the resource just created, so treating it as a poll target would poll
    // the resource and mistake its body for an operation status. The distinction is
    // recorded in the handle so it is not re-sniffed on every poll.

    private JObject ResolvePollTarget(HttpResponseMessage response)
    {
        string url;

        if (TryHeader(response, "Azure-AsyncOperation", out url))
            return new JObject { ["u"] = url, ["k"] = "azureAsync", ["v"] = HANDLE_VERSION };

        if (TryHeader(response, "Operation-Location", out url))
            return new JObject { ["u"] = url, ["k"] = "azureAsync", ["v"] = HANDLE_VERSION };

        if (response.StatusCode != HttpStatusCode.Accepted)
            return null;

        if (response.Headers.Location != null)
            return new JObject { ["u"] = response.Headers.Location.ToString(), ["k"] = "location", ["v"] = HANDLE_VERSION };

        if (TryHeader(response, "Location", out url))
            return new JObject { ["u"] = url, ["k"] = "location", ["v"] = HANDLE_VERSION };

        return null;
    }

    private static bool TryHeader(HttpResponseMessage response, string name, out string value)
    {
        value = null;
        IEnumerable<string> values;

        if (response.Headers.TryGetValues(name, out values))
        {
            value = values.FirstOrDefault();
        }
        else if (response.Content != null && response.Content.Headers.TryGetValues(name, out values))
        {
            value = values.FirstOrDefault();
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    private static int ReadRetryAfter(HttpResponseMessage response)
    {
        var seconds = DEFAULT_RETRY_AFTER_SECONDS;

        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                seconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
            }
            else if (response.Headers.RetryAfter.Date.HasValue)
            {
                seconds = (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }
        }

        if (seconds < 1) seconds = 1;
        if (seconds > MAX_RETRY_AFTER_SECONDS) seconds = MAX_RETRY_AFTER_SECONDS;
        return seconds;
    }

    // ─── Status vocabulary ────────────────────────────────────────────────────────
    // Services spell these differently. ARM uses InProgress, Cognitive Services uses
    // notStarted/running, Fabric adds Undetermined. Collapse them so the calling flow
    // only ever branches on four values. Extend the Running case for your service.

    private static string NormalizeStatus(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Running";

        switch (raw.Trim().ToLowerInvariant())
        {
            case "succeeded":
            case "success":
            case "completed":
                return "Succeeded";

            case "failed":
            case "error":
                return "Failed";

            case "canceled":
            case "cancelled":
                return "Canceled";

            case "notstarted":
            case "not started":
            case "accepted":
            case "inprogress":
            case "in progress":
            case "running":
            case "undetermined":
            case "resuming":
            case "updating":
                return "Running";

            default:
                // Unknown vocabulary is treated as still running rather than as
                // success, so an unrecognised state can never be mistaken for done.
                return "Running";
        }
    }

    // ─── Error normalization ──────────────────────────────────────────────────────
    // ARM:                { "error": { "code", "message", "details" } }
    // Fabric:             { "errorCode", "message", "moreDetails": [] }
    // Cognitive Services: { "error": { "code", "innererror": {...} } }

    private static JObject NormalizeError(string rawBody, int httpStatus)
    {
        var normalized = new JObject
        {
            ["code"] = "Unknown",
            ["message"] = string.IsNullOrWhiteSpace(rawBody) ? "No error body returned." : rawBody
        };

        // Only report an HTTP status when the transport itself failed. A long-running
        // operation that fails while reporting HTTP 200 would otherwise surface
        // "httpStatus": 200 on an error object, which reads as a contradiction.
        if (httpStatus >= 400)
        {
            normalized["httpStatus"] = httpStatus;
        }

        var parsed = ParseLoose(rawBody) as JObject;
        if (parsed == null) return normalized;

        var nested = parsed["error"] as JObject;
        if (nested != null)
        {
            normalized["code"] = nested.Value<string>("code") ?? "Unknown";
            normalized["message"] = nested.Value<string>("message") ?? normalized.Value<string>("message");
            if (nested["details"] != null) normalized["details"] = nested["details"];
            return normalized;
        }

        if (parsed["errorCode"] != null)
        {
            normalized["code"] = parsed.Value<string>("errorCode");
            normalized["message"] = parsed.Value<string>("message") ?? normalized.Value<string>("message");
            if (parsed["moreDetails"] != null) normalized["details"] = parsed["moreDetails"];
            return normalized;
        }

        if (parsed["code"] != null)
        {
            normalized["code"] = parsed.Value<string>("code");
            normalized["message"] = parsed.Value<string>("message") ?? normalized.Value<string>("message");
        }

        return normalized;
    }

    // ─── Handle encoding ──────────────────────────────────────────────────────────
    // The handle round-trips through a flow variable, so it must be a single opaque
    // string with no characters that need escaping. base64url of a compact JSON
    // document satisfies that and stays debuggable.

    private static string EncodeHandle(JObject handle)
    {
        var json = handle.ToString(Newtonsoft.Json.Formatting.None);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static JObject DecodeHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new ArgumentException("An operation handle is required.");

        var padded = handle.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        JObject parsed;
        try
        {
            parsed = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
        }
        catch
        {
            throw new ArgumentException("The operation handle is not a valid handle produced by a start operation.");
        }

        var url = parsed.Value<string>("u");
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("The operation handle does not contain a polling URL.");

        // The handle is user-editable in a flow and the poll carries a bearer token,
        // so a tampered handle must not be able to redirect credentials elsewhere.
        Uri probe;
        if (!Uri.TryCreate(url, UriKind.Absolute, out probe) || probe.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The operation handle polling URL must be absolute HTTPS.");

        return parsed;
    }

    private string ReadHandleFromRequest()
    {
        var query = this.Context.Request.RequestUri == null
            ? string.Empty
            : this.Context.Request.RequestUri.Query;

        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && string.Equals(parts[0], HANDLE_PARAMETER, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    // ─── Response shaping ─────────────────────────────────────────────────────────

    private static JObject Envelope(
        string status,
        bool isComplete,
        string handle,
        int retryAfterSeconds,
        JToken result,
        JObject error)
    {
        var envelope = new JObject
        {
            ["status"] = status,
            ["isComplete"] = isComplete,
            ["retryAfterSeconds"] = isComplete
                ? 0
                : (retryAfterSeconds > 0 ? retryAfterSeconds : DEFAULT_RETRY_AFTER_SECONDS),
            ["operationHandle"] = handle
        };

        if (result != null) envelope["result"] = result;
        if (error != null) envelope["error"] = error;

        return envelope;
    }

    private static HttpResponseMessage Json(JObject body)
    {
        // Always HTTP 200. Operation failure is reported in the envelope so the
        // calling flow can branch on status rather than handling a raised error.
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(body.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private static JToken ParseLoose(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JObject();
        try { return JToken.Parse(raw); }
        catch { return new JValue(raw); }
    }

    // Documented quirk: OperationId arrives base64-encoded in some regions.
    private string ResolveOperationId()
    {
        var id = this.Context.OperationId;
        if (string.IsNullOrEmpty(id)) return id;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(id));
            // Only trust the decode if it produced clean printable ASCII; otherwise
            // the original value was never base64 to begin with.
            if (decoded.Length > 0 && decoded.All(c => c >= 32 && c < 127))
            {
                return decoded;
            }
        }
        catch (FormatException) { }

        return id;
    }
}
