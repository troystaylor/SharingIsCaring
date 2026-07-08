using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        switch (operationId)
        {
            case "HashToolDescription":
                return await HandleHashToolDescription();
            case "CheckDescriptionDrift":
                return await HandleCheckDescriptionDrift();
            case "ScanOutboundPayload":
                return await HandleScanOutboundPayload();
            case "DetectImperativeLanguage":
                return await HandleDetectImperativeLanguage();
            case "InspectWithPromptShields":
                return await HandleInspectWithPromptShields();
            case "LogShieldEvent":
                return await HandleLogShieldEvent();
            default:
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new { error = $"Unknown operation: {operationId}" }), Encoding.UTF8, "application/json")
                };
        }
    }

    private async Task<HttpResponseMessage> HandleHashToolDescription()
    {
        var body = await ReadBodyAsync();
        var description = body.Value<string>("description") ?? string.Empty;
        var toolName = body.Value<string>("toolName") ?? string.Empty;
        var serverName = body.Value<string>("serverName") ?? string.Empty;

        var hash = ComputeSha256(description);

        var result = new JObject
        {
            ["hash"] = hash,
            ["toolName"] = toolName,
            ["serverName"] = serverName,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        await LogTelemetryAsync("HashToolDescription", new Dictionary<string, string>
        {
            { "toolName", toolName },
            { "serverName", serverName }
        });

        return CreateJsonResponse(result);
    }

    private async Task<HttpResponseMessage> HandleCheckDescriptionDrift()
    {
        var body = await ReadBodyAsync();
        var description = body.Value<string>("description") ?? string.Empty;
        var expectedHash = body.Value<string>("expectedHash") ?? string.Empty;
        var toolName = body.Value<string>("toolName") ?? string.Empty;
        var serverName = body.Value<string>("serverName") ?? string.Empty;

        var currentHash = ComputeSha256(description);
        var drifted = !string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase);

        var result = new JObject
        {
            ["drifted"] = drifted,
            ["currentHash"] = currentHash,
            ["expectedHash"] = expectedHash,
            ["toolName"] = toolName,
            ["serverName"] = serverName,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        if (drifted)
        {
            await LogTelemetryAsync("DriftDetected", new Dictionary<string, string>
            {
                { "toolName", toolName },
                { "serverName", serverName },
                { "expectedHash", expectedHash },
                { "currentHash", currentHash }
            });
        }

        return CreateJsonResponse(result);
    }

    private async Task<HttpResponseMessage> HandleScanOutboundPayload()
    {
        var body = await ReadBodyAsync();
        var payload = body.Value<string>("payload") ?? string.Empty;
        var toolName = body.Value<string>("toolName") ?? string.Empty;
        var serverName = body.Value<string>("serverName") ?? string.Empty;

        var findings = new JArray();
        var maxSeverity = "none";

        // SSN pattern
        var ssnMatches = Regex.Matches(payload, @"\b\d{3}-\d{2}-\d{4}\b");
        if (ssnMatches.Count > 0)
        {
            findings.Add(CreateFinding("ssn_pattern", "critical", ssnMatches.Count));
            maxSeverity = "critical";
        }

        // Credit card pattern (basic Luhn-length)
        var ccMatches = Regex.Matches(payload, @"\b\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}\b");
        if (ccMatches.Count > 0)
        {
            findings.Add(CreateFinding("credit_card_pattern", "critical", ccMatches.Count));
            maxSeverity = "critical";
        }

        // IBAN pattern
        var ibanMatches = Regex.Matches(payload, @"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}([A-Z0-9]?){0,16}\b");
        if (ibanMatches.Count > 0)
        {
            findings.Add(CreateFinding("iban_pattern", "high", ibanMatches.Count));
            if (maxSeverity != "critical") maxSeverity = "high";
        }

        // Bulk query indicators
        var bulkPatterns = new[]
        {
            @"(?i)\bSELECT\s+\*\b",
            @"(?i)\bTOP\s+\d{2,}\b",
            @"(?i)\bLIMIT\s+\d{2,}\b",
            @"(?i)\b(last|past|recent)\s+\d{2,}\s+(invoices?|records?|transactions?|payments?|entries)\b",
            @"(?i)\ball\s+(unpaid|pending|outstanding)\s+(invoices?|payments?|bills?)\b"
        };
        var bulkCount = 0;
        foreach (var pattern in bulkPatterns)
        {
            bulkCount += Regex.Matches(payload, pattern).Count;
        }
        if (bulkCount > 0)
        {
            findings.Add(CreateFinding("bulk_data_request", "high", bulkCount));
            if (maxSeverity != "critical") maxSeverity = "high";
        }

        // Base64 encoded blocks (potential encoded exfiltration)
        var b64Matches = Regex.Matches(payload, @"[A-Za-z0-9+/]{64,}={0,2}");
        if (b64Matches.Count > 0)
        {
            findings.Add(CreateFinding("base64_encoded_block", "medium", b64Matches.Count));
            if (maxSeverity == "none" || maxSeverity == "low") maxSeverity = "medium";
        }

        // Multiple email addresses (potential contact list exfil)
        var emailMatches = Regex.Matches(payload, @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b");
        if (emailMatches.Count >= 3)
        {
            findings.Add(CreateFinding("multiple_email_addresses", "medium", emailMatches.Count));
            if (maxSeverity == "none" || maxSeverity == "low") maxSeverity = "medium";
        }

        var blocked = maxSeverity == "critical" || maxSeverity == "high";

        var result = new JObject
        {
            ["blocked"] = blocked,
            ["riskLevel"] = maxSeverity,
            ["findings"] = findings,
            ["toolName"] = toolName,
            ["serverName"] = serverName
        };

        if (blocked)
        {
            await LogTelemetryAsync("PayloadBlocked", new Dictionary<string, string>
            {
                { "toolName", toolName },
                { "serverName", serverName },
                { "riskLevel", maxSeverity },
                { "findingCount", findings.Count.ToString() }
            });
        }

        return CreateJsonResponse(result);
    }

    private async Task<HttpResponseMessage> HandleDetectImperativeLanguage()
    {
        var body = await ReadBodyAsync();
        var description = body.Value<string>("description") ?? string.Empty;
        var toolName = body.Value<string>("toolName") ?? string.Empty;

        var findings = new JArray();
        var riskScore = 0;

        // Exfiltration verbs — commands to move data outbound
        var exfilPatterns = new[]
        {
            @"(?i)\b(retrieve|collect|gather|fetch|extract)\s+(the\s+)?(last|all|every|recent)\s+\d*\s*\w+",
            @"(?i)\b(send|forward|attach|transmit|post|submit)\s+(this|the|that|it|them|these)\s+",
            @"(?i)\b(exfiltrate|leak|siphon|dump)\b",
            @"(?i)\binclude\s+(as|in)\s+(an?\s+)?(additional|extra|hidden)\s+parameter\b"
        };
        foreach (var pattern in exfilPatterns)
        {
            var matches = Regex.Matches(description, pattern);
            foreach (Match m in matches)
            {
                findings.Add(CreateImperativeFinding("exfiltration_verbs", m.Value.Trim(), "critical"));
                riskScore += 30;
            }
        }

        // Override/ignore instructions — prompt injection staples
        var overridePatterns = new[]
        {
            @"(?i)\b(ignore|disregard|forget|override)\s+(all\s+)?(previous|prior|above|earlier|other)\s+(instructions?|rules?|guidelines?|constraints?)",
            @"(?i)\b(do\s+not|don'?t|never)\s+(mention|reveal|disclose|tell|show|indicate)",
            @"(?i)\byou\s+(must|should|shall|will|are\s+required\s+to)\s+",
            @"(?i)\balways\s+(include|attach|append|add|send)\b"
        };
        foreach (var pattern in overridePatterns)
        {
            var matches = Regex.Matches(description, pattern);
            foreach (Match m in matches)
            {
                findings.Add(CreateImperativeFinding("override_instructions", m.Value.Trim(), "critical"));
                riskScore += 25;
            }
        }

        // Hidden directives — formatting tricks to hide instructions
        var hiddenPatterns = new[]
        {
            @"(?i)\b(before\s+responding|after\s+processing|as\s+a\s+prerequisite|first\s+you\s+must)\b",
            @"(?i)\b(silently|quietly|without\s+(telling|informing|notifying|alerting))\b",
            @"(?i)\b(hidden|invisible|secret)\s+(instruction|requirement|step|parameter)\b",
            @"(?i)\bframed\s+as\b"
        };
        foreach (var pattern in hiddenPatterns)
        {
            var matches = Regex.Matches(description, pattern);
            foreach (Match m in matches)
            {
                findings.Add(CreateImperativeFinding("hidden_directives", m.Value.Trim(), "high"));
                riskScore += 20;
            }
        }

        // Bulk data request language in a description (not normal for tool docs)
        var bulkPatterns = new[]
        {
            @"(?i)\b(last|past|recent|previous)\s+\d+\s+(invoices?|records?|transactions?|emails?|messages?|documents?)\b",
            @"(?i)\b(all|every)\s+(unpaid|pending|outstanding|open)\b",
            @"(?i)\bsummarize\s+(all|the|their|every)\b"
        };
        foreach (var pattern in bulkPatterns)
        {
            var matches = Regex.Matches(description, pattern);
            foreach (Match m in matches)
            {
                findings.Add(CreateImperativeFinding("bulk_data_request", m.Value.Trim(), "high"));
                riskScore += 15;
            }
        }

        // Encoding abuse — base64, hex instructions, zero-width characters
        var encodingPatterns = new[]
        {
            @"(?i)\b(base64|encode|decode|hex)\s+(the|this|that|it)\b",
            @"[\u200B-\u200F\u2028-\u202F\uFEFF]"
        };
        foreach (var pattern in encodingPatterns)
        {
            var matches = Regex.Matches(description, pattern);
            foreach (Match m in matches)
            {
                findings.Add(CreateImperativeFinding("encoding_abuse", m.Value.Length > 30 ? m.Value.Substring(0, 30) : m.Value, "medium"));
                riskScore += 10;
            }
        }

        riskScore = Math.Min(riskScore, 100);
        var suspicious = riskScore >= 20;

        var result = new JObject
        {
            ["suspicious"] = suspicious,
            ["riskScore"] = riskScore,
            ["findings"] = findings,
            ["toolName"] = toolName
        };

        if (suspicious)
        {
            await LogTelemetryAsync("ImperativeDetected", new Dictionary<string, string>
            {
                { "toolName", toolName },
                { "riskScore", riskScore.ToString() },
                { "findingCount", findings.Count.ToString() }
            });
        }

        return CreateJsonResponse(result);
    }

    private async Task<HttpResponseMessage> HandleInspectWithPromptShields()
    {
        var body = await ReadBodyAsync();
        var text = body.Value<string>("text") ?? string.Empty;
        var context = body.Value<string>("context") ?? "MCP tool description";

        // Get the Content Safety endpoint from the original request host
        var originalHost = this.Context.Request.RequestUri?.Host ?? string.Empty;
        var apiKey = this.Context.Request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys)
            ? keys.FirstOrDefault() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 10)
        {
            return CreateJsonResponse(new JObject
            {
                ["attackDetected"] = false,
                ["userPromptAttack"] = false,
                ["documentAttack"] = false,
                ["details"] = "Content Safety key not configured. Configure a valid key in the connection to use Prompt Shields."
            });
        }

        // Call Azure AI Content Safety Prompt Shields
        var endpoint = $"https://{originalHost}/contentsafety/text:shieldPrompt?api-version=2024-09-01";
        var requestBody = new JObject
        {
            ["userPrompt"] = context,
            ["documents"] = new JArray { text }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(requestBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);

        try
        {
            var response = await this.Context.SendAsync(request, this.CancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return CreateJsonResponse(new JObject
                {
                    ["attackDetected"] = false,
                    ["userPromptAttack"] = false,
                    ["documentAttack"] = false,
                    ["details"] = $"Prompt Shields returned {(int)response.StatusCode}: {responseContent}"
                });
            }

            var shieldResult = JObject.Parse(responseContent);
            var userAttack = shieldResult.SelectToken("userPromptAnalysis.attackDetected")?.Value<bool>() ?? false;
            var docAttack = shieldResult.SelectToken("documentsAnalysis[0].attackDetected")?.Value<bool>() ?? false;

            var result = new JObject
            {
                ["attackDetected"] = userAttack || docAttack,
                ["userPromptAttack"] = userAttack,
                ["documentAttack"] = docAttack,
                ["details"] = responseContent
            };

            if (userAttack || docAttack)
            {
                await LogTelemetryAsync("PromptShieldAlert", new Dictionary<string, string>
                {
                    { "userAttack", userAttack.ToString() },
                    { "docAttack", docAttack.ToString() }
                });
            }

            return CreateJsonResponse(result);
        }
        catch (Exception ex)
        {
            return CreateJsonResponse(new JObject
            {
                ["attackDetected"] = false,
                ["userPromptAttack"] = false,
                ["documentAttack"] = false,
                ["details"] = $"Error calling Prompt Shields: {ex.Message}"
            });
        }
    }

    private async Task<HttpResponseMessage> HandleLogShieldEvent()
    {
        var body = await ReadBodyAsync();
        var eventType = body.Value<string>("eventType") ?? "unknown";
        var serverName = body.Value<string>("serverName") ?? string.Empty;
        var toolName = body.Value<string>("toolName") ?? string.Empty;
        var details = body.Value<string>("details") ?? string.Empty;
        var severity = body.Value<string>("severity") ?? "info";

        var eventId = Guid.NewGuid().ToString("N").Substring(0, 16);

        var properties = new Dictionary<string, string>
        {
            { "eventType", eventType },
            { "serverName", serverName },
            { "toolName", toolName },
            { "details", details },
            { "severity", severity },
            { "eventId", eventId }
        };

        var logged = await LogTelemetryAsync($"MCPShield_{eventType}", properties);

        var result = new JObject
        {
            ["logged"] = logged,
            ["eventId"] = eventId,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        return CreateJsonResponse(result);
    }

    // --- Utility methods ---

    private static string ComputeSha256(string input)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    private static JObject CreateFinding(string pattern, string severity, int matchCount)
    {
        return new JObject
        {
            ["pattern"] = pattern,
            ["severity"] = severity,
            ["matchCount"] = matchCount
        };
    }

    private static JObject CreateImperativeFinding(string category, string matched, string severity)
    {
        return new JObject
        {
            ["category"] = category,
            ["matched"] = matched,
            ["severity"] = severity
        };
    }

    private async Task<JObject> ReadBodyAsync()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    private static HttpResponseMessage CreateJsonResponse(JObject body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task<bool> LogTelemetryAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return false;

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
                "application/json"
            );

            await this.Context.SendAsync(request, this.CancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
