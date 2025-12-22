using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Check if the operation ID matches what is specified in the OpenAPI definition of the connector
        if (this.Context.OperationId == "GetDataWithLargeInteger")
        {
            return await this.HandleLargeIntegerTransformation().ConfigureAwait(false);
        }

        // Handle other operations or forward as-is
        return await this.HandleDefaultOperation().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleLargeIntegerTransformation()
    {
        try
        {
            // Forward the request to the backend API
            HttpResponseMessage response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

            // Only transform successful responses
            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var jsonResponse = JObject.Parse(responseString);
                TransformIdFieldsToString(jsonResponse);
                
                response.Content = CreateJsonContent(jsonResponse.ToString());
            }

            return response;
        }
        catch (Exception ex)
        {
            // Log the error if logging becomes available
            this.Context.Logger?.LogError($"Error transforming large integer: {ex.Message}");
            
            // Return error response
            var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            errorResponse.Content = CreateJsonContent(new JObject
            {
                ["error"] = "Failed to transform large integer response",
                ["details"] = ex.Message
            }.ToString());
            
            return errorResponse;
        }
    }

    private async Task<HttpResponseMessage> HandleDefaultOperation()
    {
        // For other operations, just forward the request as-is
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    private void TransformIdFieldsToString(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var obj = (JObject)token;
                
                // Transform "id" field if it exists and is not already a string
                var idField = obj["id"];
                if (idField != null && idField.Type != JTokenType.String)
                {
                    obj["id"] = new JValue(idField.ToString());
                }
                
                foreach (var property in obj.Properties())
                {
                    TransformIdFieldsToString(property.Value);
                }
                break;
                
            case JTokenType.Array:
                var array = (JArray)token;
                foreach (var item in array)
                {
                    TransformIdFieldsToString(item);
                }
                break;
        }
    }
}
