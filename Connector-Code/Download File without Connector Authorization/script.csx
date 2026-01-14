using System.Web;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Get the query string parameters
        var queryString = this.Context.Request.RequestUri.Query;
        var queryParams = System.Web.HttpUtility.ParseQueryString(queryString);

        // Get the value of the fileURL query string parameter
        var fileUrl = queryParams["fileURL"];

        // Set the fileURL as the full URL of this.Context
        this.Context.Request.RequestUri = new Uri(fileUrl);

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }
}