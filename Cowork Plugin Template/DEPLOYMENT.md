# Deployment Guide

End-to-end guide for taking a Cowork plugin from local development to
production deployment.

## Lifecycle Overview

```
Development → Validation → Sideload Testing → Partner Center → App Store → Admin Deployment
                                                                              ↓
                                                                         User Sync
```

## 1. Development

### Local workflow

1. Edit skills, manifest, and server code
2. Run `.\package.ps1 -SkipIcons` to validate locally
3. Fix any errors before committing

### Version management

The `version` field in `manifest.json` follows semantic versioning:

```json
"version": "1.0.0"
```

| Change type | Version bump | Example |
|-------------|-------------|---------|
| Bug fix or typo in a skill | Patch | 1.0.0 → 1.0.1 |
| New skill added, existing skills unchanged | Minor | 1.0.0 → 1.1.0 |
| Breaking change (renamed skills, changed tool schemas) | Major | 1.0.0 → 2.0.0 |

**Keep the `id` (GUID) stable across versions.** Changing the GUID creates a
new plugin instead of updating the existing one. Users would need to acquire it
again and lose their preferences.

### Environment-specific connector URLs

For plugins with connectors, use different MCP server URLs per environment:

| Environment | MCP server URL | Purpose |
|-------------|---------------|---------|
| Dev | `https://mcp-dev.{{company}}.com/mcp` | Developer testing with test data |
| Staging | `https://mcp-staging.{{company}}.com/mcp` | Pre-production validation with production-like data |
| Production | `https://mcp.{{company}}.com/mcp` | Live deployment |

Create separate `manifest.json` files or use a build script to inject the
correct URL at packaging time:

```powershell
# Example: inject production URL at build time
$manifest = Get-Content manifest.json -Raw | ConvertFrom-Json
$manifest.agentConnectors[0].toolSource.remoteMcpServer.mcpServerUrl = "https://mcp.zava.com/mcp"
$manifest | ConvertTo-Json -Depth 10 | Set-Content manifest.json
.\package.ps1
```

## 2. Automated Validation (CI/CD)

The included GitHub Actions workflow (`.github/workflows/validate-plugin.yml`)
runs on every push and PR that touches plugin files:

- **Structure validation** — runs `package.ps1` and fails the build on errors
- **Placeholder detection** — warns if `{{ }}` placeholders remain in files
- **Skill size check** — warns if any SKILL.md exceeds 3,000 words
- **Artifact upload** — packages the .zip on main branch merges

### Extending the pipeline

Add these steps for connector plugins:

```yaml
# Smoke test your MCP server
- name: MCP server health check
  shell: pwsh
  run: |
    $body = @{
      jsonrpc = "2.0"; id = 1; method = "initialize"
      params = @{ protocolVersion = "2025-03-26"; capabilities = @{}; clientInfo = @{ name = "ci"; version = "1.0" } }
    } | ConvertTo-Json -Depth 5
    $response = Invoke-RestMethod -Uri "$env:MCP_SERVER_URL" -Method Post -Body $body -ContentType "application/json"
    if (-not $response.result.serverInfo) {
      Write-Error "MCP server did not return valid initialize response"
      exit 1
    }
    Write-Host "MCP server OK: $($response.result.serverInfo.name) v$($response.result.serverInfo.version)"
```

## 3. Sideload Testing

Before submitting to the App Store, test in your own tenant:

1. Run `.\package.ps1` to generate the .zip (requires icons)
2. Go to **M365 Admin Center** → **Manage Apps** → **Upload custom app**
3. Upload the .zip
4. Open **Cowork** → **Sources & Skills**
5. Verify all skills appear and activate on the correct trigger phrases
6. Test the connector sign-in flow (if applicable)
7. Test each skill with realistic prompts

### Test checklist

- [ ] All skills appear in Sources & Skills panel
- [ ] Skills activate on expected trigger phrases
- [ ] Skills do NOT activate on unrelated prompts
- [ ] Connector sign-in flow completes successfully
- [ ] Tool calls return data scoped to the signed-in user
- [ ] Tool calls complete within 30 seconds
- [ ] Error messages are user-friendly (not raw API errors)
- [ ] Auth-expired scenario shows sign-in prompt (not a crash)
- [ ] Disable/enable toggle works for each skill
- [ ] Skills work in a fresh conversation after plugin was disabled and re-enabled

### Updating a sideloaded plugin

Upload a new .zip with the same `id` (GUID) and an incremented `version`.
The existing sideloaded app is replaced. Active conversations using the old
version aren't interrupted — new conversations use the updated version.

## 4. Partner Center Submission

For App Store distribution:

1. Go to [Partner Center](https://partner.microsoft.com/)
2. Create a new Microsoft 365 app listing
3. Upload your .zip package
4. Provide marketing metadata (description, screenshots, categories)
5. **Register authentication credentials** (if using connectors):
   - OAuth: Provide client ID, client secret, authorization URL, token URL, scopes
   - API Key: Provide the key collection mechanism details
   - Partner Center stores these in the Enterprise Token Store and generates
     the `referenceId` that goes in your manifest
6. Submit for validation

### Validation review

Partner Center runs the same [validation rules](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development#validation-rules)
your `package.ps1` checks, plus additional compliance and security review.
Passing `package.ps1` locally does not guarantee store approval — the store
review also covers:

- Security and privacy compliance
- Content policy adherence
- App icon and branding guidelines
- Publisher verification

## 5. Admin Deployment

After store approval, tenant admins deploy your plugin:

1. **M365 Admin Center** → **Copilot** → **Agents** → **All agents**
2. Find your plugin by name
3. Choose deployment scope:
   - **Entire organization** — all licensed Copilot users get it automatically
   - **Specific users/groups** — targeted deployment via security groups

### Deployment behavior

| Aspect | Behavior |
|--------|----------|
| Acquisition | Automatic for target users — no user action needed |
| Removal | Users can't remove admin-deployed plugins |
| Enable/disable | Users can toggle plugins on/off for their own sessions |
| Auth | Each user completes their own sign-in (admin can't sign in on behalf) |
| Label | Shows "Managed by your organization" in plugin details |

## 6. Monitoring in Production

### Microsoft Purview audit logs

Cowork interactions are captured in Microsoft Purview under **Copilot activities**.
Audit Standard provides these at no extra cost. Use them to monitor:

- Which skills are being activated
- Which connectors are being called
- Which users are using the plugin
- Error rates and failure patterns

### MCP server monitoring

For connector plugins, monitor your MCP server independently:

- **Response times** — track p50, p95, p99 against the 30-second limit
- **Error rates** — 4xx vs 5xx, authentication failures
- **Tool call distribution** — which tools are called most often
- **Cold start frequency** — if using serverless hosting

Use Application Insights, Datadog, or your preferred observability platform.

## 7. Updates and Rollback

### Pushing updates

1. Increment `version` in `manifest.json`
2. Package and submit the new .zip through Partner Center
3. After approval, the update propagates on the next sync cycle
4. Active conversations aren't interrupted — new conversations use the updated version

### Rollback

If you need to pull a plugin:

- **Sideloaded:** Delete the app from M365 Admin Center → Manage Apps
- **Store-published:** Submit a removal request through Partner Center, or have
  the tenant admin block the plugin (Agents → All agents → set to Blocked)

When a plugin is removed or blocked:
- Skills and connectors from that package are removed on the next sync cycle
- Active conversations continue working until they end
- New conversations don't have the plugin's capabilities

### Breaking changes

Avoid changing tool names or removing tools between versions — this breaks
any skills (yours or third-party) that reference those tools by name. If you
must change tool names:

1. Add the new tool alongside the old one (minor version)
2. Update skills to reference the new tool name
3. Remove the old tool in a future major version
4. Communicate the deprecation timeline to users
