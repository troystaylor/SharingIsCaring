---
name: learning-plan
description: |
  Creates structured learning plans from Microsoft Learn documentation.
  Use when the user asks to "create a learning plan", "help me learn",
  "what should I study for", "build a study guide", "prepare for certification",
  "get up to speed on", "onboard me to", or wants to learn a Microsoft technology
  from scratch or deepen their expertise.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Learning
---

# Create Learning Plans

## What This Skill Does

Researches Microsoft Learn documentation for a given topic and produces a
structured, sequenced learning plan. Organizes articles, tutorials, and code
samples into a logical progression from fundamentals to advanced topics.

## When to Activate

- User wants to learn a new Microsoft technology
- User is preparing for a Microsoft certification
- User asks to "get up to speed" or "onboard" to a topic
- User wants a study guide or reading list
- User asks "where do I start" with a technology

## Workflow

1. **Clarify the learning goal.** Determine:
   - **Topic:** What technology or skill? (e.g., "Azure networking",
     "Power Platform connectors", "Copilot extensibility")
   - **Level:** Beginner, intermediate, or preparing for a certification?
   - **Time:** How much time do they have? (affects depth and scope)
   - **Focus:** Conceptual understanding, hands-on skills, or both?

2. **Research foundational content.** Use `microsoft_docs_search` to
   find overview and getting-started articles:

   > `microsoft_docs_search(query: "Azure networking fundamentals overview")`
   > `microsoft_docs_search(query: "Azure networking getting started tutorial")`

3. **Research intermediate/advanced content.** Search for deeper topics:

   > `microsoft_docs_search(query: "Azure virtual network peering configuration")`
   > `microsoft_docs_search(query: "Azure network security groups best practices")`

4. **Find hands-on examples.** Use `microsoft_code_sample_search` for
   practical implementation examples:

   > `microsoft_code_sample_search(query: "Azure virtual network Bicep template")`

5. **Sequence the content.** Organize into a logical learning path:
   - **Phase 1: Foundations** — Concepts, overviews, "what is" articles
   - **Phase 2: Core skills** — Tutorials, how-to guides, configurations
   - **Phase 3: Advanced** — Best practices, architecture patterns, troubleshooting
   - **Phase 4: Practice** — Code samples, hands-on labs, real-world scenarios

6. **Create the plan document.** Present as a structured plan. Offer to
   save it as a document the user can reference later.

## Output Format

**Learning Plan: Azure Networking**

*Estimated time: 8-10 hours | Level: Beginner to Intermediate*

### Phase 1: Foundations (2 hours)

| # | Topic | Resource | Time |
|---|-------|----------|------|
| 1 | What is Azure Virtual Network | [Overview](https://learn.microsoft.com/azure/virtual-network/virtual-networks-overview) | 20 min |
| 2 | IP addressing concepts | [Plan IP addressing](https://learn.microsoft.com/azure/virtual-network/ip-services/ip-addressing) | 20 min |
| 3 | Network security fundamentals | [NSG overview](https://learn.microsoft.com/azure/virtual-network/network-security-groups-overview) | 30 min |
| 4 | DNS in Azure | [Azure DNS overview](https://learn.microsoft.com/azure/dns/dns-overview) | 20 min |

### Phase 2: Core Skills (3 hours)

| # | Topic | Resource | Time |
|---|-------|----------|------|
| 5 | Create a virtual network | [Tutorial](https://learn.microsoft.com/azure/virtual-network/quick-create-portal) | 30 min |
| 6 | Configure network peering | [Tutorial](https://learn.microsoft.com/azure/virtual-network/tutorial-connect-virtual-networks-portal) | 30 min |
| 7 | Set up NSG rules | [Tutorial](https://learn.microsoft.com/azure/virtual-network/tutorial-filter-network-traffic) | 30 min |
| 8 | Private endpoints | [Tutorial](https://learn.microsoft.com/azure/private-link/create-private-endpoint-portal) | 45 min |

### Phase 3: Advanced (3 hours)

| # | Topic | Resource | Time |
|---|-------|----------|------|
| 9 | Hub-spoke network topology | [Architecture guide](https://learn.microsoft.com/azure/architecture/networking/architecture/hub-spoke) | 45 min |
| 10 | Network security best practices | [Best practices](https://learn.microsoft.com/azure/security/fundamentals/network-best-practices) | 30 min |
| 11 | Azure Firewall | [Overview + tutorial](https://learn.microsoft.com/azure/firewall/overview) | 45 min |
| 12 | Troubleshooting connectivity | [Network Watcher](https://learn.microsoft.com/azure/network-watcher/network-watcher-monitoring-overview) | 30 min |

**Next steps:**
- Want me to save this as a document?
- Should I find certification-specific content for AZ-700?
- I can expand any phase with more detail.

## Handling Edge Cases

- **Very broad topic:** If the user says "teach me Azure," ask what area
  they're most interested in. Azure is too broad for a single plan.
- **Certification prep:** If they mention a specific exam (AZ-104,
  AZ-700), search for the exam study guide on Learn and structure the
  plan around the exam objectives.
- **Time-constrained:** If they have "an hour," focus on Phase 1 only
  and recommend the single best overview article.
- **Already experienced:** If they say they know the basics, skip
  Phase 1 and start with Phase 2 or 3. Ask what they already know.
