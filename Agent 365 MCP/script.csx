using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Routing is handled by connector policy (RouteRequestToEndpoint).
        // This stub satisfies connector packaging requirements.
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
