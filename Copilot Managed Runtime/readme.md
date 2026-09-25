# Copilot Managed Runtime

A dual-mode Power Platform custom connector for **Microsoft Copilot Managed Runtime** (public preview). It exposes typed REST operations for Power Automate **and** an MCP endpoint for Copilot Studio agents, both backed by the same Power Platform API service that the `ms` CLI uses.

Microsoft Copilot Managed Runtime lets you build custom web applications with React, TypeScript, and Vite, then host, deploy, and share them through Power Platform. The official tooling is the `@microsoft/managed-apps-cli` npm package, invoked as `ms`. This connector wraps the same server API so you can drive that lifecycle from flows and agents instead of a terminal.

Apps you publish are listed for users at [managedapps.cloud.microsoft](https://managedapps.cloud.microsoft/) and for administrators under **Apps** in the Microsoft 365 admin center.

### A note on naming

The product was called **Managed Apps** before it was renamed to **Copilot Managed Runtime**. The old name survives in identifiers that are not safe to change, and you will see all three of these:

| Identifier | Value |
| --- | --- |
| npm packages | `@microsoft/managed-apps-cli`, `@microsoft/managed-apps` |
| API route prefix | `/appframework` |
| User portal domain | `managedapps.cloud.microsoft` |

This connector's own operation IDs and MCP tool names keep the `managedapps` prefix for the same reason — renaming them would break agents and flows already bound to them.

## Licensing

Every user who runs an app needs one of the following. This applies to you while developing and to everyone you share an app with.

| Option | Behavior |
| --- | --- |
| Power Apps Premium plan | Covers all app operations without consuming Copilot Credits |
| Copilot Credits | Charged per app launch and per API call, at 0.1 credit per call |

Without sufficient credits a user is warned first and can keep working, then blocked once they hit **20 app operations** or **five minutes** of use, whichever comes first. Power Apps Premium users are not subject to those limits.

## Is there really an API?

Yes. There is no published REST reference yet, so the surface here was derived from the shipped CLI and then confirmed against the live service.

The decisive evidence is the contrast between two responses on an environment-scoped host:

| Request | Response |
| --- | --- |
| `GET /appframework/apps?api-version=1` | `403` &mdash; `InsufficientDelegatedPermissions` |
| `GET /appframework/totally-not-a-route?api-version=1` | `404` &mdash; `RouteNotFound` |

An unknown path is rejected by routing. `/appframework/apps` gets past routing and fails only on authorization, which means the route is real and serving traffic. The 403 body names what it wants:

```json
{
  "code": "Forbidden",
  "innererror": {
    "code": "InsufficientDelegatedPermissions",
    "message": "Authorization denied: Application missing required delegated permissions: [PowerApps.Apps.Read, ManagedApps.ReadWrite.All, All.All.ReadWrite]"
  }
}
```

`ManagedApps.ReadWrite.All` is a published delegated scope ("Manage managed apps") on the first-party **Power Platform API** application `8578e004-a5c6-46e7-913e-12f58912df43`. It is user-consentable, so this is a supported surface rather than an internal backchannel.

## How the endpoint is addressed

Copilot Managed Runtime calls do **not** go to `api.powerplatform.com` directly. They go to an environment-scoped host derived from the environment ID: lowercase it, strip the dashes, then split the last two characters off into their own label.

```
b5e65502-f2e4-ecc7-8e66-df670e46b100
  → b5e65502f2e4ecc78e66df670e46b1.00.environment.api.powerplatform.com
```

The swagger declares `api.powerplatform.com` as the host to satisfy Power Platform, and [script.csx](./script.csx) rewrites every request to the correct environment host. That is why every operation takes an `environmentId`, and why all operations are listed in `scriptOperations`.

This also works for default environments whose ID carries a `Default-` prefix &mdash; the same normalization applies and the resulting host routes correctly.

## Setup

### 1. Register an Entra application

Create an app registration and grant it delegated permission to the Power Platform API.

```powershell
az ad app create --display-name "Copilot Managed Runtime Connector"
```

Add these delegated permissions from the **Power Platform API** (`8578e004-a5c6-46e7-913e-12f58912df43`):

| Scope | Purpose |
| --- | --- |
| `ManagedApps.ReadWrite.All` | Copilot Managed Runtime app lifecycle &mdash; required |
| `PowerApps.Apps.Read` | Reading app metadata |
| `PowerApps.Environments.Read` | Environment dropdown |

All three are user-consentable, so admin consent is optional unless your tenant requires it.

### 2. Deploy the connector

Replace `[YOUR_CLIENT_ID]` in [apiProperties.json](./apiProperties.json) with your real client ID first. Leave `[YOUR_REDIRECT_URL]` as a placeholder &mdash; the platform generates a per-connector redirect URL on deploy and discards whatever you supply.

Authenticate against the target environment:

```powershell
pac auth create --environment <environment-id>
```

Because `scriptOperations` is non-empty, the script and the definition must always travel together. Omitting `--script-file` fails with `InvalidScriptDefinitionUrlWithNonNullOperations`.

**Create the connector in two steps.** A script-enabled connector needs a function app assigned from a regional pool when it is first created, and requesting that during `create` frequently fails. Create the connector without the script, then attach the script with an update:

```powershell
# Step 1 - create without the script
$p = Get-Content apiProperties.json -Raw | ConvertFrom-Json
$p.properties.PSObject.Properties.Remove('scriptOperations')
$p | ConvertTo-Json -Depth 40 | Set-Content "$env:TEMP\noscript.json" -Encoding UTF8

pac connector create `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file "$env:TEMP\noscript.json"

# Step 2 - attach the script using the ID returned above
pac connector update `
    --connector-id <connector-id> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Updating an existing connector is a single command, since it already holds an assigned function app:

```powershell
pac connector update `
    --connector-id <connector-id> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Add `--environment <environment-id>` to any of these to target an environment other than the active auth profile.

### 3. Verify what deployed

A success message means the row was written, not that the definition survived intact. Download it back and check:

```powershell
pac connector download --connector-id <connector-id> --outputDirectory ./verify
```

Expect 22 operations, `x-ms-agentic-protocol: mcp-streamable-1.0` on the `/mcp` path, and the environment dropdowns still present.

### 4. Register the generated redirect URL

Read the generated URL back and add it to the app registration, or OAuth consent will fail:

```powershell
# from ./verify/apiProperties.json:
# properties.connectionParameters.token.oAuthSettings.redirectUrl

az ad app update --id <appId> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<per-connector-url>"
```

The generated URL encodes both the connector name and the environment, so a connector deployed to dev and prod produces two different URLs and **both** must be registered.

## Operations

Every operation except `GetEnvironmentDropdown` requires an `environmentId`, supplied by a dynamic dropdown in the designer.

### Apps

| Operation | Description |
| --- | --- |
| `ListApps` | List Copilot Managed Runtime apps in an environment, filtered by `read` or `write` permission, with `$skiptoken` paging |
| `CreateApp` | Create a new Copilot Managed Runtime app and its backing repository |
| `GetApp` | Get one app by ID |
| `DeleteApp` | Delete an app; reports whether it existed |
| `LocateApp` | Resolve the tenant, environment, and geo hosting an app |

### Build and deploy

| Operation | Description |
| --- | --- |
| `BuildApp` | Start a server-side build from the backing repository |
| `GetBuildOperation` | Poll build status and result |
| `GetBuildOperationLog` | Fetch the build log for diagnosing failures |
| `DeployApp` | Deploy a built commit so players can reach it |
| `UploadBuild` | Upload a prebuilt zip artifact; the service commits it and returns a commit SHA |

#### Uploading an artifact instead of building

`UploadBuild` is the one operation that writes code into the app's repository. You send a zip of the **built** app output, the service commits it and returns a `commitSha`, and you pass that SHA to `DeployApp`. That skips the server-side build entirely, so `BuildApp` is not needed on this path.

This is not a Git push and does not replace one. Ordinary source control stays native Git — `git add`, `git commit`, `git push` — which is what the repository binding operations below exist to authorize.

> **Size cap.** Power Platform limits the total payload through a custom connector to **100 MB**, while the service itself accepts up to **500 MB**. The connector ceiling is the one you hit first, and the connector checks it before sending so an oversized artifact fails immediately with a clear message rather than a late `413`. For anything larger, use `ms deploy --artifact <file>.zip` instead. Over MCP the limit is effectively lower still, because the zip must be base64 encoded and base64 adds roughly a third to the payload.

### Sharing and access

| Operation | Description |
| --- | --- |
| `ListShareLinks` | List share links granting access to an app |
| `CreateShareLink` | Create a share link |
| `DeleteShareLink` | Revoke a share link |
| `ListRoleAssignments` | List users and groups assigned roles, with paging |
| `ModifyRoleAssignments` | Grant and revoke roles in one call |

### Connectivity

| Operation | Description |
| --- | --- |
| `ListConnectors` | Connectors available as Copilot Managed Runtime app data sources |
| `GetConnector` | Metadata for one connector |
| `ListConnections` | The caller's existing connections for a connector |

### Repository binding

Binding an app to your own GitHub repository requires the repository owner to authorize the Managed Apps GitHub App. That runs as a device code flow across two operations.

| Operation | Description |
| --- | --- |
| `StartGitHubAuth` | Start authorization for a repository URL; returns a one-time code, a verification URI, and a session ID |
| `GetGitHubAuthStatus` | Poll the session until it reaches a terminal state |

`StartGitHubAuth` takes a full repository URL such as `https://github.com/owner/repo`. GitHub Enterprise Cloud hosts work too, and the owner, repository, and GitHub base URI are parsed out for you. Show the user the `userCode` and `verificationUri`, then poll `GetGitHubAuthStatus` no faster than the returned `pollIntervalSeconds` until `expiresIn` elapses.

`state` is one of:

| State | Meaning |
| --- | --- |
| `Pending` | The user has not finished signing in. Keep polling. |
| `Complete` | Authorized. `githubLogin` and `mappingExpiresAt` are populated. |
| `Denied` | The user declined. Binding cannot proceed. |
| `NotFound` | The session is unknown, usually a server restart. Start a new one. |
| `Expired` | The window closed before sign-in finished. Start a new one. |

Pass the `oauthBaseUri` returned by `StartGitHubAuth` straight back into `GetGitHubAuthStatus`. It defaults to `https://github.com`, which is wrong for GitHub Enterprise Cloud repositories.

## MCP tools

The `/mcp` endpoint speaks JSON-RPC 2.0 and implements `initialize`, `tools/list`, `tools/call`, `resources/list`, `ping`, and the `notifications/*` methods.

Tools are named with a `managedapps_` prefix and mirror the REST operations, plus one extra:

- `managedapps_list_environments` &mdash; discovery tool so an agent can find the `environmentId` that every other tool requires

Then `managedapps_list_apps`, `managedapps_get_app`, `managedapps_create_app`, `managedapps_delete_app`, `managedapps_locate_app`, `managedapps_build_app`, `managedapps_get_build_operation`, `managedapps_get_build_log`, `managedapps_deploy_app`, `managedapps_upload_build`, `managedapps_list_share_links`, `managedapps_create_share_link`, `managedapps_delete_share_link`, `managedapps_list_role_assignments`, `managedapps_modify_role_assignments`, `managedapps_list_connectors`, `managedapps_get_connector`, `managedapps_list_connections`, `managedapps_start_github_auth`, and `managedapps_get_github_auth_status`.

`managedapps_upload_build` takes the zip as base64 in `zipContentBase64`.

### Example agent flows

- *"Which Copilot Managed Runtime apps can I edit in the Contoso environment?"* → `managedapps_list_environments` then `managedapps_list_apps` with `permission: "write"`
- *"Deploy the latest build of the expense tracker."* → `managedapps_list_apps`, `managedapps_build_app`, poll `managedapps_get_build_operation`, then `managedapps_deploy_app`
- *"Why did that build fail?"* → `managedapps_get_build_log`
- *"Who has access to this app?"* → `managedapps_list_role_assignments` and `managedapps_list_share_links`
- *"Connect my app to github.com/contoso/expenses."* → `managedapps_start_github_auth`, show the user the code and URI, then poll `managedapps_get_github_auth_status`
- *"Ship this build I already have."* → `managedapps_upload_build` with the zip, then `managedapps_deploy_app` with the returned `commitSha`

## Caveats

**This is preview, and explicitly volatile.** The `microsoft/managed-apps` README states that APIs, templates, and tooling may change before general availability. There is no versioned public contract for these routes yet, so treat this connector as tracking a moving target.

**Repository create and delete are excluded; GitHub authorization is not.** The CLI builds routes under `/appframework/git/repositories`, but that path returned `RouteNotFound` during testing while `/appframework/apps` did not. Those operations appear to be feature-gated or not uniformly deployed, so they are left out rather than shipped broken. The GitHub device code routes under `/appframework/github/auth` are a separate surface and are included.

**Response schemas are best-effort.** Because there is no published reference, the response shapes were reconstructed from the CLI's own mock fixtures and type usage. The script projects responses defensively, reading both flat and `properties`-nested shapes, so a server-side shape change degrades to null fields rather than a hard failure.

**`api-version=1` is pinned.** Deployment has a newer `manageddevops` backend behind a CLI feature gate; this connector uses the stable `appframework` path.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `pac connector create` fails with an unexplained error when `--script-file` is supplied | A new script-enabled connector could not be assigned a function app | Create without the script, then attach it with `pac connector update` |
| `InvalidScriptDefinitionUrlWithNonNullOperations` | `scriptOperations` is declared but no script was uploaded | Add `--script-file script.csx`, or remove `scriptOperations` for a script-free deploy |
| `401` on every call after deploying | The generated per-connector redirect URL is not registered on the app registration | Read it back with `pac connector download` and add it with `az ad app update --web-redirect-uris` |
| A connection cannot be created at all | `clientId` is still `[YOUR_CLIENT_ID]` | Set your real app registration ID and redeploy |
| `403 InsufficientDelegatedPermissions` | The app registration is missing `ManagedApps.ReadWrite.All` | Add the delegated permission from the Power Platform API and reconsent |
| `UploadBuild` rejects the artifact before sending | The zip is larger than the 100 MB connector payload limit, or the content is not a zip | Use `ms deploy --artifact` for large artifacts; confirm you are sending built output packaged as a zip |
| `UploadBuild` returns an invalid artifact error | The zip does not contain the built app output and its configuration file | Package the build output directory rather than the source tree |
| `UploadBuild` reports a repository type mismatch | The app's recorded repository type differs from what the service expects | Confirm the app's repository binding before retrying |
| `DeployApp` reports no successful build is available | No green build exists for the commit | Upload or push a commit and wait for its build to succeed |
| GitHub authorization never leaves `Pending` | The user has not completed sign-in at the verification URI | Resend the code and URI; the session ends after `expiresIn` seconds |

## Application Insights

Telemetry is off by default. To enable it, replace the placeholder in [script.csx](./script.csx):

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=<your-key>;...";
```

The connector logs MCP method calls, tool invocations, and tool errors. Telemetry failures are swallowed so they never affect the caller.

## Validation

```powershell
ppcv "./Managed Apps"
```

## References

- [What is Copilot Managed Runtime](https://learn.microsoft.com/microsoft-365/managed-apps/) &mdash; product overview and the three app creation entry points
- [Copilot Managed Runtime SDK overview](https://learn.microsoft.com/microsoft-365/managed-apps/developer/) &mdash; CLI and SDK prerequisites, repository options, and licensing
- [Copilot Managed Runtime overview for admins](https://learn.microsoft.com/microsoft-365/admin/manage/apps/) &mdash; governance, inventory, and billing
- [Announcing Copilot Managed Runtime](https://www.microsoft.com/en-us/copilot/blog/copilot-studio/build-where-you-want-run-with-confidence-now-microsoft-hosts-and-manages-the-code-created-by-copilot/) &mdash; launch post
- [microsoft/managed-apps](https://github.com/microsoft/managed-apps) &mdash; templates, issues, and the plugin for AI coding agents
- [`@microsoft/managed-apps-cli`](https://www.npmjs.com/package/@microsoft/managed-apps-cli) &mdash; the `ms` CLI this connector mirrors
- [Power Platform API reference](https://learn.microsoft.com/power-platform/admin/programmability-and-extensibility/powerplatform-api-reference)

## Author

Troy Taylor &mdash; [troy@troystaylor.com](mailto:troy@troystaylor.com) &mdash; [github.com/troystaylor](https://github.com/troystaylor)
