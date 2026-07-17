---
name: internet-search
description: |
  Search the internet for real-time information using browser automation.
  Use when the user asks to "search for", "look up", "find information about",
  "what is the latest on", or any request requiring current web data.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Internet Search

## When to Activate

- "Search for [topic]", "look up [thing]", "find info about [subject]"
- "What's the latest on [topic]", "current pricing for [service]"
- Any question that requires up-to-date information not in training data

## Workflow

1. **Create session**: `create_browser_session()`
2. **Search**: `navigate(session_id, "https://www.bing.com/search?q=<encoded_query>", wait_until: "domcontentloaded")`
3. **Extract results**: `extract(session_id, "#b_results .b_algo", extract: "textContent", all: true)`
4. **Follow links if needed**: `navigate(session_id, url)` to specific results for deeper content
5. **Synthesize**: Summarize findings with source URLs
6. **Destroy session**: `destroy_session(session_id)`

## Tips

- URL-encode the query: replace spaces with `+` or `%20`
- Extract from `#b_results .b_algo` for Bing organic results
- For Google: use `div.g` selector instead
- Limit to top 5-10 results to avoid context overflow
- Always cite source URLs in the response

## Output Format

### Search Results: [Query]

| # | Title | Source |
|---|-------|--------|
| 1 | ... | [url] |
| 2 | ... | [url] |

**Summary:** Key findings synthesized from the results.
