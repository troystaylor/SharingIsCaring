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
    private const string SERVER_NAME = "sharepoint-embedded-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-06-18";
    private static bool _isInitialized = false;
    private static string _logLevel = "info";
    
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject { ["name"] = "get_containers", ["description"] = "List all file storage containers in the tenant" },
        new JObject { ["name"] = "create_container", ["description"] = "Create a new file storage container" },
        new JObject { ["name"] = "get_container", ["description"] = "Get container details" },
        new JObject { ["name"] = "delete_container", ["description"] = "Delete a file storage container" },
        new JObject { ["name"] = "restore_container", ["description"] = "Restore a deleted container" },
        new JObject { ["name"] = "list_container_items", ["description"] = "List files and folders in a container" },
        new JObject { ["name"] = "upload_file", ["description"] = "Upload a file to a container" },
        new JObject { ["name"] = "delete_file", ["description"] = "Delete a file from a container" }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var requestPath = this.Context.Request.RequestUri.AbsolutePath;
        
        if (requestPath.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var requestJson = JObject.Parse(requestBody);
            return await HandleMCPRequestAsync(requestJson).ConfigureAwait(false);
        }
        
        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        {
            Content = new StringContent("{\"error\": \"This connector supports MCP protocol at /mcp endpoint\"}")
        };
    }

    private async Task<HttpResponseMessage> HandleMCPRequestAsync(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];

        switch (method)
        {
            case "initialize":
                return CreateSuccessResponse(new JObject
                {
                    ["protocolVersion"] = PROTOCOL_VERSION,
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = SERVER_NAME,
                        ["version"] = SERVER_VERSION
                    }
                }, id);
            case "tools/list":
                return CreateSuccessResponse(new JObject
                {
                    ["tools"] = AVAILABLE_TOOLS
                }, id);
            default:
                return CreateErrorResponse(id, -32601, "Method not found");
        }
    }

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject 
        { 
            ["jsonrpc"] = "2.0", 
            ["result"] = result, 
            ["id"] = id 
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        return response;
    }

    private HttpResponseMessage CreateErrorResponse(JToken id, int code, string message)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            },
            ["id"] = id
        };
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        return response;
    }
}