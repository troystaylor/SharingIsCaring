using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared core. Both heads call this, which is what guarantees they read
    /// the same profiles and resolve citations identically.
    /// </summary>
    public static IServiceCollection AddMongoCopilotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection("Mongo"));
        services.Configure<UiOptions>(configuration.GetSection("Ui"));

        // Singleton, and constructed eagerly by the hosts at startup so that invalid
        // profiles fail the process rather than the first request.
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<CitationUrlResolver>();
        services.AddSingleton<DocumentProjector>();
        services.AddSingleton<MongoConnectionFactory>();

        return services;
    }
}
