---
name: web-research
description: |
  Conduct multi-site web research by visiting multiple URLs, extracting content,
  and synthesizing findings into a structured report with citations. Use when the
  user asks to research a topic, compare options, or gather intelligence from the web.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: report-and-summarize
---

# Web Research

## When to Activate

- "Research [topic]", "compare [A] vs [B]", "what are the pricing plans for [service]"
- "Find documentation about [API]", "competitive intelligence on [company]"
- Any request requiring information from multiple websites

## Workflow

1. **Create session**: `create_browser_session()`
2. **Plan** 2-5 target URLs based on the user's question
3. **For each site**:
   a. `navigate(session_id, url, wait_until: "networkidle")`
   b. `extract(session_id, "main", extract: "textContent")` (or "article", "body")
4. **Process** if needed: `execute_code(code: "analysis script", language: "python")`
5. **Synthesize** into a report with source citations
6. **Save to memory** if valuable: `save_memory(key: "research/[topic]", value: summary)`
7. **Destroy session**: `destroy_session(session_id)`

## Output Format

### Research Report: [Topic]

**Summary:** 2-3 sentence overview

| Source | Key Finding | URL |
|--------|-------------|-----|
| ... | ... | ... |

**Detailed Findings:** Per-source breakdown

## Missing Tools

- To email the research summary: "Please enable Work IQ Mail in Cowork Settings > Tools."
- To save to OneDrive: "Please enable Work IQ OneDrive in Cowork Settings > Tools."
