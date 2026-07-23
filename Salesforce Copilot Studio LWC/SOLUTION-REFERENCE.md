# Copilot Studio + Salesforce — Solution Reference

## Executive Summary

This document defines a single AI agent built on Microsoft Copilot Studio and integrated with Salesforce Service Cloud. The agent is surfaced across multiple channels — customer portal, documentation site, and Salesforce Service Console — with the same federated knowledge search and autonomous actions available everywhere. Channel-specific behavior (e.g., case productivity tools for support engineers) is layered on top of the shared foundation.

---

## 1  Federated Knowledge Search

The agent answers questions by searching across two authoritative sources and returning a unified, cited response. Both sources are available on **all channels**.

| Source | Location | Connector | Notes |
|---|---|---|---|
| Knowledge Base articles | Salesforce Knowledge (SFDC) | Salesforce MCP connector | Respect article visibility per user context |
| Product documentation | https://docs.example.com/ | Public website grounding | Crawl/index site; honor robots.txt |

**Behavior:**

- Queries are run against both sources in parallel.
- Results are ranked by relevance and recency, then synthesized into a conversational answer.
- Each answer includes inline citations linking back to the source article or doc page.
- Salesforce Knowledge links use the `sf://` scheme (navigates in Lightning, harmless label on other channels).
- Product doc links use standard `https://` URLs (open in a new tab on all channels).
- If no relevant content is found, the agent transparently tells the user and offers to open a case.

---

## 2  Surface Points

| Channel | Implementation | Authentication | Audience |
|---|---|---|---|
| **Salesforce Service Console** | LWC side panel via M365 Agents SDK Direct Connect | Delegated auth (Entra ID popup → `CopilotStudio.Copilots.Invoke`) | Support engineers |
| **Customer portal** | Copilot Studio web chat embedded in portal | SSO — portal identity via Entra ID / SAML bridge | End customers |
| **Documentation site** | Copilot Studio widget on https://docs.example.com/ | Anonymous or lightweight auth | Anyone |

---

## 3  Intelligent Case Triage

The agent guides users through a structured triage flow before creating a case, maximizing deflection for lower-severity issues. Available on all channels.

```
User describes issue
        │
        ▼
  ┌───────────────┐
  │ Classify       │
  │ severity       │
  └───────┬───────┘
          │
     ┌────┴────┐
     │         │
   Sev 1    Sev 2–4
     │         │
     ▼         ▼
  Immediate   Offer self-service
  case        solutions from KB /
  creation    docs before case
     │         │
     │    ┌────┴────┐
     │    │         │
     │  Resolved  Unresolved
     │    │         │
     │   End      Create case
     │              │
     └──────┬───────┘
            ▼
      Case created
      in Salesforce
```

**Sev 1 (Critical / Production Down):**

- Skip self-service; immediately route to case creation.
- Collect required fields (contact, environment, description, impact) conversationally or via adaptive card form.
- Create the case in Salesforce via Apex action or Salesforce connector.
- Return case number and expected response SLA to the user.

**Sev 2–4 (High / Medium / Low):**

- Present relevant KB articles and doc links before offering case creation.
- If the user confirms the issue is resolved, close the interaction (no case created — tracked as a deflection).
- If unresolved after self-service attempt, proceed to case creation with the conversation context pre-populated.

---

## 4  Autonomous Actions

These actions allow the agent to perform operations on behalf of the user. Availability depends on the user's role and permissions, not the channel.

| Action | Description | Backend | Auth Requirement |
|---|---|---|---|
| **Create case** | Collect severity, description, category; create Case object in SFDC | Salesforce API (Apex / REST) | Verified identity |
| **Case summary** | Generate a concise summary of the full case timeline | Salesforce Case API — read case + feed items | Verified identity |
| **Closure summary** | Draft a closure summary suitable for the customer and internal records | Salesforce Case API — read + write | Verified identity |
| **Customer response email** | Suggest a professional reply email based on case context and KB matches | Salesforce EmailMessage API | Verified identity |
| **KB article suggestion** | Recommend existing KBs for the current case; draft a new KB if none exist | Salesforce Knowledge API | Verified identity |
| **Case categorization** | Analyze case content and suggest category, subcategory, product area, root cause | Salesforce Case API — field update | Verified identity |
| **Revoke / activate / deactivate license** | Manage license assignments for a customer's org | Product licensing API | Verified identity + entitlement check |
| **Create portal user** | Provision a new user in the customer portal | Portal user management API | Admin role verification |
| **Reset password** | Trigger a password reset email for a portal user | Identity provider API | Verified identity + MFA step-up |
| **Surface relevant KBs** | Return direct links and summaries for matching Knowledge articles | Salesforce Knowledge API | None (portal-published articles) |

> **Security note:** All mutating actions require verified identity. Destructive or sensitive actions (license revocation, password reset) include a confirmation step and, where applicable, MFA step-up.

---

## 5  Reporting & Trend Analysis

| Capability | Description | Data Sources |
|---|---|---|
| **Trend detection** | Identify spikes in case volume by category, product area, or error signature | Salesforce reports / SOQL aggregate queries |
| **Common issue clustering** | Group recent cases by similarity to surface emerging patterns | Case data + NLP clustering |
| **Manager dashboard queries** | Answer natural-language questions like "Top 5 case drivers last month?" | Salesforce reports / SOQL |
| **SLA compliance** | Report on cases approaching or breaching SLA thresholds | Salesforce Entitlement / Milestone objects |
| **Deflection metrics** | Track how often the agent resolved issues without creating a case | Custom analytics object or Platform Events |

---

## 6  Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        COPILOT STUDIO                           │
│                                                                 │
│              ┌──────────────────────────┐                       │
│              │  Unified AI Agent         │                       │
│              └────────────┬─────────────┘                       │
│                           │                                     │
│  ┌────────────────────────┴───────────────────────────┐         │
│  │              Knowledge Sources                      │         │
│  │  ┌──────────────┐    ┌──────────────────────────┐  │         │
│  │  │ SF Knowledge  │    │ docs.example.com         │  │         │
│  │  └──────────────┘    └──────────────────────────┘  │         │
│  └────────────────────────────────────────────────────┘         │
│                                                                 │
│  ┌────────────────────────────────────────────────────┐         │
│  │              Action Plugins                         │         │
│  │  Case CRUD · Licensing · User Mgmt · Email         │         │
│  │  KB Authoring · Categorization · Triage            │         │
│  └────────────────────────────────────────────────────┘         │
└──────────────────────────┬──────────────────────────────────────┘
                           │  Direct Connect (M365 Agents SDK)
                           │
                ┌──────────┴──────────┐
                │  Azure Functions     │
                │  Middleware           │
                └──────────┬──────────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
┌─────────┴──┐   ┌────────┴───┐   ┌────────┴────────┐
│ Customer    │   │ Docs Site   │   │ SF Service      │
│ Portal      │   │ (widget)    │   │ Console (LWC)   │
└─────────────┘   └────────────┘   └─────────────────┘
```

### 6.1  Integration Pattern

This project implements the **Direct Connect** pattern via Azure Functions middleware:

- **Salesforce → Azure Functions** — Apex callouts from the LWC to the middleware REST API.
- **Azure Functions → Copilot Studio** — M365 Agents SDK SSE connection to the Copilot Studio Direct Connect endpoint.
- **Copilot Studio → Data Sources** — MCP connector and plugins configured within Copilot Studio reach Salesforce and the docs site.

### 6.2  Security Model

| Layer | Mechanism |
|---|---|
| Customer portal → Middleware | Function-level auth key + portal session token |
| Docs site → Middleware | Function-level auth key (anonymous users) or OAuth |
| SF Console → Middleware | Function-level auth key + Named Credential (Apex) |
| Middleware → Copilot Studio | Entra ID delegated auth with `CopilotStudio.Copilots.Invoke` scope |
| Copilot Studio → Salesforce | Salesforce MCP connector (OAuth 2.0) |
| Sensitive actions | Confirmation prompts + MFA step-up where required |

---

## 7  Implementation Phases

### Phase 1 — Foundation (Complete)

- [x] Azure Functions middleware with Direct Connect
- [x] LWC chat component for Salesforce Service Console
- [x] Apex controller for middleware communication
- [x] Delegated authentication (OAuth2 code flow via MSAL)
- [x] End-to-end conversation flow (start → message → end)
- [x] Markdown-to-HTML response formatting
- [x] Session persistence across page refreshes
- [x] Channel-aware record links (`sf://` scheme)

### Phase 2 — Knowledge & Triage (Complete)

- [x] Configure Salesforce MCP connector (29 tools)
- [x] Add the product documentation site as a public website knowledge source
- [x] Build triage via Instructions with severity classification logic
- [x] Implement case creation action (create_case MCP tool + Swagger endpoints)
- [x] Deflection tracking and metrics (Copilot_Interaction__c + log_interaction MCP tool)

### Phase 3 — Autonomous Actions (Complete)

- [x] Build case summary generation (get_case_timeline + agent instructions)
- [x] Build closure summary generation (get_case_timeline + agent instructions)
- [x] Build customer response email suggestion (send_case_email MCP tool)
- [x] Build KB article suggestion / drafting (suggest_kb_for_case + draft_kb_article MCP tools)
- [x] Build case categorization action (categorize_case MCP tool)
- [x] Licensing actions — handled via agent instructions (case creation workflow, no direct API)
- [x] Portal user creation and password reset — handled via agent instructions (case creation workflow, no direct API)

### Phase 4 — Additional Channels (Deferred)

- [ ] Embed Copilot Studio widget on customer portal
- [ ] Embed Copilot Studio widget on documentation site

### Phase 5 — Reporting & Analytics (Complete)

- [x] Trend detection queries (get_case_trends MCP tool + agent instructions)
- [x] Manager dashboard natural-language queries (agent instructions — maps questions to reporting tools)
- [x] SLA compliance reporting (get_sla_compliance MCP tool + agent instructions)
- [x] Deflection metrics dashboard (get_deflection_metrics MCP tool + agent instructions)
- [ ] Common issue clustering (requires NLP/ML pipeline — future enhancement)

---

## 8  Key Considerations

### Data Governance

- **KB visibility**: Article visibility is controlled by Salesforce per user context. Portal users see only published articles; internal users see all.
- **Case data**: Access governed by Salesforce sharing rules and user profiles.
- **PII handling**: Conversation logs in Copilot Studio and Azure Functions should follow data retention policies. Avoid persisting PII in middleware logs.

### Scalability

- The Azure Functions middleware uses an in-memory session store. For production multi-instance deployments, replace with Azure Redis Cache or Cosmos DB.
- Copilot Studio handles scaling of the AI orchestration layer natively.

### Licensing

- Microsoft Copilot Studio license required for the agent.
- Salesforce API call limits should be evaluated based on expected conversation volume.

### Success Metrics

| Metric | Target |
|---|---|
| Case deflection rate | ≥ 30% of interactions resolved without case |
| Self-service satisfaction (CSAT) | ≥ 4.0 / 5.0 |
| Mean time to resolution (MTTR) | Reduce by ≥ 20% |
| First-contact resolution rate | Increase by ≥ 15% |
| Engineer search time | Reduce by ≥ 50% |
| KB article coverage | Increase new KB creation by ≥ 25% |
