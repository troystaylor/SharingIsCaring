using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string SERVER_NAME = "onetrust-mcp";
    private const string VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-12-01";
    private const string BASE_URL = "https://app.onetrust.com/api";
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "createRisk",
            ["description"] = "Create a new risk in the OneTrust Risk Register. Use when the user wants to log, report, or create a risk entry.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the risk (max 300 characters)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the risk (max 4000 characters)"
                    },
                    ["orgGroupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Organization group identifier (UUID)"
                    },
                    ["treatmentPlan"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Treatment plan for the risk (max 4000 characters)"
                    },
                    ["treatment"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Remediation details for the risk (max 4000 characters)"
                    },
                    ["deadline"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Deadline for the risk in ISO 8601 format (YYYY-MM-DDTHH:MM:SS.FFFZ)"
                    },
                    ["reminderDays"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Number of days before the deadline to send a reminder"
                    },
                    ["threatId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Risk threat identifier (UUID)"
                    },
                    ["categoryIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "List of risk category identifiers (UUIDs)"
                    }
                },
                ["required"] = new JArray { "orgGroupId" }
            }
        },
        new JObject
        {
            ["name"] = "createIncident",
            ["description"] = "Create a new incident in the OneTrust Incident Register. Use when the user wants to report, log, or create a security or privacy incident.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the incident"
                    },
                    ["incidentTypeName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Type of incident (defaults to 'Unknown')"
                    },
                    ["orgGroupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Organization group identifier (UUID)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Brief description of the incident"
                    },
                    ["dateOccurred"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Date and time the incident occurred (ISO 8601 UTC)"
                    },
                    ["dateDiscovered"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Date and time the incident was discovered (ISO 8601 UTC)"
                    },
                    ["deadline"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Investigation or notification deadline (ISO 8601 UTC)"
                    },
                    ["sourceType"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Creation source: MANUAL, ASSESSMENT, WEBFORM, or INTEGRATION (defaults to INTEGRATION)"
                    },
                    ["rootCause"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Root cause summary of the incident"
                    },
                    ["notificationNeeded"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Whether notification is required: YES, NO, or UNKNOWN (defaults to UNKNOWN)"
                    },
                    ["autoAssessJurisdictions"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "If true, automatically assess jurisdictions after creation"
                    }
                },
                ["required"] = new JArray { "name", "orgGroupId" }
            }
        },
        new JObject
        {
            ["name"] = "getIncident",
            ["description"] = "Get details of a specific incident by its identifier.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["incidentId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the incident"
                    }
                },
                ["required"] = new JArray { "incidentId" }
            }
        },
        new JObject
        {
            ["name"] = "searchIncidents",
            ["description"] = "Search for incidents using filters and full-text search. Returns a paginated list.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["fullText"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full-text search across name, description, type, assignee, org group, root cause, creator, and stage"
                    },
                    ["page"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Page number (zero-based, default 0)"
                    },
                    ["size"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Results per page (1-2000, default 20)"
                    },
                    ["sort"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Sort: createdDate,asc | createdDate,desc | name,asc | name,desc"
                    }
                }
            }
        },
        new JObject
        {
            ["name"] = "getOrganizations",
            ["description"] = "Get the hierarchical list of organizations. Use to look up Organization Group IDs needed for creating risks and incidents.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }
        },
        new JObject
        {
            ["name"] = "upsertRisk",
            ["description"] = "Create or update a risk based on matching attributes. If a risk with matching attributes exists it will be updated, otherwise created.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Type of the risk"
                    },
                    ["source"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Source of the risk"
                    },
                    ["orgGroupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Organization group identifier (UUID)"
                    },
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the risk (max 300 characters)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the risk (max 4000 characters)"
                    },
                    ["matchAttributes"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Comma-separated field names to match on for upsert (e.g., 'name,orgGroupId')"
                    }
                },
                ["required"] = new JArray { "type", "source", "orgGroupId" }
            }
        },
        new JObject
        {
            ["name"] = "updateIncident",
            ["description"] = "Update an existing incident in the Incident Register.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["incidentId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the incident to update"
                    },
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated name of the incident"
                    },
                    ["incidentTypeName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated incident type name"
                    },
                    ["orgGroupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Organization group identifier (UUID)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated description"
                    },
                    ["rootCause"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated root cause summary"
                    }
                },
                ["required"] = new JArray { "incidentId", "name", "incidentTypeName", "orgGroupId" }
            }
        },
        new JObject
        {
            ["name"] = "updateIncidentStage",
            ["description"] = "Move an incident to a different workflow stage by stage name.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entityId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The incident entity identifier (UUID)"
                    },
                    ["nextStageName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The name of the next workflow stage"
                    },
                    ["comment"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional comment for the stage transition"
                    }
                },
                ["required"] = new JArray { "entityId", "nextStageName" }
            }
        },
        new JObject
        {
            ["name"] = "updateRisk",
            ["description"] = "Fully update an existing risk. Requires the result/disposition field.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["riskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the risk to update"
                    },
                    ["result"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Risk result: Accepted, Avoided, Reduced, Rejected, Transferred, or Ignored"
                    },
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated name (max 300 characters)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated description (max 4000 characters)"
                    },
                    ["treatment"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated remediation details (max 4000 characters)"
                    },
                    ["deadline"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated deadline in ISO 8601 format"
                    }
                },
                ["required"] = new JArray { "riskId", "result" }
            }
        },
        new JObject
        {
            ["name"] = "deleteRisk",
            ["description"] = "Delete a risk from the Risk Register by its identifier.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["riskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the risk to delete"
                    }
                },
                ["required"] = new JArray { "riskId" }
            }
        },
        new JObject
        {
            ["name"] = "updateRiskStage",
            ["description"] = "Move a risk to a different workflow stage.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["riskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the risk"
                    },
                    ["direction"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Direction: First, Next, Previous, Last, or Specific"
                    },
                    ["nextStageId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Target stage identifier (required when direction is Specific)"
                    },
                    ["comment"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional comment for the stage transition"
                    }
                },
                ["required"] = new JArray { "riskId", "direction" }
            }
        },
        new JObject
        {
            ["name"] = "linkIncidentToInventory",
            ["description"] = "Link an incident to inventory items such as assets, processing activities, vendors, or entities.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["incidentId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the incident"
                    },
                    ["inventoryType"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Type: assets, processing-activities, vendors, or entities"
                    },
                    ["inventoryIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "List of inventory item identifiers (UUIDs) to link"
                    }
                },
                ["required"] = new JArray { "incidentId", "inventoryType", "inventoryIds" }
            }
        },
        new JObject
        {
            ["name"] = "getRiskTemplate",
            ["description"] = "Get details of a specific risk template by its identifier.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["riskTemplateId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the risk template"
                    }
                },
                ["required"] = new JArray { "riskTemplateId" }
            }
        },
        new JObject
        {
            ["name"] = "modifyRisk",
            ["description"] = "Partially update specific fields of an existing risk. Only provided fields will be modified.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["riskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (UUID) of the risk to modify"
                    },
                    ["name"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated name (max 300 characters)"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated description (max 4000 characters)"
                    },
                    ["deadline"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Updated deadline in ISO 8601 format"
                    },
                    ["reminderDays"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Updated reminder days before deadline"
                    }
                },
                ["required"] = new JArray { "riskId" }
            }
        }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            this.Context.Logger.LogInformation($"Request received. CorrelationId: {correlationId}, OperationId: {this.Context.OperationId}");

            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                Path = this.Context.Request.RequestUri.AbsolutePath,
                Method = this.Context.Request.Method.Method
            });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMCPProtocolAsync(correlationId).ConfigureAwait(false);

                case "CreateRisk":
                case "CreateIncident":
                case "GetIncident":
                case "SearchIncidents":
                case "GetOrganizations":
                case "UpsertRisk":
                case "UpdateIncident":
                case "UpdateIncidentStage":
                case "UpdateRisk":
                case "DeleteRisk":
                case "UpdateRiskStage":
                case "LinkIncidentToInventory":
                case "GetRiskTemplate":
                case "ModifyRisk":
                    return await HandlePassthroughAsync(correlationId).ConfigureAwait(false);

                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"CorrelationId: {correlationId}, Error: {ex.Message}");

            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });

            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"Request completed. CorrelationId: {correlationId}, Duration: {duration.TotalMilliseconds}ms");

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                DurationMs = duration.TotalMilliseconds,
                OperationId = this.Context.OperationId
            });
        }
    }

    // ========================================
    // PASSTHROUGH HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandlePassthroughAsync(string correlationId)
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

        await LogToAppInsights("PassthroughCompleted", new
        {
            CorrelationId = correlationId,
            OperationId = this.Context.OperationId,
            StatusCode = (int)response.StatusCode
        });

        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMCPProtocolAsync(string correlationId)
    {
        try
        {
            var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var requestObj = JObject.Parse(requestBody);

            var method = requestObj["method"]?.ToString();
            var id = requestObj["id"];

            this.Context.Logger.LogInformation($"MCP method: {method}, CorrelationId: {correlationId}");

            await LogToAppInsights("MCPMethodInvoked", new
            {
                CorrelationId = correlationId,
                Method = method
            });

            return method switch
            {
                "initialize" => CreateMCPSuccessResponse(new JObject
                {
                    ["protocolVersion"] = PROTOCOL_VERSION,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = SERVER_NAME,
                        ["version"] = VERSION
                    }
                }, id),

                "initialized" or
                "notifications/initialized" or
                "notifications/cancelled" => CreateMCPSuccessResponse(new JObject(), id),

                "tools/list" => CreateMCPSuccessResponse(new JObject { ["tools"] = AVAILABLE_TOOLS }, id),

                "tools/call" => await HandleToolsCallAsync(requestObj, id, correlationId).ConfigureAwait(false),

                "resources/list" => CreateMCPSuccessResponse(new JObject { ["resources"] = new JArray() }, id),

                "ping" => CreateMCPSuccessResponse(new JObject(), id),

                _ => CreateMCPErrorResponse(-32601, "Method not found", method, id)
            };
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"MCP error: {ex.Message}, CorrelationId: {correlationId}");

            await LogToAppInsights("MCPError", new
            {
                CorrelationId = correlationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });

            return CreateMCPErrorResponse(-32603, $"Internal error: {ex.Message}", null, null);
        }
    }

    // ========================================
    // MCP TOOLS/CALL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject requestObj, JToken id, string correlationId)
    {
        var toolName = requestObj["params"]?["name"]?.ToString();
        var args = requestObj["params"]?["arguments"] as JObject ?? new JObject();

        this.Context.Logger.LogInformation($"Tool call: {toolName}, CorrelationId: {correlationId}");

        await LogToAppInsights("ToolCallStarted", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            string result = toolName switch
            {
                "createRisk" => await ExecuteCreateRiskAsync(args).ConfigureAwait(false),
                "createIncident" => await ExecuteCreateIncidentAsync(args).ConfigureAwait(false),
                "getIncident" => await ExecuteGetIncidentAsync(args).ConfigureAwait(false),
                "searchIncidents" => await ExecuteSearchIncidentsAsync(args).ConfigureAwait(false),
                "getOrganizations" => await ExecuteGetOrganizationsAsync().ConfigureAwait(false),
                "upsertRisk" => await ExecuteUpsertRiskAsync(args).ConfigureAwait(false),
                "updateIncident" => await ExecuteUpdateIncidentAsync(args).ConfigureAwait(false),
                "updateIncidentStage" => await ExecuteUpdateIncidentStageAsync(args).ConfigureAwait(false),
                "updateRisk" => await ExecuteUpdateRiskAsync(args).ConfigureAwait(false),
                "deleteRisk" => await ExecuteDeleteRiskAsync(args).ConfigureAwait(false),
                "updateRiskStage" => await ExecuteUpdateRiskStageAsync(args).ConfigureAwait(false),
                "linkIncidentToInventory" => await ExecuteLinkIncidentToInventoryAsync(args).ConfigureAwait(false),
                "getRiskTemplate" => await ExecuteGetRiskTemplateAsync(args).ConfigureAwait(false),
                "modifyRisk" => await ExecuteModifyRiskAsync(args).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown tool: {toolName}")
            };

            await LogToAppInsights("ToolCallCompleted", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                Success = true
            });

            return CreateMCPSuccessResponse(new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result
                    }
                },
                ["isError"] = false
            }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Tool error: {toolName}, Error: {ex.Message}, CorrelationId: {correlationId}");

            await LogToAppInsights("ToolCallFailed", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                ErrorMessage = ex.Message
            });

            return CreateMCPSuccessResponse(new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            }, id);
        }
    }

    // ========================================
    // TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<string> ExecuteCreateRiskAsync(JObject args)
    {
        var body = new JObject();

        if (args["name"] != null) body["name"] = args["name"].ToString();
        if (args["description"] != null) body["description"] = args["description"].ToString();
        body["orgGroupId"] = args["orgGroupId"]?.ToString() ?? throw new ArgumentException("orgGroupId is required");
        if (args["treatmentPlan"] != null) body["treatmentPlan"] = args["treatmentPlan"].ToString();
        if (args["treatment"] != null) body["treatment"] = args["treatment"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        if (args["reminderDays"] != null) body["reminderDays"] = args["reminderDays"].Value<long>();
        if (args["threatId"] != null) body["threatId"] = args["threatId"].ToString();
        if (args["categoryIds"] != null) body["categoryIds"] = args["categoryIds"];

        var response = await MakeOneTrustRequestAsync("POST", "/risk/v3/risks", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
        return response;
    }

    private async Task<string> ExecuteCreateIncidentAsync(JObject args)
    {
        var body = new JObject();

        body["name"] = args["name"]?.ToString() ?? throw new ArgumentException("name is required");
        body["incidentTypeName"] = args["incidentTypeName"]?.ToString() ?? "Unknown";
        body["orgGroupId"] = args["orgGroupId"]?.ToString() ?? throw new ArgumentException("orgGroupId is required");
        if (args["description"] != null) body["description"] = args["description"].ToString();
        if (args["dateOccurred"] != null) body["dateOccurred"] = args["dateOccurred"].ToString();
        if (args["dateDiscovered"] != null) body["dateDiscovered"] = args["dateDiscovered"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        body["sourceType"] = args["sourceType"]?.ToString() ?? "INTEGRATION";
        if (args["rootCause"] != null) body["rootCause"] = args["rootCause"].ToString();
        body["notificationNeeded"] = args["notificationNeeded"]?.ToString() ?? "UNKNOWN";
        if (args["autoAssessJurisdictions"] != null) body["autoAssessJurisdictions"] = args["autoAssessJurisdictions"].Value<bool>();

        var response = await MakeOneTrustRequestAsync("POST", "/incident/v1/incidents", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
        return response;
    }

    private async Task<string> ExecuteGetIncidentAsync(JObject args)
    {
        var incidentId = args["incidentId"]?.ToString() ?? throw new ArgumentException("incidentId is required");
        return await MakeOneTrustRequestAsync("GET", $"/incident/v1/incidents/{incidentId}", null).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSearchIncidentsAsync(JObject args)
    {
        var queryParams = new List<string>();
        if (args["page"] != null) queryParams.Add($"page={args["page"].Value<int>()}");
        if (args["size"] != null) queryParams.Add($"size={args["size"].Value<int>()}");
        if (args["sort"] != null) queryParams.Add($"sort={Uri.EscapeDataString(args["sort"].ToString())}");
        var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";

        var body = new JObject();
        if (args["fullText"] != null) body["fullText"] = args["fullText"].ToString();
        if (args["filters"] != null) body["filters"] = args["filters"];

        return await MakeOneTrustRequestAsync("POST", $"/incident/v1/incidents/search{queryString}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGetOrganizationsAsync()
    {
        return await MakeOneTrustRequestAsync("GET", "/access/v1/external/organizations", null).ConfigureAwait(false);
    }

    private async Task<string> ExecuteUpsertRiskAsync(JObject args)
    {
        var queryString = "";
        if (args["matchAttributes"] != null) queryString = $"?matchAttributes={Uri.EscapeDataString(args["matchAttributes"].ToString())}";

        var body = new JObject();
        body["type"] = args["type"]?.ToString() ?? throw new ArgumentException("type is required");
        body["source"] = args["source"]?.ToString() ?? throw new ArgumentException("source is required");
        body["orgGroupId"] = args["orgGroupId"]?.ToString() ?? throw new ArgumentException("orgGroupId is required");
        if (args["name"] != null) body["name"] = args["name"].ToString();
        if (args["description"] != null) body["description"] = args["description"].ToString();
        if (args["treatmentPlan"] != null) body["treatmentPlan"] = args["treatmentPlan"].ToString();
        if (args["treatment"] != null) body["treatment"] = args["treatment"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        if (args["categoryIds"] != null) body["categoryIds"] = args["categoryIds"];

        return await MakeOneTrustRequestAsync("PUT", $"/risk/v2/risks/upsert{queryString}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteUpdateIncidentAsync(JObject args)
    {
        var incidentId = args["incidentId"]?.ToString() ?? throw new ArgumentException("incidentId is required");

        var body = new JObject();
        body["name"] = args["name"]?.ToString() ?? throw new ArgumentException("name is required");
        body["incidentTypeName"] = args["incidentTypeName"]?.ToString() ?? "Unknown";
        body["orgGroupId"] = args["orgGroupId"]?.ToString() ?? throw new ArgumentException("orgGroupId is required");
        if (args["description"] != null) body["description"] = args["description"].ToString();
        if (args["dateOccurred"] != null) body["dateOccurred"] = args["dateOccurred"].ToString();
        if (args["dateDiscovered"] != null) body["dateDiscovered"] = args["dateDiscovered"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        if (args["rootCause"] != null) body["rootCause"] = args["rootCause"].ToString();
        if (args["sourceType"] != null) body["sourceType"] = args["sourceType"].ToString();
        if (args["notificationNeeded"] != null) body["notificationNeeded"] = args["notificationNeeded"].ToString();

        return await MakeOneTrustRequestAsync("PUT", $"/incident/v1/incidents/{incidentId}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteUpdateIncidentStageAsync(JObject args)
    {
        var entityId = args["entityId"]?.ToString() ?? throw new ArgumentException("entityId is required");

        var body = new JObject();
        body["nextStageName"] = args["nextStageName"]?.ToString() ?? throw new ArgumentException("nextStageName is required");
        if (args["comment"] != null)
        {
            body["parameters"] = new JObject { ["comment"] = args["comment"].ToString() };
        }

        return await MakeOneTrustRequestAsync("POST", $"/incident/v1/assignments/entities/{entityId}/stage", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteUpdateRiskAsync(JObject args)
    {
        var riskId = args["riskId"]?.ToString() ?? throw new ArgumentException("riskId is required");

        var body = new JObject();
        body["result"] = args["result"]?.ToString() ?? throw new ArgumentException("result is required");
        if (args["name"] != null) body["name"] = args["name"].ToString();
        if (args["description"] != null) body["description"] = args["description"].ToString();
        if (args["orgGroupId"] != null) body["orgGroupId"] = args["orgGroupId"].ToString();
        if (args["treatment"] != null) body["treatment"] = args["treatment"].ToString();
        if (args["treatmentPlan"] != null) body["treatmentPlan"] = args["treatmentPlan"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        if (args["reminderDays"] != null) body["reminderDays"] = args["reminderDays"].Value<long>();
        if (args["threatId"] != null) body["threatId"] = args["threatId"].ToString();
        if (args["categoryIds"] != null) body["categoryIds"] = args["categoryIds"];

        return await MakeOneTrustRequestAsync("PUT", $"/risk/v2/risks/{riskId}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteDeleteRiskAsync(JObject args)
    {
        var riskId = args["riskId"]?.ToString() ?? throw new ArgumentException("riskId is required");
        return await MakeOneTrustRequestAsync("DELETE", $"/risk/v2/risks/{riskId}", null).ConfigureAwait(false);
    }

    private async Task<string> ExecuteUpdateRiskStageAsync(JObject args)
    {
        var riskId = args["riskId"]?.ToString() ?? throw new ArgumentException("riskId is required");

        var body = new JObject();
        body["direction"] = args["direction"]?.ToString() ?? throw new ArgumentException("direction is required");
        if (args["nextStageId"] != null) body["nextStageId"] = args["nextStageId"].ToString();
        if (args["comment"] != null)
        {
            body["parameters"] = new JObject { ["comment"] = args["comment"].ToString() };
        }

        return await MakeOneTrustRequestAsync("POST", $"/risk/v2/risks/{riskId}/assign-stage", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteLinkIncidentToInventoryAsync(JObject args)
    {
        var incidentId = args["incidentId"]?.ToString() ?? throw new ArgumentException("incidentId is required");
        var inventoryType = args["inventoryType"]?.ToString() ?? throw new ArgumentException("inventoryType is required");

        var body = new JObject();
        body["inventoryIds"] = args["inventoryIds"] ?? throw new ArgumentException("inventoryIds is required");
        if (args["sourceType"] != null) body["sourceType"] = args["sourceType"].ToString();

        return await MakeOneTrustRequestAsync("POST", $"/incident/v1/incidents/{incidentId}/inventory-links/{inventoryType}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGetRiskTemplateAsync(JObject args)
    {
        var riskTemplateId = args["riskTemplateId"]?.ToString() ?? throw new ArgumentException("riskTemplateId is required");
        return await MakeOneTrustRequestAsync("GET", $"/risk-template/v1/templates/{riskTemplateId}", null).ConfigureAwait(false);
    }

    private async Task<string> ExecuteModifyRiskAsync(JObject args)
    {
        var riskId = args["riskId"]?.ToString() ?? throw new ArgumentException("riskId is required");

        var body = new JObject();
        if (args["name"] != null) body["name"] = args["name"].ToString();
        if (args["description"] != null) body["description"] = args["description"].ToString();
        if (args["deadline"] != null) body["deadline"] = args["deadline"].ToString();
        if (args["reminderDays"] != null) body["reminderDays"] = args["reminderDays"].Value<long>();
        if (args["orgGroupId"] != null) body["orgGroupId"] = args["orgGroupId"].ToString();
        if (args["result"] != null) body["result"] = args["result"].ToString();
        if (args["treatment"] != null) body["treatment"] = args["treatment"].ToString();
        if (args["treatmentPlan"] != null) body["treatmentPlan"] = args["treatmentPlan"].ToString();

        return await MakeOneTrustRequestAsync("PATCH", $"/risk/v2/risks/{riskId}", body.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
    }

    // ========================================
    // HTTP CLIENT FOR ONETRUST API
    // ========================================

    private async Task<string> MakeOneTrustRequestAsync(string method, string relativeUrl, string body)
    {
        var url = $"{BASE_URL}{relativeUrl}";
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        // Copy auth header from the original request
        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OneTrust API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{{\"status\":{(int)response.StatusCode},\"message\":\"Success\"}}";
        }

        return content;
    }

    // ========================================
    // MCP JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateMCPSuccessResponse(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (id != null) response["id"] = id;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMCPErrorResponse(int code, string message, string data, JToken id)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error
        };
        if (id != null) response["id"] = id;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

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
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Telemetry error: {ex.Message}");
        }
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
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }
}
