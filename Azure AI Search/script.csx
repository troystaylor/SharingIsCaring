using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Azure AI Search — Power Mission Control Connector                         ║
// ║                                                                            ║
// ║  Covers Azure AI Search REST API (2025-09-01):                             ║
// ║  Indexes, Documents (search, get, index), Indexers, Data Sources,          ║
// ║  Skillsets, Synonym Maps, and Service Statistics.                           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Configuration (edit these) ────────────────────────────────────────

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "azure-ai-search-mcp",
            Version = "1.0.0",
            Title = "Azure AI Search MCP",
            Description = "Power Mission Control connector for Azure AI Search. Manage indexes, search documents, run indexers, and configure AI enrichment."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Logging = true,
            Completions = true
        }
    };

    // ── Mission Control Configuration ─────────────────────────────────────

    private static readonly MissionControlOptions McOptions = new MissionControlOptions
    {
        ServiceName = "search",
        BaseApiUrl = "", // Set dynamically from connection parameter
        DiscoveryMode = DiscoveryMode.Static,
        BatchMode = BatchMode.Sequential,
        MaxBatchSize = 10,
        DefaultPageSize = 50,
        CacheExpiryMinutes = 10,
        MaxDiscoverResults = 3,
        SummarizeResponses = true,
        MaxBodyLength = 500,
        MaxTextLength = 1000,
        DefaultApiVersion = "2025-09-01",
    };

    // ── Capability Index ─────────────────────────────────────────────────

    private const string CAPABILITY_INDEX = @"[
        {
            ""cid"": ""list_indexes"",
            ""endpoint"": ""/indexes"",
            ""method"": ""GET"",
            ""outcome"": ""List all search indexes in the service"",
            ""domain"": ""indexes"",
            ""requiredParams"": [],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""create_index"",
            ""endpoint"": ""/indexes"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new search index with fields, analyzers, and scoring profiles"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""name"", ""fields""],
            ""optionalParams"": [""suggesters"", ""scoringProfiles"", ""corsOptions"", ""analyzers"", ""tokenizers"", ""tokenFilters"", ""charFilters"", ""similarity"", ""semantic""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""fields\"":{\""type\"":\""array\"",\""items\"":{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""type\"":{\""type\"":\""string\"",\""enum\"":[\""Edm.String\"",\""Edm.Int32\"",\""Edm.Int64\"",\""Edm.Double\"",\""Edm.Boolean\"",\""Edm.DateTimeOffset\"",\""Edm.GeographyPoint\"",\""Collection(Edm.String)\"",\""Collection(Edm.Single)\""]},\""searchable\"":{\""type\"":\""boolean\""},\""filterable\"":{\""type\"":\""boolean\""},\""sortable\"":{\""type\"":\""boolean\""},\""facetable\"":{\""type\"":\""boolean\""},\""key\"":{\""type\"":\""boolean\""},\""retrievable\"":{\""type\"":\""boolean\""},\""analyzer\"":{\""type\"":\""string\""}}}},\""semantic\"":{\""type\"":\""object\""}},\""required\"":[\""name\"",\""fields\""]}""
        },
        {
            ""cid"": ""get_index"",
            ""endpoint"": ""/indexes/{indexName}"",
            ""method"": ""GET"",
            ""outcome"": ""Get the definition of a specific search index including its fields and configuration"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""indexName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""delete_index"",
            ""endpoint"": ""/indexes/{indexName}"",
            ""method"": ""DELETE"",
            ""outcome"": ""Delete a search index and all its documents permanently"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""indexName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""get_index_statistics"",
            ""endpoint"": ""/indexes/{indexName}/stats"",
            ""method"": ""GET"",
            ""outcome"": ""Get document count and storage size for a specific index"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""indexName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""search_documents"",
            ""endpoint"": ""/indexes/{indexName}/docs/search"",
            ""method"": ""POST"",
            ""outcome"": ""Search for documents in an index using full-text search, filters, facets, and semantic ranking"",
            ""domain"": ""search"",
            ""requiredParams"": [""indexName""],
            ""optionalParams"": [""search"", ""searchFields"", ""filter"", ""orderby"", ""select"", ""top"", ""skip"", ""count"", ""facets"", ""highlight"", ""highlightPreTag"", ""highlightPostTag"", ""queryType"", ""searchMode"", ""scoringProfile"", ""semanticConfiguration"", ""answers"", ""captions"", ""vectorQueries""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""search\"":{\""type\"":\""string\"",\""description\"":\""Full-text search query\""},\""filter\"":{\""type\"":\""string\"",\""description\"":\""OData filter expression\""},\""orderby\"":{\""type\"":\""string\""},\""select\"":{\""type\"":\""string\"",\""description\"":\""Comma-separated fields to return\""},\""top\"":{\""type\"":\""integer\""},\""skip\"":{\""type\"":\""integer\""},\""count\"":{\""type\"":\""boolean\""},\""queryType\"":{\""type\"":\""string\"",\""enum\"":[\""simple\"",\""full\"",\""semantic\""]},\""searchMode\"":{\""type\"":\""string\"",\""enum\"":[\""any\"",\""all\""]},\""semanticConfiguration\"":{\""type\"":\""string\""},\""answers\"":{\""type\"":\""string\""},\""captions\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""get_document"",
            ""endpoint"": ""/indexes/{indexName}/docs/{key}"",
            ""method"": ""GET"",
            ""outcome"": ""Retrieve a specific document from an index by its key"",
            ""domain"": ""search"",
            ""requiredParams"": [""indexName"", ""key""],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""count_documents"",
            ""endpoint"": ""/indexes/{indexName}/docs/$count"",
            ""method"": ""GET"",
            ""outcome"": ""Get the total number of documents in an index"",
            ""domain"": ""search"",
            ""requiredParams"": [""indexName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""index_documents"",
            ""endpoint"": ""/indexes/{indexName}/docs/index"",
            ""method"": ""POST"",
            ""outcome"": ""Upload, merge, or delete documents in an index using a batch of actions"",
            ""domain"": ""documents"",
            ""requiredParams"": [""indexName"", ""value""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""value\"":{\""type\"":\""array\"",\""description\"":\""Array of document actions. Each must have @search.action (upload, merge, mergeOrUpload, delete) and document fields\"",\""items\"":{\""type\"":\""object\""}}},\""required\"":[\""value\""]}""
        },
        {
            ""cid"": ""autocomplete"",
            ""endpoint"": ""/indexes/{indexName}/docs/autocomplete"",
            ""method"": ""POST"",
            ""outcome"": ""Get autocomplete suggestions for partial query text based on index content"",
            ""domain"": ""search"",
            ""requiredParams"": [""indexName"", ""search"", ""suggesterName""],
            ""optionalParams"": [""autocompleteMode"", ""filter"", ""fuzzy"", ""highlightPreTag"", ""highlightPostTag"", ""searchFields"", ""top""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""search\"":{\""type\"":\""string\""},\""suggesterName\"":{\""type\"":\""string\""},\""autocompleteMode\"":{\""type\"":\""string\"",\""enum\"":[\""oneTerm\"",\""twoTerms\"",\""oneTermWithContext\""]}},\""required\"":[\""search\"",\""suggesterName\""]}""
        },
        {
            ""cid"": ""suggest"",
            ""endpoint"": ""/indexes/{indexName}/docs/suggest"",
            ""method"": ""POST"",
            ""outcome"": ""Get document suggestions matching partial query text for type-ahead experiences"",
            ""domain"": ""search"",
            ""requiredParams"": [""indexName"", ""search"", ""suggesterName""],
            ""optionalParams"": [""filter"", ""fuzzy"", ""highlightPreTag"", ""highlightPostTag"", ""orderby"", ""searchFields"", ""select"", ""top""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""search\"":{\""type\"":\""string\""},\""suggesterName\"":{\""type\"":\""string\""},\""filter\"":{\""type\"":\""string\""},\""select\"":{\""type\"":\""string\""},\""top\"":{\""type\"":\""integer\""}},\""required\"":[\""search\"",\""suggesterName\""]}""
        },
        {
            ""cid"": ""list_indexers"",
            ""endpoint"": ""/indexers"",
            ""method"": ""GET"",
            ""outcome"": ""List all indexers configured in the search service"",
            ""domain"": ""indexers"",
            ""requiredParams"": [],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""create_indexer"",
            ""endpoint"": ""/indexers"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new indexer to automatically pull data from a data source into an index"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""name"", ""dataSourceName"", ""targetIndexName""],
            ""optionalParams"": [""skillsetName"", ""schedule"", ""parameters"", ""fieldMappings"", ""outputFieldMappings""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""dataSourceName\"":{\""type\"":\""string\""},\""targetIndexName\"":{\""type\"":\""string\""},\""skillsetName\"":{\""type\"":\""string\""},\""schedule\"":{\""type\"":\""object\"",\""properties\"":{\""interval\"":{\""type\"":\""string\""},\""startTime\"":{\""type\"":\""string\""}}}},\""required\"":[\""name\"",\""dataSourceName\"",\""targetIndexName\""]}""
        },
        {
            ""cid"": ""get_indexer"",
            ""endpoint"": ""/indexers/{indexerName}"",
            ""method"": ""GET"",
            ""outcome"": ""Get the definition of a specific indexer"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""run_indexer"",
            ""endpoint"": ""/indexers/{indexerName}/run"",
            ""method"": ""POST"",
            ""outcome"": ""Trigger an indexer to run immediately and import data from its data source"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""get_indexer_status"",
            ""endpoint"": ""/indexers/{indexerName}/status"",
            ""method"": ""GET"",
            ""outcome"": ""Get the current status and execution history of an indexer"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""reset_indexer"",
            ""endpoint"": ""/indexers/{indexerName}/reset"",
            ""method"": ""POST"",
            ""outcome"": ""Reset an indexer so its next run reprocesses all documents from scratch"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""delete_indexer"",
            ""endpoint"": ""/indexers/{indexerName}"",
            ""method"": ""DELETE"",
            ""outcome"": ""Delete an indexer from the search service"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""list_datasources"",
            ""endpoint"": ""/datasources"",
            ""method"": ""GET"",
            ""outcome"": ""List all data source connections configured in the search service"",
            ""domain"": ""datasources"",
            ""requiredParams"": [],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""create_datasource"",
            ""endpoint"": ""/datasources"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new data source connection for indexers to pull data from"",
            ""domain"": ""datasources"",
            ""requiredParams"": [""name"", ""type"", ""credentials"", ""container""],
            ""optionalParams"": [""description"", ""dataChangeDetectionPolicy"", ""dataDeletionDetectionPolicy""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""type\"":{\""type\"":\""string\"",\""enum\"":[\""azuresql\"",\""cosmosdb\"",\""azureblob\"",\""azuretable\"",\""adlsgen2\"",\""sharepoint\""]},\""credentials\"":{\""type\"":\""object\"",\""properties\"":{\""connectionString\"":{\""type\"":\""string\""}}},\""container\"":{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""query\"":{\""type\"":\""string\""}}}},\""required\"":[\""name\"",\""type\"",\""credentials\"",\""container\""]}""
        },
        {
            ""cid"": ""get_datasource"",
            ""endpoint"": ""/datasources/{dataSourceName}"",
            ""method"": ""GET"",
            ""outcome"": ""Get the definition of a specific data source connection"",
            ""domain"": ""datasources"",
            ""requiredParams"": [""dataSourceName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""delete_datasource"",
            ""endpoint"": ""/datasources/{dataSourceName}"",
            ""method"": ""DELETE"",
            ""outcome"": ""Delete a data source connection from the search service"",
            ""domain"": ""datasources"",
            ""requiredParams"": [""dataSourceName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""list_skillsets"",
            ""endpoint"": ""/skillsets"",
            ""method"": ""GET"",
            ""outcome"": ""List all AI enrichment skillsets in the search service"",
            ""domain"": ""skillsets"",
            ""requiredParams"": [],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""create_skillset"",
            ""endpoint"": ""/skillsets"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new AI enrichment skillset with cognitive skills for document processing"",
            ""domain"": ""skillsets"",
            ""requiredParams"": [""name"", ""skills""],
            ""optionalParams"": [""description"", ""cognitiveServices"", ""knowledgeStore""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""skills\"":{\""type\"":\""array\"",\""items\"":{\""type\"":\""object\""}},\""description\"":{\""type\"":\""string\""},\""cognitiveServices\"":{\""type\"":\""object\""}},\""required\"":[\""name\"",\""skills\""]}""
        },
        {
            ""cid"": ""get_skillset"",
            ""endpoint"": ""/skillsets/{skillsetName}"",
            ""method"": ""GET"",
            ""outcome"": ""Get the definition of a specific AI enrichment skillset"",
            ""domain"": ""skillsets"",
            ""requiredParams"": [""skillsetName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""delete_skillset"",
            ""endpoint"": ""/skillsets/{skillsetName}"",
            ""method"": ""DELETE"",
            ""outcome"": ""Delete an AI enrichment skillset from the search service"",
            ""domain"": ""skillsets"",
            ""requiredParams"": [""skillsetName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""list_synonym_maps"",
            ""endpoint"": ""/synonymmaps"",
            ""method"": ""GET"",
            ""outcome"": ""List all synonym maps in the search service"",
            ""domain"": ""synonyms"",
            ""requiredParams"": [],
            ""optionalParams"": [""$select""]
        },
        {
            ""cid"": ""create_synonym_map"",
            ""endpoint"": ""/synonymmaps"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new synonym map to expand or replace search terms during query processing"",
            ""domain"": ""synonyms"",
            ""requiredParams"": [""name"", ""synonyms""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""format\"":{\""type\"":\""string\"",\""default\"":\""solr\""},\""synonyms\"":{\""type\"":\""string\"",\""description\"":\""Synonym rules in Solr format, one rule per line. E.g. USA, United States, United States of America\""}},\""required\"":[\""name\"",\""synonyms\""]}""
        },
        {
            ""cid"": ""get_service_statistics"",
            ""endpoint"": ""/servicestats"",
            ""method"": ""GET"",
            ""outcome"": ""Get service-level statistics including document counts, storage usage, and resource counters"",
            ""domain"": ""admin"",
            ""requiredParams"": [],
            ""optionalParams"": []
        },
        {
            ""cid"": ""analyze_text"",
            ""endpoint"": ""/indexes/{indexName}/analyze"",
            ""method"": ""POST"",
            ""outcome"": ""Analyze how a specific analyzer breaks text into tokens for debugging search behavior"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""indexName"", ""text"", ""analyzer""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""text\"":{\""type\"":\""string\"",\""description\"":\""Text to analyze\""},\""analyzer\"":{\""type\"":\""string\"",\""description\"":\""Analyzer name e.g. standard.lucene or en.microsoft\""}},\""required\"":[\""text\"",\""analyzer\""]}""
        },
        {
            ""cid"": ""update_index"",
            ""endpoint"": ""/indexes/{indexName}"",
            ""method"": ""PUT"",
            ""outcome"": ""Update an existing search index definition including fields, analyzers, and scoring profiles"",
            ""domain"": ""indexes"",
            ""requiredParams"": [""indexName"", ""name"", ""fields""],
            ""optionalParams"": [""suggesters"", ""scoringProfiles"", ""corsOptions"", ""analyzers"", ""semantic""]
        },
        {
            ""cid"": ""update_indexer"",
            ""endpoint"": ""/indexers/{indexerName}"",
            ""method"": ""PUT"",
            ""outcome"": ""Update an existing indexer configuration including schedule, field mappings, and skillset"",
            ""domain"": ""indexers"",
            ""requiredParams"": [""indexerName"", ""name"", ""dataSourceName"", ""targetIndexName""],
            ""optionalParams"": [""skillsetName"", ""schedule"", ""parameters"", ""fieldMappings"", ""outputFieldMappings""]
        },
        {
            ""cid"": ""update_datasource"",
            ""endpoint"": ""/datasources/{dataSourceName}"",
            ""method"": ""PUT"",
            ""outcome"": ""Update an existing data source connection string, container, or change detection policy"",
            ""domain"": ""datasources"",
            ""requiredParams"": [""dataSourceName"", ""name"", ""type"", ""credentials"", ""container""],
            ""optionalParams"": [""description"", ""dataChangeDetectionPolicy"", ""dataDeletionDetectionPolicy""]
        },
        {
            ""cid"": ""update_skillset"",
            ""endpoint"": ""/skillsets/{skillsetName}"",
            ""method"": ""PUT"",
            ""outcome"": ""Update an existing AI enrichment skillset with modified cognitive skills"",
            ""domain"": ""skillsets"",
            ""requiredParams"": [""skillsetName"", ""name"", ""skills""],
            ""optionalParams"": [""description"", ""cognitiveServices"", ""knowledgeStore""]
        },
        {
            ""cid"": ""update_synonym_map"",
            ""endpoint"": ""/synonymmaps/{synonymMapName}"",
            ""method"": ""PUT"",
            ""outcome"": ""Update an existing synonym map with new synonym rules"",
            ""domain"": ""synonyms"",
            ""requiredParams"": [""synonymMapName"", ""name"", ""synonyms""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""get_synonym_map"",
            ""endpoint"": ""/synonymmaps/{synonymMapName}"",
            ""method"": ""GET"",
            ""outcome"": ""Get the definition of a specific synonym map including its rules"",
            ""domain"": ""synonyms"",
            ""requiredParams"": [""synonymMapName""],
            ""optionalParams"": []
        },
        {
            ""cid"": ""delete_synonym_map"",
            ""endpoint"": ""/synonymmaps/{synonymMapName}"",
            ""method"": ""DELETE"",
            ""outcome"": ""Delete a synonym map from the search service"",
            ""domain"": ""synonyms"",
            ""requiredParams"": [""synonymMapName""],
            ""optionalParams"": []
        }
    ]";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // Derive base URL from the swagger host (set in apiDefinition.swagger.json)
        var requestUri = this.Context.Request.RequestUri;
        McOptions.BaseApiUrl = $"{requestUri.Scheme}://{requestUri.Host}";

        // Forward auth headers from inbound request to outbound Azure AI Search calls
        var customHeaders = new Dictionary<string, string>();
        if (this.Context.Request.Headers.TryGetValues("api-key", out var apiKeyValues))
            customHeaders["api-key"] = apiKeyValues.FirstOrDefault() ?? "";
        if (this.Context.Request.Headers.Authorization != null)
            customHeaders["Authorization"] = this.Context.Request.Headers.Authorization.ToString();
        if (customHeaders.Count > 0)
            McOptions.CustomHeaders = customHeaders;

        var handler = new McpRequestHandler(Options);
        MissionControl.RegisterMission(handler, McOptions, CAPABILITY_INDEX, this);
        RegisterResources(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Knowledge Resources ──────────────────────────────────────────────

    /// <summary>Send a GET request to the Search API with proper auth headers.</summary>
    private async Task<string> SearchApiGetAsync(string path)
    {
        var url = $"{McOptions.BaseApiUrl}/{path.TrimStart('/')}";
        if (!url.Contains("api-version"))
            url += (url.Contains("?") ? "&" : "?") + $"api-version={McOptions.DefaultApiVersion}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (McOptions.CustomHeaders != null)
            foreach (var kvp in McOptions.CustomHeaders)
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private void RegisterResources(McpRequestHandler handler)
    {
        // Static resource: list of all indexes (grounding for what's searchable)
        handler.AddResource("search://indexes", "Search Indexes",
            "All search indexes available in this Azure AI Search service. Use this to understand what knowledge bases exist before searching.",
            handler: async (ct) =>
            {
                var content = await SearchApiGetAsync("indexes?$select=name").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = "search://indexes", ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.9; });

        // Resource template: index schema by name (fields, types, analyzers)
        handler.AddResourceTemplate("search://indexes/{indexName}/schema", "Index Schema",
            "Field definitions, types, and search attributes for a specific index. Provides the knowledge structure needed to formulate accurate search queries.",
            handler: async (uri, ct) =>
            {
                var parameters = McpRequestHandler.ExtractUriParameters("search://indexes/{indexName}/schema", uri);
                var indexName = parameters.ContainsKey("indexName") ? parameters["indexName"] : "";
                var content = await SearchApiGetAsync($"indexes/{Uri.EscapeDataString(indexName)}").ConfigureAwait(false);

                try
                {
                    var indexDef = JObject.Parse(content);
                    var schema = new JObject
                    {
                        ["indexName"] = indexName,
                        ["fields"] = indexDef["fields"],
                        ["semantic"] = indexDef["semantic"],
                        ["suggesters"] = indexDef["suggesters"]
                    };
                    content = schema.ToString(Newtonsoft.Json.Formatting.Indented);
                }
                catch { }

                return new JArray { new JObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.8; });

        // Resource template: index statistics
        // Resource template: index statistics
        handler.AddResourceTemplate("search://indexes/{indexName}/stats", "Index Statistics",
            "Document count and storage size for a specific index.",
            handler: async (uri, ct) =>
            {
                var parameters = McpRequestHandler.ExtractUriParameters("search://indexes/{indexName}/stats", uri);
                var indexName = parameters.ContainsKey("indexName") ? parameters["indexName"] : "";
                var content = await SearchApiGetAsync($"indexes/{Uri.EscapeDataString(indexName)}/stats").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = content } };
            });

        // Static resource: service-level statistics
        handler.AddResource("search://service/stats", "Service Statistics",
            "Service-level statistics including total document counts, storage usage, and resource counters across all indexes.",
            handler: async (ct) =>
            {
                var content = await SearchApiGetAsync("servicestats").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = "search://service/stats", ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.7; });

        // Static resource: data sources list
        handler.AddResource("search://datasources", "Data Sources",
            "All data source connections configured in this search service. Shows where indexers pull data from.",
            handler: async (ct) =>
            {
                var content = await SearchApiGetAsync("datasources?$select=name,type,description").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = "search://datasources", ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.6; });

        // Static resource: skillsets list
        handler.AddResource("search://skillsets", "Skillsets",
            "All AI enrichment skillsets configured in this search service. Shows what cognitive processing is available.",
            handler: async (ct) =>
            {
                var content = await SearchApiGetAsync("skillsets?$select=name,description").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = "search://skillsets", ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.5; });

        // Static resource: synonym maps list
        handler.AddResource("search://synonymmaps", "Synonym Maps",
            "All synonym maps configured in this search service. Shows query expansion and term replacement rules.",
            handler: async (ct) =>
            {
                var content = await SearchApiGetAsync("synonymmaps?$select=name").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = "search://synonymmaps", ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.5; });

        // Resource template: indexer status and execution history
        handler.AddResourceTemplate("search://indexers/{indexerName}/status", "Indexer Status",
            "Current status and execution history for a specific indexer. Shows last run time, success/failure, errors, and documents processed.",
            handler: async (uri, ct) =>
            {
                var parameters = McpRequestHandler.ExtractUriParameters("search://indexers/{indexerName}/status", uri);
                var indexerName = parameters.ContainsKey("indexerName") ? parameters["indexerName"] : "";
                var content = await SearchApiGetAsync($"indexers/{Uri.EscapeDataString(indexerName)}/status").ConfigureAwait(false);
                return new JArray { new JObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = content } };
            },
            annotationsConfig: a => { a["audience"] = new JArray("assistant"); a["priority"] = 0.7; });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey)) return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
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

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK + ORCHESTRATION ENGINE                           ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler (MCP 2025-11-25) plus mission control classes:  ║
// ║  MissionControlOptions, CapabilityIndex, ApiProxy, McpChainClient,         ║
// ║  DiscoveryEngine, MissionControl.                                          ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Mission Control Configuration ────────────────────────────────────────────

public enum DiscoveryMode { Static, Hybrid, McpChain }
public enum BatchMode { Sequential, BatchEndpoint }

public class MissionControlOptions
{
    /// <summary>Service name used in tool names (scan_{ServiceName}, launch_{ServiceName}).</summary>
    public string ServiceName { get; set; } = "api";

    /// <summary>Discovery mode: Static (embedded index), Hybrid (index + describe), McpChain (external MCP).</summary>
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.Static;

    /// <summary>Base URL for all API calls (no trailing slash).</summary>
    public string BaseApiUrl { get; set; }

    /// <summary>Default API version appended to URL if set (e.g., "v1.0").</summary>
    public string DefaultApiVersion { get; set; }

    /// <summary>Batch execution mode: Sequential (one at a time) or BatchEndpoint (native $batch).</summary>
    public BatchMode BatchMode { get; set; } = BatchMode.Sequential;

    /// <summary>Path for native batch endpoint (e.g., "/$batch").</summary>
    public string BatchEndpointPath { get; set; } = "/$batch";

    /// <summary>Maximum requests per batch call.</summary>
    public int MaxBatchSize { get; set; } = 20;

    /// <summary>Default page size for GET collection endpoints ($top).</summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Discovery cache TTL in minutes.</summary>
    public int CacheExpiryMinutes { get; set; } = 10;

    /// <summary>Describe/metadata cache TTL in minutes (Hybrid mode).</summary>
    public int DescribeCacheTTL { get; set; } = 30;

    /// <summary>Maximum results returned by discover tool.</summary>
    public int MaxDiscoverResults { get; set; } = 3;

    /// <summary>Enable response summarization (HTML stripping, truncation). Default true.</summary>
    public bool SummarizeResponses { get; set; } = true;

    /// <summary>Max characters for body/HTML fields in summarization.</summary>
    public int MaxBodyLength { get; set; } = 500;

    /// <summary>Max characters for text fields in summarization.</summary>
    public int MaxTextLength { get; set; } = 1000;

    // Hybrid mode
    /// <summary>Describe endpoint pattern with {resource} placeholder (e.g., "/sobjects/{resource}/describe").</summary>
    public string DescribeEndpointPattern { get; set; }

    // McpChain mode
    /// <summary>External MCP server endpoint URL.</summary>
    public string McpChainEndpoint { get; set; }

    /// <summary>Tool name to call on the external MCP server.</summary>
    public string McpChainToolName { get; set; }

    /// <summary>Query prefix for MCP chain searches (e.g., "Microsoft Graph").</summary>
    public string McpChainQueryPrefix { get; set; }

    /// <summary>Author-defined smart defaults. Key = endpoint pattern substring, Value = action(endpoint, queryParams).</summary>
    public Dictionary<string, Action<string, JObject>> SmartDefaults { get; set; }

    /// <summary>Custom headers added to every API request. Use for non-standard auth (e.g., api-key header).</summary>
    public Dictionary<string, string> CustomHeaders { get; set; }
}

// ── Capability Entry ─────────────────────────────────────────────────────────

public class CapabilityEntry
{
    public string Cid { get; set; }
    public string Endpoint { get; set; }
    public string Method { get; set; }
    public string Outcome { get; set; }
    public string Domain { get; set; }
    public string[] RequiredParams { get; set; }
    public string[] OptionalParams { get; set; }
    public string SchemaJson { get; set; }
}

// ── Capability Index ─────────────────────────────────────────────────────────

public class CapabilityIndex
{
    private readonly List<CapabilityEntry> _entries;

    public CapabilityIndex(string indexJson)
    {
        _entries = new List<CapabilityEntry>();
        if (string.IsNullOrWhiteSpace(indexJson)) return;

        var array = JArray.Parse(indexJson);
        foreach (var item in array)
        {
            _entries.Add(new CapabilityEntry
            {
                Cid = item.Value<string>("cid") ?? "",
                Endpoint = item.Value<string>("endpoint") ?? "",
                Method = (item.Value<string>("method") ?? "GET").ToUpperInvariant(),
                Outcome = item.Value<string>("outcome") ?? "",
                Domain = item.Value<string>("domain") ?? "",
                RequiredParams = item["requiredParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                OptionalParams = item["optionalParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                SchemaJson = item.Value<string>("schemaJson")
            });
        }
    }

    /// <summary>
    /// Search the index by keyword query and optional domain filter.
    /// Scoring: exact cid match (100), domain match (50), keyword overlap on outcome (10 per word).
    /// Returns top N results ordered by score descending.
    /// </summary>
    public List<CapabilityEntry> Search(string query, string domain = null, int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(domain))
            return _entries.Take(maxResults).ToList();

        var queryLower = (query ?? "").ToLowerInvariant();
        var queryWords = queryLower.Split(new[] { ' ', '_', '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var domainLower = (domain ?? "").ToLowerInvariant();

        var scored = new List<KeyValuePair<CapabilityEntry, int>>();

        foreach (var entry in _entries)
        {
            int score = 0;
            var cidLower = entry.Cid.ToLowerInvariant();
            var outcomeLower = entry.Outcome.ToLowerInvariant();
            var entryDomainLower = entry.Domain.ToLowerInvariant();
            var endpointLower = entry.Endpoint.ToLowerInvariant();

            // Exact CID match
            if (cidLower == queryLower) score += 100;
            // Partial CID match
            else if (cidLower.Contains(queryLower) || queryLower.Contains(cidLower)) score += 60;

            // Domain filter match
            if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower == domainLower) score += 50;
            else if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower != domainLower) score -= 20;

            // Keyword overlap on outcome, endpoint, and cid
            foreach (var word in queryWords)
            {
                if (word.Length < 2) continue;
                if (outcomeLower.Contains(word)) score += 10;
                if (cidLower.Contains(word)) score += 15;
                if (endpointLower.Contains(word)) score += 8;
            }

            // Method match (if query contains GET, POST, etc.)
            var methodUpper = entry.Method.ToUpperInvariant();
            if (queryLower.Contains(methodUpper.ToLowerInvariant()) && methodUpper.Length > 2) score += 5;

            if (score > 0) scored.Add(new KeyValuePair<CapabilityEntry, int>(entry, score));
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>Get a specific capability by CID (exact match).</summary>
    public CapabilityEntry Get(string cid)
    {
        return _entries.FirstOrDefault(e =>
            string.Equals(e.Cid, cid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get all entries in the index.</summary>
    public List<CapabilityEntry> GetAll() => new List<CapabilityEntry>(_entries);

    /// <summary>Total number of indexed capabilities.</summary>
    public int Count => _entries.Count;
}

// ── Cache Infrastructure ─────────────────────────────────────────────────────

internal class CacheEntry
{
    public JObject Result { get; set; }
    public DateTime Expiry { get; set; }
}

// ── API Proxy ────────────────────────────────────────────────────────────────

public class ApiProxy
{
    private readonly MissionControlOptions _options;
    private const int MAX_RETRIES = 3;

    // Common collection endpoint patterns for auto-$top injection
    private static readonly string[] CollectionPatterns = new[]
    {
        "/messages", "/events", "/users", "/groups", "/teams", "/channels",
        "/members", "/children", "/items", "/lists", "/tasks", "/contacts",
        "/calendars", "/drives", "/sites", "/records", "/customers", "/orders",
        "/products", "/invoices", "/accounts", "/leads", "/cases", "/tickets"
    };

    public ApiProxy(MissionControlOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Execute a single API request. Forwards auth, applies smart defaults,
    /// handles retries, and summarizes responses.
    /// </summary>
    public async Task<JObject> InvokeAsync(
        ScriptBase context,
        string endpoint,
        string method,
        JObject body = null,
        JObject queryParams = null,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        // Validate endpoint (warn but allow unknown)
        string warning = null;
        if (index != null)
        {
            var match = index.GetAll().Any(e =>
                string.Equals(e.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase) ||
                EndpointMatchesPattern(e.Endpoint, endpoint));
            if (!match)
                warning = $"Endpoint '{endpoint}' is not in the capability index. Proceeding anyway.";
        }

        // Build URL
        var url = BuildUrl(endpoint, method, queryParams, apiVersion);

        // Execute with retry
        return await ExecuteWithRetryAsync(context, url, method, body, warning).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute multiple API requests. Sequential mode: one at a time, in order.
    /// BatchEndpoint mode: single POST to $batch path.
    /// </summary>
    public async Task<JObject> BatchInvokeAsync(
        ScriptBase context,
        JArray requests,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        if (requests == null || requests.Count == 0)
            return CreateErrorResult("batch_empty", "No requests provided",
                "Provide at least one request in the requests array.",
                "The requests array is empty.");

        if (requests.Count > _options.MaxBatchSize)
            return CreateErrorResult("batch_too_large",
                $"Batch exceeds maximum size of {_options.MaxBatchSize}",
                $"Split into batches of {_options.MaxBatchSize} or fewer.",
                $"Too many requests. Maximum is {_options.MaxBatchSize}, got {requests.Count}.");

        if (_options.BatchMode == BatchMode.BatchEndpoint)
            return await ExecuteBatchEndpointAsync(context, requests, apiVersion).ConfigureAwait(false);

        return await ExecuteSequentialBatchAsync(context, requests, apiVersion, index).ConfigureAwait(false);
    }

    /// <summary>Strip HTML tags, decode entities, collapse whitespace, truncate.</summary>
    public void SummarizeResponse(JToken token)
    {
        if (!_options.SummarizeResponses) return;
        SummarizeToken(token, _options.MaxBodyLength, _options.MaxTextLength);
    }

    // ── Private: URL Building ────────────────────────────────────────────

    private string BuildUrl(string endpoint, string method, JObject queryParams, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;

        // Build path
        var path = endpoint?.TrimStart('/') ?? "";
        string url;
        if (!string.IsNullOrWhiteSpace(version) && !baseUrl.Contains(version))
            url = $"{baseUrl}/{version}/{path}";
        else
            url = $"{baseUrl}/{path}";

        // Apply smart defaults
        queryParams = queryParams ?? new JObject();
        ApplySmartDefaults(endpoint, method, queryParams);

        // Build query string
        var queryParts = new List<string>();
        foreach (var prop in queryParams.Properties())
        {
            var val = prop.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                queryParts.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(val)}");
        }

        if (queryParts.Count > 0)
            url += (url.Contains("?") ? "&" : "?") + string.Join("&", queryParts);

        return url;
    }

    private void ApplySmartDefaults(string endpoint, string method, JObject queryParams)
    {
        var endpointLower = (endpoint ?? "").ToLowerInvariant();
        var methodUpper = (method ?? "GET").ToUpperInvariant();

        // Built-in: $top for GET collection endpoints
        if (methodUpper == "GET" && IsCollectionEndpoint(endpointLower))
        {
            if (queryParams["$top"] == null && queryParams["top"] == null &&
                queryParams["per_page"] == null && queryParams["limit"] == null &&
                queryParams["pageSize"] == null)
            {
                queryParams["$top"] = _options.DefaultPageSize;
            }
        }

        // Author-defined smart defaults
        if (_options.SmartDefaults != null)
        {
            foreach (var kvp in _options.SmartDefaults)
            {
                if (endpointLower.Contains(kvp.Key.ToLowerInvariant()))
                    kvp.Value?.Invoke(endpoint, queryParams);
            }
        }
    }

    private bool IsCollectionEndpoint(string endpointLower)
    {
        // Ends with a collection name (not an ID segment)
        var lastSegment = endpointLower.Split('/').LastOrDefault() ?? "";
        if (lastSegment.StartsWith("{") || Guid.TryParse(lastSegment, out _)) return false;

        return CollectionPatterns.Any(p => endpointLower.EndsWith(p, StringComparison.OrdinalIgnoreCase))
            || (lastSegment.Length > 2 && !lastSegment.Contains("{"));
    }

    private static bool EndpointMatchesPattern(string pattern, string endpoint)
    {
        var patternParts = pattern.Split('/');
        var endpointParts = endpoint.Split('/');
        if (patternParts.Length != endpointParts.Length) return false;

        for (int i = 0; i < patternParts.Length; i++)
        {
            if (patternParts[i].StartsWith("{") && patternParts[i].EndsWith("}")) continue;
            if (!string.Equals(patternParts[i], endpointParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // ── Private: HTTP Execution ──────────────────────────────────────────

    private async Task<JObject> ExecuteWithRetryAsync(
        ScriptBase context, string url, string method,
        JObject body, string warning, int retryCount = 0)
    {
        var httpMethod = new HttpMethod(method.ToUpperInvariant());
        var request = new HttpRequestMessage(httpMethod, url);

        // Forward auth
        if (context.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = context.Context.Request.Headers.Authorization;

        // Custom headers (e.g., api-key for Azure AI Search)
        if (_options.CustomHeaders != null)
        {
            foreach (var kvp in _options.CustomHeaders)
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        // Content-Type for write operations
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (httpMethod == HttpMethod.Post || httpMethod.Method == "PATCH" || httpMethod.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await context.Context.SendAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CreateErrorResult("connection_error", ex.Message,
                "Check that the API is reachable and try again.",
                $"Failed to connect to {url}: {ex.Message}");
        }

        var statusCode = (int)response.StatusCode;
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // 429: Retry with backoff
        if (statusCode == 429 && retryCount < MAX_RETRIES)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (int.TryParse(val, out var seconds))
                    retryAfter = Math.Min(seconds, 30);
            }
            await Task.Delay(retryAfter * 1000).ConfigureAwait(false);
            return await ExecuteWithRetryAsync(context, url, method, body, warning, retryCount + 1).ConfigureAwait(false);
        }

        // Error responses
        if (statusCode == 401 || statusCode == 403)
        {
            return CreateErrorResult("permission_denied", $"HTTP {statusCode}",
                "Check that your account has the required permissions. Contact your administrator if needed.",
                $"Access denied ({statusCode}). You don't have permission for this operation.");
        }

        if (statusCode == 404)
        {
            return CreateErrorResult("not_found", $"HTTP 404",
                "Verify the endpoint path and any resource IDs. Use scan to find the correct endpoint.",
                $"Resource not found at {url}. The endpoint may be incorrect or the resource may not exist.");
        }

        if (statusCode >= 400)
        {
            var errorDetail = "";
            try { errorDetail = JObject.Parse(responseBody)?["message"]?.ToString() ?? responseBody; }
            catch { errorDetail = responseBody; }

            return CreateErrorResult("api_error", $"HTTP {statusCode}: {errorDetail}",
                "Check the request parameters and try again.",
                $"API returned error {statusCode}: {errorDetail}");
        }

        // Success
        JToken data;
        try
        {
            data = string.IsNullOrWhiteSpace(responseBody)
                ? new JObject { ["success"] = true }
                : JToken.Parse(responseBody);
        }
        catch
        {
            data = new JObject { ["text"] = responseBody };
        }

        // Summarize if enabled
        SummarizeResponse(data);

        // Build result
        var result = new JObject
        {
            ["success"] = true,
            ["data"] = data
        };

        // Check for pagination
        if (data is JObject dataObj)
        {
            var nextLink = dataObj.Value<string>("@odata.nextLink")
                ?? dataObj.Value<string>("nextLink")
                ?? dataObj.Value<string>("next_page_url")
                ?? dataObj.Value<string>("next");

            if (!string.IsNullOrWhiteSpace(nextLink))
            {
                result["hasMore"] = true;
                result["nextLink"] = nextLink;
                result["nextPageHint"] = $"Call launch again with the nextLink value as the full URL to get the next page.";
            }
        }

        if (!string.IsNullOrWhiteSpace(warning))
            result["warning"] = warning;

        return result;
    }

    // ── Private: Batch Execution ─────────────────────────────────────────

    private async Task<JObject> ExecuteSequentialBatchAsync(
        ScriptBase context, JArray requests, string apiVersion, CapabilityIndex index)
    {
        var responses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var req in requests)
        {
            var id = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var endpoint = req.Value<string>("endpoint") ?? "";
            var method = req.Value<string>("method") ?? "GET";
            var body = req["body"] as JObject;
            var qp = req["query_params"] as JObject;

            try
            {
                var result = await InvokeAsync(context, endpoint, method, body, qp, apiVersion, index).ConfigureAwait(false);
                var success = result.Value<bool?>("success") ?? false;
                if (success) successCount++; else errorCount++;

                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = success,
                    ["status"] = success ? 200 : 400,
                    ["data"] = result
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = false,
                    ["status"] = 500,
                    ["error"] = ex.Message
                });
            }
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = responses
        };
    }

    private async Task<JObject> ExecuteBatchEndpointAsync(
        ScriptBase context, JArray requests, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;
        var batchUrl = !string.IsNullOrWhiteSpace(version)
            ? $"{baseUrl}/{version}{_options.BatchEndpointPath}"
            : $"{baseUrl}{_options.BatchEndpointPath}";

        var batchRequests = new JArray();
        foreach (var req in requests)
        {
            var batchReq = new JObject
            {
                ["id"] = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                ["method"] = (req.Value<string>("method") ?? "GET").ToUpperInvariant(),
                ["url"] = req.Value<string>("endpoint") ?? ""
            };

            var body = req["body"] as JObject;
            var method = batchReq.Value<string>("method");
            if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
            {
                batchReq["body"] = body;
                batchReq["headers"] = new JObject { ["Content-Type"] = "application/json" };
            }

            batchRequests.Add(batchReq);
        }

        var batchBody = new JObject { ["requests"] = batchRequests };
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, batchUrl)
        {
            Content = new StringContent(batchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };

        if (context.Context.Request.Headers.Authorization != null)
            httpRequest.Headers.Authorization = context.Context.Request.Headers.Authorization;

        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject batchResponse;
        try { batchResponse = JObject.Parse(content); }
        catch { return CreateErrorResult("batch_parse_error", "Failed to parse batch response", "Try individual requests instead.", content); }

        var batchResponses = batchResponse["responses"] as JArray ?? new JArray();
        var resultResponses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var resp in batchResponses)
        {
            var status = resp.Value<int?>("status") ?? 0;
            var success = status >= 200 && status < 300;
            if (success) successCount++; else errorCount++;

            var processed = new JObject
            {
                ["id"] = resp.Value<string>("id"),
                ["status"] = status,
                ["success"] = success
            };

            if (success)
            {
                var bodyData = resp["body"];
                if (bodyData != null) SummarizeResponse(bodyData);
                processed["data"] = bodyData;
            }
            else
            {
                processed["error"] = resp["body"];
            }

            resultResponses.Add(processed);
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = resultResponses
        };
    }

    // ── Private: Response Summarization ──────────────────────────────────

    private void SummarizeToken(JToken token, int maxBodyLength, int maxTextLength)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name == "body" || name == "bodypreview" || name == "description") && prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    var stripped = StripHtml(val);
                    if (stripped.Length > maxBodyLength)
                    {
                        obj[prop.Name] = stripped.Substring(0, maxBodyLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                    else
                    {
                        obj[prop.Name] = stripped;
                    }
                }
                else if (prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    if (val.Length > maxTextLength)
                    {
                        obj[prop.Name] = val.Substring(0, maxTextLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray)
                {
                    SummarizeToken(prop.Value, maxBodyLength, maxTextLength);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                SummarizeToken(item, maxBodyLength, maxTextLength);
        }
    }

    /// <summary>Strip HTML tags, decode common entities, collapse whitespace.</summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
                    .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }

    // ── Private: Error Helpers ───────────────────────────────────────────

    private static JObject CreateErrorResult(string error, string message, string suggestion, string friendlyMessage)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = error,
            ["code"] = error,
            ["message"] = message,
            ["friendlyMessage"] = friendlyMessage,
            ["suggestion"] = suggestion
        };
    }
}

// ── MCP Chain Client ─────────────────────────────────────────────────────────

public class McpChainClient
{
    private static readonly Dictionary<string, CacheEntry> _chainCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discover capabilities by chaining to an external MCP server.
    /// Handles initialize handshake, tools/call, and result caching.
    /// </summary>
    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        MissionControlOptions options,
        string query,
        string category = null)
    {
        var cacheKey = $"{query}|{category ?? ""}".ToLowerInvariant();

        // Check cache
        if (_chainCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        try
        {
            var endpoint = options.McpChainEndpoint;
            var toolName = options.McpChainToolName;
            var prefix = options.McpChainQueryPrefix ?? "";

            // Step 1: Initialize handshake
            var initRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "power-mission-control",
                        ["version"] = "3.0.0"
                    }
                }
            };

            await SendMcpRequestAsync(context, endpoint, initRequest).ConfigureAwait(false);

            // Step 2: Send initialized notification
            var notifRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await SendMcpRequestAsync(context, endpoint, notifRequest).ConfigureAwait(false);

            // Step 3: Call tool
            var enhancedQuery = string.IsNullOrWhiteSpace(prefix)
                ? query
                : $"{prefix} {category ?? ""} API {query}".Trim();

            var toolRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/call",
                ["params"] = new JObject
                {
                    ["name"] = toolName,
                    ["arguments"] = new JObject { ["query"] = enhancedQuery }
                }
            };

            var toolResponse = await SendMcpRequestAsync(context, endpoint, toolRequest).ConfigureAwait(false);

            // Parse results and extract operations
            var operations = ExtractOperationsFromMcpResponse(toolResponse);

            var result = new JObject
            {
                ["success"] = true,
                ["operationCount"] = operations.Count,
                ["operations"] = operations,
                ["cached"] = false
            };

            // Cache result
            _chainCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(options.CacheExpiryMinutes)
            };

            return result;
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "mcp_chain_failed",
                ["message"] = ex.Message,
                ["friendlyMessage"] = "Failed to discover operations via external documentation. Try a different query.",
                ["suggestion"] = "Rephrase your query or try invoking a known endpoint directly."
            };
        }
    }

    private async Task<JObject> SendMcpRequestAsync(ScriptBase context, string endpoint, JObject request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(request.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    /// <summary>
    /// Extract API operations from MCP tool response using regex patterns.
    /// Looks for HTTP method + endpoint path patterns in the response text.
    /// </summary>
    private JArray ExtractOperationsFromMcpResponse(JObject response)
    {
        var operations = new JArray();
        var contentArray = response?["result"]?["content"] as JArray;
        if (contentArray == null) return operations;

        var fullText = "";
        foreach (var content in contentArray)
        {
            if (content.Value<string>("type") == "text")
                fullText += content.Value<string>("text") + "\n";
        }

        // Regex patterns to extract HTTP method + endpoint from documentation
        var patterns = new[]
        {
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            @"(?:endpoint|path|url|route):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(fullText, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string httpMethod, path;
                if (match.Groups.Count >= 3)
                {
                    httpMethod = match.Groups[1].Value.ToUpperInvariant();
                    path = match.Groups[2].Value;
                }
                else
                {
                    httpMethod = "GET";
                    path = match.Groups[1].Value;
                }

                var key = $"{httpMethod} {path}";
                if (seen.Contains(key)) continue;
                seen.Add(key);

                operations.Add(new JObject
                {
                    ["endpoint"] = path,
                    ["method"] = httpMethod,
                    ["description"] = $"{httpMethod} {path}",
                    ["source"] = "documentation"
                });
            }
        }

        return operations;
    }
}

// ── Discovery Engine ─────────────────────────────────────────────────────────

public class DiscoveryEngine
{
    private readonly MissionControlOptions _options;
    private readonly CapabilityIndex _index;
    private readonly McpChainClient _chainClient;
    private readonly ApiProxy _proxy;

    private static readonly Dictionary<string, CacheEntry> _describeCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    public DiscoveryEngine(MissionControlOptions options, CapabilityIndex index, ApiProxy proxy, McpChainClient chainClient = null)
    {
        _options = options;
        _index = index;
        _proxy = proxy;
        _chainClient = chainClient;
    }

    /// <summary>
    /// Discover capabilities based on query and discovery mode.
    /// Returns matching operations with optional schemas.
    /// </summary>
    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        string query,
        string domain = null,
        bool includeSchema = false)
    {
        switch (_options.DiscoveryMode)
        {
            case DiscoveryMode.Static:
                return DiscoverStatic(query, domain, includeSchema);

            case DiscoveryMode.Hybrid:
                return await DiscoverHybridAsync(context, query, domain, includeSchema).ConfigureAwait(false);

            case DiscoveryMode.McpChain:
                if (_chainClient == null)
                    return new JObject { ["success"] = false, ["error"] = "McpChainClient not configured" };
                return await _chainClient.DiscoverAsync(context, _options, query, domain).ConfigureAwait(false);

            default:
                return DiscoverStatic(query, domain, includeSchema);
        }
    }

    private JObject DiscoverStatic(string query, string domain, bool includeSchema)
    {
        if (_index == null)
            return new JObject { ["success"] = false, ["error"] = "No capability index configured" };

        var matches = _index.Search(query, domain, _options.MaxDiscoverResults);
        var operations = new JArray();

        foreach (var entry in matches)
        {
            var op = new JObject
            {
                ["cid"] = entry.Cid,
                ["endpoint"] = entry.Endpoint,
                ["method"] = entry.Method,
                ["outcome"] = entry.Outcome,
                ["domain"] = entry.Domain
            };

            if (entry.RequiredParams != null && entry.RequiredParams.Length > 0)
                op["requiredParams"] = new JArray(entry.RequiredParams);
            if (entry.OptionalParams != null && entry.OptionalParams.Length > 0)
                op["optionalParams"] = new JArray(entry.OptionalParams);

            if (includeSchema && !string.IsNullOrWhiteSpace(entry.SchemaJson))
            {
                try { op["inputSchema"] = JObject.Parse(entry.SchemaJson); }
                catch { op["inputSchema"] = entry.SchemaJson; }
            }

            operations.Add(op);
        }

        return new JObject
        {
            ["success"] = true,
            ["operationCount"] = operations.Count,
            ["totalCapabilities"] = _index.Count,
            ["operations"] = operations,
            ["cached"] = false
        };
    }

    private async Task<JObject> DiscoverHybridAsync(
        ScriptBase context, string query, string domain, bool includeSchema)
    {
        // Start with static index search
        var result = DiscoverStatic(query, domain, includeSchema);
        if (!result.Value<bool>("success")) return result;

        // If include_schema is requested and we have a describe endpoint pattern,
        // fetch live schemas for matched operations
        if (includeSchema && !string.IsNullOrWhiteSpace(_options.DescribeEndpointPattern))
        {
            var operations = result["operations"] as JArray;
            if (operations != null)
            {
                foreach (var op in operations)
                {
                    var endpoint = op.Value<string>("endpoint") ?? "";
                    var resource = ExtractResourceFromEndpoint(endpoint);
                    if (string.IsNullOrWhiteSpace(resource)) continue;

                    // Check describe cache
                    var describeCacheKey = $"describe:{resource}";
                    if (_describeCache.TryGetValue(describeCacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                    {
                        op["liveSchema"] = cached.Result.DeepClone();
                        op["liveSchemaSource"] = "cache";
                        continue;
                    }

                    // Fetch live schema from describe endpoint
                    try
                    {
                        var describePath = _options.DescribeEndpointPattern.Replace("{resource}", resource);
                        var describeResult = await _proxy.InvokeAsync(context, describePath, "GET").ConfigureAwait(false);

                        if (describeResult.Value<bool?>("success") == true)
                        {
                            var describeData = describeResult["data"];
                            op["liveSchema"] = describeData;
                            op["liveSchemaSource"] = "live";

                            // Cache describe result
                            _describeCache[describeCacheKey] = new CacheEntry
                            {
                                Result = (describeData as JObject)?.DeepClone() as JObject ?? new JObject(),
                                Expiry = DateTime.UtcNow.AddMinutes(_options.DescribeCacheTTL)
                            };
                        }
                    }
                    catch { /* Describe failed, proceed with static schema only */ }
                }
            }
        }

        return result;
    }

    /// <summary>Extract resource name from an endpoint path (e.g., "/items/{id}" → "items").</summary>
    private static string ExtractResourceFromEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var segments = endpoint.Trim('/').Split('/');
        // Find the first non-parameter segment
        foreach (var seg in segments)
        {
            if (!seg.StartsWith("{") && !string.IsNullOrWhiteSpace(seg))
                return seg;
        }
        return null;
    }
}

// ── MissionControl (Registration) ────────────────────────────────────────────

public static class MissionControl
{
    /// <summary>
    /// Register scan, launch, and sequence tools on the MCP handler.
    /// This is the main entry point for mission control mode.
    /// </summary>
    public static void RegisterMission(
        McpRequestHandler handler,
        MissionControlOptions options,
        string capabilityIndexJson,
        ScriptBase context)
    {
        var index = !string.IsNullOrWhiteSpace(capabilityIndexJson)
            ? new CapabilityIndex(capabilityIndexJson)
            : null;

        var proxy = new ApiProxy(options);
        var chainClient = options.DiscoveryMode == DiscoveryMode.McpChain ? new McpChainClient() : null;
        var discovery = new DiscoveryEngine(options, index, proxy, chainClient);

        var serviceName = options.ServiceName ?? "api";

        // ── scan_{service} ────────────────────────────────────────────────

        var scanDescription = $"Scan for available {serviceName} operations matching your intent. " +
            $"Always call this before launch_{serviceName} to find the correct endpoint and required parameters. " +
            $"Returns operation summaries with endpoints, methods, and descriptions. " +
            $"Use include_schema=true to get full input parameter details for a specific operation.";

        handler.AddTool($"scan_{serviceName}", scanDescription,
            schemaConfig: s => s
                .String("query", "Natural language description of what you want to do (e.g., 'create a customer', 'list orders')", required: true)
                .String("domain", $"Filter by domain category (optional)", required: false)
                .Boolean("include_schema", "Set true to include full input parameter schemas in the results (costs more tokens)", required: false),
            handler: async (args, ct) =>
            {
                var query = args.Value<string>("query") ?? "";
                var domain = args.Value<string>("domain");
                var includeSchema = args.Value<bool?>("include_schema") ?? false;

                return await discovery.DiscoverAsync(context, query, domain, includeSchema).ConfigureAwait(false);
            },
            annotationsConfig: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── launch_{service} ─────────────────────────────────────────────

        var launchDescription = $"Launch a {serviceName} API operation. " +
            $"Use scan_{serviceName} first to find the correct endpoint. " +
            $"Replace any {{id}} placeholders in the endpoint with actual values.";

        handler.AddTool($"launch_{serviceName}", launchDescription,
            schemaConfig: s => s
                .String("endpoint", "API endpoint path (e.g., '/customers', '/orders/123')", required: true)
                .String("method", "HTTP method", required: true, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .Object("body", "Request body for POST/PATCH/PUT operations", nested => { }, required: false)
                .Object("query_params", "Query parameters as key-value pairs", nested => { }, required: false)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var endpoint = args.Value<string>("endpoint") ?? "";
                var method = args.Value<string>("method") ?? "GET";
                var body = args["body"] as JObject;
                var queryParams = args["query_params"] as JObject;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.InvokeAsync(context, endpoint, method, body, queryParams, apiVersion, index).ConfigureAwait(false);
            });

        // ── sequence_{service} ───────────────────────────────────────────

        var batchDescription = $"Launch a sequence of multiple {serviceName} API operations in a single call. " +
            $"Maximum {options.MaxBatchSize} requests per sequence. " +
            $"Each request needs an id, endpoint, and method.";

        handler.AddTool($"sequence_{serviceName}", batchDescription,
            schemaConfig: s => s
                .Array("requests", "Array of API requests to execute",
                    itemSchema: new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique identifier for this request" },
                            ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "API endpoint path" },
                            ["method"] = new JObject { ["type"] = "string", ["description"] = "HTTP method (GET, POST, PATCH, PUT, DELETE)" },
                            ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body (for POST/PATCH/PUT)" },
                            ["query_params"] = new JObject { ["type"] = "object", ["description"] = "Query parameters" }
                        },
                        ["required"] = new JArray("endpoint", "method")
                    }, required: true)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var requests = args["requests"] as JArray;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.BatchInvokeAsync(context, requests, apiVersion, index).ConfigureAwait(false);
            });
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

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
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

// ── Internal Registration Classes ────────────────────────────────────────────

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

// ── McpRequestHandler ────────────────────────────────────────────────────────

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

    // ── Tool Registration ────────────────────────────────────────────────

    public McpRequestHandler AddTool(
        string name, string description,
        Action<McpSchemaBuilder> schemaConfig,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotationsConfig = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        JObject outputSchema = null;
        if (outputSchemaConfig != null) { var ob = new McpSchemaBuilder(); outputSchemaConfig(ob); outputSchema = ob.Build(); }

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

    // ── Resource Registration ─────────────────────────────────────────────

    public McpRequestHandler AddResource(
        string uri, string name, string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        _resources[uri] = new McpResourceDefinition
        {
            Uri = uri, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        };
        return this;
    }

    public McpRequestHandler AddResourceTemplate(
        string uriTemplate, string name, string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        });
        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    public McpRequestHandler AddPrompt(
        string name, string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name, Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };
        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize": return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping": return SerializeSuccess(id, new JObject());

                case "tools/list": return HandleToolsList(id);
                case "tools/call": return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list": return HandleResourcesList(id);
                case "resources/templates/list": return HandleResourceTemplatesList(id);
                case "resources/read": return await HandleResourcesReadAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/subscribe":
                case "resources/unsubscribe": return SerializeSuccess(id, new JObject());

                case "prompts/list": return HandlePromptsList(id);
                case "prompts/get": return await HandlePromptsGetAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                case "logging/setLevel": return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex) { return SerializeError(id, ex.Code, ex.Message); }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools) capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources) capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts) capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging) capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions) capabilities["completions"] = new JObject();

        var serverInfo = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject { ["protocolVersion"] = clientProtocolVersion, ["capabilities"] = capabilities, ["serverInfo"] = serverInfo };
        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;

        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version });
        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.InputSchema };
            if (!string.IsNullOrWhiteSpace(tool.Title)) toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null) toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0) toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }
        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private string HandleResourcesList(JToken id)
    {
        var arr = new JArray();
        foreach (var r in _resources.Values)
        {
            var o = new JObject { ["uri"] = r.Uri, ["name"] = r.Name };
            if (!string.IsNullOrWhiteSpace(r.Description)) o["description"] = r.Description;
            if (!string.IsNullOrWhiteSpace(r.MimeType)) o["mimeType"] = r.MimeType;
            if (r.Annotations != null && r.Annotations.Count > 0) o["annotations"] = r.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resources"] = arr });
    }

    private string HandleResourceTemplatesList(JToken id)
    {
        var arr = new JArray();
        foreach (var t in _resourceTemplates)
        {
            var o = new JObject { ["uriTemplate"] = t.UriTemplate, ["name"] = t.Name };
            if (!string.IsNullOrWhiteSpace(t.Description)) o["description"] = t.Description;
            if (!string.IsNullOrWhiteSpace(t.MimeType)) o["mimeType"] = t.MimeType;
            if (t.Annotations != null && t.Annotations.Count > 0) o["annotations"] = t.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resourceTemplates"] = arr });
    }

    private async Task<string> HandleResourcesReadAsync(JToken id, JObject request, CancellationToken ct)
    {
        var uri = (request["params"] as JObject)?.Value<string>("uri");
        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                return SerializeSuccess(id, new JObject { ["contents"] = contents });
            }
            catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    return SerializeSuccess(id, new JObject { ["contents"] = contents });
                }
                catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
            }
        }

        return SerializeError(id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return false;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}")) continue;
            if (!string.Equals(tp[i], up[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return result;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}"))
                result[tp[i].Substring(1, tp[i].Length - 2)] = up[i];
        }
        return result;
    }

    private string HandlePromptsList(JToken id)
    {
        var arr = new JArray();
        foreach (var p in _prompts.Values)
        {
            var o = new JObject { ["name"] = p.Name };
            if (!string.IsNullOrWhiteSpace(p.Description)) o["description"] = p.Description;
            if (p.Arguments.Count > 0)
            {
                var args = new JArray();
                foreach (var a in p.Arguments)
                {
                    var ao = new JObject { ["name"] = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.Description)) ao["description"] = a.Description;
                    if (a.Required) ao["required"] = true;
                    args.Add(ao);
                }
                o["arguments"] = args;
            }
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["prompts"] = arr });
    }

    private async Task<string> HandlePromptsGetAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var name = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(name))
            return SerializeError(id, McpErrorCode.InvalidParams, "Prompt name is required");
        if (!_prompts.TryGetValue(name, out var prompt))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Prompt not found: {name}");

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) result["description"] = prompt.Description;
            return SerializeSuccess(id, result);
        }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
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
                callResult = new JObject { ["content"] = contentArray, ["isError"] = jobj.Value<bool?>("isError") ?? false };
                if (jobj["structuredContent"] is JObject structured) callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject po) text = po.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s) text = s;
                else text = result == null ? "{}" : JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } }, ["isError"] = true });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } }, ["isError"] = true });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } }, ["isError"] = true });
        }
    }

    // ── Content Helpers ──────────────────────────────────────────────────

    public static JObject TextContent(string text) => new JObject { ["type"] = "text", ["text"] = text };
    public static JObject ImageContent(string base64Data, string mimeType) => new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject AudioContent(string base64Data, string mimeType) => new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject { ["type"] = "resource", ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType } };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(JToken id, JObject result) =>
        new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None);

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null) =>
        SerializeError(id, (int)code, message, data);

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}
