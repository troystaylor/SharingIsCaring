using Microsoft.Extensions.Configuration;

namespace Mongo.Copilot.Core.Profiles;

public static class ProfileConfigurationExtensions
{
    /// <summary>Relative location of the profiles file, from whichever root contains it.</summary>
    private const string RelativePath = "config/profiles.json";

    /// <summary>How far up the tree to look before giving up.</summary>
    private const int MaxParentLevels = 5;

    /// <summary>
    /// Adds <c>config/profiles.json</c>, searching the content root and then its parents.
    /// </summary>
    /// <remarks>
    /// The search exists because the two heads run from different roots. In a container the
    /// file sits beside the binaries at <c>/app/config</c>, but during local development
    /// <c>dotnet run --project src/Mongo.Copilot.Federated</c> sets the content root to the
    /// project directory while the profiles live at the solution root. Resolving only against
    /// the content root would make the documented first-run command fail with "no collections
    /// configured", which reads like a configuration mistake rather than a path mismatch.
    /// </remarks>
    public static IConfigurationBuilder AddMongoCopilotProfiles(
        this IConfigurationBuilder builder,
        string contentRootPath)
    {
        var found = FindProfilesFile(contentRootPath);

        // Still added when absent so that the resulting error comes from profile validation,
        // which names the missing collections, rather than from a hard file-not-found.
        return builder.AddJsonFile(
            found ?? Path.Combine(contentRootPath, RelativePath),
            optional: true,
            reloadOnChange: false);
    }

    private static string? FindProfilesFile(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        for (var level = 0; level <= MaxParentLevels && directory is not null; level++)
        {
            var candidate = Path.Combine(directory.FullName, "config", "profiles.json");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
