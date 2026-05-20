// =============================================================================
// Entra app registration outputs (manual provisioning required)
//
// Microsoft Graph Bicep types (microsoftGraphV1) for `applications` are still
// rolling out and require the Graph Bicep extension to be enabled on the
// subscription. To keep `azd up` reliable everywhere, this module does NOT
// create the Entra app registration. Instead it emits a deterministic
// identifierUri the rest of the deployment uses, plus a step-by-step
// instructions string. Provision the app reg out-of-band (Portal, az CLI, or
// Graph) and configure APIM's expected audience to match `identifierUri`.
//
// Optional: replace this module body with a real `microsoftGraphV1/applications`
// resource once Graph Bicep is GA in your tenant.
// =============================================================================

@description('Logical environment name (e.g. dev, prod) used by azd.')
param environmentName string

@description('Deterministic token derived from subscription + environment used to keep names unique and stable across deployments.')
param resourceToken string

// The expected audience APIM will validate against. APIM JWT policy compares
// the `aud` claim of inbound tokens to this URI. Provision the Entra app
// registration with this same identifierUri and an exposed scope.
var identifierUriValue = 'api://slack-cowork-mcp-${resourceToken}'

var appDisplayName = 'Slack Cowork MCP (${environmentName})'

var instructionsText = '''
Entra app registration steps (one-time, out-of-band):

1. Create an app registration:
     az ad app create --display-name "${appDisplayName}" --sign-in-audience AzureADMyOrg

2. Set the identifier URI (must match `identifierUri` exactly so APIM JWT validation passes):
     az ad app update --id <appId> --identifier-uris "${identifierUriValue}"

3. Expose an API scope (e.g. `access_as_user`):
     az ad app update --id <appId> --set api.oauth2PermissionScopes='[{
       "id":"<new-guid>",
       "adminConsentDescription":"Allow Copilot to call the Slack Cowork MCP server on behalf of the signed-in user.",
       "adminConsentDisplayName":"Access Slack Cowork MCP",
       "isEnabled":true,
       "type":"User",
       "userConsentDescription":"Allow Copilot to call the Slack Cowork MCP server on your behalf.",
       "userConsentDisplayName":"Access Slack Cowork MCP",
       "value":"access_as_user"
     }]'

4. Grant the Microsoft 365 Copilot service principal access to the exposed scope (or use admin consent in the portal).

5. Use the identifierUri "${identifierUriValue}" in the Teams Developer Portal SSO registration audience for the custom federated connector flow.

The Cowork-plugin route (/mcp/full) does NOT require this app reg — it authenticates via the user's Slack bearer token forwarded by Cowork from the Enterprise Token Store. This app reg is required only when:
  - Fronting /mcp/federated with APIM JWT validation, or
  - Using Microsoft Entra SSO for the federated connector instead of an OAuth 2.0 client registration.
'''

@description('Stable identifier URI used as the JWT audience by APIM. Provision the real Entra app registration with this exact identifierUri.')
output identifierUri string = identifierUriValue

@description('Suggested display name for the Entra app registration.')
output displayName string = appDisplayName

@description('Human-readable provisioning instructions.')
output instructions string = instructionsText
