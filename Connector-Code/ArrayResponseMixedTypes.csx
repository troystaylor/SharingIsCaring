using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private Microsoft.Extensions.Logging.ILogger _logger;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        _logger = this.Context.Logger;

        try
        {
            string operationId = this.Context.OperationId;
            string decodedOperationId = operationId;

            try
            {
                byte[] data = Convert.FromBase64String(operationId);
                decodedOperationId = System.Text.Encoding.UTF8.GetString(data);
                _logger.LogInformation($"Decoded operationId: {decodedOperationId}");
            }
            catch (FormatException)
            {
            }

            if (decodedOperationId == "GetAllSources")
            {
                _logger.LogInformation("Processing GetAllSources operation");
                return await HandlerConvertArrayToObject();
            }

            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                "InvalidOperation",
                $"The operation '{operationId}' is not supported."
            );
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                HttpStatusCode.InternalServerError,
                "ServerError",
                $"An unexpected error occurred: {ex.Message}"
            );
        }
    }
    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string errorCode, string errorMessage)
    {
        _logger?.LogError($"Error: {statusCode} - {errorCode} - {errorMessage}");

        var response = new HttpResponseMessage(statusCode);
        var error = $"{{\"error\": {{\"code\": \"{errorCode}\", \"message\": \"{errorMessage}\"}}}}";
        response.Content = new StringContent(error, System.Text.Encoding.UTF8, "application/json");
        return response;
    }
    private async Task<HttpResponseMessage> HandlerConvertArrayToObject()
    {
        try
        {
            _logger?.LogInformation("Starting HandlerConvertArrayToObject");
            _logger?.LogInformation($"Request URI: {this.Context.Request.RequestUri}");

            Uri requestUri = this.Context.Request.RequestUri;
            _logger?.LogInformation($"Using request URL: {requestUri}");

            var requestMessage = new HttpRequestMessage(this.Context.Request.Method, requestUri);

            foreach (var header in this.Context.Request.Headers)
            {
                if (!header.Key.StartsWith("Content-") &&
                    header.Key != "Host" &&
                    header.Key != "User-Agent")
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            _logger?.LogInformation("Sending request to external API");
            HttpResponseMessage externalResponse = await this.Context.SendAsync(requestMessage, this.CancellationToken);

            if (!externalResponse.IsSuccessStatusCode)
            {
                _logger?.LogError($"External API call failed with status: {externalResponse.StatusCode}");
                return CreateErrorResponse(
                    externalResponse.StatusCode,
                    "ExternalApiError",
                    $"External API call failed with status: {externalResponse.StatusCode}"
                );
            }

            string responseContent = await externalResponse.Content.ReadAsStringAsync();
            _logger?.LogInformation($"External API response received: {responseContent}");

            try
            {
                JToken responseData = JToken.Parse(responseContent);
                _logger?.LogInformation($"Parsed response data of type: {responseData.Type}");
                JObject metadataObject = new JObject();
                JArray resultsArray = new JArray();

                if (responseData is JArray responseArray && responseArray.Count > 1)
                {
                    _logger?.LogInformation($"Found array with {responseArray.Count} items, extracting second element"); JToken firstElement = responseArray[0]; metadataObject = firstElement.ToObject<JObject>() ?? new JObject();
                    JToken secondElement = responseArray[1];

                    if (secondElement is JArray secondElementArray)
                    {
                        _logger?.LogInformation("Second element is an array, adding its items to results array");
                        foreach (var item in secondElementArray)
                        {
                            resultsArray.Add(item);
                        }
                    }
                    else
                    {
                        _logger?.LogInformation("Second element is not an array, adding it directly to results array");
                        resultsArray.Add(secondElement);
                    }
                }
                else
                {
                    _logger?.LogInformation("Response is not an array or has fewer than 2 elements, returning empty results array");
                }

                var responseObject = new JObject
                {
                    ["metadata"] = metadataObject,
                    ["results"] = resultsArray
                };

                _logger?.LogInformation($"Constructed response object with metadata and results: {responseObject}");

                return CreateJsonResponse(responseObject);
            }
            catch (Exception jsonEx)
            {
                _logger?.LogError($"Failed to parse external API response: {jsonEx.Message}");
                return CreateErrorResponse(
                    HttpStatusCode.BadGateway,
                    "ResponseParsingError",
                    $"Failed to parse external API response: {jsonEx.Message}"
                );
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error processing request: {ex.Message}");
            return CreateErrorResponse(
                HttpStatusCode.InternalServerError,
                "ProcessingError",
                $"Error processing request: {ex.Message}"
            );
        }
    }
    private HttpResponseMessage CreateJsonResponse(JToken content)
    {
        _logger?.LogInformation($"Creating JSON response with content type: {content.Type}");

        string responseContent;
        if (content is JObject jObject)
        {
            responseContent = jObject.ToString(Newtonsoft.Json.Formatting.None);
            _logger?.LogInformation("Using object as response");
        }
        else if (content is JArray array)
        {
            // Wrap in a results object for backward compatibility if directly called with array
            var wrappedObject = new JObject
            {
                ["results"] = array
            };
            responseContent = wrappedObject.ToString(Newtonsoft.Json.Formatting.None);
            _logger?.LogInformation("Wrapped array content in results object");
        }
        else
        {
            // For any other token type, create a results array with the token
            var resultsArray = new JArray { content };
            var wrappedObject = new JObject
            {
                ["results"] = resultsArray
            };
            responseContent = wrappedObject.ToString(Newtonsoft.Json.Formatting.None);
            _logger?.LogInformation("Wrapped non-array content in results array within object");
        }

        _logger?.LogInformation($"Response content: {responseContent}");

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json");
        response.Headers.Add("x-processed-by", "PowerPlatformConnector");

        return response;
    }
}
