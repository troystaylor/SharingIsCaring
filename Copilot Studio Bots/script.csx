using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string ServerName = "copilot-studio-bots";
    private const string ServerVersion = "1.2.1";
    private const string ProtocolVersion = "2025-11-25";
    private const string ApiVersion = "2024-10-01";
    private const string BackendBaseUrl = "https://api.powerplatform.com/copilotstudio";
    private const int MaxInlineSnapshotBytes = 4 * 1024 * 1024;

    private const string BotPath = "/environments/{environmentId}/bots/{botId}";

    // The channel manifest sits under agents/{id}/channels rather than bots/{id}, so it
    // needs its own template even though it shares the copilotstudio base URL.
    private const string AgentChannelDownloadPath =
        "/environments/{environmentId}/agents/{botId}/channels/{channelName}/download";

    // Microsoft documents M365 as the only supported channel. An undocumented value
    // has no defined error response, so it is rejected here rather than sent blind.
    private const string DefaultAgentChannel = "M365";
    private static readonly string[] SupportedAgentChannels = { "M365" };

    // The environment picker is served from a different Power Platform API surface
    // than the Bots operations, so it is addressed absolutely rather than via basePath.
    private const string PlatformApiBase = "https://api.powerplatform.com";
    private const string EnvironmentsUrl =
        PlatformApiBase + "/environmentmanagement/environments?api-version=" + ApiVersion;

    // Agent inventory (undocumented resource query API). Ported from the Power Platform
    // Admin connector, where the quirks below were verified against the live service.
    private const string ResourceQueryApiVersion = "2022-03-01-preview";
    private const string AgentResourceType = "'microsoft.copilotstudio/agents'";
    private const int AgentPageSize = 1000;
    private const int AgentMaxPages = 10;

    // Optional hardcoded telemetry (Mission Control style)
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            if (this.Context.OperationId == "InvokeMCP")
            {
                return await HandleMcpRequestAsync().ConfigureAwait(false);
            }

            if (this.Context.OperationId == "GetEnvironmentDropdown")
            {
                return await HandleEnvironmentDropdownAsync().ConfigureAwait(false);
            }

            if (this.Context.OperationId == "GetAgentDropdown")
            {
                return await HandleAgentDropdownAsync().ConfigureAwait(false);
            }

            if (this.Context.OperationId == "ListAgents")
            {
                return await HandleListAgentsOperationAsync().ConfigureAwait(false);
            }

            // Standard REST operations pass through unchanged.
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync(ex, "ExecuteAsync").ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { error = ex.Message }),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    /// <summary>
    /// Backs the environment dropdown. Flattens the environmentmanagement response
    /// into the { id, name } shape that x-ms-dynamic-values expects.
    /// </summary>
    private async Task<HttpResponseMessage> HandleEnvironmentDropdownAsync()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Get, EnvironmentsUrl);
        outbound.Headers.Add("Accept", "application/json");

        if (this.Context.Request.Headers.Contains("Authorization"))
        {
            outbound.Headers.Add("Authorization", this.Context.Request.Headers.GetValues("Authorization"));
        }

        var response = await this.Context.SendAsync(outbound, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(content ?? string.Empty, Encoding.UTF8, "application/json")
            };
        }

        var dropdown = new JArray();
        try
        {
            var environments = JObject.Parse(content)["value"] as JArray ?? new JArray();
            foreach (var env in environments)
            {
                var id = ResolveEnvironmentId(env);
                if (string.IsNullOrEmpty(id)) continue;

                dropdown.Add(new JObject
                {
                    ["id"] = id,
                    ["name"] = FirstValue(env, "displayName", "properties.displayName", "name")?.ToString() ?? id
                });
            }
        }
        catch
        {
            // An unparseable payload yields an empty picker rather than a broken designer.
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                dropdown.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    /// <summary>
    /// Returns the first non-null token among the supplied paths.
    /// <para>
    /// The environmentmanagement API returns environment fields at the top level
    /// (displayName, type, state, url). Older BAP-shaped payloads nest the same data
    /// under "properties". Verified against the live service: none of 19 environments
    /// returned a "properties" object, so a nested-only read yields null for every
    /// field and the picker falls back to showing raw GUIDs.
    /// </para>
    /// </summary>
    private static JToken FirstValue(JToken source, params string[] paths)
    {
        if (source == null) return null;

        foreach (var path in paths)
        {
            JToken token;
            try
            {
                token = source.SelectToken(path);
            }
            catch (JsonException)
            {
                continue;
            }

            if (token != null && token.Type != JTokenType.Null) return token;
        }

        return null;
    }

    /// <summary>
    /// The environments API may report the identifier as a bare GUID in "name" or as an
    /// ARM-style resource path in "id". The Bots operations need the bare GUID either way.
    /// </summary>
    private static string ResolveEnvironmentId(JToken environment)
    {
        var name = environment["name"]?.ToString();
        if (IsGuid(name)) return name;

        var id = environment["id"]?.ToString();
        if (IsGuid(id)) return id;

        if (!string.IsNullOrEmpty(id))
        {
            var segments = id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (IsGuid(segments[i])) return segments[i];
            }
        }

        return string.IsNullOrEmpty(name) ? id : name;
    }

    private static bool IsGuid(string value)
    {
        Guid parsed;
        return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out parsed);
    }

    // ─── Agent inventory (undocumented resourcequery API) ────────────────
    //
    // Ported from the Power Platform Admin connector. Two quirks were verified
    // against the live service and must not be "cleaned up":
    //   1. Every clause serializes "$type" first — it is a polymorphic discriminator
    //      and the service rejects the document otherwise.
    //   2. SkipToken does not advance between calls, so Skip offsets are the only
    //      working pager. The orderby carries a unique tiebreaker because createdAt
    //      ties shift the skip window and silently drop rows.

    private JObject BuildAgentQuery(string environmentId, int skip)
    {
        var clauses = new JArray
        {
            new JObject
            {
                ["$type"] = "where",
                ["FieldName"] = "type",
                ["Operator"] = "in~",
                ["Values"] = new JArray { AgentResourceType }
            },
            new JObject
            {
                ["$type"] = "extend",
                ["FieldName"] = "joinKey",
                ["Expression"] = "tolower(tostring(properties.environmentId))"
            }
        };

        if (!string.IsNullOrEmpty(environmentId))
        {
            clauses.Add(new JObject
            {
                ["$type"] = "where",
                ["FieldName"] = "joinKey",
                ["Operator"] = "==",
                ["Values"] = new JArray { $"'{environmentId.ToLowerInvariant()}'" }
            });
        }

        clauses.Add(new JObject
        {
            ["$type"] = "project",
            ["FieldList"] = new JArray { "joinKey", "name", "type", "properties" }
        });

        clauses.Add(new JObject
        {
            ["$type"] = "join",
            ["JoinKind"] = "leftouter",
            ["RightTable"] = new JObject
            {
                ["TableName"] = "PowerPlatformResources",
                ["Clauses"] = new JArray
                {
                    new JObject
                    {
                        ["$type"] = "where",
                        ["FieldName"] = "type",
                        ["Operator"] = "==",
                        ["Values"] = new JArray { "'microsoft.powerplatform/environments'" }
                    },
                    new JObject
                    {
                        ["$type"] = "project",
                        ["FieldList"] = new JArray
                        {
                            "joinKey = tolower(name)",
                            "environmentName = properties.displayName",
                            "environmentRegion = location",
                            "environmentType = properties.environmentType",
                            "isManagedEnvironment = properties.isManaged"
                        }
                    }
                }
            },
            ["LeftColumnName"] = "joinKey",
            ["RightColumnName"] = "joinKey"
        });

        clauses.Add(new JObject
        {
            ["$type"] = "orderby",
            ["FieldNamesAscDesc"] = new JObject
            {
                ["tostring(properties.createdAt)"] = "desc",
                ["name"] = "asc"
            }
        });

        return new JObject
        {
            ["Options"] = new JObject
            {
                ["Top"] = AgentPageSize,
                ["Skip"] = skip,
                ["SkipToken"] = string.Empty
            },
            ["TableName"] = "PowerPlatformResources",
            ["Clauses"] = clauses
        };
    }

    private async Task<JObject> FetchAgentsAsync(string environmentId)
    {
        var agents = new JArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skip = 0;
        var pages = 0;
        var truncated = false;

        while (true)
        {
            var body = BuildAgentQuery(environmentId, skip);
            var content = await CallResourceQueryAsync(body).ConfigureAwait(false);

            var rows = JObject.Parse(content)["data"] as JArray ?? new JArray();

            foreach (var row in rows)
            {
                var resourceName = row["name"]?.ToString();
                if (!string.IsNullOrEmpty(resourceName) && !seen.Add(resourceName)) continue;
                agents.Add(ProjectAgent(row));
            }

            pages++;
            if (rows.Count < AgentPageSize) break;
            if (pages >= AgentMaxPages) { truncated = true; break; }
            skip += AgentPageSize;
        }

        return new JObject
        {
            ["agentCount"] = agents.Count,
            ["truncated"] = truncated,
            ["environmentId"] = string.IsNullOrEmpty(environmentId) ? (JToken)JValue.CreateNull() : environmentId,
            ["agents"] = agents
        };
    }

    private static JObject ProjectAgent(JToken row)
    {
        var props = row["properties"];
        var viewers = props?["sharedWithViewers"];
        var editors = props?["sharedWithEditors"];

        var harness = TriState(props, "isCLIAgent");

        return new JObject
        {
            ["displayName"] = Val(props?["displayName"]),
            ["botId"] = Val(row["name"]),
            ["schemaName"] = Val(props?["schemaName"]),
            ["environmentId"] = Val(props?["environmentId"]),
            ["environmentName"] = Val(row["environmentName"]),
            ["environmentType"] = Val(row["environmentType"]),
            ["environmentIsManaged"] = Val(row["isManagedEnvironment"]),
            ["createdAt"] = Val(props?["createdAt"]),
            ["createdBy"] = Val(props?["createdBy"]),
            ["lastPublishedAt"] = Val(props?["lastPublishedAt"]),
            ["ownerId"] = Val(props?["ownerId"]),
            ["isQuarantined"] = Val(props?["isQuarantined"]),
            ["isCLIAgent"] = harness,
            ["harness"] = HarnessLabel(harness),
            ["sharedWithViewersUserCount"] = Val(viewers?["userCount"]),
            ["sharedWithViewersEntireTenant"] = Val(viewers?["entireTenant"]),
            ["sharedWithEditorsUserCount"] = Val(editors?["userCount"]),
            ["channels"] = props?["channels"] as JArray ?? new JArray()
        };
    }

    /// <summary>
    /// isCLIAgent only separates the GitHub Copilot harness from the other two.
    /// "false" cannot distinguish Standard from Copilot Chat, so the label says so
    /// rather than inventing a precision the field does not have.
    /// </summary>
    private static string HarnessLabel(string triState)
    {
        switch (triState)
        {
            case "true": return "GitHub Copilot";
            case "false": return "Standard or Copilot Chat";
            default: return "unknown";
        }
    }

    // Absent is reported as "unknown" so a missing field is never mistaken for false.
    private static string TriState(JToken parent, string propertyName)
    {
        var token = parent?[propertyName];
        if (token == null || token.Type == JTokenType.Null) return "unknown";
        if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";

        bool parsed;
        return bool.TryParse(token.ToString(), out parsed) ? (parsed ? "true" : "false") : "unknown";
    }

    private static JToken Val(JToken token)
    {
        return token == null || token.Type == JTokenType.Null ? JValue.CreateNull() : token;
    }

    private static bool IsTrue(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return false;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>();

        bool parsed;
        return bool.TryParse(token.ToString(), out parsed) && parsed;
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= 400 ? value : value.Substring(0, 400) + "...";
    }

    // ─── Containment ─────────────────────────────────────────────────────

    /// <summary>
    /// Read-only. Returns the agents that match the containment criteria, with the
    /// reasons each one qualified, so the decision is reviewable before anything acts.
    /// Never returns agents whose harness is unknown unless explicitly asked, because
    /// an absent isCLIAgent is not evidence of the expensive harness.
    /// </summary>
    private async Task<JObject> FindContainmentCandidatesAsync(JObject args)
    {
        var environmentId = args.Value<string>("environmentId");
        var includeUnknownHarness = args["includeUnknownHarness"] != null
            && ToBoolean(args["includeUnknownHarness"], "includeUnknownHarness");
        var requireTenantWide = args["requireTenantWide"] == null
            || ToBoolean(args["requireTenantWide"], "requireTenantWide");
        var requireNeverPublished = args["requireNeverPublished"] != null
            && ToBoolean(args["requireNeverPublished"], "requireNeverPublished");

        var inventory = await FetchAgentsAsync(environmentId).ConfigureAwait(false);
        var agents = (JArray)inventory["agents"];

        var candidates = new JArray();
        var skippedUnknownHarness = 0;
        var alreadyQuarantined = 0;

        foreach (var agent in agents)
        {
            var harness = agent["isCLIAgent"]?.ToString();

            if (harness == "unknown")
            {
                skippedUnknownHarness++;
                if (!includeUnknownHarness) continue;
            }
            else if (harness != "true")
            {
                continue;
            }

            if (IsTrue(agent["isQuarantined"]))
            {
                alreadyQuarantined++;
                continue;
            }

            var tenantWide = IsTrue(agent["sharedWithViewersEntireTenant"]);
            var neverPublished = string.IsNullOrEmpty(agent["lastPublishedAt"]?.ToString());

            if (requireTenantWide && !tenantWide) continue;
            if (requireNeverPublished && !neverPublished) continue;

            var reasons = new JArray();
            if (harness == "true") reasons.Add("githubCopilotHarness");
            if (harness == "unknown") reasons.Add("harnessUnknown");
            if (tenantWide) reasons.Add("sharedWithEntireTenant");
            if (neverPublished) reasons.Add("neverPublished");

            var candidate = (JObject)agent.DeepClone();
            candidate["reasons"] = reasons;
            candidates.Add(candidate);
        }

        return new JObject
        {
            ["candidateCount"] = candidates.Count,
            ["scannedCount"] = agents.Count,
            ["truncated"] = inventory["truncated"],
            ["skippedUnknownHarness"] = skippedUnknownHarness,
            ["skippedAlreadyQuarantined"] = alreadyQuarantined,
            ["criteria"] = new JObject
            {
                ["environmentId"] = string.IsNullOrEmpty(environmentId) ? (JToken)JValue.CreateNull() : environmentId,
                ["includeUnknownHarness"] = includeUnknownHarness,
                ["requireTenantWide"] = requireTenantWide,
                ["requireNeverPublished"] = requireNeverPublished
            },
            ["candidates"] = candidates
        };
    }

    /// <summary>
    /// Acts only on an explicit list of agents. Deliberately takes no filter, so a
    /// caller cannot quarantine a population it has not first seen and named.
    /// </summary>
    private async Task<JObject> ContainAgentsAsync(JObject args)
    {
        if (!ToBoolean(args["confirm"], "confirm"))
        {
            throw new ArgumentException(
                "Quarantining blocks end user access. Set confirm to true to proceed.");
        }

        var targets = args["agents"] as JArray;
        if (targets == null || targets.Count == 0)
        {
            throw new ArgumentException(
                "Provide an agents array of { environmentId, botId } pairs. " +
                "Run find_containment_candidates first; this tool does not accept a filter.");
        }

        var results = new JArray();
        var succeeded = 0;

        foreach (var target in targets)
        {
            var envId = target["environmentId"]?.ToString();
            var botId = target["botId"]?.ToString();

            if (string.IsNullOrWhiteSpace(envId) || string.IsNullOrWhiteSpace(botId))
            {
                results.Add(new JObject
                {
                    ["environmentId"] = envId,
                    ["botId"] = botId,
                    ["succeeded"] = false,
                    ["error"] = "Both environmentId and botId are required."
                });
                continue;
            }

            try
            {
                var endpoint = BotPath
                    .Replace("{environmentId}", Uri.EscapeDataString(envId))
                    .Replace("{botId}", Uri.EscapeDataString(botId))
                    + "/api/botQuarantine/SetAsQuarantined?api-version=" + ApiVersion;

                var response = await InvokeBackendAsync(HttpMethod.Post, endpoint, null).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode) succeeded++;

                results.Add(new JObject
                {
                    ["environmentId"] = envId,
                    ["botId"] = botId,
                    ["succeeded"] = response.IsSuccessStatusCode,
                    ["status"] = (int)response.StatusCode,
                    ["response"] = string.IsNullOrWhiteSpace(content) ? null : Truncate(content)
                });
            }
            catch (Exception ex)
            {
                // One failure must not abandon the rest of the batch.
                results.Add(new JObject
                {
                    ["environmentId"] = envId,
                    ["botId"] = botId,
                    ["succeeded"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        return new JObject
        {
            ["requested"] = targets.Count,
            ["succeeded"] = succeeded,
            ["failed"] = targets.Count - succeeded,
            ["results"] = results
        };
    }

    /// <summary>
    /// The undocumented resourcequery API intermittently rejects a payload it accepted
    /// moments earlier with 400 "KQLOM format is wrong or it cannot be null". Reproduced
    /// against the live service with a byte-identical body that had just succeeded, so it
    /// is a service fault rather than a malformed request. Without a retry a single blip
    /// fails the agent inventory, the agent picker, and both containment tools.
    /// </summary>
    private async Task<string> CallResourceQueryAsync(JObject body)
    {
        const int maxAttempts = 3;
        var url = PlatformApiBase + "/resourcequery/resources/query?api-version=" + ResourceQueryApiVersion;

        HttpResponseMessage response = null;
        string content = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response = await CallPlatformApiAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
            content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return content;
            if (!IsTransientResourceQueryFailure(response.StatusCode, content)) break;
            if (attempt == maxAttempts) break;

            await Task.Delay(400 * attempt).ConfigureAwait(false);
        }

        var retried = IsTransientResourceQueryFailure(response.StatusCode, content)
            ? $" after {maxAttempts} attempts"
            : string.Empty;

        throw new InvalidOperationException(
            $"Agent inventory query returned {(int)response.StatusCode}{retried}: {Truncate(content)}");
    }

    private static bool IsTransientResourceQueryFailure(HttpStatusCode status, string body)
    {
        if ((int)status == 429) return true;
        if ((int)status >= 500) return true;

        // The connector builds this query itself, so a caller cannot supply a genuinely
        // malformed one. A KQLOM complaint here is the known intermittent fault.
        return status == HttpStatusCode.BadRequest &&
               body != null &&
               body.IndexOf("KQLOM", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task<HttpResponseMessage> CallPlatformApiAsync(HttpMethod method, string absoluteUrl, JObject body)
    {
        var outbound = new HttpRequestMessage(method, absoluteUrl);
        outbound.Headers.Add("Accept", "application/json");

        if (body != null)
        {
            outbound.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        if (this.Context.Request.Headers.Contains("Authorization"))
        {
            outbound.Headers.Add("Authorization", this.Context.Request.Headers.GetValues("Authorization"));
        }

        return await this.Context.SendAsync(outbound, this.CancellationToken).ConfigureAwait(false);
    }

    private string GetQueryParam(string name)
    {
        var query = this.Context.Request.RequestUri?.Query;
        if (string.IsNullOrEmpty(query)) return null;

        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair.Length == 0) continue;
            var parts = pair.Split(new[] { '=' }, 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            }
        }

        return null;
    }

    private HttpResponseMessage JsonResponse(JToken payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                payload.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task<HttpResponseMessage> HandleListAgentsOperationAsync()
    {
        try
        {
            var result = await FetchAgentsAsync(GetQueryParam("environmentId")).ConfigureAwait(false);
            return JsonResponse(result);
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync(ex, "ListAgents").ConfigureAwait(false);
            return JsonResponse(new JObject { ["error"] = ex.Message }, HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Backs the agent dropdown, cascading from the selected environment.
    /// </summary>
    private async Task<HttpResponseMessage> HandleAgentDropdownAsync()
    {
        var dropdown = new JArray();

        try
        {
            var inventory = await FetchAgentsAsync(GetQueryParam("environmentId")).ConfigureAwait(false);
            foreach (var agent in (JArray)inventory["agents"])
            {
                var id = agent["botId"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;

                var label = agent["displayName"]?.ToString();
                if (string.IsNullOrEmpty(label)) label = id;

                // Surface the expensive harness in the picker itself.
                if (agent["isCLIAgent"]?.ToString() == "true") label += "  (GitHub Copilot)";

                dropdown.Add(new JObject { ["id"] = id, ["name"] = label });
            }
        }
        catch
        {
            // A failed inventory yields an empty picker rather than a broken designer.
        }

        return JsonResponse(dropdown);
    }

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: invalid JSON");
        }

        var method = request.Value<string>("method");
        var requestId = request["id"];

        await LogToAppInsightsAsync("MCP_Request", new Dictionary<string, string>
        {
            { "method", method ?? string.Empty },
            { "requestId", requestId?.ToString() ?? "null" }
        }).ConfigureAwait(false);

        // JSON-RPC notification: no response body required
        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        switch (method)
        {
            case "initialize":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false },
                        ["resources"] = new JObject { ["listChanged"] = false },
                        ["prompts"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion
                    }
                });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCallAsync(request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // --- Maker evaluation ---
            McpTool(
                "get_test_sets",
                "Retrieve all test sets for a specific Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "get_test_set_details",
                "Retrieve details of a specific test set for a Copilot Studio agent.",
                AgentSchema(
                    new JObject { ["testSetId"] = Prop("Test set ID") },
                    "testSetId")),

            McpTool(
                "start_evaluation",
                "Start an asynchronous evaluation run for a test set. By default evaluates the draft (unpublished) version.",
                AgentSchema(
                    new JObject
                    {
                        ["testSetId"] = Prop("Test set ID"),
                        ["mcsConnectionId"] = Prop("Optional Copilot Studio connection ID for authenticated evaluation"),
                        ["runOnPublishedBot"] = Prop("When true, evaluates the published version. Defaults to false (draft).", "boolean"),
                        ["evaluationRunName"] = Prop("Optional name for this evaluation run for dashboards and reports.")
                    },
                    "testSetId")),

            McpTool(
                "list_test_runs",
                "List all evaluation runs for a specific agent.",
                AgentSchema()),

            McpTool(
                "get_run_details",
                "Retrieve detailed results for a specific evaluation run.",
                AgentSchema(
                    new JObject { ["testRunId"] = Prop("Test run ID") },
                    "testRunId")),

            McpTool(
                "download_evaluation_snapshot",
                "Download the agent content snapshot captured for an evaluation run as a ZIP file.",
                AgentSchema(
                    new JObject { ["testRunId"] = Prop("Test run ID") },
                    "testRunId")),

            McpTool(
                "download_agent_channel_manifest",
                "Download the channel manifest package for an agent as a ZIP file. Use this to archive or inspect what an agent publishes to a channel. Only the M365 channel is currently supported.",
                AgentSchema(
                    new JObject
                    {
                        ["channelName"] = Prop(
                            "Channel to download. Only M365 is currently supported; omit to use it."),
                        ["includeAgentSchema"] = Prop(
                            "True to include the agent schema in the package. Omit to exclude it.",
                            "boolean")
                    })),

            // --- Administration ---
            McpTool(
                "get_quarantine_status",
                "Retrieve the quarantine status of a Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "quarantine_agent",
                "Quarantine a Copilot Studio agent, blocking end user access while leaving it available to makers and administrators.",
                AgentSchema()),

            McpTool(
                "unquarantine_agent",
                "Release a Copilot Studio agent from quarantine, restoring end user access.",
                AgentSchema()),

            McpTool(
                "get_connector_consent_bypass",
                "Get the admin connector consent bypass setting for a Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "set_connector_consent_bypass",
                "Set the admin connector consent bypass setting for a Copilot Studio agent. Enabling the bypass suppresses the end user connection consent prompt.",
                AgentSchema(
                    new JObject
                    {
                        ["adminConsentBypass"] = Prop("True to suppress the end user connection consent prompt, false to require consent.", "boolean")
                    },
                    "adminConsentBypass")),

            McpTool(
                "reassign_agent",
                "Reassign ownership of a Copilot Studio agent to a different Microsoft Entra user.",
                AgentSchema(
                    new JObject { ["newOwnerAadUserId"] = Prop("Microsoft Entra object ID of the new owner") },
                    "newOwnerAadUserId")),

            McpTool(
                "delete_agent",
                "Permanently delete a Copilot Studio agent. This cannot be undone, so confirm must be set to true.",
                AgentSchema(
                    new JObject { ["confirm"] = Prop("Must be true to acknowledge that deletion is permanent.", "boolean") },
                    "confirm")),

            // --- Entra Agent ID migration (preview) ---
            McpTool(
                "migrate_agent_identity",
                "Migrate a Copilot Studio agent from its legacy app-registration identity to a Microsoft Entra Agent ID. The application (client) ID is preserved, so channel registrations and connectors keep resolving. Returns status 'Migrated' or 'AlreadyMigrated'. Migrate in small batches and validate each agent before continuing. Preview feature.",
                AgentSchema()),

            McpTool(
                "rollback_agent_identity",
                "Revert a Copilot Studio agent from its Microsoft Entra Agent ID back to the legacy app-registration identity. Use this when a migrated agent fails validation. Returns status 'RolledBack' or 'NotMigrated'. Preview feature.",
                AgentSchema()),

            // --- Inventory and containment ---
            McpTool(
                "list_agents",
                "Inventory Copilot Studio agents with their harness. isCLIAgent is 'true' for the GitHub Copilot harness, 'false' for Standard or Copilot Chat, and 'unknown' when the platform did not report it. Omit environmentId to scan the whole tenant.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = Prop("Optional environment ID. Omit to inventory the whole tenant.")
                    }
                }),

            McpTool(
                "find_containment_candidates",
                "Read-only. Find GitHub Copilot harness agents that match containment criteria and return the reasons each qualified. Run this before contain_agents. Agents whose harness is unknown are excluded by default, because an absent field is not evidence of the expensive harness.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = Prop("Optional environment ID. Omit to scan the whole tenant."),
                        ["requireTenantWide"] = Prop("Only include agents shared with the entire tenant. Defaults to true.", "boolean"),
                        ["requireNeverPublished"] = Prop("Only include agents that have never been published. Defaults to false.", "boolean"),
                        ["includeUnknownHarness"] = Prop("Include agents whose harness the platform did not report. Defaults to false.", "boolean")
                    }
                }),

            McpTool(
                "contain_agents",
                "Quarantine an explicit list of agents, blocking end user access while leaving them editable by makers and administrators. Takes no filter by design: run find_containment_candidates first and pass the agents you decided to contain. Requires confirm.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["agents"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "The specific agents to quarantine.",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["environmentId"] = Prop("Environment ID"),
                                    ["botId"] = Prop("Agent (bot) ID")
                                },
                                ["required"] = new JArray { "environmentId", "botId" }
                            }
                        },
                        ["confirm"] = Prop("Must be true to acknowledge that this blocks end user access.", "boolean")
                    },
                    ["required"] = new JArray { "agents", "confirm" }
                })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject Prop(string description, string type = "string")
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    /// <summary>
    /// Builds a tool input schema that always requires environmentId and botId,
    /// plus any tool-specific properties and their required keys.
    /// </summary>
    private static JObject AgentSchema(JObject extraProperties = null, params string[] extraRequired)
    {
        var properties = new JObject
        {
            ["environmentId"] = Prop("Environment ID"),
            ["botId"] = Prop("Agent (bot) ID")
        };

        if (extraProperties != null)
        {
            foreach (var p in extraProperties.Properties())
            {
                properties[p.Name] = p.Value;
            }
        }

        var required = new JArray { "environmentId", "botId" };
        if (extraRequired != null)
        {
            foreach (var key in extraRequired)
            {
                required.Add(key);
            }
        }

        return new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private JObject McpTool(string name, string description, JObject inputSchema)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params");
        }

        var toolName = paramsObj.Value<string>("name");
        var args = paramsObj["arguments"] as JObject ?? new JObject();

        try
        {
            // Binary downloads are handled separately so the ZIP payload is never read as text.
            if (string.Equals(toolName, "download_evaluation_snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleSnapshotDownloadAsync(args, requestId).ConfigureAwait(false);
            }

            if (string.Equals(toolName, "download_agent_channel_manifest", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleChannelManifestDownloadAsync(args, requestId).ConfigureAwait(false);
            }

            // These tools compose several calls, so they return a built result
            // rather than proxying one backend response.
            switch ((toolName ?? string.Empty).ToLowerInvariant())
            {
                case "list_agents":
                    return await CompositeResultAsync(
                        requestId, toolName,
                        FetchAgentsAsync(args.Value<string>("environmentId"))).ConfigureAwait(false);

                case "find_containment_candidates":
                    return await CompositeResultAsync(
                        requestId, toolName,
                        FindContainmentCandidatesAsync(args)).ConfigureAwait(false);

                case "contain_agents":
                    return await CompositeResultAsync(
                        requestId, toolName,
                        ContainAgentsAsync(args)).ConfigureAwait(false);
            }

            string endpoint;
            HttpMethod httpMethod;
            JObject requestBody = null;

            switch ((toolName ?? string.Empty).ToLowerInvariant())
            {
                case "get_test_sets":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/makerevaluation/testsets");
                    break;

                case "get_test_set_details":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testSetId" },
                        BotPath + "/api/makerevaluation/testsets/{testSetId}");
                    break;

                case "start_evaluation":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testSetId" },
                        BotPath + "/api/makerevaluation/testsets/{testSetId}/run");
                    requestBody = new JObject();
                    if (!string.IsNullOrWhiteSpace(args.Value<string>("mcsConnectionId")))
                        requestBody["mcsConnectionId"] = args.Value<string>("mcsConnectionId");
                    if (args["runOnPublishedBot"] != null)
                        requestBody["RunOnPublishedBot"] = ToBoolean(args["runOnPublishedBot"], "runOnPublishedBot");
                    if (!string.IsNullOrWhiteSpace(args.Value<string>("evaluationRunName")))
                        requestBody["EvaluationRunName"] = args.Value<string>("evaluationRunName");
                    break;

                case "list_test_runs":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/makerevaluation/testruns");
                    break;

                case "get_run_details":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testRunId" },
                        BotPath + "/api/makerevaluation/testruns/{testRunId}");
                    break;

                case "get_quarantine_status":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine");
                    break;

                case "quarantine_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine/SetAsQuarantined");
                    break;

                case "unquarantine_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine/SetAsUnquarantined");
                    break;

                case "get_connector_consent_bypass":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/connectorConsentBypass");
                    break;

                case "set_connector_consent_bypass":
                    if (args["adminConsentBypass"] == null)
                    {
                        throw new ArgumentException("Missing required argument: adminConsentBypass");
                    }

                    httpMethod = HttpMethod.Put;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/connectorConsentBypass");
                    requestBody = new JObject
                    {
                        ["adminConsentBypass"] = ToBoolean(args["adminConsentBypass"], "adminConsentBypass")
                    };
                    break;

                case "reassign_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "newOwnerAadUserId" },
                        BotPath + "/api/botAdminOperations/reassign");
                    requestBody = new JObject
                    {
                        ["NewOwnerAadUserId"] = args.Value<string>("newOwnerAadUserId")
                    };
                    break;

                case "delete_agent":
                    if (!ToBoolean(args["confirm"], "confirm"))
                    {
                        throw new ArgumentException("Deleting an agent is permanent. Set confirm to true to proceed.");
                    }

                    httpMethod = HttpMethod.Delete;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botAdminOperations");
                    break;

                case "migrate_agent_identity":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/agentidentitymigration/migrate");
                    break;

                case "rollback_agent_identity":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/agentidentitymigration/rollback");
                    break;

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32602, $"Unknown tool: {toolName}");
            }

            var response = await InvokeBackendAsync(httpMethod, endpoint, requestBody).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JToken parsed;
            if (string.IsNullOrWhiteSpace(content))
            {
                // Operations such as reassign return 204 No Content on success.
                parsed = new JObject
                {
                    ["status"] = (int)response.StatusCode,
                    ["succeeded"] = response.IsSuccessStatusCode
                };
            }
            else
            {
                try { parsed = JToken.Parse(content); }
                catch { parsed = new JValue(content); }
            }

            await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
            {
                { "tool", toolName ?? string.Empty },
                { "status", ((int)response.StatusCode).ToString() }
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = parsed.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = !response.IsSuccessStatusCode
            });
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync(ex, "HandleToolsCallAsync").ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    /// <summary>
    /// Wraps a composed (multi-call) tool result in the MCP content envelope.
    /// Partial batch failures are reported inside the payload, so isError is reserved
    /// for the case where nothing succeeded at all.
    /// </summary>
    private async Task<HttpResponseMessage> CompositeResultAsync(
        JToken requestId, string toolName, Task<JObject> work)
    {
        var result = await work.ConfigureAwait(false);

        var failedAll = result["requested"] != null
            && result.Value<int>("requested") > 0
            && result.Value<int>("succeeded") == 0;

        await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
        {
            { "tool", toolName ?? string.Empty },
            { "status", failedAll ? "failed" : "ok" }
        }).ConfigureAwait(false);

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            },
            ["isError"] = failedAll
        });
    }

    private async Task<HttpResponseMessage> HandleSnapshotDownloadAsync(JObject args, JToken requestId)
    {
        var endpoint = BuildEndpoint(args,
            new[] { "environmentId", "botId", "testRunId" },
            BotPath + "/api/makerevaluation/testruns/{testRunId}/snapshot");

        return await HandleBinaryDownloadAsync(
            requestId,
            endpoint,
            toolName: "download_evaluation_snapshot",
            fallbackFileName: "evaluation-snapshot-" + args.Value<string>("testRunId") + ".zip",
            resourceUriPrefix: "copilotstudio://evaluation-snapshot/",
            oversizeHint: "Use the Download Agent Evaluation Snapshot REST operation in a flow to save the file to storage.",
            extraTelemetry: null).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleChannelManifestDownloadAsync(JObject args, JToken requestId)
    {
        var channelName = ResolveAgentChannel(args["channelName"]);

        // BuildEndpoint substitutes from args, so the resolved default has to be written
        // back before the template is expanded.
        var callArgs = (JObject)args.DeepClone();
        callArgs["channelName"] = channelName;

        var optionalQuery = new Dictionary<string, string>();
        var includeSchema = args["includeAgentSchema"];
        if (includeSchema != null && includeSchema.Type != JTokenType.Null)
        {
            optionalQuery["includeAgentSchema"] =
                ToBoolean(includeSchema, "includeAgentSchema") ? "true" : "false";
        }

        var endpoint = BuildEndpoint(callArgs,
            new[] { "environmentId", "botId", "channelName" },
            AgentChannelDownloadPath,
            optionalQuery);

        return await HandleBinaryDownloadAsync(
            requestId,
            endpoint,
            toolName: "download_agent_channel_manifest",
            fallbackFileName: "agent-channel-manifest-" + channelName + ".zip",
            resourceUriPrefix: "copilotstudio://agent-channel-manifest/",
            oversizeHint: "Use the Download Agent Channel Manifest REST operation in a flow to save the file to storage.",
            extraTelemetry: new Dictionary<string, string>
            {
                { "channel", channelName },
                { "includeAgentSchema", optionalQuery.ContainsKey("includeAgentSchema")
                    ? optionalQuery["includeAgentSchema"]
                    : "unset" }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared ZIP retrieval for every binary tool. Reads the payload as bytes so the
    /// archive is never corrupted by text decoding, and keeps the inline size ceiling
    /// in one place.
    /// </summary>
    private async Task<HttpResponseMessage> HandleBinaryDownloadAsync(
        JToken requestId,
        string endpoint,
        string toolName,
        string fallbackFileName,
        string resourceUriPrefix,
        string oversizeHint,
        IDictionary<string, string> extraTelemetry)
    {
        var response = await InvokeBackendAsync(HttpMethod.Get, endpoint, null).ConfigureAwait(false);

        var telemetry = new Dictionary<string, string>
        {
            { "tool", toolName },
            { "status", ((int)response.StatusCode).ToString() }
        };

        if (extraTelemetry != null)
        {
            foreach (var kvp in extraTelemetry) telemetry[kvp.Key] = kvp.Value;
        }

        await LogToAppInsightsAsync("MCP_ToolCall", telemetry).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Download failed with status {(int)response.StatusCode}. {errorText}"
                    }
                },
                ["isError"] = true
            });
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var fileName = GetDownloadFileName(response, fallbackFileName);

        var summary = new JObject
        {
            ["fileName"] = fileName,
            ["mimeType"] = "application/zip",
            ["sizeBytes"] = bytes.Length
        };

        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = summary.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };

        if (bytes.Length <= MaxInlineSnapshotBytes)
        {
            content.Add(new JObject
            {
                ["type"] = "resource",
                ["resource"] = new JObject
                {
                    ["uri"] = resourceUriPrefix + Uri.EscapeDataString(fileName),
                    ["mimeType"] = "application/zip",
                    ["blob"] = Convert.ToBase64String(bytes)
                }
            });
        }
        else
        {
            content.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = $"The file is {bytes.Length} bytes, which exceeds the {MaxInlineSnapshotBytes} byte inline limit. {oversizeHint}"
            });
        }

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = content,
            ["isError"] = false
        });
    }

    private static string ResolveAgentChannel(JToken value)
    {
        if (value == null || value.Type == JTokenType.Null) return DefaultAgentChannel;

        var requested = value.ToString().Trim();
        if (requested.Length == 0) return DefaultAgentChannel;

        foreach (var supported in SupportedAgentChannels)
        {
            if (string.Equals(supported, requested, StringComparison.OrdinalIgnoreCase)) return supported;
        }

        throw new ArgumentException(
            $"Channel '{requested}' is not supported. Microsoft documents only " +
            string.Join(", ", SupportedAgentChannels) +
            ". An undocumented channel has no defined error response, so it is not sent.");
    }

    /// <summary>
    /// Resolves the download file name from Content-Disposition, falling back to a
    /// generated name. The header is attacker-influenced service data that ends up in
    /// an MCP resource URI, so it is reduced to a bare, bounded file name rather than
    /// trusted as-is.
    /// </summary>
    private static string GetDownloadFileName(HttpResponseMessage response, string fallbackFileName)
    {
        string candidate = null;

        try
        {
            var disposition = response.Content?.Headers?.ContentDisposition;
            candidate = disposition?.FileNameStar ?? disposition?.FileName;
        }
        catch
        {
            // A malformed header throws on parse; fall through to the generated name.
        }

        return SanitizeDownloadFileName(candidate, SanitizeDownloadFileName(fallbackFileName, "download.zip"));
    }

    private static string SanitizeDownloadFileName(string candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;

        var name = candidate.Trim().Trim('"').Trim();

        // Keep only the final segment so a traversal or absolute path cannot survive.
        var separator = name.LastIndexOfAny(new[] { '/', '\\' });
        if (separator >= 0) name = name.Substring(separator + 1);

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        var cleaned = builder.ToString().Trim().Trim('.');

        if (cleaned.Length == 0) return fallback;

        if (!cleaned.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) cleaned += ".zip";

        // Bound the length so a hostile header cannot produce an unwieldy resource URI.
        const int maxLength = 120;
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned.Substring(0, maxLength - 4).TrimEnd('.', ' ') + ".zip";
        }

        return cleaned;
    }

    private static bool ToBoolean(JToken value, string argumentName)
    {
        if (value == null || value.Type == JTokenType.Null)
        {
            throw new ArgumentException($"Missing required argument: {argumentName}");
        }

        if (value.Type == JTokenType.Boolean)
        {
            return value.Value<bool>();
        }

        bool parsedValue;
        if (bool.TryParse(value.ToString(), out parsedValue))
        {
            return parsedValue;
        }

        throw new ArgumentException($"Argument {argumentName} must be a boolean.");
    }

    private string BuildEndpoint(
        JObject args,
        string[] requiredKeys,
        string pathTemplate,
        IDictionary<string, string> optionalQuery = null)
    {
        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrWhiteSpace(args.Value<string>(key)))
            {
                throw new ArgumentException($"Missing required argument: {key}");
            }
        }

        var path = pathTemplate;
        foreach (var key in requiredKeys)
        {
            path = path.Replace("{" + key + "}", Uri.EscapeDataString(args.Value<string>(key)));
        }

        var queryParts = new List<string> { "api-version=" + ApiVersion };
        if (optionalQuery != null)
        {
            foreach (var kvp in optionalQuery)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    queryParts.Add(Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value));
                }
            }
        }

        return path + "?" + string.Join("&", queryParts);
    }

    private async Task<HttpResponseMessage> InvokeBackendAsync(HttpMethod method, string endpointWithQuery, JObject body)
    {
        return await CallPlatformApiAsync(method, BackendBaseUrl + endpointWithQuery, body).ConfigureAwait(false);
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = result
                }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JObject
                    {
                        ["code"] = code,
                        ["message"] = message
                    }
                }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static bool IsAppInsightsConfigured()
    {
        return APP_INSIGHTS_ENABLED
            && !string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            && !APP_INSIGHTS_KEY.Contains("INSERT_YOUR");
    }

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!IsAppInsightsConfigured())
            return;

        try
        {
            var payload = new
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
                        properties = properties ?? new Dictionary<string, string>()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail for telemetry
        }
    }

    private async Task LogExceptionToAppInsightsAsync(Exception ex, string operation)
    {
        if (!IsAppInsightsConfigured())
            return;

        try
        {
            var payload = new
            {
                name = "Microsoft.ApplicationInsights.Exception",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "ExceptionData",
                    baseData = new
                    {
                        ver = 2,
                        exceptions = new[]
                        {
                            new
                            {
                                typeName = ex.GetType().Name,
                                message = ex.Message,
                                hasFullStack = true,
                                stack = ex.StackTrace
                            }
                        },
                        severityLevel = 3,
                        properties = new Dictionary<string, string>
                        {
                            { "operation", operation },
                            { "connector", "Copilot Studio Bots" }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail for telemetry
        }
    }
}
