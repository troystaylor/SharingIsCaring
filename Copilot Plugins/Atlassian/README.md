# Atlassian

Cowork / Copilot plugin for the official [Atlassian remote MCP server](https://support.atlassian.com/atlassian-ai-gateway/docs/get-started-with-the-atlassian-remote-mcp-server/) (`https://mcp.atlassian.com/v2/mcp`, source [atlassian/atlassian-mcp-server](https://github.com/atlassian/atlassian-mcp-server)). One connector gives access to Jira, Confluence, Compass, Bitbucket Cloud, Loom, Goals, Projects, Teams, Teamwork Graph and more, limited to what the signed-in user and the organization's admin settings allow.

| Component | Path |
| --- | --- |
| Connector `atlassian` (remote MCP, OAuth with PKCE through the Teams Developer Portal) | `appPackage/manifest.json` |
| Shared skills: `atlassian-access`, `jql-cql-builder`, `evidence-and-uncertainty`, `write-safety` | `appPackage/skills/*/SKILL.md` |
| Scenario skills: `support-triage`, `technical-design-draft`, `spec-verify`, `knowledge-maintenance`, `status-reporting`, `code-review`, `worklog-signals`, `release-notes`, `backlog-hygiene`, `issue-brief`, `my-atlassian-queue`, `meeting-actions-to-jira` | `appPackage/skills/*/SKILL.md` |

The shared skills hold the rules (cloudId bootstrap, tool choice, citations, query patterns, evidence, write guardrails). Each scenario skill tells the agent to apply them. Atlassian's own v2 skills were used only as a reference for tool names and conventions.

| Skill | Scenario | Atlassian tools it relies on | Writes |
| --- | --- | --- | --- |
| `support-triage` | Support and incident triage | `searchJiraIssuesUsingJql`, `getJiraIssue`, comments, changelogs, `searchConfluence`, `getConfluenceContent` | No |
| `technical-design-draft` | Technical design drafting | `searchConfluence`, `getConfluenceContent`, templates | Draft page, optional |
| `spec-verify` | Verifying a spec before generating JSON | Confluence search and read | No |
| `knowledge-maintenance` | KB upkeep | Jira issue, `searchConfluence`, version list and diff | Draft update or comment, after approval |
| `status-reporting` | Weekly status and executive summary | JQL totals, boards, sprints, versions, Confluence | Draft page, optional |
| `code-review` | Pull request review | Bitbucket Cloud file, PR, diff, comments, pipeline logs, plus Confluence | No |
| `worklog-signals` | Worklog and activity signals (optional) | `listJiraIssueWorklogs`, issue activity | No |
| `release-notes` | Release notes from a Jira version | `getJiraProjectVersions`, `searchJiraIssuesUsingJql`, `searchConfluence` | Draft page, optional |
| `backlog-hygiene` | Backlog audit | `searchJiraIssuesUsingJql`, changelogs, issue links | No |
| `issue-brief` | Summary of one issue | `getJiraIssue`, comments, changelogs, links, worklogs | No |
| `my-atlassian-queue` | Personal work queue | `searchJiraIssuesUsingJql`, `searchConfluence`, `atlassianUserInfo` | No |
| `meeting-actions-to-jira` | Meeting action items to tasks | `getConfluenceContent`, `lookupJiraAccountId`, `createJiraIssue` | Yes (phase 2) |

## Configure for your organization

`jql-cql-builder` has a reference table with `[[CONFIGURE: ...]]` placeholders for project keys, space keys, issue types, statuses, custom field IDs, labels and board IDs. Scenario skills also reference the guidelines space, the design template, the integration spaces, RAG thresholds and the drafts parent page. Fill these in before relying on the skills; unfilled rows make the agent look values up instead of guessing.

## How the server exposes tools

The v2 server lists only a few primary tools (for example `getJiraIssue`, `searchJiraIssuesUsingJql`, `createJiraIssue`, `getConfluenceContent`, `searchConfluence`, `search`) and reaches the rest through `discover` plus the execute tools (`executeRead`, `executeWrite`, `executeDestructive`). The skills describe this convention. The plugin has no `mcpToolDescription`, so the live `tools/list` is used. `delete_jira` and `manage_jira` tools are off by default and must be enabled by an Atlassian org admin; no skill uses them.

Admins grant access by permission group, and each tool inherits its group's access. The plugin needs these groups:

| Phase | Groups | Scopes to request |
| --- | --- | --- |
| 1 (read-only) | `search_jira`, `read_jira`, `search_confluence`, `read_confluence`, `read_bitbucket` (Cloud) | `search:jira:agent-interface`, `read:jira:agent-interface`, `search:confluence:agent-interface`, `read:confluence:agent-interface`, `read:bitbucket:agent-interface` |
| 2 (writes, later) | `write_jira`, `write_confluence` | `write:jira:agent-interface`, `write:confluence:agent-interface` (users must consent again) |
| Optional | `search_code`, `search_atlassian`, `read_teamwork_graph` | `search:code:agent-interface`, `search:rovo:agent-interface`, `read:all:twg` (the last two use Rovo credits) |
| Never | `delete_jira`, `manage_jira` | leave off |

Because the OAuth scopes are what the token can do, registering the Teams client with only the phase 1 scopes keeps the plugin read-only even if a skill is misused.

Atlassian's supported-tools page lists Bitbucket write tools (`write_bitbucket`: pull request create, update, merge, approve, comment, branch, commit, pipeline run) and a Code Search group (`search_code`: `searchCode`, `getCodeFile`, `listCodeDirectory`, `scanCodeRepo`, `getCodeSymbol`, `diffCodeSymbols`, OAuth only). No skill uses the Bitbucket write tools.

### Limiting tools

- The app manifest has no allow-list for a connector's tools. The only related property is `mcpToolDescription`, and when it is present dynamic discovery is not used, so a hand-trimmed `tools/list` file would hide tools from the agent. Microsoft Learn says Cowork ignores that file, so this is unverified.
- `write_confluence` also holds permission and public-link tools and credit-heavy infographic tools, and `discover` can reach any tool the token allows. The hard limits are the OAuth scopes and the admin's permission groups; the skills (`atlassian-access`, `write-safety`) forbid the risky tools as a second layer.
- To capture a flat `tools/list` for `--tool-description`, connect an MCP client that can sign in (for example MCP Inspector) to `https://mcp.atlassian.com/v2/mcp?tools=all`.

## Setup

1. **Atlassian**: you need an Atlassian Cloud site with Jira, Confluence or another supported product. An org admin can control which tools, scopes and client domains are allowed under Atlassian Administration > Rovo MCP server. Jira Service Management tools need API token authentication and are not available through this plugin.
2. **Create an OAuth client at Atlassian.** Atlassian has no developer-console app for its MCP server; clients are created through its dynamic client registration endpoint, which can also be called by hand. It accepted the Teams redirect URI in testing and returned a client ID and a client secret that does not expire (`token_endpoint_auth_method` `client_secret_post`). Run this once and keep the secret out of source control:

   ```powershell
   $body = @{
     client_name = "Atlassian MCP for Microsoft 365 Copilot"
     redirect_uris = @("https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect")
     grant_types = @("authorization_code", "refresh_token")
     response_types = @("code")
     token_endpoint_auth_method = "client_secret_post"
   } | ConvertTo-Json
   $r = Invoke-RestMethod -Method Post -ContentType "application/json" -Body $body `
     -Uri "https://auth.atlassian.com/VCeDsk8ZHncYF1g234fKtc4lNipbBhu3/dcr/register"
   "client_id: $($r.client_id)"
   $r.client_secret | Set-Clipboard   # paste into the Developer Portal, then clear the clipboard
   ```

   The path segment is the authorization server ID from `https://mcp.atlassian.com/.well-known/oauth-protected-resource/v2/mcp`; re-read it if registration returns 404.
3. **Teams Developer Portal** ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) > Tools > OAuth client registration > Register client:
   - **Registration name**: for example `Atlassian MCP`.
   - **Base URL**: `https://mcp.atlassian.com/v2/mcp`.
   - **Restrict usage by org**: **My organization only** for one tenant; **Any Microsoft 365 organization** for cross-tenant use.
   - **Restrict usage by app**: **Any Teams app**. Do not bind it to a Teams app ID, or provisioning succeeds and every tool call returns 404.
   - **Client ID** and **Client secret**: from step 2.
   - **Authorization endpoint**: `https://auth.atlassian.com/authorize`.
   - **Token endpoint** and **Refresh endpoint**: `https://auth.atlassian.com/oauth/token`.
   - **Scope**: `offline_access read:me read:account search:jira:agent-interface read:jira:agent-interface search:confluence:agent-interface read:confluence:agent-interface read:bitbucket:agent-interface` for phase 1 (read-only). Later, add `write:jira:agent-interface` and `write:confluence:agent-interface`, and optionally `search:code:agent-interface`, `search:rovo:agent-interface` and `read:all:twg`. Leave out `delete:` and `manage:` scopes.
   - **Enable Proof Key for Code Exchange (PKCE)**: enabled (Atlassian supports S256 only).
   - After saving, the portal shows the **OAuth client registration ID**.
4. Copy the registration ID into `env/.env.dev.user` (copy `env/.env.dev.user.sample`) as `ATLASSIAN_OAUTH_REFERENCE_ID=<id>`. Nothing secret is committed; the manifest only references `${{ATLASSIAN_OAUTH_REFERENCE_ID}}`.
5. An Atlassian org admin controls which client domains may complete OAuth under Atlassian Administration > Rovo MCP server. Registration accepted the Teams redirect, but if sign-in is blocked, ask the admin to allow `https://teams.microsoft.com/**` (unverified).

A dynamic client registration alternative exists (`wiqd plugin add connector --auth-type dcr`, which omits the `authorization` block), but Microsoft supports it in Cowork only, not Copilot Chat, and the Teams Developer Portal does not support it yet. This plugin uses a static OAuth client so it works in both.

## Build and test

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

If `package` or deep validation reports `ATLASSIAN_OAUTH_REFERENCE_ID` missing, set it in the shell: `$env:ATLASSIAN_OAUTH_REFERENCE_ID = "<id>"`. Bump `version` in `appPackage/manifest.json` before uploading a rebuilt package. `env/.env.dev` is git-ignored because provisioning writes your tenant and app IDs to it.

## Known issues

- **Verification:** check sign-in, tool discovery and one read and one write before continuing.
- **Authorize parameters:** the Teams vault sends a standard authorization request. Atlassian's MCP flow may also expect an RFC 8707 `resource` (or `audience`) parameter; if sign-in succeeds but tool calls return 401, this is the likely cause.
- **`mcpToolDescription`:** Microsoft Learn says Cowork discovers tools at runtime and ignores this file, but the WIQD reference says Cowork may show "Could not verify connection" for a URL-only connector. If that happens, capture `tools/list` with a token (for example with MCP Inspector) and add it with `wiqd plugin add connector --tool-description`.
- **Token refresh:** the Microsoft token store has not refreshed Salesforce tokens in earlier testing. Atlassian grants `offline_access`, but refresh behavior here is untested.
- **Credits:** `search` (Rovo), `getTeamworkGraphContext` and similar Teamwork Graph tools use Rovo credits from your organization's shared pool. The skills prefer JQL and CQL and report a `Rovo-credit calls: N` line, but a skill cannot send telemetry; counting credits centrally needs server-side or Atlassian-side reporting.
- **Idempotency:** Atlassian write tools have no idempotency key. `write-safety` emulates one with a run marker and a duplicate check.
- **Exact counts:** the skills report counts only from a returned total or by paging to the end. Whether the search tools return a total in this version is unverified.
- **Bitbucket:** tools work only for Bitbucket Cloud workspaces linked to an Atlassian organization; confirm your Bitbucket workspace is linked before relying on `code-review`.
- **Approvals:** `knowledge-maintenance` prepares a change package for your own approval process (for example a Power Automate flow); no approval connector is wired in this plugin.
- Every call runs as the signed-in user and is recorded in the Atlassian audit log. IP allowlisting applies; hosted clients can come from Microsoft's IP ranges rather than your network.
- The v1 endpoints still work and expose v2 tools. Tool names differ from v1 (for example `getConfluencePage` is now `getConfluenceContent`), so keep skills on v2 names.
- Default icons (`appPackage/color.png` 192x192, `outline.png` 32x32), developer name and URLs in the manifest are placeholders to replace before publishing.

## Attribution

The skills are written for this plugin and follow the conventions of Atlassian's v2 skills in [atlassian/atlassian-mcp-server](https://github.com/atlassian/atlassian-mcp-server/tree/main/skills) (Apache-2.0).
