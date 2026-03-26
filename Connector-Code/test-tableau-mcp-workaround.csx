/// <summary>
/// Complete Tableau MCP Connector Script with Schema Transformation
/// 
/// Workaround for Tableau MCP connector issue:
/// System.FormatException when exclusiveMinimum is set to integer instead of boolean.
/// 
/// This script intercepts MCP responses and normalizes exclusiveMinimum/exclusiveMaximum 
/// properties in tool schemas to maintain OpenAPI 2.0 compatibility.
/// 
/// Issue: Tableau's schema may define exclusiveMinimum as an integer (e.g., 0 instead of false)
/// which causes FormatException in Power Platform's MCP handler.
/// 
/// Solution: Transform the MCP response content to convert integer constraint values to boolean.
/// </summary>

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            // Get the operation ID to route requests
            var operationId = this.Context.OperationId;

            // Handle MCP endpoint
            if (operationId == "InvokeMCP")
            {
                return await HandleMCPRequest();
            }

            // For other operations, pass through to backend
            return await ForwardRequestToBackend();
        }
        catch (Exception ex)
        {
            // Log error
            this.Context.Logger.LogError($"[Tableau MCP] Error in ExecuteAsync: {ex.Message}");
            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Handle MCP endpoint requests with schema transformation.
    /// </summary>
    private async Task<HttpResponseMessage> HandleMCPRequest()
    {
        try
        {
            // Read the request body
            var requestContent = await this.Context.Request.Content.ReadAsStringAsync();

            // Forward to Tableau backend
            var backendResponse = await ForwardRequestToBackend();

            if (!backendResponse.IsSuccessStatusCode)
            {
                return backendResponse;
            }

            // Parse the response
            var responseContent = await backendResponse.Content.ReadAsStringAsync();

            // Transform the response to fix schema issues
            var transformer = new TableauMCPSchemaTransformer();
            var transformedContent = transformer.FixExclusiveConstraintsInResponse(responseContent);

            // Return transformed response
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(transformedContent, Encoding.UTF8, "application/json")
            };
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[Tableau MCP] Error handling MCP request: {ex.Message}");
            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Forward the request to the Tableau backend.
    /// </summary>
    private async Task<HttpResponseMessage> ForwardRequestToBackend()
    {
        // Create a new request to the backend
        var backendUri = BuildBackendUri();
        var request = new HttpRequestMessage(this.Context.Request.Method, backendUri);

        // Copy headers
        foreach (var header in this.Context.Request.Headers)
        {
            // Skip host header
            if (header.Key.ToLower() != "host")
            {
                request.Headers.Add(header.Key, string.Join(",", header.Value));
            }
        }

        // Copy content if present
        if (this.Context.Request.Content != null)
        {
            var content = await this.Context.Request.Content.ReadAsStringAsync();
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        // Add authorization header from context
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        // Send request using context's HTTP client
        return await this.Context.SendAsync(request, this.CancellationToken);
    }

    /// <summary>
    /// Build the backend URI based on the request path.
    /// </summary>
    private Uri BuildBackendUri()
    {
        var baseUri = new Uri("https://api.tableau.com");
        var path = this.Context.Request.RequestUri.PathAndQuery;
        return new Uri(baseUri, path);
    }

    /// <summary>
    /// Create a JSON error response.
    /// </summary>
    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        var errorResponse = new
        {
            jsonrpc = "2.0",
            error = new
            {
                code = -32603,
                message = "Internal error",
                data = message
            },
            id = (string)null
        };

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(errorResponse),
                Encoding.UTF8,
                "application/json")
        };
    }
}

/// <summary>
/// Schema transformation utility for Tableau MCP responses.
/// Fixes OpenAPI 2.0 compliance issues where Tableau defines constraints as integers.
/// </summary>
public class TableauMCPSchemaTransformer
{
    /// <summary>
    /// Transform MCP response content to fix schema validation issues.
    /// </summary>
    public string FixExclusiveConstraintsInResponse(string responseContent)
    {
        try
        {
            if (string.IsNullOrEmpty(responseContent))
                return responseContent;

            var responseJson = JObject.Parse(responseContent);

            // Fix schema issues in the response
            FixExclusiveConstraintsInTools(responseJson);

            return responseJson.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Exception ex)
        {
            // Log error but don't fail - return original content
            // In production, would log to Application Insights
            return responseContent;
        }
    }

    /// <summary>
    /// Recursively walk through the tools array and fix exclusive constraints.
    /// </summary>
    private void FixExclusiveConstraintsInTools(JObject responseJson)
    {
        var tools = responseJson["result"]?["tools"] as JArray;
        if (tools == null)
            return;

        foreach (var tool in tools.OfType<JObject>())
        {
            // Navigate to inputSchema
            var inputSchema = tool["inputSchema"] as JObject;
            if (inputSchema != null)
            {
                FixExclusiveConstraintsInSchema(inputSchema);
            }
        }
    }

    /// <summary>
    /// Fix exclusive constraints in a schema object (handles nested structures).
    /// </summary>
    private void FixExclusiveConstraintsInSchema(JObject schema)
    {
        if (schema == null)
            return;

        // Fix properties object
        var properties = schema["properties"] as JObject;
        if (properties != null)
        {
            foreach (var prop in properties.Properties())
            {
                var propSchema = prop.Value as JObject;
                if (propSchema != null)
                {
                    FixExclusiveConstraintsInProperty(propSchema);
                }
            }
        }

        // Handle items (array type)
        var items = schema["items"] as JObject;
        if (items != null)
        {
            FixExclusiveConstraintsInProperty(items);
        }

        // Handle additionalProperties
        var additionalProps = schema["additionalProperties"] as JObject;
        if (additionalProps != null)
        {
            FixExclusiveConstraintsInProperty(additionalProps);
        }

        // Handle composite schemas (oneOf, anyOf, allOf)
        FixCompositeSchemas(schema);
    }

    /// <summary>
    /// Fix exclusive constraints in a single property schema.
    /// </summary>
    private void FixExclusiveConstraintsInProperty(JObject schema)
    {
        if (schema == null)
            return;

        // Fix exclusiveMinimum
        ConvertConstraintToBoolean(schema, "exclusiveMinimum");

        // Fix exclusiveMaximum
        ConvertConstraintToBoolean(schema, "exclusiveMaximum");

        // Fix multipleOf if it's an integer when it should be a number
        var multipleOf = schema["multipleOf"];
        if (multipleOf?.Type == JTokenType.Integer)
        {
            schema["multipleOf"] = (double)multipleOf.Value<int>();
        }

        // Recurse into nested structures
        var properties = schema["properties"] as JObject;
        if (properties != null)
        {
            foreach (var prop in properties.Properties())
            {
                var propSchema = prop.Value as JObject;
                if (propSchema != null)
                {
                    FixExclusiveConstraintsInProperty(propSchema);
                }
            }
        }

        var items = schema["items"] as JObject;
        if (items != null)
        {
            FixExclusiveConstraintsInProperty(items);
        }

        var additionalProps = schema["additionalProperties"] as JObject;
        if (additionalProps != null)
        {
            FixExclusiveConstraintsInProperty(additionalProps);
        }

        FixCompositeSchemas(schema);
    }

    /// <summary>
    /// Fix composite schemas (oneOf, anyOf, allOf).
    /// </summary>
    private void FixCompositeSchemas(JObject schema)
    {
        if (schema == null)
            return;

        foreach (var compositeKeyword in new[] { "oneOf", "anyOf", "allOf" })
        {
            var compositeArray = schema[compositeKeyword] as JArray;
            if (compositeArray != null)
            {
                foreach (var item in compositeArray.OfType<JObject>())
                {
                    FixExclusiveConstraintsInProperty(item);
                }
            }
        }
    }

    /// <summary>
    /// Convert a constraint (e.g., exclusiveMinimum) from integer to boolean.
    /// In OpenAPI 2.0: these must be boolean. If integer 0 = false, non-zero = true.
    /// </summary>
    private void ConvertConstraintToBoolean(JObject schema, string constraintName)
    {
        var constraint = schema[constraintName];
        if (constraint == null)
            return;

        // Already boolean - no change needed
        if (constraint.Type == JTokenType.Boolean)
            return;

        // Convert integer to boolean
        if (constraint.Type == JTokenType.Integer)
        {
            int intValue = constraint.Value<int>();
            schema[constraintName] = (intValue != 0);
            return;
        }

        // Convert float to boolean
        if (constraint.Type == JTokenType.Float)
        {
            double dblValue = constraint.Value<double>();
            schema[constraintName] = (dblValue != 0.0);
            return;
        }

        // Convert string to boolean
        if (constraint.Type == JTokenType.String)
        {
            string strValue = constraint.Value<string>();
            if (!bool.TryParse(strValue, out bool boolValue))
            {
                // If not a valid boolean string, treat as true if non-empty
                boolValue = !string.IsNullOrEmpty(strValue);
            }
            schema[constraintName] = boolValue;
        }
    }
}
