using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                Path = Context.Request.RequestUri?.AbsolutePath ?? "unknown"
            });

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                case "GetObjectSchema":
                    response = await HandleGetObjectSchemaAsync();
                    break;
                default:
                    response = await Context.SendAsync(Context.Request, CancellationToken);
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            throw;
        }
    }

    #region Dynamic Schema Handler

    private async Task<HttpResponseMessage> HandleGetObjectSchemaAsync()
    {
        // Extract sObject name from the request path
        var path = Context.Request.RequestUri.AbsolutePath;
        var segments = path.Split('/');
        var sobjectIndex = Array.IndexOf(segments, "sobjects") + 1;
        var sObject = sobjectIndex > 0 && sobjectIndex < segments.Length
            ? segments[sobjectIndex]
            : "Account";

        // Call describe endpoint for the sObject
        var baseUri = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var describeUrl = $"{baseUri}/services/data/v66.0/sobjects/{sObject}/describe";

        var describeRequest = new HttpRequestMessage(HttpMethod.Get, describeUrl);
        if (Context.Request.Headers.Authorization != null)
            describeRequest.Headers.Authorization = Context.Request.Headers.Authorization;
        describeRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var describeResponse = await Context.SendAsync(describeRequest, CancellationToken);
        var describeContent = await describeResponse.Content.ReadAsStringAsync();

        if (!describeResponse.IsSuccessStatusCode)
        {
            return describeResponse;
        }

        var describeResult = JObject.Parse(describeContent);
        var fields = describeResult["fields"] as JArray ?? new JArray();

        // Build JSON Schema from Salesforce field metadata
        var properties = new JObject();
        var required = new JArray();

        foreach (var field in fields)
        {
            var name = field["name"]?.ToString();
            var label = field["label"]?.ToString() ?? name;
            var sfType = field["type"]?.ToString() ?? "string";
            var createable = field["createable"]?.ToObject<bool>() ?? false;
            var nillable = field["nillable"]?.ToObject<bool>() ?? true;

            // Only include createable fields (skip formula, auto-number, etc.)
            if (!createable || string.IsNullOrEmpty(name))
                continue;

            var jsonType = MapSalesforceTypeToJsonType(sfType);
            properties[name] = new JObject
            {
                ["type"] = jsonType,
                ["x-ms-summary"] = label,
                ["description"] = label
            };

            // Add picklist values if available
            var picklistValues = field["picklistValues"] as JArray;
            if (picklistValues != null && picklistValues.Count > 0)
            {
                var enumValues = new JArray();
                foreach (var pv in picklistValues)
                {
                    if (pv["active"]?.ToObject<bool>() == true)
                        enumValues.Add(pv["value"]?.ToString());
                }
                if (enumValues.Count > 0)
                    properties[name]["enum"] = enumValues;
            }

            if (!nillable)
                required.Add(name);
        }

        // Return the schema response
        var schemaResponse = new JObject
        {
            ["schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(schemaResponse.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private string MapSalesforceTypeToJsonType(string sfType)
    {
        switch (sfType?.ToLower())
        {
            case "int":
            case "integer":
            case "long":
                return "integer";
            case "double":
            case "currency":
            case "percent":
                return "number";
            case "boolean":
                return "boolean";
            case "date":
            case "datetime":
            case "time":
                return "string"; // ISO 8601 format
            default:
                return "string";
        }
    }

    #endregion

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await Context.Request.Content.ReadAsStringAsync();
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("MCPRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasParams = @params.HasValues
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCall(@params, requestId, correlationId);

            case "resources/list":
                return HandleResourcesList(requestId);

            case "resources/read":
                return HandleResourcesRead(@params, requestId);

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "salesforce-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Query Tools
            CreateTool("query", "Execute a SOQL query to retrieve records. Use for searching and filtering data.",
                new JObject
                {
                    ["soql"] = new JObject { ["type"] = "string", ["description"] = "SOQL query (e.g., SELECT Id, Name FROM Account WHERE Industry = 'Technology' LIMIT 10)" }
                },
                new[] { "soql" }),

            CreateTool("search", "Execute a SOSL search across multiple objects for full-text search.",
                new JObject
                {
                    ["sosl"] = new JObject { ["type"] = "string", ["description"] = "SOSL query (e.g., FIND {Acme} IN ALL FIELDS RETURNING Account(Id, Name), Contact(Id, Name))" }
                },
                new[] { "sosl" }),

            // Record Tools
            CreateTool("get_record", "Get a single Salesforce record by ID.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact, Lead, Opportunity)" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID (15 or 18 character)" },
                    ["fields"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated fields to return (optional)" }
                },
                new[] { "object", "id" }),

            CreateTool("create_record", "Create a new record in Salesforce.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact, Lead)" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values for the new record" }
                },
                new[] { "object", "data" }),

            CreateTool("update_record", "Update an existing Salesforce record.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values to update" }
                },
                new[] { "object", "id", "data" }),

            CreateTool("delete_record", "Delete a Salesforce record.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID" }
                },
                new[] { "object", "id" }),

            // Metadata Tools
            CreateTool("list_objects", "List all available sObjects in the org.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("describe_object", "Get detailed metadata about an sObject including fields and relationships.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact)" }
                },
                new[] { "object" }),

            CreateTool("get_limits", "Get the org's API limits and current usage.",
                new JObject(),
                Array.Empty<string>()),

            // Analytics Tools
            CreateTool("list_reports", "List available reports in the org.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("run_report", "Execute a Salesforce report and return results.",
                new JObject
                {
                    ["report_id"] = new JObject { ["type"] = "string", ["description"] = "Report ID" }
                },
                new[] { "report_id" }),

            CreateTool("list_dashboards", "List available dashboards in the org.",
                new JObject(),
                Array.Empty<string>()),

            // Chatter Tools
            CreateTool("post_to_chatter", "Post a message to Chatter.",
                new JObject
                {
                    ["subject_id"] = new JObject { ["type"] = "string", ["description"] = "User ID or Record ID to post to" },
                    ["message"] = new JObject { ["type"] = "string", ["description"] = "Message text" }
                },
                new[] { "subject_id", "message" }),

            CreateTool("get_chatter_feed", "Get Chatter feed for news, company, or a record.",
                new JObject
                {
                    ["feed_type"] = new JObject { ["type"] = "string", ["description"] = "Feed type: news, company, record, user-profile" }
                },
                new[] { "feed_type" }),

            // Composite Tool
            CreateTool("composite", "Execute multiple API operations in a single request.",
                new JObject
                {
                    ["all_or_none"] = new JObject { ["type"] = "boolean", ["description"] = "Roll back all on any failure" },
                    ["requests"] = new JObject 
                    { 
                        ["type"] = "array", 
                        ["description"] = "Array of requests with method, url, referenceId, and optional body",
                        ["items"] = new JObject { ["type"] = "object" }
                    }
                },
                new[] { "requests" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private HttpResponseMessage HandleResourcesList(JToken requestId)
    {
        var resources = new JArray
        {
            new JObject
            {
                ["uri"] = "salesforce://reference/soql",
                ["name"] = "SOQL Query Reference",
                ["description"] = "Comprehensive guide to Salesforce Object Query Language (SOQL) including syntax, operators, functions, and examples",
                ["mimeType"] = "text/plain"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = resources });
    }

    private HttpResponseMessage HandleResourcesRead(JObject @params, JToken requestId)
    {
        var uri = @params["uri"]?.ToString();

        if (uri == "salesforce://reference/soql")
        {
            var content = GetSoqlReference();
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "text/plain",
                        ["text"] = content
                    }
                }
            });
        }

        return CreateJsonRpcErrorResponse(requestId, -32002, "Resource not found", uri);
    }

    private string GetSoqlReference()
    {
        return @"# SOQL (Salesforce Object Query Language) Reference

## Basic Syntax
SELECT field1, field2, ... FROM ObjectName [WHERE conditions] [ORDER BY field] [LIMIT n] [OFFSET n]

## SELECT Clause
- SELECT Id, Name, Industry FROM Account
- SELECT COUNT() FROM Account (aggregate only)
- SELECT COUNT(Id), Industry FROM Account GROUP BY Industry
- SELECT * is NOT supported - must specify fields

## FROM Clause
Single object: FROM Account
With relationship (parent): FROM Contact (queries Contact, can traverse to Account)
With relationship (child): SELECT Id, (SELECT Id FROM Contacts) FROM Account

## WHERE Clause Operators

### Comparison Operators
=       Equal: WHERE Industry = 'Technology'
!=      Not equal: WHERE Industry != 'Technology'
<       Less than: WHERE Amount < 10000
<=      Less or equal: WHERE Amount <= 10000
>       Greater than: WHERE Amount > 10000
>=      Greater or equal: WHERE Amount >= 10000

### String Operators
LIKE    Pattern match: WHERE Name LIKE 'Acme%'
        % = zero or more chars, _ = single char
        WHERE Email LIKE '%@gmail.com'

### Set Operators
IN          WHERE Industry IN ('Technology', 'Finance', 'Healthcare')
NOT IN      WHERE Industry NOT IN ('Agriculture', 'Energy')
INCLUDES    For multi-select picklists: WHERE Interests__c INCLUDES ('Golf', 'Tennis')
EXCLUDES    WHERE Interests__c EXCLUDES ('Golf')

### Logical Operators
AND     WHERE Industry = 'Technology' AND AnnualRevenue > 1000000
OR      WHERE Industry = 'Technology' OR Industry = 'Finance'
NOT     WHERE NOT Industry = 'Technology'
        Parentheses for grouping: WHERE (Industry = 'Tech' OR Industry = 'Finance') AND AnnualRevenue > 1000000

### Null Checks
= null      WHERE Email = null
!= null     WHERE Email != null

## Date Literals (Use instead of hardcoded dates)

### Relative Today
TODAY                   Today
YESTERDAY               Yesterday
TOMORROW                Tomorrow

### Relative Days
LAST_N_DAYS:n           Last n days: WHERE CreatedDate = LAST_N_DAYS:30
NEXT_N_DAYS:n           Next n days: WHERE CloseDate = NEXT_N_DAYS:7

### Week
THIS_WEEK               Current week (Sun-Sat)
LAST_WEEK               Previous week
NEXT_WEEK               Next week
LAST_N_WEEKS:n          Last n weeks
NEXT_N_WEEKS:n          Next n weeks

### Month
THIS_MONTH              Current month
LAST_MONTH              Previous month
NEXT_MONTH              Next month
LAST_N_MONTHS:n         Last n months: WHERE CreatedDate = LAST_N_MONTHS:3
NEXT_N_MONTHS:n         Next n months

### Quarter
THIS_QUARTER            Current quarter
LAST_QUARTER            Previous quarter
NEXT_QUARTER            Next quarter
THIS_FISCAL_QUARTER     Current fiscal quarter
LAST_FISCAL_QUARTER     Previous fiscal quarter
NEXT_FISCAL_QUARTER     Next fiscal quarter
LAST_N_QUARTERS:n       Last n quarters
NEXT_N_QUARTERS:n       Next n quarters

### Year
THIS_YEAR               Current year
LAST_YEAR               Previous year
NEXT_YEAR               Next year
THIS_FISCAL_YEAR        Current fiscal year
LAST_FISCAL_YEAR        Previous fiscal year
NEXT_FISCAL_YEAR        Next fiscal year
LAST_N_YEARS:n          Last n years
NEXT_N_YEARS:n          Next n years

## Aggregate Functions
COUNT()     Total records: SELECT COUNT() FROM Account
COUNT(field) Non-null count: SELECT COUNT(Industry) FROM Account
SUM(field)  Sum: SELECT SUM(Amount) FROM Opportunity
AVG(field)  Average: SELECT AVG(Amount) FROM Opportunity
MIN(field)  Minimum: SELECT MIN(Amount) FROM Opportunity
MAX(field)  Maximum: SELECT MAX(Amount) FROM Opportunity

### GROUP BY
SELECT Industry, COUNT(Id) FROM Account GROUP BY Industry
SELECT Industry, SUM(AnnualRevenue) FROM Account GROUP BY Industry

### HAVING (filter aggregates)
SELECT Industry, COUNT(Id) cnt FROM Account GROUP BY Industry HAVING COUNT(Id) > 10

## ORDER BY
ORDER BY Name               Ascending (default)
ORDER BY Name ASC           Ascending explicit
ORDER BY Name DESC          Descending
ORDER BY Name ASC NULLS FIRST   Nulls first
ORDER BY Name ASC NULLS LAST    Nulls last
ORDER BY CreatedDate DESC, Name ASC   Multiple fields

## LIMIT and OFFSET
LIMIT n         Return max n records: LIMIT 100
OFFSET n        Skip first n records: OFFSET 10
                Max OFFSET: 2000
                Combine: LIMIT 10 OFFSET 20 (records 21-30)

## Relationship Queries

### Parent-to-Child (Subquery)
SELECT Id, Name, (SELECT Id, LastName FROM Contacts) FROM Account
- Child relationship name is usually plural (Contacts, Opportunities, Cases)
- Check object metadata for exact relationship name

### Child-to-Parent (Dot Notation)
SELECT Id, LastName, Account.Name, Account.Industry FROM Contact
- Use relationship name (usually same as parent object)
- Can traverse up to 5 levels: Account.Owner.Manager.Name

## Common Standard Objects

### Account
Key fields: Id, Name, Industry, Type, Website, Phone, BillingCity, BillingState, BillingCountry, AnnualRevenue, NumberOfEmployees, OwnerId, CreatedDate
Relationships: Owner (User), Contacts, Opportunities, Cases

### Contact
Key fields: Id, FirstName, LastName, Name, Email, Phone, Title, Department, AccountId, OwnerId, MailingCity, MailingState
Relationships: Account, Owner (User), Cases, Opportunities (via OpportunityContactRole)

### Lead
Key fields: Id, FirstName, LastName, Name, Email, Phone, Company, Title, Status, LeadSource, Industry, ConvertedAccountId, ConvertedContactId, ConvertedOpportunityId
Relationships: Owner (User), ConvertedAccount, ConvertedContact, ConvertedOpportunity

### Opportunity
Key fields: Id, Name, Amount, StageName, CloseDate, Probability, Type, LeadSource, AccountId, OwnerId, IsClosed, IsWon
Relationships: Account, Owner (User), OpportunityLineItems, OpportunityContactRoles

### Case
Key fields: Id, CaseNumber, Subject, Description, Status, Priority, Origin, Type, AccountId, ContactId, OwnerId, IsClosed
Relationships: Account, Contact, Owner (User), CaseComments

### Task
Key fields: Id, Subject, Description, Status, Priority, ActivityDate, WhoId (Contact/Lead), WhatId (Account/Opportunity/etc), OwnerId, IsClosed
Relationships: Who (Contact or Lead), What (any object), Owner

### Event
Key fields: Id, Subject, Description, StartDateTime, EndDateTime, IsAllDayEvent, WhoId, WhatId, OwnerId
Relationships: Who, What, Owner

### User
Key fields: Id, Username, Email, FirstName, LastName, Name, Title, Department, IsActive, ProfileId, UserRoleId, ManagerId
Relationships: Profile, UserRole, Manager

## Common Query Examples

### Find accounts by industry
SELECT Id, Name, Industry, AnnualRevenue FROM Account WHERE Industry = 'Technology' LIMIT 100

### Find contacts with email at a company
SELECT Id, FirstName, LastName, Email FROM Contact WHERE Account.Name = 'Acme Corp' AND Email != null

### Find open opportunities closing this month
SELECT Id, Name, Amount, StageName, CloseDate, Account.Name FROM Opportunity WHERE IsClosed = false AND CloseDate = THIS_MONTH ORDER BY CloseDate ASC

### Find high-value opportunities
SELECT Id, Name, Amount, StageName, Account.Name FROM Opportunity WHERE Amount > 100000 AND StageName != 'Closed Lost' ORDER BY Amount DESC LIMIT 20

### Count opportunities by stage
SELECT StageName, COUNT(Id), SUM(Amount) FROM Opportunity WHERE IsClosed = false GROUP BY StageName

### Find recent leads
SELECT Id, Name, Company, Email, Status, CreatedDate FROM Lead WHERE CreatedDate = LAST_N_DAYS:7 ORDER BY CreatedDate DESC

### Find accounts with contacts
SELECT Id, Name, (SELECT Id, Name, Email FROM Contacts WHERE Email != null) FROM Account WHERE Industry = 'Technology' LIMIT 50

### Find contacts and their account info
SELECT Id, Name, Email, Account.Name, Account.Industry FROM Contact WHERE Account.Industry = 'Technology' LIMIT 100

### Search across name fields
SELECT Id, Name, Phone FROM Account WHERE Name LIKE '%software%'

### Find records created by specific user
SELECT Id, Name, CreatedDate FROM Account WHERE CreatedById = '005XXXXXXXXXXXX' AND CreatedDate = THIS_MONTH

## Tips and Best Practices

1. Always specify needed fields - SELECT * is not supported
2. Use LIMIT to avoid returning too many records (max 50,000 per query)
3. Use date literals instead of hardcoded dates for maintainability
4. Use relationship queries to reduce API calls
5. Index fields in WHERE clauses for better performance (Id, Name, OwnerId, CreatedDate, custom fields with External ID)
6. Use COUNT() for existence checks instead of retrieving records
7. Escape single quotes in strings: WHERE Name = 'O\\'Reilly'
8. Use bind variables in Apex for dynamic values (prevents SOQL injection)
";
    }

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("MCPToolCall", new
        {
            CorrelationId = correlationId,
            Tool = toolName,
            HasArguments = arguments.HasValues
        });

        try
        {
            var result = await ExecuteToolAsync(toolName, arguments);

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
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPToolError", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                ErrorMessage = ex.Message
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
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
            });
        }
    }

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // Query
            case "query":
                var soql = args["soql"]?.ToString();
                return await CallSalesforceApi("GET", $"/query?q={Uri.EscapeDataString(soql)}");

            case "search":
                var sosl = args["sosl"]?.ToString();
                return await CallSalesforceApi("GET", $"/search?q={Uri.EscapeDataString(sosl)}");

            // Records
            case "get_record":
                var fields = args["fields"]?.ToString();
                var getPath = $"/sobjects/{args["object"]}/{args["id"]}";
                if (!string.IsNullOrEmpty(fields)) getPath += $"?fields={fields}";
                return await CallSalesforceApi("GET", getPath);

            case "create_record":
                return await CallSalesforceApi("POST", $"/sobjects/{args["object"]}", args["data"] as JObject);

            case "update_record":
                await CallSalesforceApi("PATCH", $"/sobjects/{args["object"]}/{args["id"]}", args["data"] as JObject);
                return new JObject { ["success"] = true, ["id"] = args["id"] };

            case "delete_record":
                await CallSalesforceApi("DELETE", $"/sobjects/{args["object"]}/{args["id"]}");
                return new JObject { ["success"] = true, ["deleted"] = args["id"] };

            // Metadata
            case "list_objects":
                return await CallSalesforceApi("GET", "/sobjects");

            case "describe_object":
                return await CallSalesforceApi("GET", $"/sobjects/{args["object"]}/describe");

            case "get_limits":
                return await CallSalesforceApi("GET", "/limits");

            // Analytics
            case "list_reports":
                return await CallSalesforceApi("GET", "/analytics/reports");

            case "run_report":
                return await CallSalesforceApi("POST", $"/analytics/reports/{args["report_id"]}");

            case "list_dashboards":
                return await CallSalesforceApi("GET", "/analytics/dashboards");

            // Chatter
            case "post_to_chatter":
                var chatterBody = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["messageSegments"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "Text",
                                ["text"] = args["message"]?.ToString()
                            }
                        }
                    },
                    ["feedElementType"] = "FeedItem",
                    ["subjectId"] = args["subject_id"]?.ToString()
                };
                return await CallSalesforceApi("POST", "/chatter/feed-elements", chatterBody);

            case "get_chatter_feed":
                return await CallSalesforceApi("GET", $"/chatter/feeds/{args["feed_type"]}/feed-elements");

            // Composite
            case "composite":
                var requests = args["requests"] as JArray;
                var compositeBody = new JObject
                {
                    ["allOrNone"] = args["all_or_none"]?.ToObject<bool>() ?? false,
                    ["compositeRequest"] = requests
                };
                return await CallSalesforceApi("POST", "/composite", compositeBody);

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> CallSalesforceApi(string method, string path, JObject body = null)
    {
        var baseUri = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var fullPath = $"{baseUri}/services/data/v66.0{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);

        // Copy authorization header
        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Salesforce API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrEmpty(content))
            return new JObject();

        // Handle array responses
        if (content.TrimStart().StartsWith("["))
        {
            var arr = JArray.Parse(content);
            return new JObject { ["items"] = arr };
        }

        return JObject.Parse(content);
    }

    #endregion

    #region JSON-RPC Helpers

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
        if (data != null) error["data"] = data;

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

    #endregion

    #region Application Insights Logging

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return; // Telemetry disabled

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        }
        catch { } // Suppress telemetry errors
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

    #endregion
}
