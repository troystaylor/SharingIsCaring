using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var envId = GetConnectionParameter("envId");
        if (string.IsNullOrWhiteSpace(envId))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Missing connection parameter: envId", Encoding.UTF8, "text/plain")
            };
        }

        // Target MCP server (Agent 365 tooling gateway)
        var serverUrl = $"https://agent365.svc.cloud.microsoft/mcp/environments/{envId}/servers/MCPManagement";

        // Forward MCP JSON-RPC body as-is
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var forwardRequest = new HttpRequestMessage(HttpMethod.Post, serverUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // Authorization handled by connector OAuth; Context.SendAsync propagates headers
        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);
        var respContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(respContent, Encoding.UTF8, "application/json")
        };
    }

    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }
}
