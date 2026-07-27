using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Frontify custom connector - JSON -> GraphQL -> JSON bridge.
///
/// Every operation sends a flat JSON body; this script builds the Frontify GraphQL
/// { query, variables } envelope, POSTs it to https://{domain}.frontify.com/graphql,
/// converts GraphQL "errors" (returned with HTTP 200) into real HTTP failures, and
/// unwraps the "data" root so the Swagger response schemas map cleanly.
///
/// Two modes per OperationId:
///   A) Server-defined document (typed operations below) - body IS the GraphQL variables.
///   B) Passthrough ("RunQuery") - body carries its own { query, variables }.
///
/// Setup: replace FrontifyDomain with your Frontify instance subdomain, e.g. "demo" for
/// demo.frontify.com. Auth is a Frontify API token supplied as the connector's API key;
/// the script adds the "Bearer " prefix and the required X-Frontify-Beta header.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION
    // ========================================

    // Frontify instance subdomain. "demo" -> https://demo.frontify.com/graphql
    private const string FrontifyDomain = "[[REPLACE_WITH_FRONTIFY_DOMAIN]]";

    private static string GraphQlEndpoint => $"https://{FrontifyDomain}.frontify.com/graphql";

    private const string PassthroughOperationId = "RunQuery";

    // Server-defined GraphQL documents keyed by OperationId. Body is passed as variables.
    // Input objects are inlined (e.g. deleteAsset) so the connector body stays flat.
    private static readonly Dictionary<string, string> GraphQlDocuments =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GetAccount"] =
                "query GetAccount { account { id } }",

            ["ListBrands"] =
                "query ListBrands { brands { id name slug avatar } }",

            ["GetBrand"] =
                "query GetBrand($id: ID!) { brand(id: $id) { id name slug avatar } }",

            ["ListBrandLibraries"] =
                "query ListBrandLibraries($id: ID!, $limit: Int, $page: Int) { " +
                "brand(id: $id) { libraries(limit: $limit, page: $page) { total items { id name } } } }",

            ["GetLibrary"] =
                "query GetLibrary($id: ID!) { library(id: $id) { id name } }",

            ["ListLibraryAssets"] =
                "query ListLibraryAssets($id: ID!, $limit: Int, $page: Int) { " +
                "library(id: $id) { assets(limit: $limit, page: $page) { total items { id title } } } }",

            ["SearchLibraryAssets"] =
                "query SearchLibraryAssets($id: ID!, $limit: Int, $page: Int, $query: AssetQueryInput) { " +
                "library(id: $id) { assets(limit: $limit, page: $page, query: $query) { total items { id title } } } }",

            ["ListLibraryCollections"] =
                "query ListLibraryCollections($id: ID!, $limit: Int, $page: Int) { " +
                "library(id: $id) { collections(limit: $limit, page: $page) { total items { id name } } } }",

            ["ListLibraryMetadataProperties"] =
                "query ListLibraryMetadataProperties($id: ID!) { " +
                "library(id: $id) { customMetadataProperties { id name helpText isRequired } } }",

            ["ListAssetComments"] =
                "query ListAssetComments($id: ID!, $limit: Int, $page: Int) { " +
                "asset(id: $id) { comments(limit: $limit, page: $page) { total items { id content isResolved createdAt creator { id name } } } } }",

            ["ListRelatedAssets"] =
                "query ListRelatedAssets($id: ID!, $limit: Int, $page: Int) { " +
                "asset(id: $id) { relatedAssets(limit: $limit, page: $page) { total items { id title } } } }",

            ["GetAsset"] =
                "query GetAsset($id: ID!) { asset(id: $id) { " +
                "id title description status externalId author createdAt modifiedAt creator { id name } } }",

            ["GetCurrentUser"] =
                "query GetCurrentUser { currentUser { id name } }",

            ["UpdateAsset"] =
                "mutation UpdateAsset($id: ID!, $data: UpdateAssetDataInput!) { " +
                "updateAsset(input: { id: $id, data: $data }) { asset { id title description } } }",

            ["AddAssetTags"] =
                "mutation AddAssetTags($id: ID!, $tags: [TagInput]) { " +
                "addAssetTags(input: { id: $id, tags: $tags }) { asset { id title } } }",

            ["DeleteAsset"] =
                "mutation DeleteAsset($id: String!) { deleteAsset(input: { id: $id }) { id } }",

            ["GetAssetDownload"] =
                "query GetAssetDownload($id: ID!) { asset(id: $id) { id title assetType: __typename " +
                "... on Image { downloadUrl previewUrl filename extension size width height } " +
                "... on Document { downloadUrl previewUrl filename extension size width height pageCount } " +
                "... on File { downloadUrl previewUrl filename extension size } " +
                "... on Video { downloadUrl filename extension size } " +
                "... on Audio { downloadUrl filename extension size } } }",

            ["ListAccountUsers"] =
                "query ListAccountUsers($limit: Int, $page: Int) { " +
                "account { users(limit: $limit, page: $page) { total items { id name } } } }",

            ["ListAccountUserGroups"] =
                "query ListAccountUserGroups($limit: Int, $page: Int) { " +
                "account { userGroups(limit: $limit, page: $page) { total items { id name } } } }",

            ["BrowseLibrary"] =
                "query BrowseLibrary($id: ID!, $limit: Int, $page: Int) { library(id: $id) { browse { " +
                "folders(limit: $limit, page: $page) { total items { id name } } " +
                "assets(limit: $limit, page: $page) { total items { id title } } } } }",

            ["ListAssetRevisions"] =
                "query ListAssetRevisions($id: ID!, $limit: Int, $page: Int) { " +
                "asset(id: $id) { revisions(limit: $limit, page: $page) { total items { id createdAt creator { id name } } } } }",

            ["AddCollectionAssets"] =
                "mutation AddCollectionAssets($collectionId: ID!, $assetIds: [ID!]!) { " +
                "addCollectionAssets(input: { collectionId: $collectionId, assetIds: $assetIds }) { collection { id name } } }",

            ["AddCustomMetadata"] =
                "mutation AddCustomMetadata($parentIds: [ID!]!, $customMetadata: [CustomMetadataInput!]!) { " +
                "addCustomMetadata(input: { parentIds: $parentIds, customMetadata: $customMetadata }) { parentIds } }",
        };

    // Per-operation unwrap path INTO "data" (dot-delimited). Omit = return whole "data".
    private static readonly Dictionary<string, string> ResponseRootPaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GetAccount"] = "account",
            ["ListBrands"] = "brands",
            ["GetBrand"] = "brand",
            ["ListBrandLibraries"] = "brand.libraries",
            ["GetLibrary"] = "library",
            ["ListLibraryAssets"] = "library.assets",
            ["SearchLibraryAssets"] = "library.assets",
            ["ListLibraryCollections"] = "library.collections",
            ["ListLibraryMetadataProperties"] = "library.customMetadataProperties",
            ["ListAssetComments"] = "asset.comments",
            ["ListRelatedAssets"] = "asset.relatedAssets",
            ["GetAsset"] = "asset",
            ["GetCurrentUser"] = "currentUser",
            ["UpdateAsset"] = "updateAsset.asset",
            ["AddAssetTags"] = "addAssetTags.asset",
            ["DeleteAsset"] = "deleteAsset",
            ["GetAssetDownload"] = "asset",
            ["ListAccountUsers"] = "account.users",
            ["ListAccountUserGroups"] = "account.userGroups",
            ["BrowseLibrary"] = "library.browse",
            ["ListAssetRevisions"] = "asset.revisions",
            ["AddCollectionAssets"] = "addCollectionAssets.collection",
            ["AddCustomMetadata"] = "addCustomMetadata",
        };

    // "brandId" is a UI-only helper for cascading library pickers; never sent as a GraphQL variable.
    private static readonly HashSet<string> ReservedBodyKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "query", "variables", "operationName", "brandId" };

    // ========================================
    // ENTRY POINT
    // ========================================
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            if (string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            {
                return await this.HandleMcp().ConfigureAwait(false);
            }

            if (string.Equals(this.Context.OperationId, "UploadAsset", StringComparison.OrdinalIgnoreCase))
            {
                return await this.HandleUploadAsset().ConfigureAwait(false);
            }

            if (string.Equals(this.Context.OperationId, "ReplaceAsset", StringComparison.OrdinalIgnoreCase))
            {
                return await this.HandleReplaceAsset().ConfigureAwait(false);
            }

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
            query = document;
            variables = body["variables"] ?? this.StripReservedKeys(body);
        }
        else
        {
            query = body.Value<string>("query");
            if (string.IsNullOrWhiteSpace(query))
            {
                return this.MakeError(HttpStatusCode.BadRequest, "NO_QUERY",
                    $"No GraphQL document registered for operation '{operationId}', and the request body " +
                    $"does not contain a 'query' field. Use a typed operation or send a body shaped like " +
                    $"{{ \"query\": \"...\", \"variables\": {{ }} }}.");
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

        // 3. Retarget the request at the Frontify GraphQL endpoint.
        this.Context.Request.Method = HttpMethod.Post;
        this.Context.Request.RequestUri = new Uri(GraphQlEndpoint);
        this.Context.Request.Content = new StringContent(
            envelope.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        // Ensure Frontify-required headers and a Bearer-prefixed token.
        this.NormalizeAuthHeader();
        if (!this.Context.Request.Headers.Contains("X-Frontify-Beta"))
        {
            this.Context.Request.Headers.Add("X-Frontify-Beta", "enabled");
        }

        // 4. Send.
        var response = await this.Context
            .SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        var responseString = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // 5. Transport-level failure: pass through, wrapped.
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
                $"Frontify returned a non-JSON response: {ex.Message}");
        }

        // 7. GraphQL-level errors (HTTP 200 + "errors") -> convert to a real failure.
        var errors = payload["errors"] as JArray;
        if (errors != null && errors.Count > 0)
        {
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

        // 8. Unwrap "data" (and optional per-operation sub-path).
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
    // UPLOAD / REPLACE ASSET (uploadFile -> chunked PUT -> createAsset/replaceAsset)
    // ========================================
    private async Task<HttpResponseMessage> HandleUploadAsset()
    {
        var (body, bodyError) = await this.ReadJsonBody().ConfigureAwait(false);
        if (bodyError != null)
        {
            return bodyError;
        }

        if (string.IsNullOrWhiteSpace(body.Value<string>("filename"))
            || string.IsNullOrWhiteSpace(body.Value<string>("content"))
            || string.IsNullOrWhiteSpace(body.Value<string>("title")))
        {
            return this.MakeError(HttpStatusCode.BadRequest, "MISSING_FIELDS",
                "filename, content (base64) and title are required.");
        }

        var (result, error) = await this.UploadAndCreate(body).ConfigureAwait(false);
        if (error != null)
        {
            return error;
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private async Task<HttpResponseMessage> HandleReplaceAsset()
    {
        var (body, bodyError) = await this.ReadJsonBody().ConfigureAwait(false);
        if (bodyError != null)
        {
            return bodyError;
        }

        var assetId = body.Value<string>("assetId");
        if (string.IsNullOrWhiteSpace(assetId)
            || string.IsNullOrWhiteSpace(body.Value<string>("filename"))
            || string.IsNullOrWhiteSpace(body.Value<string>("content")))
        {
            return this.MakeError(HttpStatusCode.BadRequest, "MISSING_FIELDS",
                "assetId, filename and content (base64) are required.");
        }

        var (fileId, uploadError) = await this.UploadBytes(body).ConfigureAwait(false);
        if (uploadError != null)
        {
            return uploadError;
        }

        var replaceEnvelope = new JObject
        {
            ["query"] = "mutation ReplaceAsset($input: ReplaceAssetInput!) { replaceAsset(input: $input) { job { assetId } } }",
            ["variables"] = new JObject { ["input"] = new JObject { ["assetId"] = assetId, ["fileId"] = fileId } }
        };
        var (replaceData, replaceError) = await this.SendGraphQl(replaceEnvelope).ConfigureAwait(false);
        if (replaceError != null)
        {
            return replaceError;
        }

        var result = new JObject
        {
            ["assetId"] = replaceData?["replaceAsset"]?["job"]?.Value<string>("assetId") ?? assetId,
            ["fileId"] = fileId
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    // Uploads bytes then creates a new asset. Returns { assetId, fileId, title }.
    private async Task<(JObject result, HttpResponseMessage error)> UploadAndCreate(JObject body)
    {
        var (fileId, uploadError) = await this.UploadBytes(body).ConfigureAwait(false);
        if (uploadError != null)
        {
            return (null, uploadError);
        }

        var createInput = new JObject { ["fileId"] = fileId, ["title"] = body.Value<string>("title") };
        this.CopyIfPresent(body, createInput, "parentId");
        this.CopyIfPresent(body, createInput, "description");
        this.CopyIfPresent(body, createInput, "externalId");
        this.CopyIfPresent(body, createInput, "author");
        if (body["tags"] is JArray tags && tags.Count > 0)
        {
            createInput["tags"] = tags;
        }

        var createEnvelope = new JObject
        {
            ["query"] = "mutation CreateAsset($input: CreateAssetInput!) { createAsset(input: $input) { job { assetId } } }",
            ["variables"] = new JObject { ["input"] = createInput }
        };
        var (createData, createError) = await this.SendGraphQl(createEnvelope).ConfigureAwait(false);
        if (createError != null)
        {
            return (null, createError);
        }

        var result = new JObject
        {
            ["assetId"] = createData?["createAsset"]?["job"]?.Value<string>("assetId"),
            ["fileId"] = fileId,
            ["title"] = body.Value<string>("title")
        };
        return (result, null);
    }

    // uploadFile mutation + chunked PUT to the presigned URLs. Returns the signed fileId.
    private async Task<(string fileId, HttpResponseMessage error)> UploadBytes(JObject body)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(body.Value<string>("content") ?? string.Empty);
        }
        catch (FormatException)
        {
            return (null, this.MakeError(HttpStatusCode.BadRequest, "INVALID_CONTENT",
                "content must be a base64-encoded string."));
        }
        long size = bytes.LongLength;

        var uploadEnvelope = new JObject
        {
            ["query"] = "mutation UploadFile($input: UploadFileInput!) { uploadFile(input: $input) { id urls } }",
            ["variables"] = new JObject
            {
                ["input"] = new JObject { ["filename"] = body.Value<string>("filename"), ["size"] = size }
            }
        };
        var (uploadData, uploadError) = await this.SendGraphQl(uploadEnvelope).ConfigureAwait(false);
        if (uploadError != null)
        {
            return (null, uploadError);
        }

        var fileId = uploadData?["uploadFile"]?.Value<string>("id");
        var urls = uploadData?["uploadFile"]?["urls"] as JArray;
        if (string.IsNullOrWhiteSpace(fileId) || urls == null || urls.Count == 0)
        {
            return (null, this.MakeError(HttpStatusCode.BadGateway, "UPLOAD_INIT_FAILED",
                "uploadFile did not return a file Id and upload URLs."));
        }

        // Presigned URLs are pre-authorized, so these PUTs must NOT carry the connector's Authorization header.
        int parts = urls.Count;
        long partSize = (size + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            long start = partSize * i;
            if (start >= size)
            {
                break;
            }
            int len = (int)Math.Min(partSize, size - start);
            var chunk = new byte[len];
            Array.Copy(bytes, start, chunk, 0, len);

            var putRequest = new HttpRequestMessage(HttpMethod.Put, new Uri((string)urls[i]))
            {
                Content = new ByteArrayContent(chunk)
            };
            var putResponse = await this.Context.SendAsync(putRequest, this.CancellationToken).ConfigureAwait(false);
            if (!putResponse.IsSuccessStatusCode)
            {
                var putBody = putResponse.Content == null
                    ? string.Empty
                    : await putResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                return (null, this.MakeError(HttpStatusCode.BadGateway, "UPLOAD_PART_FAILED",
                    $"Part {i + 1}/{parts} upload failed with HTTP {(int)putResponse.StatusCode}. {putBody}"));
            }
        }

        return (fileId, null);
    }

    private async Task<(JObject body, HttpResponseMessage error)> ReadJsonBody()
    {
        var rawBody = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return (string.IsNullOrWhiteSpace(rawBody) ? new JObject() : JObject.Parse(rawBody), null);
        }
        catch (JsonException ex)
        {
            return (null, this.MakeError(HttpStatusCode.BadRequest, "INVALID_JSON",
                $"Request body is not valid JSON: {ex.Message}"));
        }
    }

    // ========================================
    // MCP ENDPOINT (dual-mode: /mcp for Copilot Studio)
    // Wires Frontify tools onto the embedded MCP framework (McpRequestHandler, below).
    // ========================================
    private static readonly McpServerOptions McpOptions = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "frontify",
            Version = "1.0.0",
            Title = "Frontify",
            Description = "Frontify brand asset management over the Model Context Protocol."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities { Tools = true },
        Instructions = ""
    };

    private async Task<HttpResponseMessage> HandleMcp()
    {
        var handler = new McpRequestHandler(McpOptions);
        this.RegisterMcpTools(handler);
        handler.OnLog = (evt, data) => this.Context.Logger.LogInformation($"[MCP] {evt}");

        var body = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(result) };
    }

    // Each read/write tool reuses a typed GraphQL document; upload_asset reuses the upload orchestration.
    private void RegisterMcpTools(McpRequestHandler handler)
    {
        handler.AddTool("list_brands", "List all brands available to the account.",
            schema: s => { },
            handler: (args, ct) => this.McpTool("ListBrands", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_brand", "Get a single brand by ID.",
            schema: s => s.String("id", "Brand ID", required: true),
            handler: (args, ct) => this.McpTool("GetBrand", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_brand_libraries", "List the libraries belonging to a brand.",
            schema: s => s.String("id", "Brand ID", required: true).Integer("limit", "Max results").Integer("page", "Page number"),
            handler: (args, ct) => this.McpTool("ListBrandLibraries", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_library_assets", "List the assets in a library.",
            schema: s => s.String("id", "Library ID", required: true).Integer("limit", "Max results").Integer("page", "Page number"),
            handler: (args, ct) => this.McpTool("ListLibraryAssets", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("search_library_assets", "Search the assets in a library.",
            schema: s => s.String("id", "Library ID", required: true).Object("query", "AssetQueryInput filter", q => { }).Integer("limit", "Max results").Integer("page", "Page number"),
            handler: (args, ct) => this.McpTool("SearchLibraryAssets", args),
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_asset", "Get an asset's metadata by ID.",
            schema: s => s.String("id", "Asset ID", required: true),
            handler: (args, ct) => this.McpTool("GetAsset", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_asset_download", "Get an asset's download and preview URLs by ID.",
            schema: s => s.String("id", "Asset ID", required: true),
            handler: (args, ct) => this.McpTool("GetAssetDownload", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("add_asset_tags", "Add tags to an asset.",
            schema: s => s.String("id", "Asset ID", required: true)
                .Array("tags", "Tags to add", new JObject { ["type"] = "object", ["properties"] = new JObject { ["value"] = new JObject { ["type"] = "string", ["description"] = "Tag value" } } }, required: true),
            handler: (args, ct) => this.McpTool("AddAssetTags", args));

        handler.AddTool("upload_asset", "Upload a base64-encoded file and create an asset.",
            schema: s => s.String("filename", "File name", required: true)
                .String("content", "Base64 file content", required: true)
                .String("title", "Asset title", required: true)
                .String("parentId", "Library, workspace, or folder ID"),
            handler: (args, ct) => this.McpUploadTool(args));
    }

    // Runs a typed GraphQL document as an MCP tool and returns the result as MCP text content.
    private async Task<JObject> McpTool(string operationId, JObject args)
    {
        var (result, error) = await this.RunGraphQlTool(operationId, args).ConfigureAwait(false);
        if (error != null)
        {
            return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(error) }, isError: true);
        }
        return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(result.ToString(Newtonsoft.Json.Formatting.None)) });
    }

    private async Task<JObject> McpUploadTool(JObject args)
    {
        var (result, error) = await this.UploadAndCreate(args).ConfigureAwait(false);
        if (error != null)
        {
            var text = error.Content == null ? "Upload failed." : await error.Content.ReadAsStringAsync().ConfigureAwait(false);
            return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(text) }, isError: true);
        }
        return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(result.ToString(Newtonsoft.Json.Formatting.None)) });
    }

    // Executes a registered GraphQL document and returns the unwrapped result token or an error string.
    private async Task<(JToken result, string error)> RunGraphQlTool(string operationId, JObject args)
    {
        var envelope = new JObject { ["query"] = GraphQlDocuments[operationId], ["variables"] = args ?? new JObject() };
        var (data, err) = await this.SendGraphQl(envelope).ConfigureAwait(false);
        if (err != null)
        {
            var text = err.Content == null ? "Tool failed." : await err.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (null, text);
        }
        JToken result = ResponseRootPaths.TryGetValue(operationId, out var path) && !string.IsNullOrWhiteSpace(path)
            ? (this.SelectPath(data, path) ?? JValue.CreateNull())
            : data;
        return (result, null);
    }

    // Sends a GraphQL envelope on a fresh request and returns (data, null) on success or
    // (null, errorResponse) when the transport or GraphQL layer reports a failure.
    private async Task<(JObject data, HttpResponseMessage error)> SendGraphQl(JObject envelope)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(GraphQlEndpoint))
        {
            Content = new StringContent(envelope.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };

        var token = this.ExtractBearerToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        request.Headers.Add("X-Frontify-Beta", "enabled");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseString = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return (null, new HttpResponseMessage(response.StatusCode)
            {
                Content = CreateJsonContent(
                    this.WrapTransportError(response.StatusCode, responseString).ToString(Newtonsoft.Json.Formatting.None))
            });
        }

        JObject payload;
        try
        {
            payload = JObject.Parse(string.IsNullOrWhiteSpace(responseString) ? "{}" : responseString);
        }
        catch (JsonException ex)
        {
            return (null, this.MakeError(HttpStatusCode.BadGateway, "INVALID_GRAPHQL_RESPONSE",
                $"Frontify returned a non-JSON response: {ex.Message}"));
        }

        var errors = payload["errors"] as JArray;
        if (errors != null && errors.Count > 0)
        {
            var message = string.Join(" | ",
                errors.Select(e => e.Value<string>("message") ?? e.ToString(Newtonsoft.Json.Formatting.None)));
            return (null, this.MakeError(HttpStatusCode.BadRequest, "GRAPHQL_ERROR", message));
        }

        return ((JObject)(payload["data"] ?? new JObject()), null);
    }

    // Returns the bearer token from the incoming request, whether the API key was entered with
    // or without the "Bearer " scheme.
    private string ExtractBearerToken()
    {
        var auth = this.Context.Request.Headers.Authorization;
        if (auth == null)
        {
            return null;
        }
        return string.IsNullOrWhiteSpace(auth.Parameter) ? auth.Scheme : auth.Parameter;
    }

    private void CopyIfPresent(JObject source, JObject target, string key)
    {
        var value = source[key];
        if (value != null && value.Type != JTokenType.Null)
        {
            target[key] = value;
        }
    }

    // ========================================
    // HELPERS
    // ========================================

    // Frontify wants "Authorization: Bearer <token>". If the API key was entered without the
    // scheme, add it; if it already has "Bearer ", leave it alone.
    private void NormalizeAuthHeader()
    {
        var auth = this.Context.Request.Headers.Authorization;
        if (auth != null && !string.IsNullOrWhiteSpace(auth.Parameter)
            && string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return; // already correct
        }

        string token = null;
        if (auth != null)
        {
            token = string.IsNullOrWhiteSpace(auth.Parameter) ? auth.Scheme : auth.Parameter;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            this.Context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

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
                ["message"] = $"Frontify returned HTTP {(int)status} ({status}).",
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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  MCP FRAMEWORK                                                              ║
// ║                                                                            ║
// ║  Stateless McpRequestHandler bringing MCP C# SDK patterns to Power          ║
// ║  Platform. Spec coverage: MCP 2025-11-25. Handles initialize, ping,         ║
// ║  tools/*, resources/*, prompts/*, completion/complete, logging/setLevel,    ║
// ║  and all notifications. Do not modify unless extending the framework.       ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

/// <summary>Server identity reported in initialize response.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

/// <summary>Capabilities declared during initialization.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

/// <summary>Standard JSON-RPC 2.0 error codes used by MCP.</summary>
public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

/// <summary>Throw from tool methods to surface a structured MCP error.</summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

/// <summary>Fluent builder for JSON Schema objects used in tool inputSchema.</summary>
public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

/// <summary>Describes a single prompt argument.</summary>
public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns to Power
/// Platform's ScriptBase model. Takes a JSON-RPC string in, returns a JSON-RPC string out.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schemaConfig,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotationsConfig = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotations,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resources[uri] = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        };

        return this;
    }

    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return HandleResourcesList(id);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(id);

                case "resources/read":
                    return await HandleResourcesReadAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return HandlePromptsList(id);

                case "prompts/get":
                    return await HandlePromptsGetAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = clientProtocolVersion
        });

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private string HandleResourcesList(JToken id)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resources.Values)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resources.Count });
        return SerializeSuccess(id, new JObject { ["resources"] = resourcesArray });
    }

    private string HandleResourceTemplatesList(JToken id)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(id, new JObject { ["resourceTemplates"] = templatesArray });
    }

    private async Task<string> HandleResourcesReadAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(id, new JObject { ["contents"] = contents });
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(id, McpErrorCode.InternalError, ex.Message);
            }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(id, new JObject { ["contents"] = contents });
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        return SerializeError(id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue;
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(JToken id)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _prompts.Values)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _prompts.Count });
        return SerializeSuccess(id, new JObject { ["prompts"] = promptsArray });
    }

    private async Task<string> HandlePromptsGetAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(id, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;

            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
                if (jobj["structuredContent"] is JObject structured)
                    callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}
