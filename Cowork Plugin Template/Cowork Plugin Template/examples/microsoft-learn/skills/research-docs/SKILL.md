---
name: research-docs
description: |
  Searches and retrieves Microsoft documentation. Use when the user asks to
  "find docs about", "how do I", "what is", "look up", "search Microsoft Learn",
  "find the article about", "get the latest docs on", or asks any question
  about Microsoft or Azure products, services, or APIs.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Research Microsoft Docs

## What This Skill Does

Searches Microsoft Learn documentation and retrieves relevant articles,
tutorials, and API references. Translates broad questions into targeted
searches and presents findings in a clear, actionable format.

## When to Activate

- User asks how to do something with a Microsoft product
- User wants to find documentation on a specific topic
- User asks "what is" a Microsoft service or feature
- User needs API reference, SDK docs, or configuration guidance
- User says "look up" or "search for" something on Microsoft Learn

## Workflow

1. **Clarify the search intent.** Determine what the user is looking for:
   - Product or service name (e.g., Azure Functions, Power Platform)
   - Task or goal (e.g., "deploy a container app", "configure SSO")
   - Concept (e.g., "what is managed identity", "how does RBAC work")

2. **Search for documentation.** Use the `microsoft_docs_search` tool
   with a clear, focused query. If the user's question is broad,
   break it into specific searches.

   > Example: `microsoft_docs_search(query: "Azure Container Apps managed identity configuration")`

3. **Evaluate the results.** Review the returned articles for relevance.
   If the top results don't match the user's intent, refine the query
   with more specific terms.

4. **Fetch full content when needed.** If a search result looks highly
   relevant but the excerpt is insufficient, use `microsoft_docs_fetch`
   to retrieve the complete article.

   > Example: `microsoft_docs_fetch(url: "https://learn.microsoft.com/azure/container-apps/managed-identities")`

5. **Find code samples when relevant.** If the user needs implementation
   examples, use `microsoft_code_sample_search` with an optional
   language filter.

   > Example: `microsoft_code_sample_search(query: "Azure Container Apps managed identity", language: "csharp")`

6. **Present findings clearly.** Summarize the key information and link
   to the source articles. Don't just dump raw search results — extract
   the answer to the user's question.

7. **Offer next steps.** Based on what was found:
   - "Want me to fetch the full article on [topic]?"
   - "Should I find code samples for this in [language]?"
   - "I can compare this with [alternative service] if that would help."

## Output Format

### For factual answers

**How to configure managed identity for Azure Container Apps**

Azure Container Apps supports both system-assigned and user-assigned managed
identities. To enable a system-assigned identity:

1. Navigate to your container app in the Azure portal
2. Select **Identity** under Settings
3. Set **System assigned** to **On**
4. Save

The identity can then access Azure resources without credentials by using
`DefaultAzureCredential` in your code.

**Source:** [Managed identities in Azure Container Apps](https://learn.microsoft.com/azure/container-apps/managed-identities)

### For multiple results

| Article | Relevance | Key Content |
|---------|-----------|-------------|
| [Managed identities overview](https://learn.microsoft.com/...) | High | Concepts, system vs. user-assigned |
| [Tutorial: Connect to Azure services](https://learn.microsoft.com/...) | High | Step-by-step with code samples |
| [Security baseline](https://learn.microsoft.com/...) | Medium | Security recommendations |

## Handling Edge Cases

- **No results:** Tell the user the topic may not be documented on
  Microsoft Learn. Suggest rephrasing or checking if the feature is
  in preview.
- **Outdated content:** Microsoft Learn refreshes daily. If the user
  says docs seem outdated, note this and suggest checking the article's
  "last updated" date.
- **Non-Microsoft topics:** This skill covers Microsoft Learn only. If
  the user asks about non-Microsoft technologies, say so and focus on
  any Microsoft integration points that are documented.

## Additional Resources

- **`references/mcp-server-reference.md`** — Tool definitions, parameter schemas, token budget control, and best practices for the Microsoft Learn MCP Server
