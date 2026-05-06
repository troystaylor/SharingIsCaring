---
name: compare-services
description: |
  Compares Microsoft and Azure services, features, or approaches side by side.
  Use when the user asks to "compare", "difference between", "which should I use",
  "pros and cons of", "Azure Functions vs Container Apps", "should I use X or Y",
  or any question that involves choosing between Microsoft technologies.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
---

# Compare Microsoft Services

## What This Skill Does

Researches two or more Microsoft/Azure services and produces a structured
comparison to help users make informed technology decisions. Pulls from
official documentation to ensure accuracy.

## When to Activate

- User asks to compare two or more services or features
- User asks "which should I use" for a scenario
- User asks about differences, trade-offs, or pros/cons
- User is making an architecture decision between Microsoft technologies

## Workflow

1. **Identify the services to compare.** Extract the specific services
   or features the user wants compared. If vague ("what's the best
   compute option"), ask what their workload looks like to narrow it down.

2. **Research each service.** Use `microsoft_docs_search` for each
   service individually:

   > `microsoft_docs_search(query: "Azure Functions overview")`
   > `microsoft_docs_search(query: "Azure Container Apps overview")`

3. **Fetch key decision articles.** Many comparison topics have dedicated
   "choose between" articles on Microsoft Learn. Search for them:

   > `microsoft_docs_search(query: "choose between Azure Functions Container Apps")`

4. **Fetch details for high-value articles.** Use `microsoft_docs_fetch`
   on any "choose the right" or "compare" articles found in step 3.

5. **Build the comparison.** Structure the comparison around dimensions
   that matter for the user's scenario:
   - **What it is** — one-sentence definition of each
   - **Best for** — primary use cases
   - **Key differences** — pricing, scaling, language support, etc.
   - **When to choose each** — decision criteria

6. **Make a recommendation.** If the user shared their scenario,
   recommend the better fit with reasoning. If not, present the
   trade-offs and ask what matters most to them.

## Output Format

**Azure Functions vs. Azure Container Apps**

| Dimension | Azure Functions | Azure Container Apps |
|-----------|----------------|---------------------|
| **Best for** | Event-driven, short-lived tasks | Long-running services, microservices |
| **Scaling** | Per-execution, scale to zero | Per-replica, scale to zero |
| **Languages** | C#, JavaScript, Python, Java, PowerShell | Any (containerized) |
| **Pricing** | Consumption: pay per execution | Consumption: pay per vCPU-second |
| **Cold start** | Yes (Consumption plan) | Yes (scale to zero) |
| **Max execution** | 10 min (Consumption), unlimited (Premium) | Unlimited |
| **Networking** | VNet integration available | VNet integration, internal ingress |

**Recommendation for your scenario:** If you need a lightweight API that
responds to events and runs for under 10 minutes, Azure Functions is simpler
and cheaper. If you need a long-running service, multiple containers, or
Dapr integration, Container Apps gives you more flexibility.

**Sources:**
- [Azure Functions overview](https://learn.microsoft.com/azure/azure-functions/functions-overview)
- [Azure Container Apps overview](https://learn.microsoft.com/azure/container-apps/overview)
- [Choose between Azure compute services](https://learn.microsoft.com/azure/architecture/guide/technology-choices/compute-decision-tree)

## Handling Edge Cases

- **Non-Microsoft comparison:** If the user asks "Azure vs. AWS," focus
  on describing the Azure service capabilities. Don't speculate about
  competing products — point the user to official migration guides if
  they exist.
- **Too many services:** If comparing more than 3, suggest narrowing
  down first or focusing on the top 2 candidates.
- **Preview vs. GA:** Always note if a service is in preview. Preview
  services may change and aren't recommended for production.
