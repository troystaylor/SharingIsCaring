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
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string ServerName = "foundry-embed";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string DefaultModel = "embed-v-4-0";
    private const int DefaultDimensions = 1024;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":       response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false); break;
                case "EmbedText":       response = await HandleEmbedTextAsync(correlationId).ConfigureAwait(false); break;
                case "EmbedImage":      response = await HandleEmbedImageAsync(correlationId).ConfigureAwait(false); break;
                case "ComputeSimilarity": response = await HandleComputeSimilarityAsync(correlationId).ConfigureAwait(false); break;
                case "IndexDocument":   response = await HandleIndexDocumentAsync(correlationId).ConfigureAwait(false); break;
                case "SearchSimilar":   response = await HandleSearchSimilarAsync(correlationId).ConfigureAwait(false); break;
                default:                response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false); break;
            }
            return response;
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message });
            throw;
        }
    }

    // ========================================
    // EMBED TEXT
    // ========================================

    private async Task<HttpResponseMessage> HandleEmbedTextAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var texts = body["texts"] as JArray;
        var inputType = body.Value<string>("input_type") ?? "document";
        var dimensions = body["dimensions"]?.Value<int>() ?? DefaultDimensions;

        if (texts == null || texts.Count == 0)
            return CreateErrorResponse(HttpStatusCode.BadRequest, "texts array is required and must not be empty");

        var request = new JObject
        {
            ["model"] = DefaultModel,
            ["input"] = texts,
            ["input_type"] = inputType,
            ["dimensions"] = dimensions
        };

        var uri = new UriBuilder(this.Context.Request.RequestUri) { Path = "/models/embeddings" }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(request.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return response;

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        var formatted = new JObject
        {
            ["embeddings"] = result["data"],
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };

        response.Content = CreateJsonContent(formatted.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // EMBED IMAGE
    // ========================================

    private async Task<HttpResponseMessage> HandleEmbedImageAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var imageUrl = body.Value<string>("image_url");
        var text = body.Value<string>("text");
        var dimensions = body["dimensions"]?.Value<int>() ?? DefaultDimensions;

        if (string.IsNullOrWhiteSpace(imageUrl))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "image_url is required");

        var inputItem = new JObject { ["image"] = imageUrl };
        if (!string.IsNullOrWhiteSpace(text))
            inputItem["text"] = text;

        var request = new JObject
        {
            ["model"] = DefaultModel,
            ["input"] = new JArray { inputItem },
            ["dimensions"] = dimensions
        };

        var uri = new UriBuilder(this.Context.Request.RequestUri) { Path = "/models/images/embeddings" }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(request.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return response;

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var embedding = result["data"]?[0]?["embedding"];

        var formatted = new JObject
        {
            ["embedding"] = embedding,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };

        response.Content = CreateJsonContent(formatted.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // COMPUTE SIMILARITY
    // ========================================

    private async Task<HttpResponseMessage> HandleComputeSimilarityAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var inputA = body.Value<string>("input_a");
        var inputAType = body.Value<string>("input_a_type") ?? "text";
        var inputB = body.Value<string>("input_b");
        var inputBType = body.Value<string>("input_b_type") ?? "text";

        if (string.IsNullOrWhiteSpace(inputA) || string.IsNullOrWhiteSpace(inputB))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "input_a and input_b are required");

        // Embed both inputs
        var embeddingA = await GetEmbeddingAsync(inputA, inputAType).ConfigureAwait(false);
        var embeddingB = await GetEmbeddingAsync(inputB, inputBType).ConfigureAwait(false);

        // Cosine similarity
        var similarity = CosineSimilarity(embeddingA, embeddingB);
        var interpretation = similarity >= 0.8 ? "Very similar" :
                             similarity >= 0.6 ? "Similar" :
                             similarity >= 0.4 ? "Somewhat related" :
                             similarity >= 0.2 ? "Loosely related" :
                             "Unrelated";

        var result = new JObject
        {
            ["similarity"] = Math.Round(similarity, 6),
            ["interpretation"] = interpretation
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private async Task<double[]> GetEmbeddingAsync(string input, string inputType)
    {
        JObject request;
        string path;

        if (inputType == "image")
        {
            request = new JObject
            {
                ["model"] = DefaultModel,
                ["input"] = new JArray { new JObject { ["image"] = input } },
                ["dimensions"] = DefaultDimensions
            };
            path = "/models/images/embeddings";
        }
        else
        {
            request = new JObject
            {
                ["model"] = DefaultModel,
                ["input"] = new JArray { input },
                ["input_type"] = "query",
                ["dimensions"] = DefaultDimensions
            };
            path = "/models/embeddings";
        }

        var apiUrl = BuildApiUrl(path);
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, request).ConfigureAwait(false);
        var embedding = result["data"]?[0]?["embedding"] as JArray;

        if (embedding == null)
            throw new Exception("Failed to generate embedding");

        return embedding.Select(t => t.Value<double>()).ToArray();
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vectors must be the same length");
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0 ? 0 : dot / denom;
    }

    // ========================================
    // INDEX DOCUMENT (AI Search)
    // ========================================

    private async Task<HttpResponseMessage> HandleIndexDocumentAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var documentId = body.Value<string>("document_id");
        var content = body.Value<string>("content");
        var title = body.Value<string>("title") ?? "";
        var searchEndpoint = body.Value<string>("search_endpoint");
        var searchIndex = body.Value<string>("search_index");
        var searchApiKey = body.Value<string>("search_api_key");
        var vectorField = body.Value<string>("vector_field") ?? "contentVector";

        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(content))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "document_id and content are required");
        if (string.IsNullOrWhiteSpace(searchEndpoint) || string.IsNullOrWhiteSpace(searchIndex) || string.IsNullOrWhiteSpace(searchApiKey))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "search_endpoint, search_index, and search_api_key are required");

        // Step 1: Embed the content
        var embedRequest = new JObject
        {
            ["model"] = DefaultModel,
            ["input"] = new JArray { content },
            ["input_type"] = "document",
            ["dimensions"] = DefaultDimensions
        };
        var embedUrl = BuildApiUrl("/models/embeddings");
        var embedResult = await SendApiRequestAsync(HttpMethod.Post, embedUrl, embedRequest).ConfigureAwait(false);
        var embedding = embedResult["data"]?[0]?["embedding"] as JArray;

        if (embedding == null)
            throw new Exception("Failed to generate embedding for document");

        // Step 2: Push to AI Search
        var searchDoc = new JObject
        {
            ["value"] = new JArray
            {
                new JObject
                {
                    ["@search.action"] = "mergeOrUpload",
                    ["id"] = documentId,
                    ["content"] = content,
                    ["title"] = title,
                    [vectorField] = embedding
                }
            }
        };

        var searchUrl = $"{searchEndpoint.TrimEnd('/')}/indexes/{searchIndex}/docs/index?api-version=2024-07-01";
        var searchRequest = new HttpRequestMessage(HttpMethod.Post, searchUrl);
        searchRequest.Headers.Add("api-key", searchApiKey);
        searchRequest.Content = new StringContent(searchDoc.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var searchResponse = await this.Context.SendAsync(searchRequest, this.CancellationToken).ConfigureAwait(false);
        var searchContent = await searchResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!searchResponse.IsSuccessStatusCode)
            throw new Exception($"AI Search indexing failed ({(int)searchResponse.StatusCode}): {searchContent}");

        var result = new JObject
        {
            ["document_id"] = documentId,
            ["status"] = "indexed",
            ["vector_dimensions"] = embedding.Count
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    // ========================================
    // SEARCH SIMILAR (AI Search)
    // ========================================

    private async Task<HttpResponseMessage> HandleSearchSimilarAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var query = body.Value<string>("query");
        var topK = body["top_k"]?.Value<int>() ?? 5;
        var searchEndpoint = body.Value<string>("search_endpoint");
        var searchIndex = body.Value<string>("search_index");
        var searchApiKey = body.Value<string>("search_api_key");
        var vectorField = body.Value<string>("vector_field") ?? "contentVector";

        if (string.IsNullOrWhiteSpace(query))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "query is required");
        if (string.IsNullOrWhiteSpace(searchEndpoint) || string.IsNullOrWhiteSpace(searchIndex) || string.IsNullOrWhiteSpace(searchApiKey))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "search_endpoint, search_index, and search_api_key are required");

        // Step 1: Embed the query
        var embedRequest = new JObject
        {
            ["model"] = DefaultModel,
            ["input"] = new JArray { query },
            ["input_type"] = "query",
            ["dimensions"] = DefaultDimensions
        };
        var embedUrl = BuildApiUrl("/models/embeddings");
        var embedResult = await SendApiRequestAsync(HttpMethod.Post, embedUrl, embedRequest).ConfigureAwait(false);
        var embedding = embedResult["data"]?[0]?["embedding"] as JArray;

        if (embedding == null)
            throw new Exception("Failed to generate query embedding");

        // Step 2: Vector search against AI Search
        var searchBody = new JObject
        {
            ["count"] = true,
            ["select"] = "id,content,title",
            ["vectorQueries"] = new JArray
            {
                new JObject
                {
                    ["kind"] = "vector",
                    ["vector"] = embedding,
                    ["fields"] = vectorField,
                    ["k"] = topK
                }
            }
        };

        var searchUrl = $"{searchEndpoint.TrimEnd('/')}/indexes/{searchIndex}/docs/search?api-version=2024-07-01";
        var searchRequest = new HttpRequestMessage(HttpMethod.Post, searchUrl);
        searchRequest.Headers.Add("api-key", searchApiKey);
        searchRequest.Content = new StringContent(searchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var searchResponse = await this.Context.SendAsync(searchRequest, this.CancellationToken).ConfigureAwait(false);
        var searchContent = await searchResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!searchResponse.IsSuccessStatusCode)
            throw new Exception($"AI Search query failed ({(int)searchResponse.StatusCode}): {searchContent}");

        var searchResult = JObject.Parse(searchContent);
        var docs = searchResult["value"] as JArray ?? new JArray();

        var results = new JArray();
        foreach (var doc in docs)
        {
            results.Add(new JObject
            {
                ["document_id"] = doc["id"],
                ["content"] = doc["content"],
                ["title"] = doc["title"],
                ["score"] = doc["@search.score"]
            });
        }

        var formatted = new JObject
        {
            ["results"] = results,
            ["count"] = results.Count
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(formatted.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        JToken requestId = null;
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");

            JObject request;
            try { request = JObject.Parse(body); }
            catch (JsonException) { return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON"); }

            var method = request.Value<string>("method") ?? "";
            requestId = request["id"];

            switch (method)
            {
                case "initialize": return HandleMcpInitialize(request, requestId);
                case "initialized": case "notifications/initialized": case "notifications/cancelled": case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                case "tools/list": return HandleMcpToolsList(requestId);
                case "tools/call": return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                case "resources/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                case "resources/templates/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                case "prompts/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
                case "completion/complete": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } });
                case "logging/setLevel": return CreateJsonRpcSuccessResponse(requestId, new JObject());
                default: return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex) { return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message); }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Microsoft Foundry Embed",
                ["description"] = "Text and image embeddings, similarity scoring, vector indexing, and search using Cohere Embed v4 on Microsoft Foundry"
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("embed_text", "Generate embedding vectors for text strings.", new JObject
            {
                ["texts"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Text strings to embed" },
                ["input_type"] = new JObject { ["type"] = "string", ["description"] = "'document' for indexing, 'query' for searching" }
            }, new[] { "texts" }),
            McpTool("embed_image", "Generate an embedding vector for an image.", new JObject
            {
                ["image_url"] = new JObject { ["type"] = "string", ["description"] = "Image URL or base64 data URI" },
                ["text"] = new JObject { ["type"] = "string", ["description"] = "Optional text to pair with the image" }
            }, new[] { "image_url" }),
            McpTool("compute_similarity", "Compute semantic similarity (0-1) between two inputs (text or image).", new JObject
            {
                ["input_a"] = new JObject { ["type"] = "string", ["description"] = "First input (text or image URL)" },
                ["input_a_type"] = new JObject { ["type"] = "string", ["description"] = "'text' or 'image'" },
                ["input_b"] = new JObject { ["type"] = "string", ["description"] = "Second input" },
                ["input_b_type"] = new JObject { ["type"] = "string", ["description"] = "'text' or 'image'" }
            }, new[] { "input_a", "input_b" }),
            McpTool("index_document", "Embed text and push to Azure AI Search index.", new JObject
            {
                ["document_id"] = new JObject { ["type"] = "string", ["description"] = "Unique document ID" },
                ["content"] = new JObject { ["type"] = "string", ["description"] = "Document text" },
                ["search_endpoint"] = new JObject { ["type"] = "string", ["description"] = "AI Search endpoint URL" },
                ["search_index"] = new JObject { ["type"] = "string", ["description"] = "Index name" },
                ["search_api_key"] = new JObject { ["type"] = "string", ["description"] = "AI Search admin key" }
            }, new[] { "document_id", "content", "search_endpoint", "search_index", "search_api_key" }),
            McpTool("search_similar", "Vector search against Azure AI Search index.", new JObject
            {
                ["query"] = new JObject { ["type"] = "string", ["description"] = "Search query text" },
                ["top_k"] = new JObject { ["type"] = "integer", ["description"] = "Number of results (default 5)" },
                ["search_endpoint"] = new JObject { ["type"] = "string", ["description"] = "AI Search endpoint URL" },
                ["search_index"] = new JObject { ["type"] = "string", ["description"] = "Index name" },
                ["search_api_key"] = new JObject { ["type"] = "string", ["description"] = "AI Search query key" }
            }, new[] { "query", "search_endpoint", "search_index", "search_api_key" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject McpTool(string name, string description, JObject properties, string[] required)
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

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");

        try
        {
            JObject toolResult;
            switch (toolName.ToLowerInvariant())
            {
                case "embed_text":
                    var texts = arguments["texts"] as JArray;
                    var inputType = arguments.Value<string>("input_type") ?? "document";
                    if (texts == null || texts.Count == 0) throw new ArgumentException("'texts' is required");
                    var embedReq = new JObject { ["model"] = DefaultModel, ["input"] = texts, ["input_type"] = inputType, ["dimensions"] = DefaultDimensions };
                    var embedRes = await SendApiRequestAsync(HttpMethod.Post, BuildApiUrl("/models/embeddings"), embedReq).ConfigureAwait(false);
                    toolResult = new JObject { ["embeddings"] = embedRes["data"], ["model"] = embedRes["model"], ["usage"] = embedRes["usage"] };
                    break;

                case "embed_image":
                    var imgUrl = arguments.Value<string>("image_url") ?? throw new ArgumentException("'image_url' is required");
                    var imgText = arguments.Value<string>("text");
                    var imgInput = new JObject { ["image"] = imgUrl };
                    if (!string.IsNullOrWhiteSpace(imgText)) imgInput["text"] = imgText;
                    var imgReq = new JObject { ["model"] = DefaultModel, ["input"] = new JArray { imgInput }, ["dimensions"] = DefaultDimensions };
                    var imgRes = await SendApiRequestAsync(HttpMethod.Post, BuildApiUrl("/models/images/embeddings"), imgReq).ConfigureAwait(false);
                    toolResult = new JObject { ["embedding"] = imgRes["data"]?[0]?["embedding"], ["model"] = imgRes["model"] };
                    break;

                case "compute_similarity":
                    var a = arguments.Value<string>("input_a") ?? throw new ArgumentException("'input_a' is required");
                    var b = arguments.Value<string>("input_b") ?? throw new ArgumentException("'input_b' is required");
                    var aType = arguments.Value<string>("input_a_type") ?? "text";
                    var bType = arguments.Value<string>("input_b_type") ?? "text";
                    var vecA = await GetEmbeddingAsync(a, aType).ConfigureAwait(false);
                    var vecB = await GetEmbeddingAsync(b, bType).ConfigureAwait(false);
                    var sim = CosineSimilarity(vecA, vecB);
                    toolResult = new JObject
                    {
                        ["similarity"] = Math.Round(sim, 6),
                        ["interpretation"] = sim >= 0.8 ? "Very similar" : sim >= 0.6 ? "Similar" : sim >= 0.4 ? "Somewhat related" : sim >= 0.2 ? "Loosely related" : "Unrelated"
                    };
                    break;

                case "index_document":
                    var docId = arguments.Value<string>("document_id") ?? throw new ArgumentException("'document_id' is required");
                    var docContent = arguments.Value<string>("content") ?? throw new ArgumentException("'content' is required");
                    var sEndpoint = arguments.Value<string>("search_endpoint") ?? throw new ArgumentException("'search_endpoint' is required");
                    var sIndex = arguments.Value<string>("search_index") ?? throw new ArgumentException("'search_index' is required");
                    var sKey = arguments.Value<string>("search_api_key") ?? throw new ArgumentException("'search_api_key' is required");
                    var vField = arguments.Value<string>("vector_field") ?? "contentVector";

                    var docEmbedReq = new JObject { ["model"] = DefaultModel, ["input"] = new JArray { docContent }, ["input_type"] = "document", ["dimensions"] = DefaultDimensions };
                    var docEmbedRes = await SendApiRequestAsync(HttpMethod.Post, BuildApiUrl("/models/embeddings"), docEmbedReq).ConfigureAwait(false);
                    var docVec = docEmbedRes["data"]?[0]?["embedding"];

                    var indexBody = new JObject { ["value"] = new JArray { new JObject { ["@search.action"] = "mergeOrUpload", ["id"] = docId, ["content"] = docContent, [vField] = docVec } } };
                    var indexUrl = $"{sEndpoint.TrimEnd('/')}/indexes/{sIndex}/docs/index?api-version=2024-07-01";
                    var indexReq = new HttpRequestMessage(HttpMethod.Post, indexUrl);
                    indexReq.Headers.Add("api-key", sKey);
                    indexReq.Content = new StringContent(indexBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    var indexRes = await this.Context.SendAsync(indexReq, this.CancellationToken).ConfigureAwait(false);
                    if (!indexRes.IsSuccessStatusCode) throw new Exception($"AI Search indexing failed: {await indexRes.Content.ReadAsStringAsync().ConfigureAwait(false)}");
                    toolResult = new JObject { ["document_id"] = docId, ["status"] = "indexed" };
                    break;

                case "search_similar":
                    var sq = arguments.Value<string>("query") ?? throw new ArgumentException("'query' is required");
                    var sTopK = arguments["top_k"]?.Value<int>() ?? 5;
                    var ssEndpoint = arguments.Value<string>("search_endpoint") ?? throw new ArgumentException("'search_endpoint' is required");
                    var ssIndex = arguments.Value<string>("search_index") ?? throw new ArgumentException("'search_index' is required");
                    var ssKey = arguments.Value<string>("search_api_key") ?? throw new ArgumentException("'search_api_key' is required");
                    var svField = arguments.Value<string>("vector_field") ?? "contentVector";

                    var qEmbedReq = new JObject { ["model"] = DefaultModel, ["input"] = new JArray { sq }, ["input_type"] = "query", ["dimensions"] = DefaultDimensions };
                    var qEmbedRes = await SendApiRequestAsync(HttpMethod.Post, BuildApiUrl("/models/embeddings"), qEmbedReq).ConfigureAwait(false);
                    var qVec = qEmbedRes["data"]?[0]?["embedding"];

                    var searchBody2 = new JObject { ["count"] = true, ["select"] = "id,content,title", ["vectorQueries"] = new JArray { new JObject { ["kind"] = "vector", ["vector"] = qVec, ["fields"] = svField, ["k"] = sTopK } } };
                    var searchUrl2 = $"{ssEndpoint.TrimEnd('/')}/indexes/{ssIndex}/docs/search?api-version=2024-07-01";
                    var searchReq2 = new HttpRequestMessage(HttpMethod.Post, searchUrl2);
                    searchReq2.Headers.Add("api-key", ssKey);
                    searchReq2.Content = new StringContent(searchBody2.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    var searchRes2 = await this.Context.SendAsync(searchReq2, this.CancellationToken).ConfigureAwait(false);
                    if (!searchRes2.IsSuccessStatusCode) throw new Exception($"AI Search query failed: {await searchRes2.Content.ReadAsStringAsync().ConfigureAwait(false)}");
                    var searchParsed = JObject.Parse(await searchRes2.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var searchDocs = searchParsed["value"] as JArray ?? new JArray();
                    var searchResults = new JArray();
                    foreach (var d in searchDocs) searchResults.Add(new JObject { ["document_id"] = d["id"], ["content"] = d["content"], ["title"] = d["title"], ["score"] = d["@search.score"] });
                    toolResult = new JObject { ["results"] = searchResults, ["count"] = searchResults.Count };
                    break;

                default: throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // HELPERS
    // ========================================

    private string BuildApiUrl(string path)
    {
        return $"https://{this.Context.Request.RequestUri.Host}{path}";
    }

    private async Task<JObject> SendApiRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (this.Context.Request.Headers.Contains("api-key"))
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
        return JObject.Parse(content);
    }

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode status, string message)
    {
        return new HttpResponseMessage(status) { Content = CreateJsonContent($"{{\"error\": \"{message}\"}}") };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint") ?? "https://dc.services.visualstudio.com/";
            if (string.IsNullOrEmpty(instrumentationKey)) return;

            var propsDict = new Dictionary<string, string> { ["ServerName"] = ServerName, ["ServerVersion"] = ServerVersion };
            if (properties != null)
            {
                var propsObj = JObject.Parse(JsonConvert.SerializeObject(properties));
                foreach (var prop in propsObj.Properties()) propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetry = new { name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event", time = DateTime.UtcNow.ToString("o"), iKey = instrumentationKey, data = new { baseType = "EventData", baseData = new { ver = 2, name = eventName, properties = propsDict } } };
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track")) { Content = new StringContent(JsonConvert.SerializeObject(telemetry), Encoding.UTF8, "application/json") };
            await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractPart(string cs, string key)
    {
        if (string.IsNullOrEmpty(cs)) return null;
        foreach (var p in cs.Split(';'))
            if (p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return p.Substring(key.Length + 1);
        return null;
    }
}
