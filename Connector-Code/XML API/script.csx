public class script: ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Check which operation ID was used
        if (this.Context.OperationId == "PurchasePost") 
        {
            return await this.ConvertAndTransformOperation().ConfigureAwait(false);
        }

        // Handle an invalid operation ID
        HttpResponseMessage response = new HttpResponseMessage(
            HttpStatusCode.BadRequest
        );
        response.Content = CreateJsonContent(
            $"Unknown operation ID '{this.Context.OperationId}'"
        );
        return response;
    }

    private async Task<HttpResponseMessage> ConvertAndTransformOperation()
    {
        // Manipulate the request data before sending it
        var requestContentAsString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestContentAsXML = JsonConvert.DeserializeXmlNode(requestContentAsString).OuterXml;
        this.Context.Request.Content = new StringContent(requestContentAsXML, Encoding.UTF8, "application/xml");

        // Manipulate the response data before returning it
        HttpResponseMessage response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(responseString);
        string jsonText = JsonConvert.SerializeXmlNode(doc);
        var result = JObject.Parse(jsonText);
        response.Content = CreateJsonContent(result["response"]["receipt"].ToString());
        
        return response;
    }
}