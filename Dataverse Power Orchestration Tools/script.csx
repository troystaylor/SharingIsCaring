using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Dataverse Power Orchestration Tools: Power MCP tool server for Copilot Studio
/// Dynamic tools loaded from tst_agentinstructions table with learned pattern discovery.
/// Orchestration tools: discover_functions, invoke_tool, orchestrate_plan, learn_patterns
/// </summary>
public class Script : ScriptBase
{
    // Application Insights telemetry (optional - leave empty to disable)
    // Format: InstrumentationKey=xxx;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // Meta-tool names
    private const string TOOL_DISCOVER_FUNCTIONS = "discover_functions";
    private const string TOOL_INVOKE_TOOL = "invoke_tool";
    private const string TOOL_ORCHESTRATE_PLAN = "orchestrate_plan";
    private const string TOOL_LEARN_PATTERNS = "learn_patterns";
    
    // Cache for dynamic content (per request lifecycle)
    private string _cachedAgentMd = null;
    private string _cachedInstructionsRecordId = null;  // For learned patterns updates
    private JArray _cachedTools = null;           // MCP format (name, description, inputSchema)
    private JArray _cachedFullTools = null;       // Full format (includes category, keywords)
    
    // Discovery cache data (from Dataverse)
    private string _cachedDiscoveredToolsJson = null;
    private DateTime? _cacheTimestamp = null;
    private int _cacheDuration = 30; // Default 30 minutes
    
    // Discovery configuration (from Dataverse)
    private bool _enableTables = true;
    private bool _enableCustomAPIs = true;
    private bool _enableActions = true;
    private HashSet<string> _blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    
    // Tool handler registry - dictionary dispatch for O(1) lookup
    private Dictionary<string, Func<JObject, Task<JObject>>> _toolHandlers;
    
    private async Task<string> GetAgentMdAsync()
    {
        // Return cached if available
        if (_cachedAgentMd != null)
            return _cachedAgentMd;
            
        try
        {
            // Query tst_agentinstructions table for active instructions
            var filter = "tst_name eq 'dataverse-tools-agent' and tst_enabled eq true";
            var select = "tst_agentinstructionsid,tst_agentmd,tst_learnedpatterns,tst_version,tst_updatecount,tst_discoveredtools,tst_discoverycache_timestamp,tst_discoverycache_duration,tst_enabletables,tst_enablecustomapis,tst_enableactions,tst_discoveryblacklist";
            var url = BuildDataverseUrl($"tst_agentinstructionses?$filter={Uri.EscapeDataString(filter)}&$select={Uri.EscapeDataString(select)}&$top=1");
            
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            var records = result["value"] as JArray;
            
            if (records == null || records.Count == 0)
            {
                _cachedAgentMd = string.Empty;
                return _cachedAgentMd;
            }
            
            var record = records[0] as JObject;
            _cachedInstructionsRecordId = record?["tst_agentinstructionsid"]?.ToString();
            var agentMd = record?["tst_agentmd"]?.ToString() ?? string.Empty;
            var learnedPatterns = record?["tst_learnedpatterns"]?.ToString();
            
            // Cache discovery data
            _cachedDiscoveredToolsJson = record?["tst_discoveredtools"]?.ToString();
            _cacheTimestamp = record?["tst_discoverycache_timestamp"]?.Value<DateTime?>();
            _cacheDuration = record?["tst_discoverycache_duration"]?.Value<int?>() ?? 30;
            
            // Cache discovery configuration
            _enableTables = record?["tst_enabletables"]?.Value<bool?>() ?? true;
            _enableCustomAPIs = record?["tst_enablecustomapis"]?.Value<bool?>() ?? true;
            _enableActions = record?["tst_enableactions"]?.Value<bool?>() ?? true;
            
            // Parse blacklist (comma or newline-separated)
            var blacklistRaw = record?["tst_discoveryblacklist"]?.ToString() ?? "";
            _blacklist = new HashSet<string>(
                blacklistRaw.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase
            );
            
            // Append learned patterns if present
            if (!string.IsNullOrWhiteSpace(learnedPatterns))
            {
                agentMd += "\n\n## LEARNED PATTERNS\n\n" + learnedPatterns;
            }
            
            _cachedAgentMd = agentMd;
            return _cachedAgentMd;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to load agents.md: {ex.Message}");
            _cachedAgentMd = string.Empty;
            return _cachedAgentMd;
        }
    }
    
    // Parse tools from agents.md JSON block and merge with discovered tools from cache
    private async Task<JArray> GetDynamicToolsAsync()
    {
        if (_cachedTools != null)
            return _cachedTools;
            
        // Get static tools from agents.md (also loads cache data)
        var agentMd = await GetAgentMdAsync().ConfigureAwait(false);
        var staticTools = ParseToolsFromAgentMd(agentMd);
        
        // Check if discovery cache is expired
        var isCacheExpired = _cacheTimestamp == null || 
                             DateTime.UtcNow - _cacheTimestamp.Value > TimeSpan.FromMinutes(_cacheDuration);
        
        if (isCacheExpired)
        {
            this.Context.Logger.LogInformation("Discovery cache expired or missing - running discovery");
            
            try
            {
                // Discover all tools (tables, Custom APIs, actions/functions)
                var discoveredTools = await DiscoverAllToolsAsync().ConfigureAwait(false);
                
                // Update cache in Dataverse
                await UpdateDiscoveryCacheAsync(discoveredTools).ConfigureAwait(false);
                
                // Merge and cache
                _cachedTools = MergeTools(staticTools, discoveredTools);
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Discovery failed: {ex.Message}. Using static tools only.");
                _cachedTools = staticTools;
            }
        }
        else
        {
            this.Context.Logger.LogInformation($"Using cached discovered tools (expires in {(_cacheTimestamp.Value.AddMinutes(_cacheDuration) - DateTime.UtcNow).TotalMinutes:F0} min)");
            
            // Parse cached discovered tools
            JArray discoveredTools = new JArray();
            if (!string.IsNullOrWhiteSpace(_cachedDiscoveredToolsJson))
            {
                try
                {
                    discoveredTools = JArray.Parse(_cachedDiscoveredToolsJson);
                }
                catch (Exception ex)
                {
                    this.Context.Logger.LogWarning($"Failed to parse cached discovered tools: {ex.Message}");
                }
            }
            
            // Merge and cache
            _cachedTools = MergeTools(staticTools, discoveredTools);
        }
        
        return _cachedTools;
    }
    
    // Get full tools with category/keywords for search
    private async Task<JArray> GetFullToolsAsync()
    {
        if (_cachedFullTools != null)
            return _cachedFullTools;
            
        var agentMd = await GetAgentMdAsync().ConfigureAwait(false);
        var staticFullTools = ParseFullToolsFromAgentMd(agentMd);
        
        // For discover_functions, we also need full metadata from discovered tools
        // Parse cached discovered tools with full metadata
        JArray discoveredFullTools = new JArray();
        if (!string.IsNullOrWhiteSpace(_cachedDiscoveredToolsJson))
        {
            try
            {
                var discoveredTools = JArray.Parse(_cachedDiscoveredToolsJson);
                // Discovered tools already have category/keywords in full format
                discoveredFullTools = discoveredTools;
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Failed to parse discovered tools for search: {ex.Message}");
            }
        }
        
        // Merge full tools (static + discovered)
        _cachedFullTools = new JArray();
        foreach (var tool in staticFullTools) _cachedFullTools.Add(tool);
        foreach (var tool in discoveredFullTools) _cachedFullTools.Add(tool);
        
        return _cachedFullTools;
    }
    
    /// <summary>
    /// Merge static tools with discovered tools, removing duplicates (static wins)
    /// </summary>
    private JArray MergeTools(JArray staticTools, JArray discoveredTools)
    {
        var merged = new JArray();
        var toolNames = new HashSet<string>();
        
        // Add static tools first (they take precedence)
        foreach (var tool in staticTools)
        {
            var name = tool["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                merged.Add(tool);
                toolNames.Add(name);
            }
        }
        
        // Add discovered tools (skip duplicates)
        foreach (var tool in discoveredTools)
        {
            var name = tool["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name) && !toolNames.Contains(name))
            {
                merged.Add(tool);
                toolNames.Add(name);
            }
        }
        
        this.Context.Logger.LogInformation($"Merged tools: {staticTools.Count} static + {discoveredTools.Count} discovered = {merged.Count} total");
        return merged;
    }
    
    /// <summary>
    /// Discover all tools from Dataverse (tables, Custom APIs, actions/functions)
    /// </summary>
    private async Task<JArray> DiscoverAllToolsAsync()
    {
        var discoveredTools = new JArray();
        
        // Phase 1: Table discovery
        if (_enableTables)
        {
            var tableTools = await DiscoverTablesAsync().ConfigureAwait(false);
            foreach (var tool in tableTools) discoveredTools.Add(tool);
        }
        
        // Phase 2: Custom API discovery
        if (_enableCustomAPIs)
        {
            var customApiTools = await DiscoverCustomAPIsAsync().ConfigureAwait(false);
            foreach (var tool in customApiTools) discoveredTools.Add(tool);
        }
        
        // Phase 3: Actions/Functions discovery
        if (_enableActions)
        {
            var actionTools = await DiscoverActionsAsync().ConfigureAwait(false);
            foreach (var tool in actionTools) discoveredTools.Add(tool);
        }
        
        this.Context.Logger.LogInformation($"Discovered {discoveredTools.Count} tools");
        return discoveredTools;
    }
    
    /// <summary>
    /// Discover all Dataverse tables and generate 6 CRUD tools per table
    /// Generates: create_{table}, get_{table}, update_{table}, delete_{table}, list_{table}, query_{table}
    /// </summary>
    private async Task<JArray> DiscoverTablesAsync()
    {
        var tools = new JArray();
        
        try
        {
            this.Context.Logger.LogInformation("Starting table discovery...");
            
            // Query EntityDefinitions with attributes expanded
            var url = BuildDataverseUrl("EntityDefinitions?$select=LogicalName,DisplayName,Description,PrimaryIdAttribute,PrimaryNameAttribute,IsCustomEntity,IsActivity&$expand=Attributes($select=LogicalName,DisplayName,Description,AttributeType,MaxLength,IsValidForCreate,IsValidForUpdate,IsValidForRead,IsPrimaryId,IsPrimaryName,RequiredLevel,Format)&$filter=IsPrivate eq false");
            
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            var entities = result["value"] as JArray;
            
            if (entities == null || entities.Count == 0)
            {
                this.Context.Logger.LogWarning("No entities discovered");
                return tools;
            }
            
            this.Context.Logger.LogInformation($"Processing {entities.Count} entities...");
            
            foreach (var entity in entities)
            {
                var entityObj = entity as JObject;
                if (entityObj == null) continue;
                
                var logicalName = entityObj["LogicalName"]?.ToString();
                if (string.IsNullOrWhiteSpace(logicalName)) continue;
                
                // Check blacklist
                if (_blacklist.Contains(logicalName))
                {
                    this.Context.Logger.LogDebug($"Skipping blacklisted table: {logicalName}");
                    continue;
                }
                
                var displayName = entityObj["DisplayName"]?.Value<JObject>()?["UserLocalizedLabel"]?["Label"]?.ToString() ?? logicalName;
                var description = entityObj["Description"]?.Value<JObject>()?["UserLocalizedLabel"]?["Label"]?.ToString() ?? "";
                var primaryIdAttr = entityObj["PrimaryIdAttribute"]?.ToString();
                var primaryNameAttr = entityObj["PrimaryNameAttribute"]?.ToString();
                var attributes = entityObj["Attributes"] as JArray;
                
                // Build detailed schema for attributes
                var createSchema = BuildAttributeSchema(attributes, "create");
                var updateSchema = BuildAttributeSchema(attributes, "update");
                var readSchema = BuildAttributeSchema(attributes, "read");
                
                // Generate 6 tools per table (all operations, fail gracefully at runtime)
                tools.Add(GenerateTableTool(logicalName, displayName, description, "create", createSchema, primaryIdAttr));
                tools.Add(GenerateTableTool(logicalName, displayName, description, "get", readSchema, primaryIdAttr));
                tools.Add(GenerateTableTool(logicalName, displayName, description, "update", updateSchema, primaryIdAttr));
                tools.Add(GenerateTableTool(logicalName, displayName, description, "delete", null, primaryIdAttr));
                tools.Add(GenerateTableTool(logicalName, displayName, description, "list", readSchema, primaryIdAttr));
                tools.Add(GenerateTableTool(logicalName, displayName, description, "query", readSchema, primaryIdAttr));
            }
            
            this.Context.Logger.LogInformation($"Discovered {tools.Count} table tools ({entities.Count} tables × 6 operations)");
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Table discovery failed: {ex.Message}");
            // Return partial results on error (fail-retry at operation level)
        }
        
        return tools;
    }
    
    /// <summary>
    /// Build JSON Schema for table attributes with detailed metadata
    /// </summary>
    private JObject BuildAttributeSchema(JArray attributes, string operation)
    {
        var properties = new JObject();
        var required = new JArray();
        
        if (attributes == null) return new JObject { ["type"] = "object", ["properties"] = properties };
        
        foreach (var attr in attributes)
        {
            var attrObj = attr as JObject;
            if (attrObj == null) continue;
            
            var logicalName = attrObj["LogicalName"]?.ToString();
            if (string.IsNullOrWhiteSpace(logicalName)) continue;
            
            var isPrimaryId = attrObj["IsPrimaryId"]?.Value<bool?>() ?? false;
            var isValidForCreate = attrObj["IsValidForCreate"]?.Value<bool?>() ?? false;
            var isValidForUpdate = attrObj["IsValidForUpdate"]?.Value<bool?>() ?? false;
            var isValidForRead = attrObj["IsValidForRead"]?.Value<bool?>() ?? false;
            
            // Filter by operation type
            if (operation == "create" && (!isValidForCreate || isPrimaryId)) continue;
            if (operation == "update" && !isValidForUpdate) continue;
            if (operation == "read" && !isValidForRead) continue;
            
            var displayName = attrObj["DisplayName"]?.Value<JObject>()?["UserLocalizedLabel"]?["Label"]?.ToString() ?? logicalName;
            var description = attrObj["Description"]?.Value<JObject>()?["UserLocalizedLabel"]?["Label"]?.ToString() ?? "";
            var attributeType = attrObj["AttributeType"]?.ToString() ?? "String";
            var maxLength = attrObj["MaxLength"]?.Value<int?>();
            var requiredLevel = attrObj["RequiredLevel"]?.Value<JObject>()?["Value"]?.ToString();
            var format = attrObj["Format"]?.ToString();
            
            // Map Dataverse types to JSON Schema types
            var schemaType = MapAttributeTypeToSchema(attributeType);
            
            var propSchema = new JObject
            {
                ["type"] = schemaType,
                ["description"] = $"{displayName}{(string.IsNullOrWhiteSpace(description) ? "" : $": {description}")}"
            };
            
            // Add detailed constraints
            if (maxLength.HasValue && maxLength.Value > 0)
                propSchema["maxLength"] = maxLength.Value;
            
            if (!string.IsNullOrWhiteSpace(format))
                propSchema["format"] = format.ToLowerInvariant();
            
            if (isPrimaryId)
                propSchema["x-ms-primary-id"] = true;
            
            properties[logicalName] = propSchema;
            
            // Track required fields (for create operation)
            if (operation == "create" && requiredLevel == "ApplicationRequired")
                required.Add(logicalName);
        }
        
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        
        if (required.Count > 0)
            schema["required"] = required;
        
        return schema;
    }
    
    /// <summary>
    /// Map Dataverse AttributeType to JSON Schema type
    /// </summary>
    private string MapAttributeTypeToSchema(string attributeType)
    {
        switch (attributeType?.ToLowerInvariant())
        {
            case "boolean":
                return "boolean";
            case "integer":
            case "bigint":
            case "picklist":
            case "state":
            case "status":
                return "integer";
            case "decimal":
            case "double":
            case "money":
                return "number";
            case "datetime":
                return "string"; // ISO 8601 format
            case "uniqueidentifier":
            case "lookup":
            case "owner":
            case "customer":
                return "string"; // GUID format
            default:
                return "string";
        }
    }
    
    /// <summary>
    /// Generate a tool definition for a table operation
    /// </summary>
    private JObject GenerateTableTool(string logicalName, string displayName, string description, string operation, JObject schema, string primaryIdAttr)
    {
        var toolName = $"{operation}_{logicalName}";
        var operationDesc = operation switch
        {
            "create" => $"Create a new {displayName} record",
            "get" => $"Get a single {displayName} record by ID",
            "update" => $"Update an existing {displayName} record",
            "delete" => $"Delete a {displayName} record",
            "list" => $"List {displayName} records with optional filtering",
            "query" => $"Query {displayName} records with advanced OData filters",
            _ => $"{operation} {displayName}"
        };
        
        var fullDescription = string.IsNullOrWhiteSpace(description) 
            ? operationDesc 
            : $"{operationDesc}. {description}";
        
        // Build inputSchema based on operation
        JObject inputSchema;
        
        switch (operation)
        {
            case "create":
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = schema["properties"],
                    ["required"] = schema["required"] ?? new JArray()
                };
                break;
                
            case "get":
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = $"The {primaryIdAttr} GUID"
                        },
                        ["select"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Comma-separated column names to return"
                        }
                    },
                    ["required"] = new JArray { "id" }
                };
                break;
                
            case "update":
                var updateProps = new JObject(schema["properties"]);
                updateProps["id"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = $"The {primaryIdAttr} GUID"
                };
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = updateProps,
                    ["required"] = new JArray { "id" }
                };
                break;
                
            case "delete":
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = $"The {primaryIdAttr} GUID"
                        }
                    },
                    ["required"] = new JArray { "id" }
                };
                break;
                
            case "list":
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["select"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Comma-separated column names to return"
                        },
                        ["filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "OData $filter expression"
                        },
                        ["orderby"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "OData $orderby expression"
                        },
                        ["top"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of records (default 10)"
                        }
                    }
                };
                break;
                
            case "query":
                inputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["select"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Comma-separated column names to return"
                        },
                        ["filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "OData $filter expression"
                        },
                        ["orderby"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "OData $orderby expression"
                        },
                        ["top"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum records to return"
                        },
                        ["expand"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Related entities to expand"
                        }
                    }
                };
                break;
                
            default:
                inputSchema = new JObject { ["type"] = "object" };
                break;
        }
        
        // Return full tool definition (MCP format + category/keywords for discover_functions)
        return new JObject
        {
            ["name"] = toolName,
            ["description"] = fullDescription,
            ["inputSchema"] = inputSchema,
            ["category"] = operation.ToUpperInvariant() switch
            {
                "CREATE" => "WRITE",
                "UPDATE" => "WRITE",
                "DELETE" => "WRITE",
                "GET" => "READ",
                "LIST" => "READ",
                "QUERY" => "READ",
                _ => "ADVANCED"
            },
            ["keywords"] = new JArray { logicalName, displayName.ToLowerInvariant(), operation, "table", "dataverse" },
            ["x-ms-table"] = logicalName,
            ["x-ms-operation"] = operation
        };
    }
    
    /// <summary>
    /// Discover all Dataverse Custom APIs and generate tools
    /// </summary>
    private async Task<JArray> DiscoverCustomAPIsAsync()
    {
        var tools = new JArray();
        
        try
        {
            this.Context.Logger.LogInformation("Starting Custom API discovery...");
            
            // Query customapis with expanded parameters and properties
            var query = "customapis?$select=uniquename,displayname,description,bindingtype,boundentitylogicalname,isfunction,isprivate&" +
                "$expand=" +
                "CustomAPIRequestParameters($select=uniquename,name,displayname,description,type,logicalentityname,isoptional)," +
                "CustomAPIResponseProperties($select=uniquename,name,displayname,description,type,logicalentityname)";
            
            var url = BuildDataverseUrl(query);
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            var apiRecords = result["value"] as JArray;
            
            if (apiRecords == null || apiRecords.Count == 0)
            {
                this.Context.Logger.LogWarning("No Custom APIs discovered");
                return tools;
            }
            
            this.Context.Logger.LogInformation($"Processing {apiRecords.Count} Custom APIs...");
            
            foreach (var apiRecord in apiRecords)
            {
                var apiObj = apiRecord as JObject;
                if (apiObj == null) continue;
                
                var uniqueName = apiObj["uniquename"]?.ToString();
                if (string.IsNullOrWhiteSpace(uniqueName)) continue;
                
                // Check blacklist
                if (_blacklist.Contains(uniqueName))
                {
                    this.Context.Logger.LogDebug($"Skipping blacklisted Custom API: {uniqueName}");
                    continue;
                }
                
                var displayName = apiObj["displayname"]?.ToString() ?? uniqueName;
                var description = apiObj["description"]?.ToString() ?? "";
                var bindingType = apiObj["bindingtype"]?.Value<int>() ?? 0;
                var boundEntityLogicalName = apiObj["boundentitylogicalName"]?.ToString();
                var isFunction = apiObj["isfunction"]?.Value<bool>() ?? false;
                var isPrivate = apiObj["isprivate"]?.Value<bool>() ?? false;
                
                var requestParams = apiObj["CustomAPIRequestParameters"] as JArray ?? new JArray();
                var responseProps = apiObj["CustomAPIResponseProperties"] as JArray ?? new JArray();
                
                // Generate tool definition
                var tool = GenerateCustomAPITool(uniqueName, displayName, description, bindingType, 
                    boundEntityLogicalName, isFunction, isPrivate, requestParams, responseProps);
                
                tools.Add(tool);
            }
            
            this.Context.Logger.LogInformation($"Discovered {tools.Count} Custom API tools");
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Custom API discovery failed: {ex.Message}");
            // Return partial results on error
        }
        
        return tools;
    }
    
    /// <summary>
    /// Generate tool definition for a Custom API
    /// </summary>
    private JObject GenerateCustomAPITool(string uniqueName, string displayName, string description,
        int bindingType, string boundEntityLogicalName, bool isFunction, bool isPrivate,
        JArray requestParams, JArray responseProps)
    {
        var toolName = $"customapi_{uniqueName}";
        
        // Build description with binding info
        var bindingInfo = bindingType switch
        {
            0 => "Global unbound",
            1 => $"Bound to {boundEntityLogicalName} entity",
            2 => $"Bound to {boundEntityLogicalName} entity collection",
            _ => "Unknown binding"
        };
        var fullDescription = string.IsNullOrWhiteSpace(description)
            ? $"{displayName} ({bindingInfo} {(isFunction ? "Function" : "Action")})"
            : $"{description} ({bindingInfo} {(isFunction ? "Function" : "Action")})";
        
        // Build inputSchema
        var properties = new JObject();
        var required = new JArray();
        
        // Add Target parameter for Entity-bound APIs
        if (bindingType == 1)
        {
            properties["Target"] = new JObject
            {
                ["type"] = "string",
                ["description"] = $"GUID of the {boundEntityLogicalName} record",
                ["format"] = "uuid"
            };
            required.Add("Target");
        }
        
        // Add request parameters
        foreach (var param in requestParams)
        {
            var paramObj = param as JObject;
            if (paramObj == null) continue;
            
            var paramUniqueName = paramObj["uniquename"]?.ToString();
            if (string.IsNullOrWhiteSpace(paramUniqueName)) continue;
            
            var paramDisplayName = paramObj["displayname"]?.ToString() ?? paramUniqueName;
            var paramDescription = paramObj["description"]?.ToString() ?? paramDisplayName;
            var paramType = paramObj["type"]?.Value<int>() ?? 10;
            var logicalEntityName = paramObj["logicalentityname"]?.ToString();
            var isOptional = paramObj["isoptional"]?.Value<bool>() ?? false;
            
            var paramSchema = new JObject
            {
                ["type"] = MapCustomAPITypeToSchema(paramType),
                ["description"] = paramDescription
            };
            
            // Add format and type hints
            switch (paramType)
            {
                case 1: // DateTime
                    paramSchema["format"] = "date-time";
                    break;
                case 12: // Guid
                    paramSchema["format"] = "uuid";
                    break;
                case 5: // EntityReference
                    paramSchema["description"] = $"{paramSchema["description"]} (EntityReference with logicalName and id)";
                    break;
                case 3: // Entity
                    if (string.IsNullOrEmpty(logicalEntityName))
                        paramSchema["description"] = $"{paramSchema["description"]} (Open type: accepts any entity with @odata.type and attributes)";
                    else
                        paramSchema["description"] = $"{paramSchema["description"]} ({logicalEntityName} entity)";
                    break;
                case 4: // EntityCollection
                    if (string.IsNullOrEmpty(logicalEntityName))
                        paramSchema["description"] = $"{paramSchema["description"]} (Open type: array of entities with @odata.type)";
                    else
                        paramSchema["description"] = $"{paramSchema["description"]} (Collection of {logicalEntityName} entities)";
                    break;
            }
            
            properties[paramUniqueName] = paramSchema;
            
            if (!isOptional)
                required.Add(paramUniqueName);
        }
        
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        
        if (required.Count > 0)
            inputSchema["required"] = required;
        
        // Build keywords for discovery
        var keywords = new JArray { uniqueName, "customapi", isFunction ? "function" : "action" };
        if (!string.IsNullOrWhiteSpace(boundEntityLogicalName))
            keywords.Add(boundEntityLogicalName);
        
        return new JObject
        {
            ["name"] = toolName,
            ["description"] = fullDescription,
            ["inputSchema"] = inputSchema,
            ["category"] = isFunction ? "READ" : "WRITE",
            ["keywords"] = keywords,
            ["x-ms-customapi"] = uniqueName,
            ["x-ms-bindingtype"] = bindingType,
            ["x-ms-isfunction"] = isFunction,
            ["x-ms-boundentity"] = boundEntityLogicalName
        };
    }
    
    /// <summary>
    /// Map Custom API type codes to JSON Schema types
    /// </summary>
    private string MapCustomAPITypeToSchema(int dataverseType)
    {
        return dataverseType switch
        {
            0 => "boolean",      // Boolean
            1 => "string",       // DateTime (ISO 8601 string)
            2 => "number",       // Decimal
            3 => "object",       // Entity
            4 => "array",        // EntityCollection
            5 => "object",       // EntityReference
            6 => "number",       // Float
            7 => "integer",      // Integer
            8 => "number",       // Money
            9 => "integer",      // Picklist
            10 => "string",      // String
            11 => "array",       // StringArray
            12 => "string",      // Guid (formatted as string)
            _ => "string"        // Default fallback
        };
    }
    
    /// <summary>
    /// Discover built-in Dataverse Actions and Functions
    /// </summary>
    private async Task<JArray> DiscoverActionsAsync()
    {
        var tools = new JArray();
        
        try
        {
            this.Context.Logger.LogInformation("Starting Actions/Functions discovery...");
            
            // Query EntityDefinitions with Actions and Functions expanded
            // Note: Actions/Functions are global operations, not entity-specific in most cases
            // We'll query the metadata for SdkMessage entities which represent all callable operations
            var query = "sdkmessages?$select=name,isprivate&" +
                "$expand=sdkmessagerequestfields($select=name,optional,clrformatter)," +
                "sdkmessageresponsefields($select=name,clrformatter)&" +
                "$filter=isprivate eq false and (categoryname eq 'Action' or categoryname eq 'Function')&" +
                "$orderby=name";
            
            var url = BuildDataverseUrl(query);
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            var messageRecords = result["value"] as JArray;
            
            if (messageRecords == null || messageRecords.Count == 0)
            {
                this.Context.Logger.LogWarning("No Actions/Functions discovered");
                return tools;
            }
            
            this.Context.Logger.LogInformation($"Processing {messageRecords.Count} Actions/Functions...");
            
            foreach (var messageRecord in messageRecords)
            {
                var messageObj = messageRecord as JObject;
                if (messageObj == null) continue;
                
                var name = messageObj["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                
                // Check blacklist
                if (_blacklist.Contains(name))
                {
                    this.Context.Logger.LogDebug($"Skipping blacklisted Action/Function: {name}");
                    continue;
                }
                
                var isPrivate = messageObj["isprivate"]?.Value<bool?>() ?? false;
                if (isPrivate) continue;
                
                var requestFields = messageObj["sdkmessagerequestfields"] as JArray ?? new JArray();
                var responseFields = messageObj["sdkmessageresponsefields"] as JArray ?? new JArray();
                
                // Generate tool definition
                var tool = GenerateActionFunctionTool(name, requestFields, responseFields);
                tools.Add(tool);
            }
            
            this.Context.Logger.LogInformation($"Discovered {tools.Count} Action/Function tools");
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Actions/Functions discovery failed: {ex.Message}");
            // Return partial results on error
        }
        
        return tools;
    }
    
    /// <summary>
    /// Generate tool definition for a Dataverse Action/Function
    /// </summary>
    private JObject GenerateActionFunctionTool(string name, JArray requestFields, JArray responseFields)
    {
        var toolName = $"action_{name}";
        var description = $"Execute {name} Dataverse operation";
        
        // Build inputSchema from request fields
        var properties = new JObject();
        var required = new JArray();
        
        foreach (var field in requestFields)
        {
            var fieldObj = field as JObject;
            if (fieldObj == null) continue;
            
            var fieldName = fieldObj["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(fieldName)) continue;
            
            var isOptional = fieldObj["optional"]?.Value<bool?>() ?? true;
            var formatter = fieldObj["clrformatter"]?.ToString();
            
            var fieldSchema = new JObject
            {
                ["type"] = MapFormatterToSchema(formatter),
                ["description"] = $"{fieldName} parameter for {name}"
            };
            
            properties[fieldName] = fieldSchema;
            
            if (!isOptional)
                required.Add(fieldName);
        }
        
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        
        if (required.Count > 0)
            inputSchema["required"] = required;
        
        return new JObject
        {
            ["name"] = toolName,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
            ["category"] = "ADVANCED",
            ["keywords"] = new JArray { name, "action", "function", "operation", "sdkmessage" },
            ["x-ms-action"] = name
        };
    }
    
    /// <summary>
    /// Map CLR formatter to JSON Schema type
    /// </summary>
    private string MapFormatterToSchema(string formatter)
    {
        if (string.IsNullOrWhiteSpace(formatter))
            return "string";
        
        return formatter.ToLowerInvariant() switch
        {
            var f when f.Contains("boolean") => "boolean",
            var f when f.Contains("int32") || f.Contains("int64") || f.Contains("integer") => "integer",
            var f when f.Contains("decimal") || f.Contains("double") || f.Contains("float") || f.Contains("money") => "number",
            var f when f.Contains("datetime") || f.Contains("guid") || f.Contains("string") => "string",
            var f when f.Contains("entity") && !f.Contains("collection") => "object",
            var f when f.Contains("collection") || f.Contains("array") => "array",
            _ => "string"
        };
    }
    
    /// <summary>
    /// Update discovery cache in Dataverse
    /// </summary>
    private async Task UpdateDiscoveryCacheAsync(JArray discoveredTools)
    {
        if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
        {
            this.Context.Logger.LogWarning("Cannot update discovery cache: no instructions record ID");
            return;
        }
        
        try
        {
            var updateUrl = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})");
            var updateBody = new JObject
            {
                ["tst_discoveredtools"] = discoveredTools.ToString(Newtonsoft.Json.Formatting.None),
                ["tst_discoverycache_timestamp"] = DateTime.UtcNow.ToString("o")
            };
            
            await SendDataverseRequest(new HttpMethod("PATCH"), updateUrl, updateBody).ConfigureAwait(false);
            
            // Update local cache
            _cachedDiscoveredToolsJson = discoveredTools.ToString(Newtonsoft.Json.Formatting.None);
            _cacheTimestamp = DateTime.UtcNow;
            
            this.Context.Logger.LogInformation($"Updated discovery cache with {discoveredTools.Count} tools");
            
            _ = LogToAppInsights("DiscoveryCacheUpdated", new Dictionary<string, string>
            {
                ["toolCount"] = discoveredTools.Count.ToString(),
                ["cacheDuration"] = _cacheDuration.ToString()
            });
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to update discovery cache: {ex.Message}");
        }
    }
    
    private JArray ParseFullToolsFromAgentMd(string agentMd)
    {
        if (string.IsNullOrWhiteSpace(agentMd))
            return new JArray();
            
        try
        {
            var toolsMarker = "## TOOLS";
            var toolsIndex = agentMd.IndexOf(toolsMarker, StringComparison.OrdinalIgnoreCase);
            if (toolsIndex < 0) return new JArray();
            
            var afterMarker = agentMd.Substring(toolsIndex + toolsMarker.Length);
            var jsonStart = afterMarker.IndexOf('[');
            if (jsonStart < 0) return new JArray();
            
            var jsonEnd = FindMatchingBracket(afterMarker, jsonStart);
            if (jsonEnd < 0) return new JArray();
            
            var jsonStr = afterMarker.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JArray.Parse(jsonStr);
        }
        catch
        {
            return new JArray();
        }
    }
    
    // Search tools by intent - matches against name, description, category, keywords
    private async Task<JObject> ExecuteDiscoverFunctions(JObject args)
    {
        var intent = args["intent"]?.ToString()?.ToLowerInvariant() ?? "";
        var category = args["category"]?.ToString()?.ToLowerInvariant();
        var maxResults = args["maxResults"]?.Value<int?>() ?? 10;
        
        if (string.IsNullOrWhiteSpace(intent) && string.IsNullOrWhiteSpace(category))
        {
            return new JObject
            {
                ["error"] = "Either 'intent' or 'category' is required",
                ["tools"] = new JArray()
            };
        }
        
        var fullTools = await GetFullToolsAsync().ConfigureAwait(false);
        var matches = new List<(JObject tool, int score)>();
        var intentWords = intent?.Split(new[] { ' ', ',', '-', '_' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
        
        foreach (var tool in fullTools)
        {
            var toolObj = tool as JObject;
            if (toolObj == null) continue;
            
            var name = toolObj["name"]?.ToString()?.ToLowerInvariant() ?? "";
            var desc = toolObj["description"]?.ToString()?.ToLowerInvariant() ?? "";
            var toolCategory = toolObj["category"]?.ToString()?.ToLowerInvariant() ?? "";
            var keywords = toolObj["keywords"] as JArray;
            var keywordList = keywords?.Select(k => k.ToString().ToLowerInvariant()).ToList() ?? new List<string>();
            
            // Extract metadata for enhanced scoring
            var isTableTool = toolObj["x-ms-table"] != null;
            var isCustomAPI = toolObj["x-ms-customapi"] != null;
            var isAction = toolObj["x-ms-action"] != null;
            var tableName = toolObj["x-ms-table"]?.ToString()?.ToLowerInvariant();
            var operation = toolObj["x-ms-operation"]?.ToString()?.ToLowerInvariant();
            
            // Category filter (exact match)
            if (!string.IsNullOrWhiteSpace(category) && toolCategory != category)
                continue;
            
            // Score based on matches
            var score = 0;
            
            foreach (var word in intentWords)
            {
                if (word.Length < 2) continue;
                
                // Exact keyword match = highest score
                if (keywordList.Contains(word)) score += 10;
                // Exact table name match (for discovered table tools)
                else if (isTableTool && tableName == word) score += 12;
                // Name contains word
                else if (name.Contains(word)) score += 8;
                // Description contains word
                else if (desc.Contains(word)) score += 3;
                // Partial keyword match
                else if (keywordList.Any(k => k.Contains(word) || word.Contains(k))) score += 5;
                // Operation match (create, update, delete, etc.)
                else if (!string.IsNullOrWhiteSpace(operation) && operation.Contains(word)) score += 6;
            }
            
            // Category match bonus (when filtering by category)
            if (!string.IsNullOrWhiteSpace(category) && toolCategory == category)
                score += 5;
            
            // Boost Custom APIs and Actions (they're more specific than generic CRUD)
            if (isCustomAPI && score > 0) score += 2;
            if (isAction && score > 0) score += 2;
            
            // Intent-based boosting for common patterns
            if (!string.IsNullOrWhiteSpace(intent))
            {
                var intentLower = intent.ToLowerInvariant();
                
                // CRUD pattern boosting
                if (intentLower.Contains("create") || intentLower.Contains("add") || intentLower.Contains("new"))
                {
                    if (operation == "create") score += 4;
                }
                else if (intentLower.Contains("update") || intentLower.Contains("modify") || intentLower.Contains("edit") || intentLower.Contains("change"))
                {
                    if (operation == "update") score += 4;
                }
                else if (intentLower.Contains("delete") || intentLower.Contains("remove"))
                {
                    if (operation == "delete") score += 4;
                }
                else if (intentLower.Contains("get") || intentLower.Contains("retrieve") || intentLower.Contains("fetch") || intentLower.Contains("find one"))
                {
                    if (operation == "get") score += 4;
                }
                else if (intentLower.Contains("list") || intentLower.Contains("all") || intentLower.Contains("find all") || intentLower.Contains("search"))
                {
                    if (operation == "list" || operation == "query") score += 4;
                }
            }
            
            if (score > 0 || !string.IsNullOrWhiteSpace(category))
            {
                matches.Add((toolObj, score));
            }
        }
        
        // Sort by score descending, take top results
        var results = matches
            .OrderByDescending(m => m.score)
            .Take(maxResults)
            .Select(m => new JObject
            {
                ["name"] = m.tool["name"],
                ["description"] = m.tool["description"],
                ["category"] = m.tool["category"],
                ["score"] = m.score
            })
            .ToList();
        
        return new JObject
        {
            ["intent"] = intent,
            ["category"] = category,
            ["matchCount"] = results.Count,
            ["tools"] = new JArray(results)
        };
    }
    
    // Execute a tool dynamically by name
    private async Task<JObject> ExecuteInvokeTool(JObject args)
    {
        var toolName = args["toolName"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new JObject
            {
                ["error"] = "'toolName' is required",
                ["success"] = false
            };
        }
        
        // Validate tool exists in dynamic tools
        var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
        var toolDef = tools.FirstOrDefault(t => t["name"]?.ToString() == toolName);
        if (toolDef == null)
        {
            return new JObject
            {
                ["error"] = $"Tool '{toolName}' not found. Use search_tools to find available tools.",
                ["success"] = false,
                ["suggestion"] = "Call search_tools with your intent to discover relevant tools"
            };
        }
        
        // Get tool arguments
        var toolArgs = args["args"] as JObject ?? new JObject();
        
        try
        {
            this.Context.Logger.LogInformation($"invoke_tool executing: {toolName}");
            var result = await ExecuteToolByName(toolName, toolArgs).ConfigureAwait(false);
            
            return new JObject
            {
                ["success"] = true,
                ["toolName"] = toolName,
                ["result"] = result
            };
        }
        catch (ArgumentException ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["toolName"] = toolName,
                ["error"] = $"Invalid arguments: {ex.Message}",
                ["inputSchema"] = toolDef["inputSchema"]
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["toolName"] = toolName,
                ["error"] = $"Execution failed: {ex.Message}"
            };
        }
    }
    
    // Execute a plan of multiple tool calls in sequence
    private async Task<JObject> ExecuteOrchestratePlan(JObject args)
    {
        var stepsToken = args["steps"];
        if (stepsToken == null || stepsToken.Type != JTokenType.Array)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "'steps' array is required"
            };
        }
        
        var steps = stepsToken as JArray;
        if (steps.Count == 0)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "'steps' array cannot be empty"
            };
        }
        
        var stopOnError = args["stopOnError"]?.Value<bool?>() ?? true;
        var results = new JArray();
        var context = new JObject(); // Shared context for variable substitution
        var allSuccess = true;
        
        this.Context.Logger.LogInformation($"orchestrate_plan starting with {steps.Count} steps");
        
        _ = LogToAppInsights("PlanStarted", new Dictionary<string, string>
        {
            ["stepCount"] = steps.Count.ToString(),
            ["stopOnError"] = stopOnError.ToString()
        });
        
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i] as JObject;
            if (step == null)
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["success"] = false,
                    ["error"] = "Invalid step format"
                });
                allSuccess = false;
                if (stopOnError) break;
                continue;
            }
            
            var toolName = step["tool"]?.ToString();
            var stepArgs = step["args"] as JObject ?? new JObject();
            var outputAs = step["outputAs"]?.ToString(); // Variable name to store result
            
            if (string.IsNullOrWhiteSpace(toolName))
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["success"] = false,
                    ["error"] = "'tool' is required in each step"
                });
                allSuccess = false;
                if (stopOnError) break;
                continue;
            }
            
            // Substitute variables from context into args
            var resolvedArgs = ResolvePlanVariables(stepArgs, context);
            
            try
            {
                this.Context.Logger.LogInformation($"Plan step {i + 1}/{steps.Count}: {toolName}");
                var result = await ExecuteToolByName(toolName, resolvedArgs).ConfigureAwait(false);
                
                // Store result in context if outputAs specified
                if (!string.IsNullOrWhiteSpace(outputAs))
                {
                    context[outputAs] = result;
                }
                
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["tool"] = toolName,
                    ["success"] = true,
                    ["result"] = result,
                    ["outputAs"] = outputAs
                });
            }
            catch (Exception ex)
            {
                results.Add(new JObject
                {
                    ["stepIndex"] = i,
                    ["tool"] = toolName,
                    ["success"] = false,
                    ["error"] = ex.Message
                });
                allSuccess = false;
                if (stopOnError) break;
            }
        }
        
        // Log successful workflow pattern for learning
        if (allSuccess && steps.Count >= 2)
        {
            _ = LogLearnedPatternAsync("workflow", steps, results);
        }
        
        return new JObject
        {
            ["success"] = allSuccess,
            ["stepsExecuted"] = results.Count,
            ["totalSteps"] = steps.Count,
            ["results"] = results,
            ["context"] = context
        };
    }
    
    /// <summary>
    /// Log a successful pattern to tst_learnedpatterns for self-improvement
    /// Appends pattern with timestamp to existing patterns, increments update count
    /// </summary>
    private async Task LogLearnedPatternAsync(string patternType, JArray steps, JArray results)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                // Ensure we have the record ID
                await GetAgentMdAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
                {
                    this.Context.Logger.LogWarning("Cannot log learned pattern: no instructions record found");
                    return;
                }
            }
            
            // Build pattern summary
            var toolSequence = new JArray();
            foreach (var step in steps)
            {
                var stepObj = step as JObject;
                toolSequence.Add(stepObj?["tool"]?.ToString() ?? "unknown");
            }
            
            var pattern = new JObject
            {
                ["type"] = patternType,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["tools"] = toolSequence,
                ["stepCount"] = steps.Count
            };
            
            // Get current learned patterns
            var getUrl = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})?$select=tst_learnedpatterns,tst_updatecount");
            var current = await SendDataverseRequest(HttpMethod.Get, getUrl, null).ConfigureAwait(false);
            
            var existingPatterns = current["tst_learnedpatterns"]?.ToString() ?? "";
            var updateCount = current["tst_updatecount"]?.Value<int?>() ?? 0;
            
            // Append new pattern (limit to last 50 patterns to prevent bloat)
            var patternLine = $"- [{DateTime.UtcNow:yyyy-MM-dd HH:mm}] {patternType}: {string.Join(" → ", toolSequence.Select(t => t.ToString()))}";
            var lines = existingPatterns.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            lines.Add(patternLine);
            if (lines.Count > 50) lines = lines.Skip(lines.Count - 50).ToList();
            
            var updatedPatterns = string.Join("\n", lines);
            
            // Update record
            var updateUrl = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})");
            var updateBody = new JObject
            {
                ["tst_learnedpatterns"] = updatedPatterns,
                ["tst_updatecount"] = updateCount + 1,
                ["tst_lastupdated"] = DateTime.UtcNow.ToString("o")
            };
            
            await SendDataverseRequest(new HttpMethod("PATCH"), updateUrl, updateBody).ConfigureAwait(false);
            
            this.Context.Logger.LogInformation($"Logged learned pattern: {patternLine}");
            
            _ = LogToAppInsights("LearnedPatternLogged", new Dictionary<string, string>
            {
                ["patternType"] = patternType,
                ["toolCount"] = toolSequence.Count.ToString(),
                ["tools"] = string.Join(",", toolSequence.Select(t => t.ToString()))
            });
        }
        catch (Exception ex)
        {
            // Don't fail the request if pattern logging fails
            this.Context.Logger.LogWarning($"Failed to log learned pattern: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get learned patterns from tst_learnedpatterns - exposes organizational learning to Copilot
    /// </summary>
    private async Task<JObject> ExecuteLearnPatterns(JObject args)
    {
        var toolNameFilter = args["toolName"]?.ToString()?.ToLowerInvariant();
        var limit = Math.Min(Math.Max(args["limit"]?.Value<int?>() ?? 10, 1), 50);
        
        try
        {
            // Ensure we have the record ID
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                await GetAgentMdAsync().ConfigureAwait(false);
            }
            
            if (string.IsNullOrWhiteSpace(_cachedInstructionsRecordId))
            {
                return new JObject
                {
                    ["patterns"] = new JArray(),
                    ["totalCount"] = 0,
                    ["message"] = "No instructions record configured. Create tst_agentinstructions with tst_name='dataverse-tools-agent'"
                };
            }
            
            // Get current learned patterns
            var url = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})?$select=tst_learnedpatterns,tst_updatecount,tst_lastupdated");
            var result = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            var patternsRaw = result["tst_learnedpatterns"]?.ToString() ?? "";
            var updateCount = result["tst_updatecount"]?.Value<int?>() ?? 0;
            var lastUpdated = result["tst_lastupdated"]?.ToString();
            
            if (string.IsNullOrWhiteSpace(patternsRaw))
            {
                return new JObject
                {
                    ["patterns"] = new JArray(),
                    ["totalCount"] = 0,
                    ["updateCount"] = updateCount,
                    ["message"] = "No patterns learned yet. Orchestrate plans to start learning."
                };
            }
            
            // Parse patterns (format: "- [timestamp] type: tool1 → tool2 → tool3")
            var lines = patternsRaw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var patterns = new List<JObject>();
            
            foreach (var line in lines.Reverse()) // Most recent first
            {
                if (!line.TrimStart().StartsWith("-")) continue;
                
                var pattern = ParsePatternLine(line);
                if (pattern == null) continue;
                
                // Apply tool filter if specified
                if (!string.IsNullOrWhiteSpace(toolNameFilter))
                {
                    var tools = pattern["tools"] as JArray;
                    if (tools == null || !tools.Any(t => t.ToString().ToLowerInvariant().Contains(toolNameFilter)))
                        continue;
                }
                
                patterns.Add(pattern);
                if (patterns.Count >= limit) break;
            }
            
            // Extract common sequences for suggestions
            var toolFrequency = new Dictionary<string, int>();
            var sequenceFrequency = new Dictionary<string, int>();
            
            foreach (var line in lines)
            {
                var pattern = ParsePatternLine(line);
                if (pattern == null) continue;
                
                var tools = pattern["tools"] as JArray;
                if (tools == null) continue;
                
                foreach (var tool in tools)
                {
                    var toolName = tool.ToString();
                    toolFrequency[toolName] = toolFrequency.GetValueOrDefault(toolName, 0) + 1;
                }
                
                // Track 2-tool sequences
                for (var i = 0; i < tools.Count - 1; i++)
                {
                    var seq = $"{tools[i]} → {tools[i + 1]}";
                    sequenceFrequency[seq] = sequenceFrequency.GetValueOrDefault(seq, 0) + 1;
                }
            }
            
            var topTools = toolFrequency.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new JObject { ["tool"] = kv.Key, ["count"] = kv.Value }).ToList();
            var topSequences = sequenceFrequency.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new JObject { ["sequence"] = kv.Key, ["count"] = kv.Value }).ToList();
            
            return new JObject
            {
                ["patterns"] = new JArray(patterns),
                ["totalCount"] = lines.Length,
                ["returnedCount"] = patterns.Count,
                ["updateCount"] = updateCount,
                ["lastUpdated"] = lastUpdated,
                ["insights"] = new JObject
                {
                    ["mostUsedTools"] = new JArray(topTools),
                    ["commonSequences"] = new JArray(topSequences)
                },
                ["filter"] = toolNameFilter
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["patterns"] = new JArray(),
                ["error"] = $"Failed to retrieve patterns: {ex.Message}"
            };
        }
    }
    
    private JObject ParsePatternLine(string line)
    {
        try
        {
            // Format: "- [2026-01-10 14:23] plan: tool1 → tool2 → tool3"
            var trimmed = line.TrimStart('-', ' ');
            
            var timestampEnd = trimmed.IndexOf(']');
            if (timestampEnd < 0) return null;
            
            var timestamp = trimmed.Substring(1, timestampEnd - 1); // Skip opening [
            var rest = trimmed.Substring(timestampEnd + 1).TrimStart();
            
            var colonPos = rest.IndexOf(':');
            if (colonPos < 0) return null;
            
            var patternType = rest.Substring(0, colonPos).Trim();
            var toolsStr = rest.Substring(colonPos + 1).Trim();
            
            var tools = toolsStr.Split(new[] { " → ", "→" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            
            return new JObject
            {
                ["timestamp"] = timestamp,
                ["type"] = patternType,
                ["tools"] = new JArray(tools),
                ["toolCount"] = tools.Count
            };
        }
        catch
        {
            return null;
        }
    }
    
    // Resolve {{variable}} placeholders in plan args from context
    private JObject ResolvePlanVariables(JObject args, JObject context)
    {
        var resolved = new JObject();
        
        foreach (var prop in args.Properties())
        {
            resolved[prop.Name] = ResolveValue(prop.Value, context);
        }
        
        return resolved;
    }
    
    private JToken ResolveValue(JToken value, JObject context)
    {
        if (value.Type == JTokenType.String)
        {
            var str = value.ToString();
            // Check for {{varName}} or {{varName.property}} pattern
            if (str.StartsWith("{{") && str.EndsWith("}}"))
            {
                var varPath = str.Substring(2, str.Length - 4).Trim();
                return ResolveVariablePath(varPath, context);
            }
            return value;
        }
        else if (value.Type == JTokenType.Object)
        {
            return ResolvePlanVariables(value as JObject, context);
        }
        else if (value.Type == JTokenType.Array)
        {
            var arr = new JArray();
            foreach (var item in value as JArray)
            {
                arr.Add(ResolveValue(item, context));
            }
            return arr;
        }
        return value;
    }
    
    private JToken ResolveVariablePath(string path, JObject context)
    {
        var parts = path.Split('.');
        JToken current = context;
        
        foreach (var part in parts)
        {
            if (current == null) return JValue.CreateNull();
            
            if (current.Type == JTokenType.Object)
            {
                current = (current as JObject)?[part];
            }
            else if (current.Type == JTokenType.Array && int.TryParse(part, out var index))
            {
                var arr = current as JArray;
                current = (index >= 0 && index < arr.Count) ? arr[index] : null;
            }
            else
            {
                return JValue.CreateNull();
            }
        }
        
        return current ?? JValue.CreateNull();
    }
    
    private JArray ParseToolsFromAgentMd(string agentMd)
    {
        if (string.IsNullOrWhiteSpace(agentMd))
            return GetFallbackTools();
            
        try
        {
            // Find JSON block after ## TOOLS marker
            var toolsMarker = "## TOOLS";
            var toolsIndex = agentMd.IndexOf(toolsMarker, StringComparison.OrdinalIgnoreCase);
            if (toolsIndex < 0)
            {
                this.Context.Logger.LogWarning("No ## TOOLS section found in agents.md");
                return GetFallbackTools();
            }
            
            var afterMarker = agentMd.Substring(toolsIndex + toolsMarker.Length);
            
            // Find JSON array - look for ```json block or raw [ ]
            var jsonStart = afterMarker.IndexOf('[');
            if (jsonStart < 0)
            {
                this.Context.Logger.LogWarning("No JSON array found in TOOLS section");
                return GetFallbackTools();
            }
            
            // Find matching closing bracket
            var jsonEnd = FindMatchingBracket(afterMarker, jsonStart);
            if (jsonEnd < 0)
            {
                this.Context.Logger.LogWarning("No closing bracket found for tools JSON");
                return GetFallbackTools();
            }
            
            var jsonStr = afterMarker.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var toolsArray = JArray.Parse(jsonStr);
            
            // Convert to MCP tool format (remove category/keywords, keep name/description/inputSchema)
            var mcpTools = new JArray();
            
            // Always inject orchestration tools first
            mcpTools.Add(GetDiscoverFunctionsDefinition());
            mcpTools.Add(GetInvokeToolDefinition());
            mcpTools.Add(GetOrchestratePlanDefinition());
            mcpTools.Add(GetLearnPatternsDefinition());
            
            foreach (var tool in toolsArray)
            {
                mcpTools.Add(new JObject
                {
                    ["name"] = tool["name"],
                    ["description"] = tool["description"],
                    ["inputSchema"] = tool["inputSchema"]
                });
            }
            
            this.Context.Logger.LogInformation($"Loaded {mcpTools.Count} tools from agents.md (includes orchestration tools)");
            return mcpTools;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Failed to parse tools from agents.md: {ex.Message}");
            return GetFallbackTools();
        }
    }
    
    private int FindMatchingBracket(string text, int openPos)
    {
        var depth = 0;
        var inString = false;
        var escapeNext = false;
        
        for (var i = openPos; i < text.Length; i++)
        {
            var c = text[i];
            
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }
            
            if (c == '\\' && inString)
            {
                escapeNext = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString) continue;
            
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        
        return -1;
    }
    
    // Orchestration tools definition (always available)
    private JObject GetDiscoverFunctionsDefinition() => new JObject
    {
        ["name"] = "discover_functions",
        ["description"] = "Search available tools by intent/keywords or category. Use this to discover relevant tools before calling them.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["intent"] = new JObject { ["type"] = "string", ["description"] = "Natural language description of what you want to do (e.g., 'create account', 'update contact', 'query metadata')" },
                ["category"] = new JObject { ["type"] = "string", ["description"] = "Filter by category: READ, WRITE, BULK, RELATIONSHIPS, METADATA, SECURITY, RECORD_MGMT, ATTACHMENTS, CHANGE_TRACKING, ASYNC, ADVANCED" },
                ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Maximum tools to return (default 10)" }
            },
            ["required"] = new JArray()
        }
    };
    
    private JObject GetInvokeToolDefinition() => new JObject
    {
        ["name"] = "invoke_tool",
        ["description"] = "Execute a tool dynamically by name. Use discover_functions first to find the right tool, then invoke_tool to execute it.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["toolName"] = new JObject { ["type"] = "string", ["description"] = "Name of the tool to execute (e.g., 'dataverse_create_row', 'dataverse_list_rows')" },
                ["args"] = new JObject { ["type"] = "object", ["description"] = "Arguments to pass to the tool as a JSON object" }
            },
            ["required"] = new JArray { "toolName" }
        }
    };
    
    private JObject GetOrchestratePlanDefinition() => new JObject
    {
        ["name"] = "orchestrate_plan",
        ["description"] = "Execute multiple tools in sequence as a plan. Supports variable substitution between steps using {{varName}} syntax. Use for multi-step operations like 'create account then add contact'.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["steps"] = new JObject
                {
                    ["type"] = "array",
                    ["description"] = "Array of plan steps to execute in order",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["tool"] = new JObject { ["type"] = "string", ["description"] = "Tool name to execute" },
                            ["args"] = new JObject { ["type"] = "object", ["description"] = "Arguments for the tool. Use {{varName}} to reference previous step outputs." },
                            ["outputAs"] = new JObject { ["type"] = "string", ["description"] = "Variable name to store this step's result for use in later steps" }
                        },
                        ["required"] = new JArray { "tool" }
                    }
                },
                ["stopOnError"] = new JObject { ["type"] = "boolean", ["description"] = "Stop plan on first error (default true)" }
            },
            ["required"] = new JArray { "steps" }
        }
    };
    
    private JObject GetLearnPatternsDefinition() => new JObject
    {
        ["name"] = "learn_patterns",
        ["description"] = "Get successful workflow patterns learned from previous executions. Use to suggest next steps or discover common tool sequences used in this organization.",
        ["inputSchema"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["toolName"] = new JObject { ["type"] = "string", ["description"] = "Filter patterns involving this specific tool (optional)" },
                ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Maximum patterns to return (default 10, max 50)" }
            },
            ["required"] = new JArray()
        }
    };
    
    // Minimal fallback tools when agents.md not configured
    private JArray GetFallbackTools() => new JArray
    {
        GetDiscoverFunctionsDefinition(),
        GetInvokeToolDefinition(),
        GetOrchestratePlanDefinition(),
        GetLearnPatternsDefinition(),
        new JObject
        {
            ["name"] = "dataverse_list_rows",
            ["description"] = "List Dataverse rows from a table with optional $select, $filter, $orderby, $top",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name (e.g., accounts)" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData $filter expression" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows (default 5)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = "dataverse_get_row",
            ["description"] = "Get a single Dataverse row by ID",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Row GUID" }
                },
                ["required"] = new JArray { "table", "id" }
            }
        }
    };

    // Server metadata
    private JObject GetServerInfo() => new JObject
    {
        ["name"] = "dataverse-power-mcp-tools-md",
        ["version"] = "2.0.0",
        ["title"] = "Dataverse Power Orchestration Tools",
        ["description"] = "Power MCP tool server for Dataverse with dynamic tools from tools.md and learned pattern discovery"
    };

    private JObject GetServerCapabilities() => new JObject
    {
        ["tools"] = new JObject { ["listChanged"] = false },
        ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
        ["prompts"] = new JObject { ["listChanged"] = false },
        ["logging"] = new JObject(),
        ["completions"] = new JObject()
    };

    // Tools are now loaded dynamically from agents.md via GetDynamicToolsAsync()
    // See ParseToolsFromAgentMd() for JSON parsing and GetFallbackTools() for minimal defaults

    private JArray GetDefinedResources() => new JArray();
    private JArray GetDefinedResourceTemplates() => new JArray();
    private JArray GetDefinedPrompts() => new JArray();

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation("Dataverse MCP Agent request received");
        
        _ = LogToAppInsights("RequestReceived", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["path"] = this.Context.Request.RequestUri.AbsolutePath,
            ["method"] = this.Context.Request.Method.Method
        });
        
        try
        {
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            // Route metadata/query operations
            if (requestPath.EndsWith("/metadata/tables", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTables metadata operation");
                return await HandleGetTables().ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/metadata/schema", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTableSchema metadata operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                var table = query["table"];
                return await HandleGetTableSchema(table).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/list", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to ListRecords query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleListRecords(
                    query["table"],
                    query["select"],
                    query["filter"],
                    string.IsNullOrEmpty(query["top"]) ? 10 : int.Parse(query["top"])
                ).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/get", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetRecord query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleGetRecord(
                    query["table"],
                    query["id"],
                    query["select"]
                ).ConfigureAwait(false);
            }

            // MCP Protocol mode - JSON-RPC 2.0
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            this.Context.Logger.LogDebug($"Request body length: {body?.Length ?? 0} characters");
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning("Empty request body received");
                return CreateErrorResponse(-32600, "Empty request body", null);
            }

            JObject payload;
            try 
            { 
                payload = JObject.Parse(body); 
            }
            catch (JsonException ex) 
            { 
                return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", null);
            }

            // Route to MCP protocol handler
            this.Context.Logger.LogInformation("Routing to MCP protocol handler");
            return await HandleMCPRequest(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["error"] = ex.Message,
                ["errorType"] = ex.GetType().Name
            });
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", null);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["durationMs"] = duration.TotalMilliseconds.ToString("F0")
            });
        }
    }

    // ---------- MCP Mode ----------
    private async Task<HttpResponseMessage> HandleMCPRequest(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];
        this.Context.Logger.LogInformation($"MCP method: {method}");

        try
        {
            switch (method)
            {
                case "initialize":
                    return CreateSuccessResponse(new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = GetServerCapabilities(),
                        ["serverInfo"] = GetServerInfo()
                    }, id);
                case "initialized":
                case "ping":
                case "notifications/cancelled":
                    return CreateSuccessResponse(new JObject(), id);
                case "tools/list":
                    var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
                    return CreateSuccessResponse(new JObject { ["tools"] = tools }, id);
                case "tools/call":
                    return await HandleToolsCall(request["params"] as JObject, id).ConfigureAwait(false);
                case "resources/list":
                    return CreateSuccessResponse(new JObject { ["resources"] = GetDefinedResources() }, id);
                case "resources/templates/list":
                    return CreateSuccessResponse(new JObject { ["resourceTemplates"] = GetDefinedResourceTemplates() }, id);
                case "resources/read":
                    return CreateErrorResponse(-32601, "resources/read not implemented", id);
                case "prompts/list":
                    return CreateSuccessResponse(new JObject { ["prompts"] = GetDefinedPrompts() }, id);
                case "prompts/get":
                    return CreateErrorResponse(-32000, "prompts not implemented", id);
                case "completion/complete":
                    return CreateSuccessResponse(new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } }, id);
                case "logging/setLevel":
                    return CreateSuccessResponse(new JObject(), id);
                default:
                    return CreateErrorResponse(-32601, $"Method not found: {method}", id);
            }
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", id);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject parms, JToken id)
    {
        if (parms == null) return CreateErrorResponse(-32602, "params object required", id);
        var toolName = parms["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName)) return CreateErrorResponse(-32602, "Tool name required", id);

        var tools = await GetDynamicToolsAsync().ConfigureAwait(false);
        if (!tools.Any(t => t["name"]?.ToString() == toolName)) return CreateErrorResponse(-32601, $"Unknown tool: {toolName}", id);

        var arguments = parms["arguments"] as JObject ?? new JObject();
        try
        {
            var result = await ExecuteToolByName(toolName, arguments).ConfigureAwait(false);
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString() } },
                ["isError"] = false
            }, id);
        }
        catch (ArgumentException ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    // ---------- Tool Execution ----------
    private void InitializeToolHandlers()
    {
        _toolHandlers = new Dictionary<string, Func<JObject, Task<JObject>>>
        {
            // Orchestration tools (use constants)
            [TOOL_DISCOVER_FUNCTIONS] = ExecuteDiscoverFunctions,
            [TOOL_INVOKE_TOOL] = ExecuteInvokeTool,
            [TOOL_ORCHESTRATE_PLAN] = ExecuteOrchestratePlan,
            [TOOL_LEARN_PATTERNS] = ExecuteLearnPatterns,
            
            // Dataverse tools (inline strings - definitions in agents.md)
            ["dataverse_list_rows"] = ExecuteListRows,
            ["dataverse_get_row"] = ExecuteGetRow,
            ["dataverse_create_row"] = ExecuteCreateRow,
            ["dataverse_update_row"] = ExecuteUpdateRow,
            ["dataverse_delete_row"] = ExecuteDeleteRow,
            ["dataverse_fetchxml"] = ExecuteFetchXml,
            ["dataverse_execute_action"] = ExecuteAction,
            ["dataverse_associate"] = ExecuteAssociate,
            ["dataverse_disassociate"] = ExecuteDisassociate,
            ["dataverse_upsert"] = ExecuteUpsert,
            ["dataverse_create_multiple"] = ExecuteCreateMultiple,
            ["dataverse_update_multiple"] = ExecuteUpdateMultiple,
            ["dataverse_upsert_multiple"] = ExecuteUpsertMultiple,
            ["dataverse_batch"] = ExecuteBatch,
            ["dataverse_execute_function"] = ExecuteFunction,
            ["dataverse_query_expand"] = ExecuteQueryExpand,
            ["dataverse_get_entity_metadata"] = ExecuteGetEntityMetadata,
            ["dataverse_get_attribute_metadata"] = ExecuteGetAttributeMetadata,
            ["dataverse_get_relationships"] = ExecuteGetRelationships,
            ["dataverse_count_rows"] = ExecuteCountRows,
            ["dataverse_aggregate"] = ExecuteAggregate,
            ["dataverse_execute_saved_query"] = ExecuteSavedQuery,
            ["dataverse_upload_attachment"] = ExecuteUploadAttachment,
            ["dataverse_download_attachment"] = ExecuteDownloadAttachment,
            ["dataverse_track_changes"] = ExecuteTrackChanges,
            ["dataverse_get_global_optionsets"] = ExecuteGetGlobalOptionSets,
            ["dataverse_get_business_rules"] = ExecuteGetBusinessRules,
            ["dataverse_get_security_roles"] = ExecuteGetSecurityRoles,
            ["dataverse_get_async_operation"] = ExecuteGetAsyncOperation,
            ["dataverse_list_async_operations"] = ExecuteListAsyncOperations,
            ["dataverse_detect_duplicates"] = ExecuteDetectDuplicates,
            ["dataverse_get_audit_history"] = ExecuteGetAuditHistory,
            ["dataverse_get_plugin_traces"] = ExecuteGetPluginTraces,
            ["dataverse_whoami"] = ExecuteWhoAmI,
            ["dataverse_set_state"] = ExecuteSetState,
            ["dataverse_assign"] = ExecuteAssign,
            ["dataverse_merge"] = ExecuteMerge,
            ["dataverse_share"] = ExecuteShare,
            ["dataverse_unshare"] = ExecuteUnshare,
            ["dataverse_modify_access"] = ExecuteModifyAccess,
            ["dataverse_add_team_members"] = ExecuteAddTeamMembers,
            ["dataverse_remove_team_members"] = ExecuteRemoveTeamMembers,
            ["dataverse_retrieve_principal_access"] = ExecuteRetrievePrincipalAccess,
            ["dataverse_initialize_from"] = ExecuteInitializeFrom,
            ["dataverse_calculate_rollup"] = ExecuteCalculateRollup
        };
    }

    private async Task<JObject> ExecuteToolByName(string toolName, JObject args)
    {
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"Executing tool: {toolName}");
        this.Context.Logger.LogDebug($"Tool arguments: {args?.ToString(Newtonsoft.Json.Formatting.None)}");
        
        if (_toolHandlers == null) InitializeToolHandlers();
        
        try
        {
            if (_toolHandlers.TryGetValue(toolName, out var handler))
            {
                var result = await handler(args).ConfigureAwait(false);
                
                _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
                {
                    ["toolName"] = toolName,
                    ["success"] = "true",
                    ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
                });
                
                return result;
            }
            
            // Check if it's a discovered table tool (format: {operation}_{table})
            var parts = toolName.Split(new[] { '_' }, 2);
            if (parts.Length == 2)
            {
                var operation = parts[0];
                var table = parts[1];
                
                // Route to appropriate handler based on operation
                switch (operation)
                {
                    case "create":
                        return await ExecuteDiscoveredCreate(table, args).ConfigureAwait(false);
                    case "get":
                        return await ExecuteDiscoveredGet(table, args).ConfigureAwait(false);
                    case "update":
                        return await ExecuteDiscoveredUpdate(table, args).ConfigureAwait(false);
                    case "delete":
                        return await ExecuteDiscoveredDelete(table, args).ConfigureAwait(false);
                    case "list":
                        return await ExecuteDiscoveredList(table, args).ConfigureAwait(false);
                    case "query":
                        return await ExecuteDiscoveredQuery(table, args).ConfigureAwait(false);
                    case "customapi":
                        // Custom API format: customapi_{uniquename}
                        return await ExecuteDiscoveredCustomAPI(table, args).ConfigureAwait(false);
                    case "action":
                        // Action/Function format: action_{name}
                        return await ExecuteDiscoveredAction(table, args).ConfigureAwait(false);
                }
            }
            
            throw new Exception($"Unknown tool: {toolName}");
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
            {
                ["toolName"] = toolName,
                ["success"] = "false",
                ["error"] = ex.Message,
                ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            });
            throw;
        }
    }

    private async Task<JObject> ExecuteListRows(JObject args)
    {
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtPage = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtPage, impersonateUserId).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFormatted = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFormatted, impersonateUserId).ConfigureAwait(false);
        return resp;
    }
    
    // ---------- Discovered Table Tool Handlers ----------
    
    /// <summary>
    /// Execute create operation for discovered table
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredCreate(string table, JObject args)
    {
        // Extract non-metadata fields for record creation
        var record = new JObject();
        foreach (var prop in args.Properties())
        {
            record[prop.Name] = prop.Value;
        }
        
        var url = BuildDataverseUrl(table);
        return await SendDataverseRequest(HttpMethod.Post, url, record).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute get operation for discovered table
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredGet(string table, JObject args)
    {
        var id = Require(args, "id");
        var select = args["select"]?.ToString();
        var qs = string.IsNullOrWhiteSpace(select) ? string.Empty : $"?$select={Uri.EscapeDataString(select)}";
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)}){qs}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute update operation for discovered table
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredUpdate(string table, JObject args)
    {
        var id = Require(args, "id");
        
        // Extract update fields (exclude id)
        var record = new JObject();
        foreach (var prop in args.Properties())
        {
            if (prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                continue;
            record[prop.Name] = prop.Value;
        }
        
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})");
        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute delete operation for discovered table
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredDelete(string table, JObject args)
    {
        var id = Require(args, "id");
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})");
        return await SendDataverseRequest(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute list operation for discovered table
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredList(string table, JObject args)
    {
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute query operation for discovered table (advanced with expand)
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredQuery(string table, JObject args)
    {
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>();
        var expand = args["expand"]?.ToString();

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        if (top.HasValue) query.Add("$top=" + Math.Min(Math.Max(top.Value, 1), 100));
        if (!string.IsNullOrWhiteSpace(expand)) query.Add("$expand=" + Uri.EscapeDataString(expand));

        var url = BuildDataverseUrl($"{table}{(query.Count > 0 ? "?" + string.Join("&", query) : "")}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Execute discovered Custom API
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredCustomAPI(string uniqueName, JObject args)
    {
        try
        {
            // Need to look up the Custom API metadata to determine binding type and function/action
            // For now, we'll try to infer from the cached discovered tools
            var discoveredTools = new JArray();
            if (!string.IsNullOrWhiteSpace(_cachedDiscoveredToolsJson))
            {
                try
                {
                    discoveredTools = JArray.Parse(_cachedDiscoveredToolsJson);
                }
                catch
                {
                    // Fall through to query metadata directly
                }
            }
            
            // Find the tool definition to get metadata
            var toolName = $"customapi_{uniqueName}";
            var toolDef = discoveredTools.FirstOrDefault(t => t["name"]?.ToString() == toolName) as JObject;
            
            int bindingType = 0;
            bool isFunction = false;
            string boundEntityLogicalName = null;
            
            if (toolDef != null)
            {
                bindingType = toolDef["x-ms-bindingtype"]?.Value<int?>() ?? 0;
                isFunction = toolDef["x-ms-isfunction"]?.Value<bool?>() ?? false;
                boundEntityLogicalName = toolDef["x-ms-boundentity"]?.ToString();
            }
            else
            {
                // Fallback: query the Custom API metadata directly
                var metadataQuery = $"customapis?$select=bindingtype,isfunction,boundentitylogicalname&$filter=uniquename eq '{uniqueName}'";
                var metadataUrl = BuildDataverseUrl(metadataQuery);
                var metadataResult = await SendDataverseRequest(HttpMethod.Get, metadataUrl, null).ConfigureAwait(false);
                var apiRecords = metadataResult["value"] as JArray;
                
                if (apiRecords == null || apiRecords.Count == 0)
                {
                    throw new Exception($"Custom API '{uniqueName}' not found");
                }
                
                var apiRecord = apiRecords[0] as JObject;
                bindingType = apiRecord["bindingtype"]?.Value<int>() ?? 0;
                isFunction = apiRecord["isfunction"]?.Value<bool>() ?? false;
                boundEntityLogicalName = apiRecord["boundentitylogicalname"]?.ToString();
            }
            
            // Build the URL based on binding type
            string url;
            switch (bindingType)
            {
                case 0: // Global unbound
                    url = BuildDataverseUrl(uniqueName);
                    break;
                    
                case 1: // Entity-bound
                    var targetId = Require(args, "Target");
                    var entitySet = GetEntitySetName(boundEntityLogicalName);
                    url = BuildDataverseUrl($"{entitySet}({SanitizeGuid(targetId)})/Microsoft.Dynamics.CRM.{uniqueName}");
                    break;
                    
                case 2: // EntityCollection-bound
                    var collectionEntitySet = GetEntitySetName(boundEntityLogicalName);
                    url = BuildDataverseUrl($"{collectionEntitySet}/Microsoft.Dynamics.CRM.{uniqueName}");
                    break;
                    
                default:
                    throw new Exception($"Unknown binding type: {bindingType}");
            }
            
            // Execute based on function vs action
            if (isFunction)
            {
                // Functions: GET with parameters in URL
                url = AppendFunctionParameters(url, args);
                return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            }
            else
            {
                // Actions: POST with parameters in body
                var body = FormatActionParameters(args);
                return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Custom API execution failed: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Execute discovered Dataverse Action/Function (SdkMessage)
    /// </summary>
    private async Task<JObject> ExecuteDiscoveredAction(string actionName, JObject args)
    {
        try
        {
            // Build URL for the action/function
            // Most Dataverse actions/functions are POST operations
            var url = BuildDataverseUrl(actionName);
            
            // Format parameters
            var body = new JObject();
            foreach (var prop in args.Properties())
            {
                body[prop.Name] = prop.Value;
            }
            
            // Execute the action
            return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Action/Function execution failed: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Append function parameters to URL
    /// </summary>
    private string AppendFunctionParameters(string url, JObject args)
    {
        var parameters = new List<string>();
        
        foreach (var prop in args.Properties())
        {
            if (prop.Name.Equals("Target", StringComparison.OrdinalIgnoreCase))
                continue; // Target is in the URL path, not a parameter
                
            var value = FormatFunctionParameterValue(prop.Value);
            parameters.Add($"{prop.Name}={value}");
        }
        
        if (parameters.Count > 0)
            return $"{url}({string.Join(",", parameters)})";
        
        return url;
    }
    
    /// <summary>
    /// Format parameter value for function URL
    /// </summary>
    private string FormatFunctionParameterValue(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.Boolean:
                return value.Value<bool>().ToString().ToLower();
            case JTokenType.String:
                return $"'{value.ToString().Replace("'", "''")}'";
            case JTokenType.Integer:
            case JTokenType.Float:
                return value.ToString();
            default:
                return $"'{value.ToString().Replace("'", "''")}'";
        }
    }
    
    /// <summary>
    /// Format action parameters for request body
    /// </summary>
    private JObject FormatActionParameters(JObject args)
    {
        var body = new JObject();
        
        foreach (var prop in args.Properties())
        {
            if (prop.Name.Equals("Target", StringComparison.OrdinalIgnoreCase))
                continue; // Target is in the URL path, not a parameter
                
            body[prop.Name] = prop.Value;
        }
        
        return body;
    }
    
    /// <summary>
    /// Get entity set name from logical name (with common mappings)
    /// </summary>
    private string GetEntitySetName(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
            throw new Exception("Entity logical name is required");
        
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
            { "task", "tasks" },
            { "email", "emails" },
            { "appointment", "appointments" },
            { "phonecall", "phonecalls" },
            { "incident", "incidents" },
            { "quote", "quotes" },
            { "salesorder", "salesorders" },
            { "invoice", "invoices" }
        };
        
        if (commonMappings.TryGetValue(logicalName, out var entitySet))
            return entitySet;
        
        // Default pluralization: add 's'
        return logicalName + "s";
    }

    private async Task<JObject> ExecuteGetRow(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var select = args["select"]?.ToString();
        var includeFmtGetRow = args["includeFormatted"]?.Value<bool?>() ?? false;
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var qs = string.IsNullOrWhiteSpace(select) ? string.Empty : $"?$select={Uri.EscapeDataString(select)}";
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)}){qs}");
        return await SendDataverseRequest(HttpMethod.Get, url, null, includeFmtGetRow, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var url = BuildDataverseUrl(table);
        return await SendDataverseRequest(HttpMethod.Post, url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteRow(JObject args)
    {
        var table = Require(args, "table");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(HttpMethod.Delete, url, null, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteFetchXml(JObject args)
    {
        var table = Require(args, "table");
        var fetchXml = Require(args, "fetchXml");
        var url = BuildDataverseUrl($"{table}?fetchXml={Uri.EscapeDataString(fetchXml)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAction(JObject args)
    {
        var action = Require(args, "action");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound action: POST /table(id)/Microsoft.Dynamics.CRM.action
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{action}");
        }
        else
        {
            // Unbound action: POST /action
            url = BuildDataverseUrl(action);
        }

        return await SendDataverseRequest(HttpMethod.Post, url, parameters).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedTable = Require(args, "relatedTable");
        var relatedId = Require(args, "relatedId");

        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        var relatedUri = BuildDataverseUrl($"{relatedTable}({SanitizeGuid(relatedId)})");
        
        // Need to build full URI for @odata.id
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var body = new JObject
        {
            ["@odata.id"] = $"{baseUrl}{relatedUri}"
        };

        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDisassociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedId = args["relatedId"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(relatedId))
        {
            // Collection-valued navigation property: DELETE /table(id)/navprop(relatedId)/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}({SanitizeGuid(relatedId)})/$ref");
        }
        else
        {
            // Single-valued navigation property: DELETE /table(id)/navprop/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        }

        return await SendDataverseRequest(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsert(JObject args)
    {
        var table = Require(args, "table");
        var keys = RequireObject(args, "keys");
        var record = RequireObject(args, "record");

        // Build alternate key selector: table(key1=value1,key2=value2)
        var keyPairs = new List<string>();
        foreach (var prop in keys.Properties())
        {
            var val = prop.Value.ToString();
            // Quote string values
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            keyPairs.Add($"{prop.Name}={quotedVal}");
        }
        var keySelector = string.Join(",", keyPairs);
        var url = BuildDataverseUrl($"{table}({keySelector})");

        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("CreateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpdateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsertMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpsertMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteBatch(JObject args)
    {
        var requestsToken = args["requests"];
        if (requestsToken == null || requestsToken.Type != JTokenType.Array)
            throw new ArgumentException("requests must be an array");

        var requests = requestsToken as JArray;
        var batchId = Guid.NewGuid().ToString();
        var batchBoundary = $"batch_{batchId}";
        var changesetId = Guid.NewGuid().ToString();
        var changesetBoundary = $"changeset_{changesetId}";

        var batchContent = new StringBuilder();
        batchContent.AppendLine($"--{batchBoundary}");
        batchContent.AppendLine($"Content-Type: multipart/mixed;boundary={changesetBoundary}");
        batchContent.AppendLine();

        int contentId = 1;
        foreach (var req in requests)
        {
            var reqObj = req as JObject;
            if (reqObj == null) continue;

            var method = reqObj["method"]?.ToString()?.ToUpper() ?? "GET";
            var url = reqObj["url"]?.ToString() ?? "";
            var bodyObj = reqObj["body"] as JObject;

            batchContent.AppendLine($"--{changesetBoundary}");
            batchContent.AppendLine("Content-Type: application/http");
            batchContent.AppendLine("Content-Transfer-Encoding: binary");
            batchContent.AppendLine($"Content-ID: {contentId++}");
            batchContent.AppendLine();
            batchContent.AppendLine($"{method} {url} HTTP/1.1");
            batchContent.AppendLine("Content-Type: application/json");
            batchContent.AppendLine();
            if (bodyObj != null)
            {
                batchContent.AppendLine(bodyObj.ToString(Newtonsoft.Json.Formatting.None));
            }
            batchContent.AppendLine();
        }

        batchContent.AppendLine($"--{changesetBoundary}--");
        batchContent.AppendLine($"--{batchBoundary}--");

        var batchReq = new HttpRequestMessage(HttpMethod.Post, BuildDataverseUrl("$batch"));
        batchReq.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        batchReq.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        batchReq.Content = new StringContent(batchContent.ToString(), Encoding.UTF8, $"multipart/mixed;boundary={batchBoundary}");

        var response = await this.Context.SendAsync(batchReq, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase,
                ["body"] = content
            };
        }

        // Parse multipart response (simplified - return raw for now)
        return new JObject
        {
            ["status"] = (int)response.StatusCode,
            ["batchResponse"] = content
        };
    }

    private async Task<JObject> ExecuteFunction(JObject args)
    {
        var function = Require(args, "function");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        // Build query string from parameters
        var queryParts = new List<string>();
        foreach (var prop in parameters.Properties())
        {
            var val = prop.Value.ToString();
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            queryParts.Add($"{prop.Name}={quotedVal}");
        }
        var queryString = queryParts.Any() ? "?" + string.Join("&", queryParts) : "";

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound function: GET /table(id)/Microsoft.Dynamics.CRM.function(params)
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{function}{queryString}");
        }
        else
        {
            // Unbound function: GET /function(params)
            url = BuildDataverseUrl($"{function}{queryString}");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteQueryExpand(JObject args)
    {
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtNext = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtNext).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var expand = Require(args, "expand");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFmt = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        query.Add("$expand=" + Uri.EscapeDataString(expand));
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFmt).ConfigureAwait(false);
        return resp;
    }

    private async Task<JObject> ExecuteGetEntityMetadata(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,SchemaName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute,EntitySetName");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAttributeMetadata(JObject args)
    {
        var table = Require(args, "table");
        var attribute = args["attribute"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{attribute}')?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }
        else
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetRelationships(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/ManyToOneRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var manyToOne = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/OneToManyRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var oneToMany = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        return new JObject
        {
            ["manyToOne"] = manyToOne["value"] ?? new JArray(),
            ["oneToMany"] = oneToMany["value"] ?? new JArray()
        };
    }

    private async Task<JObject> ExecuteCountRows(JObject args)
    {
        var table = Require(args, "table");
        var filter = args["filter"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query.Add("$filter=" + Uri.EscapeDataString(filter));
        }
        query.Add("$count=true");
        query.Add("$top=0"); // Don't return any rows, just the count
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
        
        return new JObject
        {
            ["count"] = resp["@odata.count"] ?? 0
        };
    }

    private async Task<JObject> ExecuteAggregate(JObject args)
    {
        var table = Require(args, "table");
        var aggregateAttribute = Require(args, "aggregateAttribute");
        var aggregateFunction = Require(args, "aggregateFunction");
        var groupBy = args["groupBy"]?.ToString();
        var filter = args["filter"]?.ToString();
        var filterOperator = args["filterOperator"]?.ToString();
        var filterValue = args["filterValue"]?.ToString();
        
        // Build FetchXML for aggregation
        var fetchXml = $"<fetch aggregate='true'>";
        fetchXml += $"<entity name='{table}'>";
        fetchXml += $"<attribute name='{aggregateAttribute}' alias='result' aggregate='{aggregateFunction}' />";
        
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            fetchXml += $"<attribute name='{groupBy}' alias='groupby' groupby='true' />";
        }
        
        if (!string.IsNullOrWhiteSpace(filter) && !string.IsNullOrWhiteSpace(filterOperator) && !string.IsNullOrWhiteSpace(filterValue))
        {
            fetchXml += $"<filter><condition attribute='{filter}' operator='{filterOperator}' value='{System.Security.SecurityElement.Escape(filterValue)}' /></filter>";
        }
        
        fetchXml += "</entity></fetch>";
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSavedQuery(JObject args)
    {
        var table = Require(args, "table");
        var viewName = Require(args, "viewName");
        var top = args["top"]?.Value<int?>() ?? 5;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Lookup the saved query by name
        var queryFilter = $"returnedtypecode eq '{table}' and name eq '{viewName.Replace("'", "''")}'";
        var queryUrl = BuildDataverseUrl($"savedqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
        var queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
        
        var savedQueries = queryResult["value"] as JArray;
        if (savedQueries == null || savedQueries.Count == 0)
        {
            // Try user query
            queryUrl = BuildDataverseUrl($"userqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
            queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
            savedQueries = queryResult["value"] as JArray;
            
            if (savedQueries == null || savedQueries.Count == 0)
            {
                return new JObject
                {
                    ["error"] = $"Saved query '{viewName}' not found for table '{table}'"
                };
            }
        }
        
        var fetchXml = savedQueries[0]["fetchxml"]?.ToString();
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return new JObject { ["error"] = "FetchXML is empty" };
        }
        
        // Modify FetchXML to apply top limit
        fetchXml = System.Text.RegularExpressions.Regex.Replace(
            fetchXml, 
            "<fetch", 
            $"<fetch top='{top}'", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUploadAttachment(JObject args)
    {
        var regarding = Require(args, "regarding");
        var regardingType = Require(args, "regardingType");
        var fileName = Require(args, "fileName");
        var mimeType = Require(args, "mimeType");
        var content = Require(args, "content");
        var subject = args["subject"]?.ToString() ?? fileName;
        
        var annotation = new JObject
        {
            ["subject"] = subject,
            ["filename"] = fileName,
            ["mimetype"] = mimeType,
            ["documentbody"] = content,
            ["objectid_" + regardingType + "@odata.bind"] = $"/{regardingType}s({SanitizeGuid(regarding)})"
        };
        
        var url = BuildDataverseUrl("annotations");
        return await SendDataverseRequest(HttpMethod.Post, url, annotation).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDownloadAttachment(JObject args)
    {
        var annotationId = Require(args, "annotationId");
        var url = BuildDataverseUrl($"annotations({SanitizeGuid(annotationId)})?$select=filename,mimetype,documentbody,filesize");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteTrackChanges(JObject args)
    {
        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var deltaToken = args["deltaToken"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        
        if (!string.IsNullOrWhiteSpace(deltaToken))
        {
            query.Add("$deltatoken=" + Uri.EscapeDataString(deltaToken));
        }
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        req.Headers.TryAddWithoutValidation("Prefer", "odata.track-changes");
        
        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["error"] = "Change tracking may not be enabled for this table",
                ["body"] = TryParseJson(content)
            };
        }
        
        return TryParseJson(content);
    }

    private async Task<JObject> ExecuteGetGlobalOptionSets(JObject args)
    {
        var optionSetName = args["optionSetName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(optionSetName))
        {
            url = BuildDataverseUrl($"GlobalOptionSetDefinitions(Name='{optionSetName}')?$select=Name,DisplayName,Options");
        }
        else
        {
            url = BuildDataverseUrl("GlobalOptionSetDefinitions?$select=Name,DisplayName,Options");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBusinessRules(JObject args)
    {
        var table = Require(args, "table");
        
        // Query workflows where category = 2 (business rule) and primary entity matches
        var filter = $"category eq 2 and primaryentity eq '{table}'";
        var url = BuildDataverseUrl($"workflows?$select=name,description,statecode,statuscode,xaml&$filter={Uri.EscapeDataString(filter)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetSecurityRoles(JObject args)
    {
        var roleName = args["roleName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var filter = $"name eq '{roleName.Replace("'", "''")}'";
            url = BuildDataverseUrl($"roles?$select=name,roleid,businessunitid&$filter={Uri.EscapeDataString(filter)}");
        }
        else
        {
            url = BuildDataverseUrl("roles?$select=name,roleid,businessunitid&$top=50");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAsyncOperation(JObject args)
    {
        var asyncOperationId = Require(args, "asyncOperationId");
        var url = BuildDataverseUrl($"asyncoperations({SanitizeGuid(asyncOperationId)})?$select=name,statuscode,statecode,message,friendlymessage,errorcode,createdon,completedon");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListAsyncOperations(JObject args)
    {
        var status = args["status"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=name,statuscode,statecode,message,createdon,completedon");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Map friendly status to statuscode values
            var statusCode = status.ToLower() switch
            {
                "inprogress" => "20",
                "succeeded" => "30",
                "failed" => "31",
                "canceled" => "32",
                _ => null
            };
            
            if (statusCode != null)
            {
                query.Add($"$filter=statuscode eq {statusCode}");
            }
        }
        
        var url = BuildDataverseUrl($"asyncoperations?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectDuplicates(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        
        // Use RetrieveDuplicates action
        var requestBody = new JObject
        {
            ["BusinessEntity"] = record,
            ["MatchingEntityName"] = table,
            ["PagingInfo"] = new JObject
            {
                ["PageNumber"] = 1,
                ["Count"] = 50
            }
        };
        
        var url = BuildDataverseUrl("RetrieveDuplicates");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAuditHistory(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Query audit table for the specific record
        var filter = $"objectid/Id eq {SanitizeGuid(recordId)} and objecttypecode eq '{table}'";
        var url = BuildDataverseUrl($"audits?$select=createdon,action,userid,attributemask,changedata&$filter={Uri.EscapeDataString(filter)}&$orderby=createdon desc&$top={top}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetPluginTraces(JObject args)
    {
        var correlationId = args["correlationId"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=typename,messageblock,exceptiondetails,createdon,correlationid");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query.Add($"$filter=correlationid eq {SanitizeGuid(correlationId)}");
        }
        
        var url = BuildDataverseUrl($"plugintracelog?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteWhoAmI(JObject args)
    {
        var url = BuildDataverseUrl("WhoAmI");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSetState(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var state = args["state"]?.Value<int?>() ?? throw new Exception("'state' is required");
        var status = args["status"]?.Value<int?>() ?? throw new Exception("'status' is required");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["State"] = state,
            ["Status"] = status
        };
        
        var url = BuildDataverseUrl("SetState");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssign(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var assigneeId = Require(args, "assigneeId");
        var assigneeType = Require(args, "assigneeType");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Assignee"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{assigneeType}",
                [assigneeType + "id"] = SanitizeGuid(assigneeId)
            }
        };
        
        var url = BuildDataverseUrl("Assign");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteMerge(JObject args)
    {
        var table = Require(args, "table");
        var targetId = Require(args, "targetId");
        var subordinateId = Require(args, "subordinateId");
        var updateContent = args["updateContent"] as JObject;
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(targetId)
            },
            ["SubordinateId"] = SanitizeGuid(subordinateId),
            ["PerformParentingChecks"] = false
        };
        
        if (updateContent != null)
        {
            requestBody["UpdateContent"] = updateContent;
        }
        
        var url = BuildDataverseUrl("Merge");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteShare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var principalType = Require(args, "principalType");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = $"Microsoft.Dynamics.CRM.{principalType}",
                    [principalType + "id"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("GrantAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUnshare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Revokee"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RevokeAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteModifyAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                    ["systemuserid"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("ModifyAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("AddMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRemoveTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("RemoveMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRetrievePrincipalAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Principal"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RetrievePrincipalAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteInitializeFrom(JObject args)
    {
        var sourceTable = Require(args, "sourceTable");
        var sourceId = Require(args, "sourceId");
        var targetTable = Require(args, "targetTable");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{sourceTable}",
                [sourceTable + "id"] = SanitizeGuid(sourceId)
            },
            ["TargetEntityName"] = targetTable,
            ["TargetFieldType"] = 0
        };
        
        var url = BuildDataverseUrl("InitializeFrom");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCalculateRollup(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var fieldName = Require(args, "fieldName");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["FieldName"] = fieldName
        };
        
        var url = BuildDataverseUrl("CalculateRollupField");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private string BuildDataverseUrl(string relativePath)
    {
        var clean = relativePath.TrimStart('/');
        return $"/api/data/v9.2/{clean}";
    }

    private async Task<JObject> SendDataverseRequest(HttpMethod method, string url, JObject body, bool includeFormatted = false, string impersonateUserId = null, string correlationId = null)
    {
        // Ensure absolute URL for Dataverse requests
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
            url = $"{baseUrl}{url}";
            this.Context.Logger.LogDebug($"Constructed absolute URL: {url}");
        }
        
        var req = new HttpRequestMessage(method, url);
        
        // Copy OAuth token from incoming request to Dataverse request
        if (this.Context.Request.Headers.Authorization != null)
        {
            req.Headers.Authorization = this.Context.Request.Headers.Authorization;
            this.Context.Logger.LogDebug("OAuth token forwarded to Dataverse request");
        }
        
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");

        // Impersonation header
        if (!string.IsNullOrWhiteSpace(impersonateUserId))
        {
            req.Headers.TryAddWithoutValidation("MSCRMCallerID", SanitizeGuid(impersonateUserId));
        }

        // Telemetry/correlation header for request tracking
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            req.Headers.TryAddWithoutValidation("x-ms-correlation-request-id", correlationId);
        }

        // Include formatted values for lookups/optionsets/money fields
        if (includeFormatted)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");
        }

        // Ask Dataverse to return representations on writes
        if (method == HttpMethod.Post || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            var preferValue = includeFormatted ? "return=representation,odata.include-annotations=\"*\"" : "return=representation";
            req.Headers.Remove("Prefer");
            req.Headers.TryAddWithoutValidation("Prefer", preferValue);
        }

        // Use wildcard ETag to allow overwrite when no specific ETag is supplied
        if (method == HttpMethod.Delete || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.TryAddWithoutValidation("If-Match", "*");
        }

        if (body != null)
        {
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Dataverse API error: {response.StatusCode} - {url}");
            // Enhanced error parsing for Dataverse errors
            var errorObj = new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase
            };

            try
            {
                var errorBody = JObject.Parse(content);
                var error = errorBody["error"];
                if (error != null)
                {
                    errorObj["errorCode"] = error["code"];
                    errorObj["message"] = error["message"];
                    errorObj["details"] = error["innererror"]?["message"] ?? error["message"];
                }
                else
                {
                    errorObj["body"] = errorBody;
                }
            }
            catch
            {
                errorObj["body"] = content;
            }

            return errorObj;
        }

        return TryParseJson(content);
    }

    private JObject TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JObject();
        try { return JObject.Parse(text); }
        catch { return new JObject { ["text"] = text }; }
    }

    private string Require(JObject obj, string name)
    {
        var val = obj?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{name} is required");
        return val;
    }

    private JObject RequireObject(JObject obj, string name)
    {
        var token = obj?[name] as JObject;
        if (token == null) throw new ArgumentException($"{name} must be an object");
        return token;
    }

    private string SanitizeGuid(string id)
    {
        var trimmed = id.Trim();
        if (Guid.TryParse(trimmed, out var g)) return g.ToString();
        throw new ArgumentException("id must be a GUID");
    }

    // ---------- Query/Metadata Handlers ----------
    private async Task<HttpResponseMessage> HandleGetTables()
    {
        try
        {
            // Query EntityDefinitions for common tables
            var url = "/api/data/v9.2/EntityDefinitions?$select=LogicalName,DisplayName&$filter=IsValidForAdvancedFind eq true and IsCustomizable/Value eq true";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve tables",
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var entities = data["value"] as JArray;
            var tables = new JArray();
            
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    var logicalName = entity["LogicalName"]?.ToString();
                    var displayName = entity["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        tables.Add(new JObject
                        {
                            ["name"] = logicalName,
                            ["displayName"] = displayName ?? logicalName
                        });
                    }
                }
            }

            return CreateHttpResponse(HttpStatusCode.OK, tables);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetTableSchema(string table)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Query EntityDefinition for attributes
            var url = $"/api/data/v9.2/EntityDefinitions(LogicalName='{table}')?$select=LogicalName&$expand=Attributes($select=LogicalName,DisplayName,AttributeType)";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve table schema",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var attributes = data["Attributes"] as JArray;
            var properties = new JObject();
            
            if (attributes != null)
            {
                foreach (var attr in attributes)
                {
                    var logicalName = attr["LogicalName"]?.ToString();
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        properties[logicalName] = new JObject
                        {
                            ["type"] = "string",  // Simplified - all properties as string
                            ["description"] = attr["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString() ?? logicalName
                        };
                    }
                }
            }

            var schema = new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = properties
                }
            };

            return CreateHttpResponse(HttpStatusCode.OK, new JObject
            {
                ["schema"] = schema
            });
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleListRecords(string table, string select, string filter, int top)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}";
            var queryParams = new List<string>();
            
            if (!string.IsNullOrEmpty(select))
                queryParams.Add($"$select={select}");
            if (!string.IsNullOrEmpty(filter))
                queryParams.Add($"$filter={filter}");
            if (top > 0 && top <= 50)
                queryParams.Add($"$top={top}");

            if (queryParams.Any())
                url += "?" + string.Join("&", queryParams);

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve records",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data["value"] ?? new JArray());
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetRecord(string table, string id, string select)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }
            if (string.IsNullOrEmpty(id))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "ID parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}({id})";
            if (!string.IsNullOrEmpty(select))
                url += $"?$select={select}";

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve record",
                    ["table"] = table,
                    ["id"] = id,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    // ---------- Helpers ----------
    // Connection parameters are not used (OAuth handles Dataverse token; AI key is constant)

    private HttpResponseMessage CreateHttpResponse(HttpStatusCode statusCode, JToken content)
    {
        var resp = new HttpResponseMessage(statusCode);
        resp.Content = CreateJsonContent(content.ToString());
        return resp;
    }

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    private HttpResponseMessage CreateErrorResponse(int code, string message, JToken id)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject { ["code"] = code, ["message"] = message },
            ["id"] = id
        };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    // ---------- Application Insights Telemetry ----------
    
    /// <summary>
    /// Send custom event to Application Insights (fire-and-forget)
    /// </summary>
    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint") 
                ?? "https://dc.services.visualstudio.com/";
            
            if (string.IsNullOrEmpty(instrumentationKey))
                return; // Telemetry disabled
            
            var telemetryData = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = instrumentationKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties)
                    }
                }
            };
            
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var request = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(telemetryData.ToString(), Encoding.UTF8, "application/json")
            };
            
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors - don't fail the main request
        }
    }
    
    private string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}



