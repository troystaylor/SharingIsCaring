using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ArdDiscovery.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient("ard-registry");
        services.AddHttpClient("mcp-proxy");
        services.AddHttpClient("obo");
        services.AddHttpClient("embedding");
        services.AddSingleton<TokenStore>();
        services.AddSingleton<RegistryClient>();
        services.AddSingleton<OAuthConfigStore>();
        services.AddSingleton<OboTokenService>();

        // Register ISearchIndex: use AI Search if configured, else Table Storage
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AiSearchEndpoint")))
        {
            services.AddSingleton<ISearchIndex, AiSearchIndex>();
        }
        else
        {
            services.AddSingleton<ISearchIndex, TableStorageIndex>();
        }

        services.AddSingleton<CatalogIndex>();
        services.AddSingleton<TrustVerifier>();
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<CrawlStateStore>();
    })
    .Build();

host.Run();
