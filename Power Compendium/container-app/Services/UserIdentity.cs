using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace LLMbook.Api.Services;

/// <summary>
/// Extracts user identity from the request's authentication claims.
/// </summary>
public static class UserIdentity
{
    /// <summary>Get the Azure AD object ID from the authenticated user.</summary>
    public static string? GetUserId(HttpRequest request)
    {
        return request.HttpContext?.User?.FindFirst("oid")?.Value
            ?? request.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>Get the display name of the authenticated user.</summary>
    public static string? GetDisplayName(HttpRequest request)
    {
        return request.HttpContext?.User?.FindFirst("name")?.Value
            ?? request.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown";
    }
}
