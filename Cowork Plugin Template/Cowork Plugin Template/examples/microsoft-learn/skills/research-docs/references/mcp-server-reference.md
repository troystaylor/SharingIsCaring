# Microsoft Learn MCP Server Reference

## Endpoint

`https://learn.microsoft.com/api/mcp` (Streamable HTTP, no authentication)

## Available Tools

| Tool | Description |
|------|-------------|
| `microsoft_docs_search` | Search official documentation, returns up to 10 content chunks (max 500 tokens each) |
| `microsoft_docs_fetch` | Fetch and convert a full documentation page to markdown |
| `microsoft_code_sample_search` | Search for code snippets and examples, returns up to 20 results |

## microsoft_docs_search

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | yes | A query about Microsoft/Azure products, services, APIs |

Returns: Up to 10 results, each with title, URL, and content excerpt.

**Token budget:** Append `?maxTokenBudget=2000` to the endpoint URL to cap
response sizes. Only affects search — fetch always returns the full page.

## microsoft_docs_fetch

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | string | yes | URL of a Microsoft Learn documentation page |

Returns: Full page content converted to markdown.

**Constraints:**
- Must be a valid HTML page from microsoft.com domain
- Binary files (PDF, DOCX, images) are not supported

## microsoft_code_sample_search

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | yes | A descriptive query or SDK/method name |
| `language` | string | no | Filter by language: csharp, javascript, typescript, python, powershell, java, go, rust, ruby, php, sql, cpp |

Returns: Up to 20 code samples with context.

## Best Practices

- Don't hardcode tool names or schemas — call `tools/list` at runtime
  to get the current set. Tool availability is dynamic.
- Use `microsoft_docs_search` first for breadth, then `microsoft_docs_fetch`
  for depth on specific articles.
- Use `microsoft_code_sample_search` when the user needs implementation
  examples — the `language` parameter significantly improves results.
- The knowledge service refreshes incrementally after content updates and
  performs a full refresh once a day.

## Sources

- [Overview](https://learn.microsoft.com/en-us/training/support/mcp)
- [Developer reference](https://learn.microsoft.com/en-us/training/support/mcp-developer-reference)
- [Best practices](https://learn.microsoft.com/en-us/training/support/mcp-best-practices)
- [Release notes](https://learn.microsoft.com/en-us/training/support/mcp-release-notes)
