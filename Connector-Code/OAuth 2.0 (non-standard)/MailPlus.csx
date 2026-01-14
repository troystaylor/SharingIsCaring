using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        //SET TO AUTHENTICATION URL
        var authURL = "https://login.flowmailer.net/oauth/token";

        // Get the API Key and Secret from the header
        var CLIENT_ID = this.Context.Request.Headers.GetValues("clientID").FirstOrDefault();
        var CLIENT_SECRET = this.Context.Request.Headers.GetValues("clientSecret").FirstOrDefault();

        // Get access token
        Uri authtUrl = new Uri(authURL);
        HttpRequestMessage authRequest = new HttpRequestMessage(HttpMethod.Post, authtUrl);
        var authRequestBody = new Dictionary<string, string>
        {
            { "client_id", CLIENT_ID },
            { "client_secret", CLIENT_SECRET },
            { "grant_type", "client_credentials" },
            { "scope", "api" }
        };
        var authRequestBodyEncoded = new FormUrlEncodedContent(authRequestBody);
        authRequest.Content = authRequestBodyEncoded;
        authRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.flowmailer.v1.12+json"));

        HttpResponseMessage authResponse = await this.Context.SendAsync(authRequest, this.CancellationToken);

        var responseString = await authResponse.Content.ReadAsStringAsync();
            
        var jsonResponse = JObject.Parse(responseString);
        jsonResponse.TryGetValue("access_token", out JToken accessToken);
        var ACCESS_TOKEN = accessToken.ToString();

        //Set JWT token
        this.Context.Request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + ACCESS_TOKEN);
        this.Context.Request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.flowmailer.v1.12+json"));

        //Send action request
        var actionResponse = await this.Context.SendAsync(this.Context.Request, this.CancellationToken);

        return actionResponse;
    }
}