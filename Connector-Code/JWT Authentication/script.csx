public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        //SET TO AUTHENTICATION URL
        var authURL = "[SET TO URL]";

        // Get the Client ID and Client Secret from the Basic Authentication header
        var authorizationHeader = this.Context.Request.Headers.Authorization.ToString();
        var credentials = authorizationHeader.Split(' ')[1];
        var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
        var CLIENT_ID = decodedCredentials.Split(':')[0];
        var CLIENT_SECRET = decodedCredentials.Split(':')[1];

        // Get JWT token
        Uri authtUrl = new Uri(authURL);
        HttpRequestMessage authRequest = new HttpRequestMessage(HttpMethod.Post, authtUrl);
        var authRequestBody = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", CLIENT_ID },
            { "client_secret", CLIENT_SECRET }
        };
        var authRequestBodyEncoded = new FormUrlEncodedContent(authRequestBody);
        authRequest.Content = authRequestBodyEncoded;
        HttpResponseMessage authResponse = await this.Context.SendAsync(authRequest, this.CancellationToken);

        // Parse the JSON response
        var responseString = await authResponse.Content.ReadAsStringAsync();
        var jsonResponse = JObject.Parse(responseString);
        jsonResponse.TryGetValue("access_token", out JToken accessToken);
        var ACCESS_TOKEN = accessToken.ToString();

        //Set JWT token
        this.Context.Request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + ACCESS_TOKEN);

        //Send action request
        var actionResponse = await this.Context.SendAsync(this.Context.Request, this.CancellationToken);

        return actionResponse;
    }
}