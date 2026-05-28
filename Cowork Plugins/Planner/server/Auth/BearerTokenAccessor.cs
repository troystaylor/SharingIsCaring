using Microsoft.AspNetCore.Http;

namespace PlannerCoworkMcp.Auth;

public interface IBearerTokenAccessor
{
    string Require();
}

public sealed class UnauthorizedPlannerException : Exception
{
    public UnauthorizedPlannerException(string message) : base(message)
    {
    }
}

public sealed class BearerTokenAccessor : IBearerTokenAccessor
{
    private readonly IHttpContextAccessor _contextAccessor;

    public BearerTokenAccessor(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public string Require()
    {
        var ctx = _contextAccessor.HttpContext;
        var auth = ctx?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedPlannerException("missing bearer token");
        }

        var token = auth.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedPlannerException("missing bearer token");
        }

        return token;
    }
}
