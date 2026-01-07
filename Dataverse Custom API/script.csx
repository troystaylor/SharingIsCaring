using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Configuration
    private const bool INCLUDE_PRIVATE_APIS = true; // Set to true to include private Custom APIs
    private const string APP_INSIGHTS_CONNECTION_STRING = ""; // Application Insights connection string for telemetry
    
    // MCP Server metadata
    private const string SERVER_NAME = "dataverse-custom-api-mcp";
    private const string SERVER_VERSION = "1.0.0";
    
    // Cache for Custom API metadata (30 minute TTL)
    private static readonly Dictionary<string, CachedCustomAPIs> _customAPICache = new Dictionary<string, CachedCustomAPIs>();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(30);

    // Dataverse type to JSON Schema type mapping
    private static readonly Dictionary<int, string> TYPE_MAPPING = new Dictionary<int, string>
    {
        { 0, "boolean" },      // Boolean
        { 1, "string" },       // DateTime (ISO 8601 string)
        { 2, "number" },       // Decimal
        { 3, "object" },       // Entity
        { 4, "array" },        // EntityCollection
        { 5, "object" },       // EntityReference
        { 6, "number" },       // Float
        { 7, "integer" },      // Integer
        { 8, "number" },       // Money
        { 9, "integer" },      // Picklist
        { 10, "string" },      // String
        { 11, "array" },       // StringArray
        { 12, "string" }       // Guid (formatted as string)
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var startTime = DateTime.UtcNow;
        var correlationId = Guid.NewGuid().ToString();
        this.Context.Logger.LogInformation($"Dataverse Custom API MCP request received. CorrelationId: {correlationId}");

        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            // Log original request
            await LogToAppInsights("OriginalRequest", new { 
                CorrelationId = correlationId,
                RequestBody = body?.Substring(0, Math.Min(1000, body?.Length ?? 0)),
                UserAgent = this.Context.Request.Headers.UserAgent?.ToString()
            });
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateErrorResponse("Request body is required", 400);
            }

            var payload = JObject.Parse(body);

            // Route MCP protocol requests
            if (payload.ContainsKey("jsonrpc"))
            {
                return await HandleMCPRequest(payload).ConfigureAwait(false);
            }

            return CreateErrorResponse("Invalid request format", 400);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Error: {ex.Message}");
            return CreateErrorResponse($"Unexpected error: {ex.Message}", 500);
        }
    }

    private async Task<HttpResponseMessage> HandleMCPRequest(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];
        var paramsObj = request["params"] as JObject;

        this.Context.Logger.LogInformation($"MCP method: {method}");
        await LogToAppInsights("MCPMethodCall", new { Method = method, HasParams = paramsObj != null });

        switch (method)
        {
            case "initialize":
                // Standard MCP initialize - tools discovered via tools/list
                // Echo back client's requested protocol version (MCP best practice)
                var protocolVersion = paramsObj?["protocolVersion"]?.ToString() ?? "2024-11-05";
                
                var initResponse = new JObject
                {
                    ["protocolVersion"] = protocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject
                        {
                            ["listChanged"] = true  // Signal that tools list should be actively queried
                        }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = SERVER_NAME,
                        ["version"] = SERVER_VERSION
                    }
                };
                
                await LogToAppInsights("MCPInitialize", new { 
                    Message = "Initialize called - tools available via tools/list",
                    ProtocolVersion = protocolVersion,
                    ClientRequestedVersion = paramsObj?["protocolVersion"]?.ToString() ?? "none",
                    ResponsePreview = initResponse.ToString().Substring(0, Math.Min(500, initResponse.ToString().Length))
                });
                
                return CreateMCPSuccessResponse(initResponse, id);

            case "notifications/initialized":
                return new HttpResponseMessage(HttpStatusCode.OK);

            case "tools/list":
                var tools = await GetToolDefinitionsAsync().ConfigureAwait(false);
                await LogToAppInsights("ToolsListResult", new { ToolCount = tools.Count });
                return CreateMCPSuccessResponse(new JObject
                {
                    ["tools"] = tools
                }, id);

            case "tools/call":
                return await HandleToolsCall(paramsObj, id).ConfigureAwait(false);

            case "ping":
                return CreateMCPSuccessResponse(new JObject(), id);

            default:
                return CreateMCPErrorResponse(-32601, $"Method not found: {method}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject parms, JToken id)
    {
        try
        {
            var toolName = parms?["name"]?.ToString();
            var arguments = parms?["arguments"] as JObject ?? new JObject();

            this.Context.Logger.LogInformation($"Tool call: {toolName}");
            await LogToAppInsights("ToolCallReceived", new { ToolName = toolName, HasArguments = arguments.Count > 0 });

            // Route management tools
            if (!string.IsNullOrEmpty(toolName))
            {
                switch (toolName)
                {
                    case "dataverse_management_create_custom_api":
                        return await HandleCreateCustomAPI(arguments, id).ConfigureAwait(false);
                    case "dataverse_management_create_api_parameter":
                        return await HandleCreateAPIParameter(arguments, id).ConfigureAwait(false);
                    case "dataverse_management_create_api_property":
                        return await HandleCreateAPIProperty(arguments, id).ConfigureAwait(false);
                    case "dataverse_management_update_custom_api":
                        return await HandleUpdateCustomAPI(arguments, id).ConfigureAwait(false);
                    case "dataverse_management_delete_custom_api":
                        return await HandleDeleteCustomAPI(arguments, id).ConfigureAwait(false);
                    case "dataverse_management_list_solutions":
                        return await HandleListSolutions(arguments, id).ConfigureAwait(false);
                }
            }

            // Parse tool name to get Custom API unique name
            if (string.IsNullOrEmpty(toolName) || !toolName.StartsWith("dataverse_", StringComparison.OrdinalIgnoreCase))
            {
                return CreateMCPErrorResponse(-32602, "Invalid tool name format", id);
            }

            var uniqueName = toolName.Substring("dataverse_".Length);

            // Look up Custom API metadata
            var customAPIs = await DiscoverCustomAPIsAsync().ConfigureAwait(false);
            var apiMetadata = customAPIs.Find(api => api.UniqueName.Equals(uniqueName, StringComparison.OrdinalIgnoreCase));

            if (apiMetadata == null)
            {
                return CreateMCPErrorResponse(-32602, $"Custom API not found: {uniqueName}", id);
            }

            // Execute the Custom API
            var result = await ExecuteCustomAPIAsync(apiMetadata, arguments).ConfigureAwait(false);

            // Return MCP response
            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = result.ToString()
                }
            };

            return CreateMCPSuccessResponse(new JObject
            {
                ["content"] = content
            }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Tool execution error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Tool execution failed: {ex.Message}", id);
        }
    }

    private async Task<JArray> GetToolDefinitionsAsync()
    {
        try
        {
            var tools = new JArray();

            // Add management tools first
            tools.Add(GetManagementToolDefinition_CreateCustomAPI());
            tools.Add(GetManagementToolDefinition_CreateAPIParameter());
            tools.Add(GetManagementToolDefinition_CreateAPIProperty());
            tools.Add(GetManagementToolDefinition_UpdateCustomAPI());
            tools.Add(GetManagementToolDefinition_DeleteCustomAPI());
            tools.Add(GetManagementToolDefinition_ListSolutions());

            // Add discovered Custom API tools
            var customAPIs = await DiscoverCustomAPIsAsync().ConfigureAwait(false);
            foreach (var api in customAPIs)
            {
                tools.Add(GenerateMCPToolDefinition(api));
            }

            this.Context.Logger.LogInformation($"Discovered {customAPIs.Count} Custom API tools + {tools.Count - customAPIs.Count} management tools");
            return tools;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Error discovering Custom APIs: {ex.Message}");
            return new JArray();
        }
    }

    private async Task<List<CustomAPIMetadata>> DiscoverCustomAPIsAsync()
    {
        var environmentUrl = this.Context.Request.RequestUri.Host;

        // Check cache
        if (_customAPICache.TryGetValue(environmentUrl, out var cached))
        {
            if (DateTime.UtcNow - cached.Timestamp < CACHE_DURATION)
            {
                this.Context.Logger.LogInformation("Using cached Custom API metadata");
                return cached.APIs;
            }
        }

        this.Context.Logger.LogInformation("Fetching Custom API metadata from Dataverse");

        // Build query to retrieve CustomAPI records with expanded parameters and properties
        var query = "/api/data/v9.2/customapis?$select=" +
            "uniquename,displayname,description,bindingtype,boundentitylogicalname,isfunction,isprivate&" +
            "$expand=" +
            "CustomAPIRequestParameters($select=uniquename,name,displayname,description,type,logicalentityname,isoptional)," +
            "CustomAPIResponseProperties($select=uniquename,name,displayname,description,type,logicalentityname)";
        
        // Add filter for private APIs if not including them
        if (!INCLUDE_PRIVATE_APIS)
        {
            query += "&$filter=isprivate eq false";
        }

        var correlationId = Guid.NewGuid().ToString();
        var response = await SendDataverseRequestAsync("GET", query, null, correlationId).ConfigureAwait(false);

        if (response["error"] != null)
        {
            throw new Exception($"Error retrieving Custom APIs: {response["error"]["message"]}");
        }

        var apis = new List<CustomAPIMetadata>();
        var apiRecords = response["value"] as JArray ?? new JArray();

        foreach (JObject apiRecord in apiRecords)
        {
            var metadata = new CustomAPIMetadata
            {
                UniqueName = apiRecord["uniquename"]?.ToString(),
                DisplayName = apiRecord["displayname"]?.ToString(),
                Description = apiRecord["description"]?.ToString(),
                BindingType = apiRecord["bindingtype"]?.Value<int>() ?? 0,
                BoundEntityLogicalName = apiRecord["boundentitylogicalname"]?.ToString(),
                IsFunction = apiRecord["isfunction"]?.Value<bool>() ?? false,
                IsPrivate = apiRecord["isprivate"]?.Value<bool>() ?? false,
                RequestParameters = new List<CustomAPIParameter>(),
                ResponseProperties = new List<CustomAPIProperty>()
            };

            // Parse request parameters
            var requestParams = apiRecord["CustomAPIRequestParameters"] as JArray ?? new JArray();
            foreach (JObject param in requestParams)
            {
                metadata.RequestParameters.Add(new CustomAPIParameter
                {
                    UniqueName = param["uniquename"]?.ToString(),
                    Name = param["name"]?.ToString(),
                    DisplayName = param["displayname"]?.ToString(),
                    Description = param["description"]?.ToString(),
                    Type = param["type"]?.Value<int>() ?? 10,
                    LogicalEntityName = param["logicalentityname"]?.ToString(),
                    IsOptional = param["isoptional"]?.Value<bool>() ?? false
                });
            }

            // Parse response properties
            var responseProps = apiRecord["CustomAPIResponseProperties"] as JArray ?? new JArray();
            foreach (JObject prop in responseProps)
            {
                metadata.ResponseProperties.Add(new CustomAPIProperty
                {
                    UniqueName = prop["uniquename"]?.ToString(),
                    Name = prop["name"]?.ToString(),
                    DisplayName = prop["displayname"]?.ToString(),
                    Description = prop["description"]?.ToString(),
                    Type = prop["type"]?.Value<int>() ?? 10,
                    LogicalEntityName = prop["logicalentityname"]?.ToString()
                });
            }

            apis.Add(metadata);
        }

        // Cache the results
        _customAPICache[environmentUrl] = new CachedCustomAPIs
        {
            APIs = apis,
            Timestamp = DateTime.UtcNow
        };

        return apis;
    }

    private JObject GenerateMCPToolDefinition(CustomAPIMetadata api)
    {
        var tool = new JObject
        {
            ["name"] = $"dataverse_{api.UniqueName}",
            ["description"] = api.Description ?? api.DisplayName ?? api.UniqueName
        };

        // Add binding type info to description
        var bindingInfo = api.BindingType switch
        {
            0 => "Global unbound",
            1 => $"Bound to {api.BoundEntityLogicalName} entity",
            2 => $"Bound to {api.BoundEntityLogicalName} entity collection",
            _ => "Unknown binding"
        };
        tool["description"] = $"{tool["description"]} ({bindingInfo} {(api.IsFunction ? "Function" : "Action")})";

        // Build input schema
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject()
        };

        var required = new JArray();

        // Add Target parameter for Entity-bound APIs
        if (api.BindingType == 1)
        {
            inputSchema["properties"]["Target"] = new JObject
            {
                ["type"] = "string",
                ["description"] = $"GUID of the {api.BoundEntityLogicalName} record"
            };
            required.Add("Target");
        }

        // Add request parameters
        foreach (var param in api.RequestParameters)
        {
            var paramSchema = new JObject
            {
                ["type"] = GetJsonSchemaType(param.Type),
                ["description"] = param.Description ?? param.DisplayName ?? param.UniqueName
            };

            // Add format hints for specific types
            if (param.Type == 1) // DateTime
            {
                paramSchema["format"] = "date-time";
            }
            else if (param.Type == 12) // Guid
            {
                paramSchema["format"] = "uuid";
            }
            else if (param.Type == 5) // EntityReference
            {
                paramSchema["description"] = $"{paramSchema["description"]} (EntityReference with logicalName and id)";
            }
            else if (param.Type == 3) // Entity
            {
                if (string.IsNullOrEmpty(param.LogicalEntityName))
                {
                    paramSchema["description"] = $"{paramSchema["description"]} (Open type: accepts any entity with @odata.type and attributes)";
                }
                else
                {
                    paramSchema["description"] = $"{paramSchema["description"]} ({param.LogicalEntityName} entity)";
                }
            }
            else if (param.Type == 4) // EntityCollection
            {
                if (string.IsNullOrEmpty(param.LogicalEntityName))
                {
                    paramSchema["description"] = $"{paramSchema["description"]} (Open type: array of entities with @odata.type)";
                }
                else
                {
                    paramSchema["description"] = $"{paramSchema["description"]} (Collection of {param.LogicalEntityName} entities)";
                }
            }

            inputSchema["properties"][param.UniqueName] = paramSchema;

            if (!param.IsOptional)
            {
                required.Add(param.UniqueName);
            }
        }

        if (required.Count > 0)
        {
            inputSchema["required"] = required;
        }

        tool["inputSchema"] = inputSchema;

        return tool;
    }

    private string GetJsonSchemaType(int dataverseType)
    {
        if (TYPE_MAPPING.TryGetValue(dataverseType, out var jsonType))
        {
            return jsonType;
        }
        return "string"; // Default fallback
    }

    private async Task<JObject> ExecuteCustomAPIAsync(CustomAPIMetadata metadata, JObject arguments)
    {
        var correlationId = Guid.NewGuid().ToString();
        this.Context.Logger.LogInformation($"Executing Custom API: {metadata.UniqueName}. CorrelationId: {correlationId}");
        
        // Build request URL
        var url = BuildCustomAPIUrl(metadata, arguments);
        
        // Determine HTTP method
        var httpMethod = metadata.IsFunction ? "GET" : "POST";
        
        JObject requestBody = null;
        
        if (metadata.IsFunction)
        {
            // Functions: Add parameters to URL query string
            url = AppendFunctionParameters(url, metadata, arguments);
        }
        else
        {
            // Actions: Add parameters to request body
            requestBody = FormatActionParameters(metadata, arguments);
        }
        
        // Execute request
        var response = await SendDataverseRequestAsync(httpMethod, url, requestBody, correlationId).ConfigureAwait(false);
        
        if (response["error"] != null)
        {
            throw new Exception($"Custom API execution failed: {response["error"]["message"]}");
        }
        
        // Parse response properties
        return ParseCustomAPIResponse(metadata, response);
    }

    private string BuildCustomAPIUrl(CustomAPIMetadata metadata, JObject arguments)
    {
        switch (metadata.BindingType)
        {
            case 0: // Global unbound
                return $"/api/data/v9.2/{metadata.UniqueName}";
            
            case 1: // Entity-bound
                if (!arguments.ContainsKey("Target"))
                {
                    throw new Exception("Target parameter is required for entity-bound Custom API");
                }
                var targetId = arguments["Target"]?.ToString();
                if (string.IsNullOrEmpty(targetId))
                {
                    throw new Exception("Target parameter cannot be empty");
                }
                var entitySet = GetEntitySetName(metadata.BoundEntityLogicalName);
                return $"/api/data/v9.2/{entitySet}({targetId})/Microsoft.Dynamics.CRM.{metadata.UniqueName}";
            
            case 2: // EntityCollection-bound
                var collectionEntitySet = GetEntitySetName(metadata.BoundEntityLogicalName);
                return $"/api/data/v9.2/{collectionEntitySet}/Microsoft.Dynamics.CRM.{metadata.UniqueName}";
            
            default:
                throw new Exception($"Unknown binding type: {metadata.BindingType}");
        }
    }

    private string AppendFunctionParameters(string url, CustomAPIMetadata metadata, JObject arguments)
    {
        var parameters = new List<string>();
        
        foreach (var param in metadata.RequestParameters)
        {
            if (arguments.ContainsKey(param.UniqueName))
            {
                var value = FormatParameterValue(param, arguments[param.UniqueName]);
                parameters.Add($"{param.UniqueName}={value}");
            }
            else if (!param.IsOptional)
            {
                throw new Exception($"Required parameter missing: {param.UniqueName}");
            }
        }
        
        if (parameters.Count > 0)
        {
            return $"{url}({string.Join(",", parameters)})";
        }
        
        return url;
    }

    private JObject FormatActionParameters(CustomAPIMetadata metadata, JObject arguments)
    {
        var body = new JObject();
        
        foreach (var param in metadata.RequestParameters)
        {
            if (arguments.ContainsKey(param.UniqueName))
            {
                body[param.UniqueName] = FormatParameterForBody(param, arguments[param.UniqueName]);
            }
            else if (!param.IsOptional)
            {
                throw new Exception($"Required parameter missing: {param.UniqueName}");
            }
        }
        
        return body;
    }

    private string FormatParameterValue(CustomAPIParameter param, JToken value)
    {
        switch (param.Type)
        {
            case 0: // Boolean
                return value.Value<bool>().ToString().ToLower();
            case 1: // DateTime
                return value.ToString();
            case 10: // String
                return $"'{value.ToString().Replace("'", "''")}'";
            case 12: // Guid
                return value.ToString();
            case 7: // Integer
            case 9: // Picklist
                return value.ToString();
            case 2: // Decimal
            case 6: // Float
            case 8: // Money
                return value.ToString();
            default:
                return $"'{value.ToString().Replace("'", "''")}'";
        }
    }

    private JToken FormatParameterForBody(CustomAPIParameter param, JToken value)
    {
        switch (param.Type)
        {
            case 5: // EntityReference
                var refObj = value as JObject;
                if (refObj != null && refObj.ContainsKey("logicalName") && refObj.ContainsKey("id"))
                {
                    return new JObject
                    {
                        ["@odata.type"] = $"Microsoft.Dynamics.CRM.{refObj["logicalName"]}",
                        [$"{refObj["logicalName"]}id"] = refObj["id"].ToString()
                    };
                }
                return value;
            
            case 3: // Entity
                var entityObj = value as JObject;
                if (entityObj != null)
                {
                    // Open type: Entity without LogicalEntityName
                    // Expects @odata.type to be provided in the input
                    if (string.IsNullOrEmpty(param.LogicalEntityName))
                    {
                        // Validate that @odata.type is present for open types
                        if (!entityObj.ContainsKey("@odata.type"))
                        {
                            throw new Exception($"Open type Entity parameter '{param.UniqueName}' requires @odata.type property");
                        }
                        return entityObj;
                    }
                    else
                    {
                        // Typed entity: add @odata.type if not present
                        if (!entityObj.ContainsKey("@odata.type"))
                        {
                            entityObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{param.LogicalEntityName}";
                        }
                        return entityObj;
                    }
                }
                return value;
            
            case 4: // EntityCollection
                var collectionArray = value as JArray;
                if (collectionArray != null && string.IsNullOrEmpty(param.LogicalEntityName))
                {
                    // Open type: validate each entity has @odata.type
                    foreach (var item in collectionArray)
                    {
                        var itemObj = item as JObject;
                        if (itemObj != null && !itemObj.ContainsKey("@odata.type"))
                        {
                            throw new Exception($"Open type EntityCollection parameter '{param.UniqueName}' requires @odata.type on each entity");
                        }
                    }
                }
                else if (collectionArray != null && !string.IsNullOrEmpty(param.LogicalEntityName))
                {
                    // Typed collection: add @odata.type to each entity if not present
                    foreach (var item in collectionArray)
                    {
                        var itemObj = item as JObject;
                        if (itemObj != null && !itemObj.ContainsKey("@odata.type"))
                        {
                            itemObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{param.LogicalEntityName}";
                        }
                    }
                }
                return value;
            
            case 8: // Money
                return new JObject { ["Value"] = value };
            
            case 11: // StringArray
                return value;
            
            default:
                return value;
        }
    }

    private JObject ParseCustomAPIResponse(CustomAPIMetadata metadata, JObject response)
    {
        var result = new JObject();
        
        // If there are response properties, extract them
        if (metadata.ResponseProperties.Count > 0)
        {
            foreach (var prop in metadata.ResponseProperties)
            {
                if (response.ContainsKey(prop.UniqueName))
                {
                    result[prop.UniqueName] = response[prop.UniqueName];
                }
            }
        }
        else
        {
            // No specific response properties, return entire response
            result = response;
        }
        
        return result;
    }

    private string GetEntitySetName(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
        {
            throw new Exception("Entity logical name is required");
        }
        
        // Common entity set name mappings
        var commonMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "account", "accounts" },
            { "contact", "contacts" },
            { "lead", "leads" },
            { "opportunity", "opportunities" },
            { "systemuser", "systemusers" },
            { "team", "teams" },
            { "businessunit", "businessunits" },
            { "organization", "organizations" },
            { "role", "roles" },
            { "task", "tasks" },
            { "email", "emails" },
            { "appointment", "appointments" },
            { "phonecall", "phonecalls" },
            { "letter", "letters" },
            { "fax", "faxes" },
            { "activitypointer", "activitypointers" },
            { "incident", "incidents" },
            { "quote", "quotes" },
            { "salesorder", "salesorders" },
            { "invoice", "invoices" },
            { "product", "products" },
            { "pricelevel", "pricelevels" },
            { "campaign", "campaigns" },
            { "list", "lists" },
            { "contract", "contracts" },
            { "entitlement", "entitlements" },
            { "queue", "queues" },
            { "annotation", "annotations" },
            { "sharepointdocument", "sharepointdocuments" }
        };
        
        if (commonMappings.TryGetValue(logicalName, out var entitySet))
        {
            return entitySet;
        }
        
        // Default pluralization: add 's'
        return logicalName + "s";
    }

    private void InvalidateCache()
    {
        var environmentUrl = this.Context.Request.RequestUri.Host;
        if (_customAPICache.ContainsKey(environmentUrl))
        {
            _customAPICache.Remove(environmentUrl);
            this.Context.Logger.LogInformation("Custom API cache invalidated");
        }
    }

    private string GetTypeDescription(int type)
    {
        return type switch
        {
            0 => "Boolean",
            1 => "DateTime",
            2 => "Decimal",
            3 => "Entity",
            4 => "EntityCollection",
            5 => "EntityReference",
            6 => "Float",
            7 => "Integer",
            8 => "Money",
            9 => "Picklist",
            10 => "String",
            11 => "StringArray",
            12 => "Guid",
            _ => "Unknown"
        };
    }

    private async Task<JObject> SendDataverseRequestAsync(string method, string path, JObject body, string correlationId = null)
    {
        try
        {
            var environmentUrl = this.Context.Request.RequestUri.Host;
            var url = $"https://{environmentUrl}{path}";

            var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Forward OAuth token from incoming request
            if (this.Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            // Add correlation ID for request tracing
            if (!string.IsNullOrEmpty(correlationId))
            {
                request.Headers.Add("x-ms-correlation-request-id", correlationId);
                this.Context.Logger.LogInformation($"Dataverse request: {method} {path}. CorrelationId: {correlationId}");
            }

            // Add OData headers
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Accept", "application/json");

            if (body != null)
            {
                request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            }

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                this.Context.Logger.LogError($"Dataverse request failed. Status: {response.StatusCode}. CorrelationId: {correlationId}");
                var errorObj = new JObject
                {
                    ["error"] = new JObject
                    {
                        ["statusCode"] = (int)response.StatusCode,
                        ["message"] = responseContent,
                        ["correlationId"] = correlationId
                    }
                };
                return errorObj;
            }

            return JObject.Parse(responseContent);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Dataverse request error: {ex.Message}. CorrelationId: {correlationId}");
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["message"] = ex.Message,
                    ["correlationId"] = correlationId
                }
            };
        }
    }

    // Management Tool Definitions
    private JObject GetManagementToolDefinition_CreateCustomAPI()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_create_custom_api",
            ["description"] = "Create a new Custom API definition in Dataverse. Custom APIs appear as MCP tools once created.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "UniqueName", "DisplayName", "BindingType", "IsFunction" },
                ["properties"] = new JObject
                {
                    ["UniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name (e.g., 'new_MyCustomAPI'). Must start with publisher prefix." },
                    ["DisplayName"] = new JObject { ["type"] = "string", ["description"] = "Display name for the Custom API" },
                    ["Description"] = new JObject { ["type"] = "string", ["description"] = "Description of what the Custom API does" },
                    ["BindingType"] = new JObject { ["type"] = "integer", ["description"] = "Binding type: 0=Global, 1=Entity-bound, 2=EntityCollection-bound", ["enum"] = new JArray { 0, 1, 2 } },
                    ["BoundEntityLogicalName"] = new JObject { ["type"] = "string", ["description"] = "Required for Entity/EntityCollection binding. Logical name of bound entity (e.g., 'account')" },
                    ["IsFunction"] = new JObject { ["type"] = "boolean", ["description"] = "true=Function (GET, side-effect free), false=Action (POST, may modify data)" },
                    ["IsPrivate"] = new JObject { ["type"] = "boolean", ["description"] = "true=Private API (for internal use), false=Public API (default: false)" },
                    ["SolutionUniqueName"] = new JObject { ["type"] = "string", ["description"] = "Solution to add Custom API to (e.g., 'MyPublisher'). Use dataverse_management_list_solutions to find solutions." }
                }
            }
        };
    }

    private JObject GetManagementToolDefinition_CreateAPIParameter()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_create_api_parameter",
            ["description"] = "Add a request parameter to an existing Custom API",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "CustomAPIUniqueName", "UniqueName", "Name", "Type" },
                ["properties"] = new JObject
                {
                    ["CustomAPIUniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name of the Custom API to add parameter to" },
                    ["UniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name for the parameter (e.g., 'InputText')" },
                    ["Name"] = new JObject { ["type"] = "string", ["description"] = "Name of the parameter as it appears in API calls" },
                    ["DisplayName"] = new JObject { ["type"] = "string", ["description"] = "Display name for the parameter" },
                    ["Description"] = new JObject { ["type"] = "string", ["description"] = "Description of the parameter" },
                    ["Type"] = new JObject { ["type"] = "integer", ["description"] = "Parameter type: 0=Boolean, 1=DateTime, 2=Decimal, 3=Entity, 4=EntityCollection, 5=EntityReference, 6=Float, 7=Integer, 8=Money, 9=Picklist, 10=String, 11=StringArray, 12=Guid" },
                    ["LogicalEntityName"] = new JObject { ["type"] = "string", ["description"] = "Required for Entity/EntityCollection/EntityReference types. Logical name of the entity." },
                    ["IsOptional"] = new JObject { ["type"] = "boolean", ["description"] = "true=Optional parameter, false=Required parameter (default: false)" }
                }
            }
        };
    }

    private JObject GetManagementToolDefinition_CreateAPIProperty()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_create_api_property",
            ["description"] = "Add a response property to an existing Custom API",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "CustomAPIUniqueName", "UniqueName", "Name", "Type" },
                ["properties"] = new JObject
                {
                    ["CustomAPIUniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name of the Custom API to add response property to" },
                    ["UniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name for the response property (e.g., 'OutputText')" },
                    ["Name"] = new JObject { ["type"] = "string", ["description"] = "Name of the property as it appears in API responses" },
                    ["DisplayName"] = new JObject { ["type"] = "string", ["description"] = "Display name for the property" },
                    ["Description"] = new JObject { ["type"] = "string", ["description"] = "Description of the property" },
                    ["Type"] = new JObject { ["type"] = "integer", ["description"] = "Property type: 0=Boolean, 1=DateTime, 2=Decimal, 3=Entity, 4=EntityCollection, 5=EntityReference, 6=Float, 7=Integer, 8=Money, 9=Picklist, 10=String, 11=StringArray, 12=Guid" },
                    ["LogicalEntityName"] = new JObject { ["type"] = "string", ["description"] = "Required for Entity/EntityCollection/EntityReference types. Logical name of the entity." }
                }
            }
        };
    }

    private JObject GetManagementToolDefinition_UpdateCustomAPI()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_update_custom_api",
            ["description"] = "Update an existing Custom API's metadata (description, private flag, etc.)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "UniqueName" },
                ["properties"] = new JObject
                {
                    ["UniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name of the Custom API to update" },
                    ["DisplayName"] = new JObject { ["type"] = "string", ["description"] = "New display name" },
                    ["Description"] = new JObject { ["type"] = "string", ["description"] = "New description" },
                    ["IsPrivate"] = new JObject { ["type"] = "boolean", ["description"] = "Update private flag" }
                }
            }
        };
    }

    private JObject GetManagementToolDefinition_DeleteCustomAPI()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_delete_custom_api",
            ["description"] = "Delete a Custom API and all its parameters and response properties. WARNING: This is permanent.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "UniqueName" },
                ["properties"] = new JObject
                {
                    ["UniqueName"] = new JObject { ["type"] = "string", ["description"] = "Unique name of the Custom API to delete" }
                }
            }
        };
    }

    private JObject GetManagementToolDefinition_ListSolutions()
    {
        return new JObject
        {
            ["name"] = "dataverse_management_list_solutions",
            ["description"] = "List all solutions in the Dataverse environment for creating Custom APIs in specific solutions",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["IncludeManaged"] = new JObject { ["type"] = "boolean", ["description"] = "Include managed solutions (default: false, only unmanaged)" }
                }
            }
        };
    }

    // Management Tool Handlers
    private async Task<HttpResponseMessage> HandleCreateCustomAPI(JObject arguments, JToken id)
    {
        try
        {
            var uniqueName = arguments["UniqueName"]?.ToString();
            var displayName = arguments["DisplayName"]?.ToString();
            var bindingType = arguments["BindingType"]?.Value<int>() ?? 0;
            var isFunction = arguments["IsFunction"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(uniqueName) || string.IsNullOrEmpty(displayName))
            {
                return CreateMCPErrorResponse(-32602, "UniqueName and DisplayName are required", id);
            }

            var body = new JObject
            {
                ["uniquename"] = uniqueName,
                ["displayname"] = displayName,
                ["bindingtype"] = bindingType,
                ["isfunction"] = isFunction,
                ["isprivate"] = arguments["IsPrivate"]?.Value<bool>() ?? false
            };

            if (arguments.ContainsKey("Description"))
                body["description"] = arguments["Description"].ToString();

            if (bindingType > 0 && arguments.ContainsKey("BoundEntityLogicalName"))
                body["boundentitylogicalname"] = arguments["BoundEntityLogicalName"].ToString();

            var path = "/api/data/v9.2/customapis";
            if (arguments.ContainsKey("SolutionUniqueName"))
            {
                path += $"?SolutionUniqueName={arguments["SolutionUniqueName"]}";
            }

            var correlationId = Guid.NewGuid().ToString();
            var response = await SendDataverseRequestAsync("POST", path, body, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to create Custom API: {response["error"]["message"]}");
            }

            // Invalidate cache to pick up new API
            InvalidateCache();

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Custom API '{uniqueName}' created successfully. ID: {response["customapiid"]}"
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"CreateCustomAPI error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to create Custom API: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleCreateAPIParameter(JObject arguments, JToken id)
    {
        try
        {
            var apiUniqueName = arguments["CustomAPIUniqueName"]?.ToString();
            var uniqueName = arguments["UniqueName"]?.ToString();
            var name = arguments["Name"]?.ToString();
            var type = arguments["Type"]?.Value<int>();

            if (string.IsNullOrEmpty(apiUniqueName) || string.IsNullOrEmpty(uniqueName) || string.IsNullOrEmpty(name) || !type.HasValue)
            {
                return CreateMCPErrorResponse(-32602, "CustomAPIUniqueName, UniqueName, Name, and Type are required", id);
            }

            // Look up Custom API ID
            var correlationId = Guid.NewGuid().ToString();
            var apiQuery = $"/api/data/v9.2/customapis?$select=customapiid&$filter=uniquename eq '{apiUniqueName}'";
            var apiResponse = await SendDataverseRequestAsync("GET", apiQuery, null, correlationId).ConfigureAwait(false);

            if (apiResponse["error"] != null || (apiResponse["value"] as JArray)?.Count == 0)
            {
                return CreateMCPErrorResponse(-32602, $"Custom API '{apiUniqueName}' not found", id);
            }

            var apiId = apiResponse["value"][0]["customapiid"].ToString();

            var body = new JObject
            {
                ["uniquename"] = uniqueName,
                ["name"] = name,
                ["type"] = type.Value,
                ["isoptional"] = arguments["IsOptional"]?.Value<bool>() ?? false,
                ["customapiid@odata.bind"] = $"/customapis({apiId})"
            };

            if (arguments.ContainsKey("DisplayName"))
                body["displayname"] = arguments["DisplayName"].ToString();
            if (arguments.ContainsKey("Description"))
                body["description"] = arguments["Description"].ToString();
            if (arguments.ContainsKey("LogicalEntityName"))
                body["logicalentityname"] = arguments["LogicalEntityName"].ToString();

            var response = await SendDataverseRequestAsync("POST", "/api/data/v9.2/customapirequestparameters", body, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to create parameter: {response["error"]["message"]}");
            }

            InvalidateCache();

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Parameter '{name}' ({GetTypeDescription(type.Value)}) added to Custom API '{apiUniqueName}'"
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"CreateAPIParameter error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to create parameter: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleCreateAPIProperty(JObject arguments, JToken id)
    {
        try
        {
            var apiUniqueName = arguments["CustomAPIUniqueName"]?.ToString();
            var uniqueName = arguments["UniqueName"]?.ToString();
            var name = arguments["Name"]?.ToString();
            var type = arguments["Type"]?.Value<int>();

            if (string.IsNullOrEmpty(apiUniqueName) || string.IsNullOrEmpty(uniqueName) || string.IsNullOrEmpty(name) || !type.HasValue)
            {
                return CreateMCPErrorResponse(-32602, "CustomAPIUniqueName, UniqueName, Name, and Type are required", id);
            }

            // Look up Custom API ID
            var correlationId = Guid.NewGuid().ToString();
            var apiQuery = $"/api/data/v9.2/customapis?$select=customapiid&$filter=uniquename eq '{apiUniqueName}'";
            var apiResponse = await SendDataverseRequestAsync("GET", apiQuery, null, correlationId).ConfigureAwait(false);

            if (apiResponse["error"] != null || (apiResponse["value"] as JArray)?.Count == 0)
            {
                return CreateMCPErrorResponse(-32602, $"Custom API '{apiUniqueName}' not found", id);
            }

            var apiId = apiResponse["value"][0]["customapiid"].ToString();

            var body = new JObject
            {
                ["uniquename"] = uniqueName,
                ["name"] = name,
                ["type"] = type.Value,
                ["customapiid@odata.bind"] = $"/customapis({apiId})"
            };

            if (arguments.ContainsKey("DisplayName"))
                body["displayname"] = arguments["DisplayName"].ToString();
            if (arguments.ContainsKey("Description"))
                body["description"] = arguments["Description"].ToString();
            if (arguments.ContainsKey("LogicalEntityName"))
                body["logicalentityname"] = arguments["LogicalEntityName"].ToString();

            var response = await SendDataverseRequestAsync("POST", "/api/data/v9.2/customapiresponseproperties", body, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to create response property: {response["error"]["message"]}");
            }

            InvalidateCache();

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Response property '{name}' ({GetTypeDescription(type.Value)}) added to Custom API '{apiUniqueName}'"
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"CreateAPIProperty error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to create response property: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleUpdateCustomAPI(JObject arguments, JToken id)
    {
        try
        {
            var uniqueName = arguments["UniqueName"]?.ToString();
            if (string.IsNullOrEmpty(uniqueName))
            {
                return CreateMCPErrorResponse(-32602, "UniqueName is required", id);
            }

            // Look up Custom API ID
            var correlationId = Guid.NewGuid().ToString();
            var apiQuery = $"/api/data/v9.2/customapis?$select=customapiid&$filter=uniquename eq '{uniqueName}'";
            var apiResponse = await SendDataverseRequestAsync("GET", apiQuery, null, correlationId).ConfigureAwait(false);

            if (apiResponse["error"] != null || (apiResponse["value"] as JArray)?.Count == 0)
            {
                return CreateMCPErrorResponse(-32602, $"Custom API '{uniqueName}' not found", id);
            }

            var apiId = apiResponse["value"][0]["customapiid"].ToString();

            var body = new JObject();
            if (arguments.ContainsKey("DisplayName"))
                body["displayname"] = arguments["DisplayName"].ToString();
            if (arguments.ContainsKey("Description"))
                body["description"] = arguments["Description"].ToString();
            if (arguments.ContainsKey("IsPrivate"))
                body["isprivate"] = arguments["IsPrivate"].Value<bool>();

            if (body.Count == 0)
            {
                return CreateMCPErrorResponse(-32602, "At least one property to update must be provided", id);
            }

            var response = await SendDataverseRequestAsync("PATCH", $"/api/data/v9.2/customapis({apiId})", body, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to update Custom API: {response["error"]["message"]}");
            }

            InvalidateCache();

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Custom API '{uniqueName}' updated successfully"
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"UpdateCustomAPI error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to update Custom API: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleDeleteCustomAPI(JObject arguments, JToken id)
    {
        try
        {
            var uniqueName = arguments["UniqueName"]?.ToString();
            if (string.IsNullOrEmpty(uniqueName))
            {
                return CreateMCPErrorResponse(-32602, "UniqueName is required", id);
            }

            // Look up Custom API ID
            var correlationId = Guid.NewGuid().ToString();
            var apiQuery = $"/api/data/v9.2/customapis?$select=customapiid&$filter=uniquename eq '{uniqueName}'";
            var apiResponse = await SendDataverseRequestAsync("GET", apiQuery, null, correlationId).ConfigureAwait(false);

            if (apiResponse["error"] != null || (apiResponse["value"] as JArray)?.Count == 0)
            {
                return CreateMCPErrorResponse(-32602, $"Custom API '{uniqueName}' not found", id);
            }

            var apiId = apiResponse["value"][0]["customapiid"].ToString();

            var response = await SendDataverseRequestAsync("DELETE", $"/api/data/v9.2/customapis({apiId})", null, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to delete Custom API: {response["error"]["message"]}");
            }

            InvalidateCache();

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Custom API '{uniqueName}' deleted successfully. All parameters and response properties were also deleted."
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"DeleteCustomAPI error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to delete Custom API: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleListSolutions(JObject arguments, JToken id)
    {
        try
        {
            var includeManaged = arguments["IncludeManaged"]?.Value<bool>() ?? false;

            var query = "/api/data/v9.2/solutions?$select=uniquename,friendlyname,version,ismanaged,description&$orderby=friendlyname";
            if (!includeManaged)
            {
                query += "&$filter=ismanaged eq false";
            }

            var correlationId = Guid.NewGuid().ToString();
            var response = await SendDataverseRequestAsync("GET", query, null, correlationId).ConfigureAwait(false);

            if (response["error"] != null)
            {
                throw new Exception($"Failed to list solutions: {response["error"]["message"]}");
            }

            var solutions = response["value"] as JArray ?? new JArray();
            var resultText = new StringBuilder();
            resultText.AppendLine($"Found {solutions.Count} solution(s):");
            resultText.AppendLine();

            foreach (JObject solution in solutions)
            {
                var uniqueName = solution["uniquename"]?.ToString();
                var friendlyName = solution["friendlyname"]?.ToString();
                var version = solution["version"]?.ToString();
                var isManaged = solution["ismanaged"]?.Value<bool>() ?? false;
                var description = solution["description"]?.ToString();

                resultText.AppendLine($"- {friendlyName} ({uniqueName})");
                resultText.AppendLine($"  Version: {version}");
                resultText.AppendLine($"  Type: {(isManaged ? "Managed" : "Unmanaged")}");
                if (!string.IsNullOrEmpty(description))
                    resultText.AppendLine($"  Description: {description}");
                resultText.AppendLine();
            }

            var content = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = resultText.ToString()
                }
            };

            return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"ListSolutions error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Failed to list solutions: {ex.Message}", id);
        }
    }

    // Data classes
    private class CachedCustomAPIs
    {
        public List<CustomAPIMetadata> APIs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class CustomAPIMetadata
    {
        public string UniqueName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int BindingType { get; set; } // 0=Global, 1=Entity, 2=EntityCollection
        public string BoundEntityLogicalName { get; set; }
        public bool IsFunction { get; set; }
        public bool IsPrivate { get; set; }
        public List<CustomAPIParameter> RequestParameters { get; set; }
        public List<CustomAPIProperty> ResponseProperties { get; set; }
    }

    private class CustomAPIParameter
    {
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Type { get; set; }
        public string LogicalEntityName { get; set; }
        public bool IsOptional { get; set; }
    }

    private class CustomAPIProperty
    {
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Type { get; set; }
        public string LogicalEntityName { get; set; }
    }

    // Application Insights Telemetry
    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return;
            }

            // Convert properties to dictionary of strings for Application Insights
            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
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
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("IngestionEndpoint=".Length);
                }
            }
            return "https://dc.services.visualstudio.com/";
        }
        catch
        {
            return "https://dc.services.visualstudio.com/";
        }
    }

    // Helper methods
    private HttpResponseMessage CreateMCPSuccessResponse(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMCPErrorResponse(int code, string message, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateErrorResponse(string message, int statusCode)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = statusCode
            }
        };
        return new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(error.ToString(), Encoding.UTF8, "application/json")
        };
    }
}
