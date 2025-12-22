public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        //SET TO AUTHENTICATION URL
        var authURL = "https://api.precisely.com/oauth/token";

        // Get the API Key and Secret from the header
        var CLIENT_ID = this.Context.Request.Headers.GetValues("API_Key").FirstOrDefault();
        var CLIENT_SECRET = this.Context.Request.Headers.GetValues("Secret").FirstOrDefault();

        // Get access token
        Uri authtUrl = new Uri(authURL);
        HttpRequestMessage authRequest = new HttpRequestMessage(HttpMethod.Post, authtUrl);
        var authRequestBody = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" }
        };
        var authRequestBodyEncoded = new FormUrlEncodedContent(authRequestBody);
        authRequest.Content = authRequestBodyEncoded;

        // Set CLIENT_ID and CLIENT_SECRET as an Authorization Header
        var clientIdAndSecret = $"{CLIENT_ID}:{CLIENT_SECRET}";
        var clientIdAndSecretBase64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(clientIdAndSecret));
        authRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", clientIdAndSecretBase64);

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