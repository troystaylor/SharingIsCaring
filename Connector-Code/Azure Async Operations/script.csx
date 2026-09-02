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
    // ─── Application Insights (hardcoded per repo standard) ───────────────────────
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string CONNECTOR_NAME = "AzureAsyncOperations";

    // ─── Tunables ─────────────────────────────────────────────────────────────────
    private const int DEFAULT_RETRY_AFTER_SECONDS = 10;
    private const int MAX_RETRY_AFTER_SECONDS = 300;
    private const int HANDLE_VERSION = 1;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = ResolveOperationId();

        try
        {
            switch (operationId)
            {
                case "CreateOrUpdateResource":
                case "DeleteResource":
                case "InvokeResourceAction":
                case "CreateOrUpdateChildResource":
                case "DeleteChildResource":
                    return await HandleStartAsync(operationId).ConfigureAwait(false);

                case "GetOperationStatus":
                    return await HandlePollAsync().ConfigureAwait(false);

                default:
                    return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                        .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("AsyncOperation_Error", new Dictionary<string, string>
            {
                ["operationId"] = operationId ?? "",
                ["exception"] = ex.GetType().Name,
                ["message"] = ex.Message
            });

            return Json(HttpStatusCode.OK, Envelope(
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

    // ══════════════════════════════════════════════════════════════════════════════
    //  ASYNC POLLING
    //
    //  The connector script runtime caps execution at 2 minutes. Azure long-running
    //  operations routinely exceed that, so this helper never waits for completion.
    //
    //    Start -> fires the request and, the moment it detects an async operation,
    //             returns an opaque handle. It does not poll even once.
    //    Poll  -> takes the handle and performs EXACTLY ONE status check.
    //
    //  The retry loop belongs in the calling flow (Do-Until on isComplete with a
    //  Delay of retryAfterSeconds), not in the script. That is the whole point:
    //  the flow owns the waiting, so the script always returns in seconds.
    // ══════════════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleStartAsync(string operationId)
    {
        var forwarded = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        var rawBody = forwarded.Content == null
            ? string.Empty
            : await forwarded.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!forwarded.IsSuccessStatusCode)
        {
            LogToAppInsights("AsyncOperation_StartFailed", new Dictionary<string, string>
            {
                ["operationId"] = operationId,
                ["httpStatus"] = ((int)forwarded.StatusCode).ToString()
            });

            return Json(HttpStatusCode.OK, Envelope(
                status: "Failed",
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: NormalizeAzureError(rawBody, (int)forwarded.StatusCode)));
        }

        // An async header is authoritative even on 200/201: ARM may return
        // 201 Created with Azure-AsyncOperation while the resource is still
        // provisioning. Resolve the poll target BEFORE deciding this finished.
        var poll = ResolvePollTarget(forwarded);

        if (poll != null)
        {
            LogToAppInsights("AsyncOperation_Started", new Dictionary<string, string>
            {
                ["operationId"] = operationId,
                ["pollKind"] = poll.Value<string>("k"),
                ["httpStatus"] = ((int)forwarded.StatusCode).ToString()
            });

            return Json(HttpStatusCode.OK, Envelope(
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
            return Json(HttpStatusCode.OK, Envelope(
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
        return Json(HttpStatusCode.OK, Envelope(
            status: "Succeeded",
            isComplete: true,
            handle: null,
            retryAfterSeconds: 0,
            result: ParseLoose(rawBody),
            error: null));
    }

    private async Task<HttpResponseMessage> HandlePollAsync()
    {
        JObject handle;
        try
        {
            handle = DecodeHandle(ReadHandleFromRequest());
        }
        catch (Exception ex)
        {
            return Json(HttpStatusCode.OK, Envelope(
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
                return Json(HttpStatusCode.OK, Envelope(
                    status: "Failed",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: null,
                    error: NormalizeAzureError(rawBody, (int)response.StatusCode)));
            }

            // Location-style: 202 means still running; a 200/201 body IS the result.
            if (string.Equals(kind, "location", StringComparison.OrdinalIgnoreCase))
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    var next = ResolvePollTarget(response) ?? handle;
                    return Json(HttpStatusCode.OK, Envelope(
                        status: "Running",
                        isComplete: false,
                        handle: EncodeHandle(next),
                        retryAfterSeconds: retryAfter,
                        result: null,
                        error: null));
                }

                return Json(HttpStatusCode.OK, Envelope(
                    status: "Succeeded",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: ParseLoose(rawBody),
                    error: null));
            }

            // Status-document style (Azure-AsyncOperation / Operation-Location):
            // a 200 carries a status field that must be inspected.
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
                return Json(HttpStatusCode.OK, Envelope(
                    status: "Running",
                    isComplete: false,
                    handle: EncodeHandle(handle),
                    retryAfterSeconds: retryAfter,
                    result: null,
                    error: null));
            }

            if (string.Equals(normalized, "Succeeded", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Envelope(
                    status: "Succeeded",
                    isComplete: true,
                    handle: null,
                    retryAfterSeconds: 0,
                    result: doc,
                    error: null));
            }

            return Json(HttpStatusCode.OK, Envelope(
                status: normalized,
                isComplete: true,
                handle: null,
                retryAfterSeconds: 0,
                result: null,
                error: NormalizeAzureError(rawBody, (int)response.StatusCode)));
        }
    }

    // ─── Poll target resolution ───────────────────────────────────────────────────
    // ARM guidance: when several async headers are present, Azure-AsyncOperation
    // wins. Operation-Location is the Cognitive Services / Foundry spelling. Both
    // are unambiguous LRO signals at any status code.
    //
    // Location is different. It only means "poll me" on a 202. On a 201 it usually
    // points at the resource that was just created, so treating it as a poll target
    // would poll the resource itself and mistake its body for an operation status.
    // The distinction is recorded in the handle so it is not re-sniffed each poll.

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
    // Every Azure service spells these differently. ARM uses InProgress, Cognitive
    // Services uses notStarted/running, Fabric adds Undetermined. Collapse them all
    // so the calling flow only ever branches on four values.

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
                // Unknown vocabulary is treated as still-running rather than as
                // success, so an unrecognised state can never be mistaken for done.
                return "Running";
        }
    }

    // ─── Error normalization ──────────────────────────────────────────────────────
    // ARM:                { "error": { "code", "message", "details" } }
    // Fabric:             { "errorCode", "message", "moreDetails": [] }
    // Cognitive Services: { "error": { "code", "innererror": {...} } }

    private static JObject NormalizeAzureError(string rawBody, int httpStatus)
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

        var armError = parsed["error"] as JObject;
        if (armError != null)
        {
            normalized["code"] = armError.Value<string>("code") ?? "Unknown";
            normalized["message"] = armError.Value<string>("message") ?? normalized.Value<string>("message");
            if (armError["details"] != null) normalized["details"] = armError["details"];
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
    // The handle round-trips through a Power Automate variable, so it must be a
    // single opaque string with no characters that need escaping. base64url of a
    // compact JSON document satisfies that and stays debuggable.

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
            throw new ArgumentException("operationHandle is required.");

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
            throw new ArgumentException("operationHandle is not a valid handle produced by a start operation.");
        }

        var url = parsed.Value<string>("u");
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("operationHandle does not contain a polling URL.");

        // The handle is user-editable in a flow and the poll carries a bearer token,
        // so a tampered handle must not be able to redirect credentials elsewhere.
        Uri probe;
        if (!Uri.TryCreate(url, UriKind.Absolute, out probe) || probe.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("operationHandle polling URL must be absolute HTTPS.");

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
            if (parts.Length == 2 && string.Equals(parts[0], "operationHandle", StringComparison.OrdinalIgnoreCase))
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

    private static HttpResponseMessage Json(HttpStatusCode code, JObject body)
    {
        return new HttpResponseMessage(code)
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

    // ─── Telemetry ────────────────────────────────────────────────────────────────

    private void LogToAppInsights(string eventName, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var props = properties ?? new Dictionary<string, string>();
            props["connector"] = CONNECTOR_NAME;
            props["correlationId"] = this.Context.CorrelationId ?? "";

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
                        ["properties"] = JObject.FromObject(props)
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            request.Content = new StringContent(
                telemetry.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");

            // Fire and forget - telemetry must never delay or fail the operation.
            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
