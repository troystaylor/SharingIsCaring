using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Web;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        //Set URLs
        var authURL = "https://airtable.com/oauth2/v1/authorize";
        var refreshURL = "https://airtable.com/oauth2/v1/token";
        var redirectURL = "https://make.powerautomate.com/";

        HttpResponseMessage response;

        if (this.Context.OperationId == "AuthorizationGet") 
        {
            var CLIENT_ID = this.Context.Request.Headers.GetValues("clientID").FirstOrDefault();
            var CLIENT_SECRET = this.Context.Request.Headers.GetValues("secret").FirstOrDefault();
            var SCOPE = this.Context.Request.Headers.GetValues("scope").FirstOrDefault();

            var STATE = GenerateState(16);
            var CODE_VERIFIER = GenerateState(43);
            var CODE_CHALLENGE = ComputeSha256Base64Url(CODE_VERIFIER);

            var authRequestParams = 
                "client_id=" + Uri.EscapeDataString(CLIENT_ID) +
                "&redirect_uri=" + Uri.EscapeDataString(redirectURL) +
                "&response_type=" + Uri.EscapeDataString("code") +
                "&scope=" + Uri.EscapeDataString(SCOPE) +
                "&state=" + Uri.EscapeDataString(STATE) +
                "&code_challenge=" + Uri.EscapeDataString(CODE_CHALLENGE) +
                "&code_challenge_method=" + Uri.EscapeDataString("S256");

            var oauthObject = new JObject
            {
                ["url"] = $"{authURL}?{authRequestParams}",
                ["state"] = STATE,
                ["code_challenge"] = CODE_CHALLENGE,
                ["code_verifier"] = CODE_VERIFIER
            };

            response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(oauthObject.ToString(), Encoding.UTF8, "application/json")
            };

            return response;
        }

        if (this.Context.OperationId == "TokenPost" || this.Context.OperationId == "RefreshPost") 
        {
            var requestBody = await this.Context.Request.Content.ReadAsStringAsync();
            var jsonBody = JObject.Parse(requestBody);
            var CLIENT_ID = jsonBody["client_id"].ToString();
            var CLIENT_SECRET = jsonBody["client_secret"].ToString();

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CLIENT_ID}:{CLIENT_SECRET}"));
            this.Context.Request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            
            switch (this.Context.OperationId)
            {
                case "TokenPost":
                    jsonBody["redirect_uri"] = redirectURL;
                    jsonBody["grant_type"] = "authorization_code";
                    jsonBody.Remove("client_secret");
                    break;

                case "RefreshPost":
                    jsonBody["grant_type"] = "refresh_token";
                    jsonBody.Remove("client_secret");
                    this.Context.Request.RequestUri = new Uri(refreshURL);
                    break;

                default:
                    break;
            }

            var formUrlEncodedBody = string.Join("&", jsonBody.Properties().Select(j => $"{Uri.EscapeDataString(j.Name)}={Uri.EscapeDataString(j.Value.ToString())}"));
            this.Context.Request.Content = new StringContent(formUrlEncodedBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            this.Context.Request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            return response;
        }

        var authorizationHeader = "Bearer " + this.Context.Request.Headers.GetValues("Token").FirstOrDefault();
        this.Context.Request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorizationHeader);

        response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }

    private string GenerateState(int length)
    {
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_";
        using (var rng = new RNGCryptoServiceProvider())
        {
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            var chars = bytes.Select(b => validChars[b % validChars.Length]).ToArray();
            return new string(chars);
        }
    }

    private string ComputeSha256Base64Url(string input)
    {
        using (var sha256 = SHA256.Create())
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(inputBytes);

            var base64Url = Convert.ToBase64String(hashBytes)
                .TrimEnd('=') // Remove any trailing '='
                .Replace('+', '-') // Replace '+' with '-'
                .Replace('/', '_'); // Replace '/' with '_'

            return base64Url;
        }
    }
}