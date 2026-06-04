using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using AgentGovernance;
using AgentGovernance.Audit;
using AgentGovernance.Policy;
using AgentGovernance.Security;
using AgentGovernance.Sre;
using AgentGovernance.Trust;
#if ACS_ENABLED
using AgentControlSpecification;
#endif

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// ========================================
// GOVERNANCE KERNEL INITIALIZATION
// ========================================

var policyPath = Environment.GetEnvironmentVariable("POLICY_PATH") ?? "policies/default.yaml";

GovernanceKernel kernel;
kernel = new GovernanceKernel(new GovernanceOptions
{
    ConflictStrategy = ConflictResolutionStrategy.DenyOverrides,
    EnableRings = true,
    EnablePromptInjectionDetection = true,
    EnableCircuitBreaker = true
});

// Load policies separately so we can catch parse errors
if (File.Exists(policyPath))
{
    try
    {
        kernel.LoadPolicy(policyPath);
        Console.WriteLine($"Policy loaded from {policyPath}: {kernel.PolicyEngine.ListPolicies().Count} policies, {kernel.PolicyEngine.ListPolicies().SelectMany(p => p.Rules).Count()} rules");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"POLICY LOAD ERROR from {policyPath}: {ex}");
    }
}
else
{
    Console.WriteLine($"Policy file not found at {policyPath}");
}

FileTrustStore? trustStore = null;
try
{
    trustStore = new FileTrustStore("/tmp/trust-scores.json", defaultScore: 500, decayRate: 10);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FileTrustStore init failed: {ex.Message}");
}

// ========================================
// API KEY MIDDLEWARE
// ========================================

var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";

app.Use(async (context, next) =>
{
    // Skip auth for health check
    if (context.Request.Path == "/health")
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-API-Key", out var providedKey) || providedKey != apiKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
        return;
    }

    await next();
});

// ========================================
// HEALTH CHECK
// ========================================

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// ========================================
// 1. EVALUATE ACTION
// ========================================

app.MapPost("/api/evaluate", (EvaluateRequest req) =>
{
    var args = req.Args?.ToDictionary(k => k.Key, v => (object)v.Value) ?? new Dictionary<string, object>();
    var result = kernel.EvaluateToolCall(
        agentId: req.AgentId ?? "unknown",
        toolName: req.ToolName,
        args: args
    );

    return Results.Ok(new
    {
        allowed = result.Allowed,
        reason = result.Reason,
        policyRule = result.PolicyDecision?.MatchedRule,
        evaluationMs = result.PolicyDecision?.EvaluationMs
    });
});

// ========================================
// 2. CHECK COMPLIANCE
// ========================================

app.MapPost("/api/compliance", (ComplianceRequest req) =>
{
    var args = req.Args?.ToDictionary(k => k.Key, v => (object)v.Value) ?? new Dictionary<string, object>();
    var evalResult = kernel.EvaluateToolCall(
        agentId: req.AgentId ?? "unknown",
        toolName: req.ToolName,
        args: args
    );

    // Map to OWASP categories
    var owaspFindings = new List<object>();

    if (!evalResult.Allowed)
    {
        owaspFindings.Add(new { category = "ASI-02", name = "Excessive Capabilities", status = "violation", detail = evalResult.Reason });
    }

    if (req.Args != null)
    {
        foreach (var arg in req.Args)
        {
            var injection = kernel.InjectionDetector.Detect(arg.Value);
            if (injection.IsInjection)
            {
                owaspFindings.Add(new { category = "ASI-01", name = "Goal Hijacking", status = "violation", detail = $"Injection detected in '{arg.Key}': {injection.InjectionType}" });
            }
        }
    }

    var grade = owaspFindings.Count == 0 ? "A" : owaspFindings.Count <= 2 ? "C" : "F";

    return Results.Ok(new
    {
        grade,
        framework = req.Framework ?? "OWASP-Agentic-2026",
        findings = owaspFindings,
        findingCount = owaspFindings.Count,
        actionAllowed = evalResult.Allowed
    });
});

// ========================================
// 3. SCORE TRUST
// ========================================

app.MapPost("/api/trust/score", (TrustRequest req) =>
{
    if (trustStore == null)
    {
        return Results.Json(new { error = "Trust store not initialized" }, statusCode: 503);
    }

    if (req.Action == "positive")
    {
        trustStore.RecordPositiveSignal(req.AgentId, boost: req.Amount ?? 25);
    }
    else if (req.Action == "negative")
    {
        trustStore.RecordNegativeSignal(req.AgentId, penalty: req.Amount ?? 50);
    }
    else if (req.Action == "set" && req.Amount.HasValue)
    {
        trustStore.SetScore(req.AgentId, req.Amount.Value);
    }

    var score = trustStore.GetScore(req.AgentId);
    var tier = score switch
    {
        >= 950 => "Critical",
        >= 800 => "Trusted",
        >= 600 => "Standard",
        >= 300 => "Restricted",
        _ => "Untrusted"
    };
    var ring = score switch
    {
        >= 950 => "Ring0",
        >= 800 => "Ring1",
        >= 600 => "Ring2",
        _ => "Ring3"
    };

    return Results.Ok(new
    {
        agentId = req.AgentId,
        score,
        tier,
        ring
    });
});

// ========================================
// 4. DETECT INJECTION
// ========================================

app.MapPost("/api/injection/detect", (InjectionRequest req) =>
{
    var result = kernel.InjectionDetector.Detect(req.Text);

    return Results.Ok(new
    {
        isInjection = result.IsInjection,
        injectionType = result.InjectionType.ToString(),
        threatLevel = result.ThreatLevel.ToString(),
        confidence = result.Confidence,
        explanation = result.Explanation
    });
});

// ========================================
// 5. LOG AUDIT EVENT
// ========================================

app.MapPost("/api/audit", (AuditRequest req) =>
{
    var eventId = Guid.NewGuid().ToString();
    var timestamp = DateTime.UtcNow;

    kernel.AuditEmitter.Emit(
        GovernanceEventType.PolicyCheck,
        req.AgentId ?? "unknown",
        sessionId: null,
        new Dictionary<string, object>
        {
            ["action"] = req.Action ?? "",
            ["tool_name"] = req.ToolName ?? "",
            ["result"] = req.Result ?? "",
            ["event_id"] = eventId
        },
        policyName: null
    );

    return Results.Ok(new
    {
        logged = true,
        eventId,
        timestamp = timestamp.ToString("o"),
        agentId = req.AgentId
    });
});

// ========================================
// 6. CHECK CIRCUIT BREAKER
// ========================================

app.MapPost("/api/circuit-breaker", (CircuitBreakerRequest req) =>
{
    var cb = kernel.CircuitBreaker;

    return Results.Ok(new
    {
        serviceId = req.ServiceId,
        state = cb.State.ToString(),
        failureCount = cb.FailureCount
    });
});

// ========================================
// 7. SCAN MCP TOOL
// ========================================

app.MapPost("/api/mcp/scan", (McpScanRequest req) =>
{
    var risks = new List<object>();
    var toolDef = req.ToolDefinition ?? "";

    // Check for tool poisoning patterns
    if (toolDef.Contains("ignore", StringComparison.OrdinalIgnoreCase) &&
        toolDef.Contains("instructions", StringComparison.OrdinalIgnoreCase))
    {
        risks.Add(new { type = "tool_poisoning", severity = "critical", detail = "Tool definition contains instruction override patterns" });
    }

    // Check for hidden instructions in descriptions
    if (toolDef.Contains("<|", StringComparison.OrdinalIgnoreCase) ||
        toolDef.Contains("[INST]", StringComparison.OrdinalIgnoreCase) ||
        toolDef.Contains("```system", StringComparison.OrdinalIgnoreCase))
    {
        risks.Add(new { type = "hidden_instructions", severity = "critical", detail = "Tool definition contains delimiter injection tokens" });
    }

    // Check for suspicious URL patterns
    if (toolDef.Contains("http://", StringComparison.OrdinalIgnoreCase))
    {
        risks.Add(new { type = "insecure_transport", severity = "warning", detail = "Tool definition references non-HTTPS URLs" });
    }

    // Check for data exfiltration patterns
    if (toolDef.Contains("send_to", StringComparison.OrdinalIgnoreCase) ||
        toolDef.Contains("exfiltrate", StringComparison.OrdinalIgnoreCase) ||
        toolDef.Contains("forward_data", StringComparison.OrdinalIgnoreCase))
    {
        risks.Add(new { type = "data_exfiltration", severity = "critical", detail = "Tool definition contains data exfiltration patterns" });
    }

    var riskLevel = risks.Count == 0 ? "safe" :
        risks.Any(r => ((dynamic)r).severity == "critical") ? "critical" : "warning";

    return Results.Ok(new
    {
        safe = risks.Count == 0,
        riskLevel,
        risks,
        riskCount = risks.Count
    });
});

// ========================================
// 8. ACS — LOAD MANIFEST
// ========================================
//
// Registers an Agent Control Specification (ACS) manifest by id. Manifest
// files live under MANIFEST_DIR (defaults to ./manifests). When the
// AgentControlSpecification SDK is wired (ACS_ENABLED), the manifest is also
// loaded into a native AgentControl instance so subsequent evaluate/transform
// calls hit the Rust core directly.

app.MapPost("/api/acs/manifest/load", (AcsLoadManifestRequest req) =>
{
    var manifestDir = Environment.GetEnvironmentVariable("MANIFEST_DIR") ?? "manifests";
    var path = Path.IsPathRooted(req.Path)
        ? req.Path
        : Path.Combine(manifestDir, req.Path);

    if (!File.Exists(path))
    {
        return Results.NotFound(new { error = $"Manifest not found at {path}" });
    }

    var id = req.Id ?? Path.GetFileNameWithoutExtension(path);

    try
    {
        AcsRegistry.Register(id, path);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Failed to load manifest: {ex.Message}" });
    }

    return Results.Ok(new
    {
        manifestId = id,
        path,
        loaded = true,
        sdkBound = AcsRegistry.SdkAvailable,
        note = AcsRegistry.SdkAvailable
            ? "Live SDK evaluation enabled."
            : "Manifest registered. Live ACS evaluation requires the AgentControlSpecification SDK — see manifests/README.md."
    });
});

// ========================================
// 9. ACS — EVALUATE INTERVENTION POINT
// ========================================
//
// Submits a snapshot at one of the 8 ACS intervention points
// (agent_startup, input, pre_model_call, post_model_call, pre_tool_call,
// post_tool_call, output, agent_shutdown) and returns the verdict.

app.MapPost("/api/acs/evaluate", async (AcsEvaluateRequest req) =>
{
    if (!AcsRegistry.TryGet(req.ManifestId, out var manifestPath))
    {
        return Results.NotFound(new { error = $"Manifest '{req.ManifestId}' not registered. Call /api/acs/manifest/load first." });
    }

#if ACS_ENABLED
    try
    {
        var result = await AcsRegistry.EvaluateAsync(req).ConfigureAwait(false);
        return Results.Ok(AcsRegistry.ProjectVerdict(result, req));
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "ACS evaluation failed", detail = ex.Message }, statusCode: 500);
    }
#else
    await Task.CompletedTask;
    _ = manifestPath;
    return Results.Json(new
    {
        error = "ACS SDK not wired",
        manifestId = req.ManifestId,
        interventionPoint = req.InterventionPoint,
        setup = "Drop an AgentControlSpecification nupkg into container-app/local-packages/ and rebuild. See manifests/README.md."
    }, statusCode: 501);
#endif
});

// ========================================
// 10. ACS — TRANSFORM PAYLOAD
// ========================================
//
// Convenience wrapper: evaluates the intervention point and, when the verdict
// is `transform`, returns the transformed policy target (e.g., redacted body).
// For `allow`/`warn` returns the original target untouched. For `deny`/`escalate`
// returns the verdict so the host can block or escalate.

app.MapPost("/api/acs/transform", async (AcsEvaluateRequest req) =>
{
    if (!AcsRegistry.TryGet(req.ManifestId, out var manifestPath))
    {
        return Results.NotFound(new { error = $"Manifest '{req.ManifestId}' not registered. Call /api/acs/manifest/load first." });
    }

#if ACS_ENABLED
    try
    {
        var result = await AcsRegistry.EvaluateAsync(req).ConfigureAwait(false);
        return Results.Ok(AcsRegistry.ProjectTransform(result, req));
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "ACS evaluation failed", detail = ex.Message }, statusCode: 500);
    }
#else
    await Task.CompletedTask;
    _ = manifestPath;
    return Results.Json(new
    {
        error = "ACS SDK not wired",
        manifestId = req.ManifestId,
        interventionPoint = req.InterventionPoint,
        setup = "Drop an AgentControlSpecification nupkg into container-app/local-packages/ and rebuild. See manifests/README.md."
    }, statusCode: 501);
#endif
});

// ========================================
// 11. CHECK VERSION
// ========================================

app.MapGet("/api/version", async () =>
{
    var runningVersion = typeof(GovernanceKernel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(GovernanceKernel).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    string? latestVersion = null;
    bool updateAvailable = false;

    try
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AgentGovernanceApi", "1.0"));
        var response = await http.GetStringAsync("https://api.nuget.org/v3-flatcontainer/microsoft.agentgovernance/index.json");
        var doc = JsonDocument.Parse(response);
        var versions = doc.RootElement.GetProperty("versions");
        latestVersion = versions[versions.GetArrayLength() - 1].GetString();
        updateAvailable = latestVersion != null && latestVersion != runningVersion.Split('+')[0];
    }
    catch
    {
        // NuGet check is best-effort
    }

    return Results.Ok(new
    {
        running = runningVersion,
        latest = latestVersion,
        updateAvailable,
        package = "Microsoft.AgentGovernance",
        nugetUrl = "https://www.nuget.org/packages/Microsoft.AgentGovernance"
    });
});

app.Run();

// ========================================
// REQUEST MODELS
// ========================================

record EvaluateRequest(string ToolName, string? AgentId, Dictionary<string, string>? Args);
record ComplianceRequest(string ToolName, string? AgentId, Dictionary<string, string>? Args, string? Framework);
record TrustRequest(string AgentId, string? Action, double? Amount);
record InjectionRequest(string Text);
record AuditRequest(string? AgentId, string? Action, string? ToolName, string? Result);
record CircuitBreakerRequest(string? ServiceId);
record McpScanRequest(string? ToolDefinition);
record AcsLoadManifestRequest(string Path, string? Id);
record AcsEvaluateRequest(string ManifestId, string InterventionPoint, JsonElement Snapshot, string? ToolName, string? Mode);

// ========================================
// ACS REGISTRY
// ========================================
//
// In-memory manifest registry plus the live SDK call path (compiled when
// ACS_ENABLED is defined). The csproj defines ACS_ENABLED automatically when
// an AgentControlSpecification nupkg is dropped into ./local-packages/.
// Build the nupkg with scripts/build-acs-nupkg.ps1.

static class AcsRegistry
{
#if ACS_ENABLED
    private static readonly Dictionary<string, AgentControl> _controls = new();
#endif
    private static readonly Dictionary<string, string> _manifests = new();
    private static readonly object _lock = new();

#if ACS_ENABLED
    public const bool SdkAvailable = true;
#else
    public const bool SdkAvailable = false;
#endif

    public static void Register(string id, string path)
    {
        lock (_lock)
        {
            _manifests[id] = path;
#if ACS_ENABLED
            // Eagerly load the manifest so syntax errors surface at registration time.
            _controls[id] = AgentControl.FromPath(path);
#endif
        }
    }

    public static bool TryGet(string id, out string path)
    {
        lock (_lock) { return _manifests.TryGetValue(id, out path!); }
    }

#if ACS_ENABLED
    public static async ValueTask<InterventionPointResult> EvaluateAsync(AcsEvaluateRequest req)
    {
        AgentControl control;
        lock (_lock)
        {
            if (!_controls.TryGetValue(req.ManifestId, out control!))
            {
                throw new InvalidOperationException($"Manifest '{req.ManifestId}' not registered.");
            }
        }

        var interventionPoint = InterventionPointExtensions.FromWireName(req.InterventionPoint);
        var mode = string.Equals(req.Mode, "evaluate_only", StringComparison.OrdinalIgnoreCase)
            ? EnforcementMode.EvaluateOnly
            : EnforcementMode.Enforce;

        return await control.EvaluateInterventionPointAsync(interventionPoint, req.Snapshot, mode)
            .ConfigureAwait(false);
    }

    public static object ProjectVerdict(InterventionPointResult result, AcsEvaluateRequest req) => new
    {
        decision = result.Verdict.Decision.ToWireName(),
        reason = result.Verdict.Reason,
        message = result.Verdict.Message,
        manifestId = req.ManifestId,
        interventionPoint = req.InterventionPoint,
        evidence = result.Verdict.Evidence,
        resultLabels = result.Verdict.ResultLabels,
        actionIdentity = result.EnforcedIdentity ?? result.ActionIdentity,
        inputIdentity = result.InputIdentity
    };

    public static object ProjectTransform(InterventionPointResult result, AcsEvaluateRequest req)
    {
        var decision = result.Verdict.Decision;
        var isTransform = decision == Decision.Transform;
        JsonElement? payload = isTransform && result.TransformedPolicyTarget.HasValue
            ? result.TransformedPolicyTarget
            : ExtractOriginalPolicyTarget(result);

        return new
        {
            decision = decision.ToWireName(),
            transformed = isTransform,
            payload,
            reason = result.Verdict.Reason,
            message = result.Verdict.Message,
            manifestId = req.ManifestId,
            interventionPoint = req.InterventionPoint
        };
    }

    private static JsonElement? ExtractOriginalPolicyTarget(InterventionPointResult result)
    {
        if (!result.PolicyInput.HasValue) return null;
        if (result.PolicyInput.Value.ValueKind != JsonValueKind.Object) return null;
        if (!result.PolicyInput.Value.TryGetProperty("policy_target", out var pt)) return null;
        if (pt.ValueKind != JsonValueKind.Object) return pt;
        return pt.TryGetProperty("value", out var v) ? v : pt;
    }
#endif
}
