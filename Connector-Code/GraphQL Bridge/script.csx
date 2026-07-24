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
/// GraphQL Bridge - JSON -> GraphQL -> JSON template for Power Platform Custom Connectors.
///
/// Purpose:
///   Lets a REST-style custom connector operation (friendly JSON in / friendly JSON out) sit
///   in front of any GraphQL endpoint (e.g. https://api.example.com/graphql).
///
/// What it solves (GraphQL vs. Power Platform Swagger mismatch):
///   1. GraphQL always POSTs a { query, variables } envelope to a single endpoint. Power Platform
///      operations are modeled per-path/verb. This script builds the envelope from a flat JSON body.
///   2. GraphQL returns HTTP 200 even when the query fails (errors live in an "errors" array).
///      This script inspects "errors" and converts them into a real non-2xx HttpResponseMessage so
///      Power Automate / Copilot Studio see the failure instead of a silent success.
///   3. GraphQL nests results under a "data" root. Swagger response schemas map poorly to that.
///      This script unwraps "data" (and optionally a per-operation sub-path) before returning.
///
/// Two ways to supply the query per operation:
///   A) Server-defined (recommended): register the GraphQL document in GraphQlDocuments keyed by
///      OperationId. The connector body is then just the variables (flat JSON) - the caller never
///      sees GraphQL. Reserved body keys "variables" and "operationName" are honored if present.
///   B) Passthrough: if no document is registered for the OperationId, the body itself must contain
///      { "query": "...", "variables": { ... }, "operationName": "..." }. Useful for a generic
///      "RunQuery" operation.
///
/// Auth: the incoming Context.Request already carries the connector's Authorization header. This
/// script only changes the method, URI path, and content - credentials pass through untouched.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION
    // ========================================

    // Path appended to the connector host to reach the GraphQL endpoint.
    // Most GraphQL APIs serve everything from a single path such as /graphql.
    private const string GraphQlPath = "/graphql";

    // Server-defined GraphQL documents keyed by OperationId. Add one entry per typed operation.
    // The connector request body is passed straight through as GraphQL "variables".
    //
    //   ["GetItemById"] = @"query GetItemById($id: ID!) {
    //       item(id: $id) { id name status createdAt }
    //   }",
    private static readonly Dictionary<string, string> GraphQlDocuments =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Register your operations here. Leave empty to force passthrough mode for every operation.
        };

    // Optional per-operation unwrap path INTO the "data" object, dot-delimited.
    // Lets a typed operation return exactly the nested object its Swagger schema expects.
    //   ["GetItemById"] = "item"                  -> returns data.item
    //   ["ListCustomerOrders"] = "customer.orders" -> returns data.customer.orders
    // If omitted/null, the entire "data" object is returned.
    private static readonly Dictionary<string, string> ResponseRootPaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
        };

    // OperationId reserved for generic passthrough (body carries its own query). Optional.
    private const string PassthroughOperationId = "RunQuery";

    // Body keys that are NOT treated as GraphQL variables when a server-defined document is used.
    private static readonly HashSet<string> ReservedBodyKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "query", "variables", "operationName" };

    // ========================================
    // ENTRY POINT
    // ========================================
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            return await this.HandleGraphQlOperation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return this.MakeError(HttpStatusCode.InternalServerError, "SCRIPT_ERROR", ex.Message);
        }
    }

    private async Task<HttpResponseMessage> HandleGraphQlOperation()
    {
        var operationId = this.Context.OperationId ?? string.Empty;

        // 1. Read and parse the incoming connector body (may be empty).
        var rawBody = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject body;
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            body = new JObject();
        }
        else
        {
            try
            {
                body = JObject.Parse(rawBody);
            }
            catch (JsonException ex)
            {
                return this.MakeError(HttpStatusCode.BadRequest, "INVALID_JSON",
                    $"Request body is not valid JSON: {ex.Message}");
            }
        }

        // 2. Build the GraphQL { query, variables, operationName } envelope.
        string query;
        JToken variables;
        string operationName = body.Value<string>("operationName");

        if (GraphQlDocuments.TryGetValue(operationId, out var document))
        {
            // Mode A - server-defined document. Body is the variables (unless it explicitly
            // supplies a "variables" object).
            query = document;
            variables = body["variables"] ?? this.StripReservedKeys(body);
        }
        else
        {
            // Mode B - passthrough. Body must carry the query itself.
            query = body.Value<string>("query");
            if (string.IsNullOrWhiteSpace(query))
            {
                return this.MakeError(HttpStatusCode.BadRequest, "NO_QUERY",
                    $"No GraphQL document registered for operation '{operationId}', and the request body " +
                    $"does not contain a 'query' field. Register the operation in GraphQlDocuments or send " +
                    $"a body shaped like {{ \"query\": \"...\", \"variables\": {{ }} }}.");
            }
            variables = body["variables"] ?? new JObject();
        }

        var envelope = new JObject { ["query"] = query };
        if (variables != null && variables.Type != JTokenType.Null)
        {
            envelope["variables"] = variables;
        }
        if (!string.IsNullOrWhiteSpace(operationName))
        {
            envelope["operationName"] = operationName;
        }

        // 3. Retarget the existing (already-authenticated) request at the GraphQL endpoint.
        var uriBuilder = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = GraphQlPath,
            Query = string.Empty
        };

        this.Context.Request.Method = HttpMethod.Post;
        this.Context.Request.RequestUri = uriBuilder.Uri;
        this.Context.Request.Content = new StringContent(
            envelope.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        // 4. Send.
        var response = await this.Context
            .SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        var responseString = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // 5. Transport-level failure (non-2xx from the GraphQL server): pass it through as-is.
        if (!response.IsSuccessStatusCode)
        {
            response.Content = CreateJsonContent(
                this.WrapTransportError(response.StatusCode, responseString).ToString(Newtonsoft.Json.Formatting.None));
            return response;
        }

        // 6. Parse the GraphQL payload.
        JObject payload;
        try
        {
            payload = JObject.Parse(string.IsNullOrWhiteSpace(responseString) ? "{}" : responseString);
        }
        catch (JsonException ex)
        {
            return this.MakeError(HttpStatusCode.BadGateway, "INVALID_GRAPHQL_RESPONSE",
                $"GraphQL endpoint returned a non-JSON response: {ex.Message}");
        }

        // 7. GraphQL-level errors (HTTP 200 + "errors" array) -> convert to a real failure.
        var errors = payload["errors"] as JArray;
        if (errors != null && errors.Count > 0)
        {
            // If "data" is null the query fully failed -> 400. If data is partial, still surface 400
            // so callers don't silently consume incomplete results.
            var errorResponse = new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = "GRAPHQL_ERROR",
                    ["message"] = string.Join(" | ",
                        errors.Select(e => e.Value<string>("message") ?? e.ToString(Newtonsoft.Json.Formatting.None))),
                    ["errors"] = errors
                }
            };
            if (payload["data"] != null && payload["data"].Type != JTokenType.Null)
            {
                errorResponse["error"]["partialData"] = payload["data"];
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent(errorResponse.ToString(Newtonsoft.Json.Formatting.None))
            };
        }

        // 8. Unwrap "data" (and optional per-operation sub-path) so response schemas map cleanly.
        var data = payload["data"] ?? new JObject();
        JToken result = data;

        if (ResponseRootPaths.TryGetValue(operationId, out var rootPath) && !string.IsNullOrWhiteSpace(rootPath))
        {
            result = this.SelectPath(data, rootPath) ?? JValue.CreateNull();
        }

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // HELPERS
    // ========================================

    // Everything in the body except reserved keys becomes GraphQL variables.
    private JObject StripReservedKeys(JObject body)
    {
        var variables = new JObject();
        foreach (var prop in body.Properties())
        {
            if (!ReservedBodyKeys.Contains(prop.Name))
            {
                variables[prop.Name] = prop.Value;
            }
        }
        return variables;
    }

    // Walk a dot-delimited path into a JToken (e.g. "brand.assets").
    private JToken SelectPath(JToken token, string dotPath)
    {
        var current = token;
        foreach (var segment in dotPath.Split('.'))
        {
            if (current == null || current.Type == JTokenType.Null)
            {
                return null;
            }
            current = current[segment];
        }
        return current;
    }

    private JObject WrapTransportError(HttpStatusCode status, string rawBody)
    {
        JToken details;
        try
        {
            details = string.IsNullOrWhiteSpace(rawBody) ? JValue.CreateNull() : JToken.Parse(rawBody);
        }
        catch
        {
            details = rawBody;
        }

        return new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = "HTTP_" + (int)status,
                ["message"] = $"GraphQL endpoint returned HTTP {(int)status} ({status}).",
                ["details"] = details
            }
        };
    }

    private HttpResponseMessage MakeError(HttpStatusCode status, string code, string message)
    {
        var body = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return new HttpResponseMessage(status)
        {
            Content = CreateJsonContent(body.ToString(Newtonsoft.Json.Formatting.None))
        };
    }
}
