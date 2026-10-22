# Atlassian MCP Server: Jira, Confluence, and Bitbucket

An [MCP](https://modelcontextprotocol.io/) server for Atlassian Cloud that you host yourself on **Azure Container Apps**. It calls the **Jira**, **Confluence**, and **Bitbucket** REST APIs directly, and it does not depend on any Atlassian-hosted MCP service. You control the tools, the data returned, and where the server runs.

**Each person signs in as themselves.** The server holds no Atlassian or Bitbucket credential and no shared service account. It uses the caller's own access token, so Jira, Confluence, and Bitbucket permissions decide what each person sees and can change, and each product's history records who did it.

One container serves three independent endpoints. Each has its own tools, its own sign-in, and its own scopes, and you can turn each one off.

| Endpoint | Product | Signs in with |
|----------|---------|---------------|
| `/jira/mcp` | Jira Cloud | Atlassian OAuth 2.0 (3LO) |
| `/confluence/mcp` | Confluence Cloud | Atlassian OAuth 2.0 (3LO) |
| `/bitbucket/mcp` | Bitbucket Cloud | Bitbucket OAuth consumer |

Jira and Confluence share one Atlassian OAuth app. Bitbucket uses a separate identity provider, so it has its own OAuth consumer.

## What it does

The tools that create or change data are **off by default**. They are not registered, so a client cannot see or call them, until you turn them on for that product.

### Jira

| Tool | What it's for | Changes data |
|------|---------------|:------------:|
| `jira_search_issues` | Search issues with JQL. Results include a `url` that opens each issue | No |
| `jira_get_issue` | Read one issue: status, people, dates, labels, description, attachments, recent comments | No |
| `jira_get_issue_transitions` | The workflow transitions available for an issue right now | No |
| `jira_list_projects` | Find a project key, with a name filter | No |
| `jira_search_users` | Find a user's account ID by name or email | No |
| `jira_get_create_metadata` | A project's issue types, and for one type every field with whether it is required and its allowed values | No |
| `jira_get_current_user` | The Atlassian account, site, and scopes the requests run with | No |
| `jira_create_issue` | Create an issue | Yes |
| `jira_update_issue` | Change fields on an issue | Yes |
| `jira_transition_issue` | Move an issue through its workflow, optionally with a comment | Yes |
| `jira_add_comment` | Comment on an issue | Yes |
| `jira_assign_issue` | Set or clear the assignee | Yes |

### Confluence

| Tool | What it's for | Changes data |
|------|---------------|:------------:|
| `confluence_search_content` | Search pages and other content with CQL | No |
| `confluence_get_page` | Read one page as plain text, with its version and a `url` | No |
| `confluence_list_pages` | Browse pages, in one space or by exact title | No |
| `confluence_get_page_children` | The direct child pages of a page | No |
| `confluence_list_spaces` | Find a space's numeric ID | No |
| `confluence_get_current_user` | The Atlassian account, site, and scopes the requests run with | No |
| `confluence_create_page` | Create a page | Yes |
| `confluence_update_page` | Add to a page (the default) or replace its body, or change its title | Yes |
| `confluence_add_comment` | Comment at the bottom of a page | Yes |

### Bitbucket

| Tool | What it's for | Changes data |
|------|---------------|:------------:|
| `bitbucket_list_workspaces` | The workspaces you belong to | No |
| `bitbucket_list_repositories` | Repositories in a workspace, with a name filter | No |
| `bitbucket_get_repository` | One repository's details | No |
| `bitbucket_list_pull_requests` | Pull requests by state | No |
| `bitbucket_get_pull_request` | One pull request: description, branches, reviewers and approvals | No |
| `bitbucket_get_pull_request_diffstat` | The files a pull request changes, with lines added and removed | No |
| `bitbucket_get_pull_request_diff` | The unified diff, cut at a size limit | No |
| `bitbucket_list_pull_request_comments` | Comments, including inline ones with file and line | No |
| `bitbucket_list_commits` | Recent commits, optionally on one branch | No |
| `bitbucket_get_file` | A text file at a branch, tag, or commit, cut at a size limit | No |
| `bitbucket_get_current_user` | The Bitbucket account the requests run as | No |
| `bitbucket_create_pull_request_comment` | Comment on a pull request, generally or on a line | Yes |
| `bitbucket_create_pull_request` | Open a pull request from an existing branch | Yes |
| `bitbucket_approve_pull_request` | Approve a pull request | Yes |

There are no tools that delete issues or pages, or that merge pull requests.

Responses are shaped for a model. Provider plumbing is stripped out, every item gets a `url` to cite, lists say whether there is more, and results are capped (see [Limits](#limits)). An empty result is returned as a success, not an error.

Models write issue descriptions, comments, and page bodies as plain text with light Markdown: blank lines separate paragraphs, `- ` makes bullets, `1. ` makes numbered items, `#` makes headings, and a fenced block makes code. The server turns that into Atlassian Document Format for Jira and storage format for Confluence. Page content is escaped, so a model cannot insert markup or macros.

## How access works

The caller signs in to Atlassian (Jira, Confluence) or Bitbucket with an OAuth 2.0 **authorization code flow** and sends the resulting access token to the server as a bearer token. The server does two things with it:

1. **Checks it.** It asks the provider whether the token is good and who it belongs to. For Atlassian that also shows which sites the token can reach, and the server requires your configured site to be one of them. A request with no token, or one the provider refuses, gets a `401` that points the client at how to sign in. A good result is trusted for 60 seconds (`Auth:ValidationCacheSeconds`), so a burst of tool calls costs one check.
2. **Uses it.** Every call to Jira, Confluence, or Bitbucket carries that same token, so it runs as that person.

What this means in practice:

- **People only see their own data.** There is no run-as user to over-provision. A person who can't open a project, space, or repository can't read it here.
- **Nothing is stored.** The server keeps no token, no refresh token, and no OAuth client secret. Tokens are read from each request, held in memory for that request, and never logged. The check cache holds a hash of the token, not the token.
- **Revoking access works.** Revoking a token stops it within `Auth:ValidationCacheSeconds`. Set it to `0` to check every call.
- **One site.** Jira and Confluence calls go only to the `Atlassian:SiteUrl` you configured, even though an Atlassian token can cover several sites. A token with no access to that site is refused with a message that says so.
- **The OAuth client secret stays with the sign-in client.** The server never receives it. Whatever signs people in, such as a Microsoft 365 Copilot OAuth registration, holds it.

The server accepts a token issued for Atlassian or Bitbucket, not for itself. The MCP authorization specification discourages servers from accepting tokens that were not issued to them. This design contains that risk because the server only ever sends the token back to the same provider, and never stores or logs it. If your security review requires the server to be its own OAuth resource, this design is not the right fit.

## Prerequisites

- An Azure subscription where you can create resources and role assignments
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and test locally
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.62 or later, signed in with `az login`
- For Jira or Confluence: an Atlassian Cloud site, and permission to create an OAuth 2.0 app in the [Atlassian developer console](https://developer.atlassian.com/console/myapps/)
- For Bitbucket: a Bitbucket Cloud workspace where you can add an OAuth consumer

Create the OAuth app and consumer in your own account and use them with your own site and workspace.

## Setup

### 1. Create the Atlassian OAuth app (Jira and Confluence)

1. In the [developer console](https://developer.atlassian.com/console/myapps/), choose **Create > OAuth 2.0 integration** and name the app.
2. Under **Authorization**, next to **OAuth 2.0 (3LO)**, choose **Configure** and enter the **callback URL** of the client that will sign people in. For Microsoft 365 Copilot that is `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`.
3. Under **Permissions**, add the **Jira API** and the **Confluence API**, and configure these scopes:

   | Product | Always | Only if you turn write tools on |
   |---------|--------|---------------------------------|
   | Jira | `read:jira-work`, `read:jira-user` | `write:jira-work` |
   | Confluence | `read:page:confluence`, `read:space:confluence`, `search:confluence` | `write:page:confluence`, `write:comment:confluence` |

   The sign-in also asks for `read:me`, so the server can show who a request runs as, and `offline_access`, so the client can refresh the sign-in. Both are requested at sign-in and are not configured as permissions.
4. Under **Settings**, copy the **Client ID** and **Secret**. They go into the client that signs people in, not into this server.

### 2. Create the Bitbucket OAuth consumer

1. In Bitbucket, open **Workspace settings > OAuth consumers** and choose **Add consumer**.
2. Enter the **callback URL** of the client that will sign people in.
3. Grant these permissions: **Account: Read**, **Repositories: Read**, **Pull requests: Read**. If you turn write tools on, grant **Pull requests: Write** instead of Read.
4. Copy the **Key** and **Secret**. They go into the client that signs people in, not into this server.

### 3. Deploy

```powershell
.\infra\deploy.ps1 `
    -ResourceGroup rg-atlassian-mcp `
    -AtlassianSiteUrl https://acme.atlassian.net
```

There are no secrets to supply. The script builds the image in Azure Container Registry, so you don't need Docker. `-AppName` is 3 to 12 characters and drives resource names and the public hostname, so choose it once.

| Switch | Effect |
|--------|--------|
| `-DisableJira`, `-DisableConfluence`, `-DisableBitbucket` | Leave a product out. `-AtlassianSiteUrl` is not needed if both Jira and Confluence are off |
| `-EnableJiraWriteTools`, `-EnableConfluenceWriteTools`, `-EnableBitbucketWriteTools` | Register that product's tools that create and change data |
| `-BitbucketWorkspace my-team` | Limit every Bitbucket tool to one workspace |
| `-AllowedIpRanges`, `-CorsAllowedOrigins`, `-MinReplicas` | Restrict ingress, allow browser clients, or allow scale to zero |

The script prints each endpoint, for example `https://<fqdn>/jira/mcp`, and the **Atlassian authorization URL** you need in the next step.

To serve Jira, Confluence, and Bitbucket from separate deployments instead, run the script once per product with the other two disabled and a different `-AppName` and `-ResourceGroup`.

### 4. Check it

```powershell
curl https://<fqdn>/healthz                                         # 200. Never depends on Atlassian or Bitbucket
curl https://<fqdn>/ready                                           # 200 "Healthy", or "Degraded" if a provider is unreachable
curl https://<fqdn>/.well-known/oauth-protected-resource/jira/mcp   # 200, names the sign-in for that endpoint
curl -i -X POST https://<fqdn>/jira/mcp                             # 401 with a WWW-Authenticate header
```

The server can't call a provider on its own, so there is no readiness test of sign-in. Sign in from your client and call `jira_get_current_user`, `confluence_get_current_user`, or `bitbucket_get_current_user`. It confirms the person the request runs as.

## Connect a client

Give the client these values. It holds the client ID and secret, and it sends the access token to the endpoint as a bearer token.

| | Jira and Confluence | Bitbucket |
|---|---|---|
| MCP endpoint | `https://<fqdn>/jira/mcp` and `https://<fqdn>/confluence/mcp` | `https://<fqdn>/bitbucket/mcp` |
| Authorization URL | `https://<fqdn>/atlassian/oauth/authorize` | `https://bitbucket.org/site/oauth2/authorize` |
| Token and refresh URL | `https://auth.atlassian.com/oauth/token` | `https://bitbucket.org/site/oauth2/access_token` |
| Client ID and secret | From the Atlassian app | The consumer's key and secret |
| Scopes | The scopes from step 1 that apply, plus `read:me offline_access`, separated by spaces | Set on the consumer. Nothing to send |
| Callback URL | The client's own, registered on the app | The client's own, registered on the consumer |

**Why Atlassian's authorization URL points at this server.** Atlassian requires an `audience=api.atlassian.com` parameter on the authorization request, and many OAuth clients, including the Teams developer portal and the Power Platform OAuth template, cannot add one. This server's `/atlassian/oauth/authorize` adds it, then redirects to Atlassian's sign-in page. Only the standard authorization parameters pass through, and the destination is always `auth.atlassian.com`. The token and refresh requests go straight from the client to Atlassian, so no code, token, or secret passes through this server.

**Microsoft 365 Copilot.** In the Teams developer portal, open **Tools > OAuth client registration** and enter the values above. Set **Applicable to apps** to **Any app**, and keep `offline_access` in the scope so the sign-in can be refreshed.

**Other MCP clients.** Obtain a token with the authorization code flow and send it as `Authorization: Bearer <token>`. Each endpoint publishes [protected resource metadata](https://datatracker.ietf.org/doc/html/rfc9728) at `/.well-known/oauth-protected-resource/<endpoint path>`, naming its authorization server and scopes. Atlassian and Bitbucket don't offer dynamic client registration, so the client needs your client ID configured in advance.

## Configuration

Set these as environment variables with `__` for nesting, for example `Atlassian__SiteUrl`. The Bicep template already does this.

| Setting | Default | Purpose |
|---------|---------|---------|
| `Atlassian:SiteUrl` | | Your site, such as `https://acme.atlassian.net`. Must be `https` with no path. Required when Jira or Confluence is on |
| `Atlassian:CloudId` | | Local development only: the site's cloud ID, used when `Auth:Enabled` is `false` |
| `Jira:Enabled`, `Confluence:Enabled`, `Bitbucket:Enabled` | `true` | Serve that product's endpoint. At least one must be on |
| `Jira:EnableWriteTools`, `Confluence:EnableWriteTools`, `Bitbucket:EnableWriteTools` | `false` | Register that product's tools that create or change data |
| `Bitbucket:Workspace` | | Limit every Bitbucket tool to this workspace slug |
| `Auth:Enabled` | `true` | Check each caller's token at the door. When off, tools can be listed without a token. The provider still checks every token on every call |
| `Auth:ValidationCacheSeconds` | `60` | How long a token the provider accepted is trusted without asking again. `0` checks every call |
| `RateLimit:PermitsPerMinute` | `120` | Per-person limit. `0` turns it off |
| `Limits:MaxItems` | `50` | Most items one list call returns, from 1 to 200 |
| `Limits:MaxResponseChars` | `100000` | Most characters of items one list call returns |
| `Limits:MaxTextChars` | `30000` | Most characters of one page, issue, comment, diff, or file returned or accepted as text |

Infrastructure parameters in `infra/main.bicep`: `minReplicas` (default `1`, so the first tool call after idle has no cold start), `maxReplicas` (`5`), `allowedIpRanges`, and `corsAllowedOrigins`.

## Limits

- **Size caps.** List calls return at most `Limits:MaxItems` items and `Limits:MaxResponseChars` characters. Pages, diffs, and files are cut at `Limits:MaxTextChars` and say so. Binary files are refused.
- **Paging.** Jira search and Confluence lists return a token (`nextPageToken`, `nextCursor`, or `nextStart`) to pass back for the next page. Bitbucket lists use a page number and return `nextPage`. A model never supplies a URL.
- **Atlassian rate limits.** Jira and Confluence apply an hourly points quota and per-second limits to OAuth apps. When one is reached, the tool returns a message that says how many seconds to wait. The quota belongs to your OAuth app, and large queries cost more points, so ask for specific fields and add restrictions to JQL and CQL.
- **Per-person rate limit.** `RateLimit:PermitsPerMinute` limits each signed-in person. A caller who is not signed in is limited by IP address.

## Run it locally

```powershell
$env:Atlassian__SiteUrl = "https://acme.atlassian.net"
$env:Atlassian__CloudId = "<your site's cloud ID>"
dotnet run --project src/Atlassian.Mcp.Host --environment Development
```

In `Development`, `Auth:Enabled` is `false`, so you can list tools at `http://localhost:5000/jira/mcp` (the port is printed at startup) with [MCP Inspector](https://github.com/modelcontextprotocol/inspector) without a token. To call a tool, send your own access token in the `Authorization: Bearer` header; the server passes it straight to the provider. Get your site's cloud ID by calling `https://api.atlassian.com/oauth/token/accessible-resources` with your token. Point it at a test site first. For Bitbucket alone, set `Jira__Enabled` and `Confluence__Enabled` to `false`.

```powershell
dotnet test Atlassian.slnx
```

On Windows, build output goes to `%LOCALAPPDATA%\AtlassianMcp\build` so a synced folder doesn't churn on `bin` and `obj`.

## Security notes

- Run it against a **test site and workspace first**, and keep write tools off until you have watched how your agent uses the read tools.
- **Changes are attributed to the person who made them.** Jira, Confluence, and Bitbucket show the signed-in user, not a service account.
- The server never logs or stores a token. HTTP client logging is set to `Warning` so request URLs, which carry JQL, CQL, and filter text, stay out of the logs. Application Insights dependency telemetry records request URLs too; the OpenTelemetry HTTP instrumentation redacts query-string values by default, but confirm that in your own workspace if queries may contain sensitive text.
- Tool arguments come from a model, so each one is validated before use: issue and project keys, page and pull request numbers, workspace and repository slugs, branch and file paths (no `..`), account IDs, UUIDs, field names, and paging tokens are checked against the shape the provider uses, so a model cannot use them to request an arbitrary path. A paging token that is a URL is refused.
- JQL and CQL are passed to the provider as text, limited in length. The real limit is the signed-in person's permissions. Rely on those, not on the tool filter.
- Failed writes are not retried automatically, so a transient error cannot create a duplicate. Create and comment tools say so, and tell the model to look before trying again.
- `confluence_update_page` adds to a page by default. Replacing a body rewrites the page from plain text, which loses macros and formatting that plain text cannot carry, so it must be asked for explicitly.
- The `/atlassian/oauth/authorize` endpoint is anonymous because sign-in has no token yet. It only redirects to `auth.atlassian.com`, always sets the `audience` and `prompt` values itself, and passes on nothing but the standard authorization parameters.
- If Atlassian or Bitbucket is down while a token is being checked, the request is refused rather than let through.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| `401` from the server | The request has no token, or the provider refused it. The client has to sign in again |
| `Your Atlassian sign-in does not have access to https://...` | The token cannot reach your configured site. Sign in with an account that can open the site, and choose that site on the consent screen. Check that `Atlassian:SiteUrl` matches the site exactly |
| Atlassian's sign-in page reports a missing or invalid `audience` | The client's authorization URL is not `https://<fqdn>/atlassian/oauth/authorize`. Use the URL `deploy.ps1` printed |
| Atlassian reports an invalid redirect or callback | The client's callback URL isn't registered on the Atlassian app (or Bitbucket consumer) |
| Atlassian reports an invalid scope | A scope in the client's request isn't added under **Permissions** on the Atlassian app |
| `The sign-in does not include a scope this action needs` | The OAuth app doesn't grant a scope the tool uses, which is usually a write scope. Add it under **Permissions**, then have the person sign in again |
| Bitbucket returns `Required scopes: ...` | The consumer is missing that permission. Add it on the consumer, then sign in again |
| `... rejected this sign-in. It has expired or been revoked` | The token expired or was revoked after it was checked. Sign in again. Keep `offline_access` in the scope so the client can refresh |
| `/ready` returns `Degraded` | Atlassian or Bitbucket is unreachable from the server. Check their status pages |
| `... returned HTTP 404 ... cannot see it` | The item doesn't exist, or the signed-in person has no access to it |
| `Error in the JQL Query` or `The query is empty` | Fix the JQL. Jira refuses unbounded queries, so include a project, assignee, or date restriction |
| Creating an issue fails with a field error such as `summary: ...` or `customfield_...: Field is required` | Call `jira_get_create_metadata` with the project and issue type to see the required fields and allowed values |
| `... has no transition 'X' available right now` | The workflow doesn't allow that move from the current status. The message lists what is available |
| `Someone else changed it first` when updating a page | The page was edited since it was read. Read it again and retry |
| `This server is limited to the workspace '...'` | `Bitbucket:Workspace` is set. Ask for that workspace, or clear the setting |
| `... rate limit reached. Retry after N seconds` | Your OAuth app's quota or a per-second limit was reached. Wait, or narrow the request |
| A page, diff, or file ends early with `truncated` | It is longer than `Limits:MaxTextChars`. Open the item's `url` for the full content, or read the diffstat first |
| The write tools are missing | That product's `EnableWriteTools` is `false`. Redeploy with `-Enable<Product>WriteTools` |
| `429` | The person exceeded `RateLimit:PermitsPerMinute` |
