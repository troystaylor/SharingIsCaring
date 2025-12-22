public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Set Content-Length header to 0 regardless of request body
        this.Context.Request.Content = new StringContent(string.Empty);
        this.Context.Request.Content.Headers.ContentLength = 0;

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }
}
