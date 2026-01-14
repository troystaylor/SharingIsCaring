# Set Content-Length Header to Zero

This script sets the `Content-Length` header to `0` for all requests, regardless of the actual request body content.

## Use Case

Some APIs require a `Content-Length: 0` header for certain operations (like DELETE or POST requests without a body), even when the connector might be sending content. This script ensures the header is always set to 0.

## How It Works

1. Replaces the request content with an empty string
2. Explicitly sets the `Content-Length` header to `0`
3. Forwards the modified request to the API

## Implementation

```csharp
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
```

## Notes

- This will override any existing request body content
- Useful for APIs that strictly validate the Content-Length header
- Can be modified to conditionally apply based on `this.Context.OperationId` if needed
