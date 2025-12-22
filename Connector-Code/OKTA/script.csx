public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // SET TO YOUR OKTA DOMAIN - Format: https://{yourOktaDomain}/oauth2/v1/token
        // Example: https://dev-12345.okta.com/oauth2/v1/token
        var authURL = "https://[YOUR-OKTA-DOMAIN]/oauth2/v1/token";
        
        // SET TO YOUR REQUIRED OKTA SCOPES (space-separated if multiple)
        // Available scopes: okta.users.read, okta.users.manage, okta.apps.read, okta.apps.manage, etc.
        // See: https://developer.okta.com/docs/api/oauth2/
        var requiredScope = "okta.users.read";

        // Get the Client ID and Client Secret from the Basic Authentication header
        var authorizationHeader = this.Context.Request.Headers.Authorization.ToString();
        var credentials = authorizationHeader.Split(' ')[1];
        var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
        var CLIENT_ID = decodedCredentials.Split(':')[0];
        var CLIENT_SECRET = decodedCredentials.Split(':')[1];

        // Get access token from Okta
        Uri authtUrl = new Uri(authURL);
        HttpRequestMessage authRequest = new HttpRequestMessage(HttpMethod.Post, authtUrl);
        var authRequestBody = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", CLIENT_ID },
            { "client_secret", CLIENT_SECRET },
            { "scope", requiredScope }  // Required for Okta OAuth
        };
        var authRequestBodyEncoded = new FormUrlEncodedContent(authRequestBody);
        authRequest.Content = authRequestBodyEncoded;
        HttpResponseMessage authResponse = await this.Context.SendAsync(authRequest, this.CancellationToken);

        // Parse the JSON response
        var responseString = await authResponse.Content.ReadAsStringAsync();
        var jsonResponse = JObject.Parse(responseString);
        jsonResponse.TryGetValue("access_token", out JToken accessToken);
        var ACCESS_TOKEN = accessToken.ToString();

        // Set Bearer token for Okta API request
        this.Context.Request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + ACCESS_TOKEN);

        // Send action request to Okta API
        var actionResponse = await this.Context.SendAsync(this.Context.Request, this.CancellationToken);

        return actionResponse;
    }
}