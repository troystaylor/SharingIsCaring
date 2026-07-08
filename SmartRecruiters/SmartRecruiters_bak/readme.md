# SmartRecruiters connector

Dual-mode Power Platform custom connector for [SmartRecruiters](https://www.smartrecruiters.com/) covering the Customer API, Application API, public Posting API, Reporting API, Webhooks, Approvals, Reviews, Interviews, Self-Scheduling, Interview Templates, Messages, Users, Configuration, Event Management, Onboarding, and Audit.

- **MCP endpoint** (`POST /mcp`) — Streamable Model Context Protocol server for Copilot Studio agents. **116 `sr_*` tools** focused on chat-natural workflows.
- **Typed REST operations** — **235 first-class operations** with full schemas, descriptions, and `x-ms-summary` on every parameter — ready for Power Automate.

## Auth

Uses the **SmartRecruiters [OAuth 2.0 General Partner Integration](https://developers.smartrecruiters.com/docs/oauth-20-general-partner-integration) (Client Credentials grant)**.

The connector collects two secure parameters:

| Parameter | Purpose |
|---|---|
| `client_id` | Master credentials (issued by the SmartRecruiters Partner & API Team) for partner-level calls, **or** customer-level credentials returned by `POST /apps-integrations/partner-api/integrations` after a customer enables your app. |
| `client_secret` | Paired secret for the Client ID above. |

The script exchanges these for a Bearer token at `POST https://api.smartrecruiters.com/identity/oauth/token` (form-encoded `grant_type=client_credentials`), caches it in-process per `client_id` for the token's full lifetime (with a 2-minute safety margin), and attaches `Authorization: Bearer ...` to every outbound call — both MCP tool invocations and the typed REST forwarders.

Because Power Platform's built-in OAuth flow does not support `client_credentials`, the credentials are passed to the script via `setheader` policy templates (`X-SR-Client-Id` / `X-SR-Client-Secret`) and stripped before any outbound request.

If you only need partner-level access (e.g. provisioning new customer integrations), provide the master credentials. For per-customer data access, provision a separate connection per customer using that customer's credentials.

### Required access scopes

When requesting master credentials from `partners@smartrecruiters.com`, ask for the full set of scopes the connector exposes. Anything missing here will return `OAuthPermissionsException` for the affected calls; the rest of the connector continues to work. Full reference: https://developers.smartrecruiters.com/docs/access-scopes

| Domain | Scopes |
|---|---|
| Company / users | `company_read`, `user_me_read`, `users_read`, `users_manage` |
| Jobs | `jobs_read`, `jobs_manage`, `jobs_publications_manage` |
| Candidates | `candidates_read`, `candidates_create`, `candidates_manage`, `candidate_status_read`, `candidates_offers_read`, `candidates_parse_resume`, `candidate_view_application_status_read` |
| Job applications | `job_applications_read`, `job_applications_manage` |
| Public apply on behalf of candidate | `candidate_applications_manage` |
| Configuration | `configuration_read`, `configuration_manage` |
| Approvals | `approvals_read`, `approvals_create` |
| Messages / notes / templates | `messages_read`, `messages_write`, `messages_manage`, `message_templates_read`, `message_templates_write` |
| Interviews & timeslots | `interviews_read`, `interviews_write`, `interview_types_read`, `interview_types_write` |
| Self-scheduling | `self_schedules_read`, `self_schedules_manage`, `schedule_preferences_read` |
| Interview templates | `interview_templates_read`, `interview_templates_manage` |
| Reviews | `reviews_read`, `reviews_write` |
| Reporting | `reporting_read`, `reporting_write` |
| Webhooks | `webhooks_read`, `webhooks_write`, `webhooks_delete`, `webhooks_manage` |
| Audit | `audit_events_read` |
| Event management | `event_management_read`, `event_management_write` |
| Utility | `url_shortener_write` |

Per the docs, request only what you need (Principle of Least Privilege). The Partner & API Team will adjust the granted scope set during review. Scopes are bound to the credentials at issue time — to add a new one later, request a credential refresh.

## Endpoint coverage (235 typed REST ops)

| Area | Operations |
|---|---|
| Application API (public-facing apply) | Post application, Get configuration, Get application status |
| Posting API (public, no auth required) | List postings, Get posting, List departments |
| Jobs API | Search, get, create, patch, update; status / status history; headcount; hiring team CRUD; job ads CRUD + publish/unpublish; positions CRUD; publication; latest approval; notes |
| Candidates API | Search, get, create (to pool / to job), update, delete; resume parse + add; application get / delete; status + history; source update; tags CRUD; attachments CRUD; properties get + bulk update; consent status + decisions + request; onboarding; offers; screening answers |
| Job Applications API | Get, delete, consent decisions, consent request |
| Users API | List, get, create, update, me, activation (activate / deactivate / email), password reset, avatar |
| Configuration API | Company, departments, hiring processes, sources, rejection / withdrawal reasons, job properties (+ values, activate / deactivate), candidate properties (+ values), predefined locations, career sites, offer properties |
| Access Groups & Roles | List groups, assign / remove users; list system roles |
| Approvals API | List pending, get, create, approve, reject; comments get + add |
| Messages / Notes API | Create note (`messages/shares`), delete note, fetch messages for candidate |
| Message Templates API | List, get, create, update, delete |
| Interviews API (Public) | CRUD; timeslots CRUD; candidate / interviewer / timeslot statuses; no-show; interview types |
| Self-Scheduling API | Available slots, create / cancel / update self-schedule, automated reschedule, schedule preferences, slot-count by interviewer / roles, search self-schedules |
| Interview Templates API | CRUD; job-level assign / find by job / by application / by hiring state; managed steps |
| Reviews API | CRUD; per-candidate / per-job listing; scorecard criteria by job |
| Candidate Offers | List for candidate, search, find by candidate, get single, latest approval, list documents, get document content |
| Reporting API | List reports, get report, list files, get file, download file, generate ad-hoc, most-recent file metadata + CSV |
| Webhooks API | Subscriptions CRUD, activate, secret key generate / get, callback log |
| Audit API | List events |
| Event Management API | Events CRUD; sessions delete; applicants pool + move; session interviewers; per-job / application / candidate event lookups |
| Onboarding API | Processes, assignments, activity attachments, fillable PDF + web form answers, web form metadata, new hires |
| Notification Preferences | List types catalog, global preferences (get / upsert), employee preferences (get all / save / update / bulk upsert / by channel) |
| URL Shortener | Shorten smartrecruiters.com URLs |

## MCP tool summary (116 `sr_*` tools)

The MCP surface intentionally focuses on chat-natural workflows. Tools are namespaced `sr_*` and descriptions use cross-references (`Call sr_search_jobs first to discover…`) so Copilot Studio reliably chains discovery → action calls. Full list lives in `script.csx HandleToolsList`.

| Domain | Tools |
|---|---|
| Discovery / read | `sr_search_jobs`, `sr_get_job`, `sr_search_candidates`, `sr_get_candidate`, `sr_get_application`, `sr_get_hiring_team`, `sr_get_job_note`, `sr_get_candidate_tags`, `sr_get_candidate_consent_status`, `sr_get_screening_answers`, … |
| Configuration / lookup | `sr_get_company_info`, `sr_list_departments`, `sr_list_hiring_processes`, `sr_list_source_types/_values`, `sr_list_rejection_reasons`, `sr_list_withdrawal_reasons`, `sr_list_job_properties`, `sr_list_candidate_properties/_values`, `sr_list_career_sites`, `sr_list_predefined_locations`, `sr_list_system_roles`, `sr_list_access_groups` |
| Public Posting API | `sr_list_public_postings`, `sr_get_public_posting`, `sr_list_public_departments` |
| Application API | `sr_post_application`, `sr_get_application_configuration`, `sr_get_candidate_application_status` |
| Jobs write | `sr_create_job`, `sr_update_job_status`, `sr_update_job_headcount`, `sr_update_job_note`, `sr_publish_job`, `sr_unpublish_job`, `sr_add_hiring_team_member`, `sr_remove_hiring_team_member`, `sr_publish_job_ad`, `sr_unpublish_job_ad` |
| Candidates write | `sr_create_candidate`, `sr_update_candidate`, `sr_delete_candidate`, `sr_parse_resume`, `sr_parse_resume_to_job`, `sr_update_application_status`, `sr_update_application_source`, `sr_add_candidate_tags`, `sr_replace_candidate_tags`, `sr_delete_candidate_tags`, `sr_update_application_properties`, `sr_request_candidate_consent`, `sr_update_onboarding_status` |
| Users | `sr_list_users`, `sr_get_user`, `sr_get_my_user`, `sr_create_user`, `sr_update_user`, `sr_activate_user`, `sr_deactivate_user`, `sr_send_activation_email`, `sr_send_password_reset` |
| Approvals | `sr_list_pending_approvals`, `sr_get_approval`, `sr_create_approval_request`, `sr_approve_request`, `sr_reject_approval`, `sr_get_approval_comments`, `sr_add_approval_comment` |
| Notes / messages | `sr_create_note`, `sr_delete_note`, `sr_fetch_messages` |
| Interviews | `sr_list_interviews`, `sr_get_interview`, `sr_create_interview`, `sr_delete_interview`, `sr_list_interview_types`, `sr_update_interview_candidate_status`, `sr_update_timeslot_interviewer_status`, `sr_update_timeslot_candidate_status`, `sr_update_timeslot_noshow` |
| Self-scheduling | `sr_search_self_schedules`, `sr_get_application_self_schedule`, `sr_request_self_reschedule`, `sr_update_self_schedule_invite` |
| Reviews / scorecards | `sr_list_reviews`, `sr_create_review`, `sr_get_scorecard_criteria_by_job` |
| Candidate offers | `sr_get_candidate_offers`, `sr_search_offers`, `sr_find_candidate_offers`, `sr_get_candidate_offer`, `sr_list_offer_documents` |
| Onboarding | `sr_get_onboarding_process`, `sr_get_onboarding_assignments`, `sr_get_web_form_answers`, `sr_get_new_hire` |
| Reports | `sr_list_reports`, `sr_get_report`, `sr_list_report_files`, `sr_get_most_recent_report_file`, `sr_generate_ad_hoc_report` |
| Webhooks | `sr_list_subscriptions`, `sr_create_subscription`, `sr_get_subscription`, `sr_delete_subscription`, `sr_activate_subscription` |
| Audit | `sr_list_audit_events` |

### Tools intentionally NOT exposed via MCP

Available as typed REST ops (and so usable from Power Automate) but skipped from the MCP surface because they're admin-config rather than chat-driven: notification preferences, URL shortener, form-answer field metadata, event session/interviewer admin, job-level interview template writes, message template CRUD, full configuration CRUD (departments / job properties / values / activate / deactivate / predefined-location create), access group user assignment, candidate attachments management, job ad CRUD, job positions CRUD, user avatar upload, and full report file download (CSV).

## Local validation

```pwsh
npm install -g ppcv  # one-time
ppcv .\SmartRecruiters
```

Expected: `PASSED ✓` with 0 errors / 0 warnings, 235 operations.

## Deploy

To the Power Platform Demo environment (`c4f149b0-9f42-e8c4-97d8-bc69b59f971c`):

```pwsh
pac connector create `
  --environment c4f149b0-9f42-e8c4-97d8-bc69b59f971c `
  --api-definition-file .\SmartRecruiters\apiDefinition.swagger.json `
  --api-properties-file  .\SmartRecruiters\apiProperties.json `
  --script-file          .\SmartRecruiters\script.csx
```

After first create, save the returned connector id and switch to `pac connector update --connector-id ... --environment ...` for revisions.

## Telemetry

Optional Application Insights — set `APP_INSIGHTS_CONNECTION_STRING` at the top of `script.csx` and the script will emit `McpRequestReceived` / `McpToolCallStarted` / `McpToolCallCompleted` / `McpToolCallError` / `RequestReceived` / `RequestCompleted` / `RequestError` / `TokenExchangeFailed` events. Leave empty to disable.

## Intentionally not in scope

- **Partner Job Board API** — uses Partner API key auth, separate auth flow, different host class. Out of scope for a Customer API connector.
- **Apps/Integrations partner-side endpoints** (`askforconsent`, `setupintegration`, `getpartnerconfig`, `gettoken`, `deleteintegration`) — these are endpoints the partner exposes on their own server for SmartRecruiters to call. Not for a connector to invoke.
- **Assessment API (2021)** — every page in the docs is marked deprecated.
- **Legacy Offer API** — deprecated; superseded by `/offers` and `/candidates/{id}/offers/*` which are included.
- **Deprecated single-property writes** — `candidatespropertiesvaluesupdate`, `candidatesonboardingupdate`, `candidatesstatusupdate`. SmartRecruiters docs say "use the /jobs/{jobId}/... variant instead" and we already have those.

## References

- SmartRecruiters Developer Hub: https://developers.smartrecruiters.com/docs/get-started
- API Reference: https://developers.smartrecruiters.com/reference/welcome
- Customer API overview: https://developers.smartrecruiters.com/docs/customer-overview
- Application API: https://developers.smartrecruiters.com/reference/apply-api
- OAuth General Partner Integration: https://developers.smartrecruiters.com/docs/oauth-20-general-partner-integration
- Access Scopes: https://developers.smartrecruiters.com/docs/access-scopes
