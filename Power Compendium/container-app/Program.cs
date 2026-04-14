using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Search.Documents;
using Azure.AI.OpenAI;
using OpenAI;
using LLMbook.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var credential = new DefaultAzureCredential();

// Blob Storage
var blobUri = new Uri(config["BookStorage:blobServiceUri"]
    ?? throw new InvalidOperationException("BookStorage:blobServiceUri not configured"));
builder.Services.AddSingleton(new BlobServiceClient(blobUri, credential));

// AI Search
var searchEndpoint = new Uri(config["BookSearch:endpoint"]
    ?? throw new InvalidOperationException("BookSearch:endpoint not configured"));
var searchIndexName = config["BookSearch:indexName"] ?? "compendium-pages";
builder.Services.AddSingleton(new SearchClient(searchEndpoint, searchIndexName, credential));

// LLM Provider — supports azure-openai, openai-compatible, foundry-local
var llmProvider = config["LLM:provider"] ?? "azure-openai";
var llmEndpoint = config["LLM:endpoint"] ?? config["AzureOpenAI:endpoint"];
var llmModel = config["LLM:deploymentName"] ?? config["AzureOpenAI:deploymentName"] ?? "gpt-4o";
var llmApiKey = config["LLM:apiKey"];
var llmJsonMode = config["LLM:supportsJsonMode"] != "false"; // default true

builder.Services.AddSingleton<BookLlmService>(sp =>
{
    return llmProvider.ToLowerInvariant() switch
    {
        "azure-openai" => new BookLlmService(
            new AzureOpenAIClient(
                new Uri(llmEndpoint ?? throw new InvalidOperationException("LLM endpoint not configured")),
                credential),
            llmModel,
            llmJsonMode),

        "openai-compatible" => new BookLlmService(
            new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(llmApiKey ?? "not-needed"),
                new OpenAIClientOptions { Endpoint = new Uri(llmEndpoint ?? throw new InvalidOperationException("LLM endpoint not configured")) }),
            llmModel,
            llmJsonMode),

        "foundry-local" => new BookLlmService(
            new OpenAIClient(
                new System.ClientModel.ApiKeyCredential("foundry-local"),
                new OpenAIClientOptions { Endpoint = new Uri(llmEndpoint ?? "http://localhost:60311/v1") }),
            llmModel,
            llmJsonMode),

        _ => throw new InvalidOperationException($"Unknown LLM provider: {llmProvider}. Use azure-openai, openai-compatible, or foundry-local.")
    };
});
builder.Services.AddSingleton<BookStorageService>();
builder.Services.AddSingleton<BookSearchService>();
builder.Services.AddSingleton<BookService>();
builder.Services.AddSingleton<McpHandler>();
builder.Services.AddSingleton(new RateLimiter
{
    MaxRequestsPerWindow = 30,
    WindowDuration = TimeSpan.FromMinutes(1)
});

builder.Services.AddControllers();

var app = builder.Build();

// Ensure infrastructure on startup — non-fatal so app starts even if RBAC is still propagating
try
{
    var blobClient = app.Services.GetRequiredService<BlobServiceClient>();
    var blobContainer = blobClient.GetBlobContainerClient("wiki-pages");
    await blobContainer.CreateIfNotExistsAsync();
    await SearchIndexSetup.EnsureIndexExistsAsync(searchEndpoint, searchIndexName);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Startup infrastructure setup failed — will retry on first request. Ensure RBAC roles are assigned.");
}

app.UseMiddleware<LLMbook.Api.Services.RateLimitingMiddleware>();
app.MapControllers();
app.Run();
