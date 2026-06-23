using System;
using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArdDiscovery.Services;

/// <summary>
/// Validates the x-api-key header against the configured backend API key.
/// Returns the userId extracted from the request (x-ms-client-principal-id header
/// in App Service auth, or falls back to a hash of the API key for local dev).
/// </summary>
public static class AuthHelper
{
    public static bool ValidateApiKey(HttpRequestData req)
    {
        var expectedKey = Environment.GetEnvironmentVariable("BackendApiKey");
        if (string.IsNullOrEmpty(expectedKey)) return true; // No key configured = open

        if (!req.Headers.TryGetValues("x-api-key", out var values))
            return false;

        foreach (var key in values)
        {
            if (string.Equals(key, expectedKey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extract user identity from the request. In production with App Service auth,
    /// this comes from x-ms-client-principal-id. For local dev, derive from API key.
    /// </summary>
    public static string GetUserId(HttpRequestData req)
    {
        // App Service / Entra ID authentication header
        if (req.Headers.TryGetValues("x-ms-client-principal-id", out var principalValues))
        {
            foreach (var val in principalValues)
            {
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }

        // Fallback: use a stable hash of the API key as user identity (local dev only)
        if (req.Headers.TryGetValues("x-api-key", out var keyValues))
        {
            foreach (var key in keyValues)
            {
                if (!string.IsNullOrEmpty(key))
                    return $"apikey-{key.GetHashCode():x8}";
            }
        }

        return "anonymous";
    }

    public static HttpResponseData Unauthorized(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Unauthorized);
        response.WriteString("{\"error\":\"Invalid or missing API key\"}");
        response.Headers.Add("Content-Type", "application/json");
        return response;
    }
}
