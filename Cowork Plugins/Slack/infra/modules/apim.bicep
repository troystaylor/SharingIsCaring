// APIM in front of the federated MCP route.
//
// Design decision: APIM JWT validation is enforced only on the federated route
// (used by the M365 admin center as a custom federated connector) because
// that channel is end-user-driven and expects Entra-issued bearer tokens
// scoped to our API app registration.
//
// The /mcp/full route is consumed directly by Cowork using its own bearer
// (the Slack user token via the Enterprise Token Store). APIM should not try
// to validate the Slack token — it just passes through. We model this with
// two operations:
//
//   POST /mcp/full       — set-variable / pass-through (no JWT validation)
//   POST /mcp/federated  — validate-jwt against expectedAudience
//
// Throttling is applied globally on the API to protect Slack from accidental
// fan-out.

param location string
param resourceToken string
param tags object

@allowed([
  'Developer'
  'Basic'
  'Standard'
  'Premium'
])
param sku string = 'Developer'

param publisherEmail string
param publisherName string
param backendUrl string
@description('Expected audience for federated route JWT validation (the API app registration identifier URI).')
param expectedAudience string
param tenantId string

resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: 'apim-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: sku
    capacity: 1
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    virtualNetworkType: 'None'
  }
  identity: { type: 'SystemAssigned' }
}

resource backend 'Microsoft.ApiManagement/service/backends@2023-05-01-preview' = {
  parent: apim
  name: 'mcp-backend'
  properties: {
    url: backendUrl
    protocol: 'http'
  }
}

resource api 'Microsoft.ApiManagement/service/apis@2023-05-01-preview' = {
  parent: apim
  name: 'slack-cowork-mcp'
  properties: {
    displayName: 'Slack Cowork MCP'
    path: ''
    protocols: [ 'https' ]
    serviceUrl: backendUrl
    subscriptionRequired: false
  }
}

// Global API-level policy: rate limit per IP, set backend.
resource apiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-05-01-preview' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <set-backend-service backend-id="mcp-backend" />
    <rate-limit-by-key calls="120" renewal-period="60" counter-key="@(context.Request.IpAddress)" />
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

// /mcp/full — pass-through. Cowork supplies its own bearer (Slack user token).
resource opFull 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = {
  parent: api
  name: 'post-mcp-full'
  properties: {
    displayName: 'POST /mcp/full'
    method: 'POST'
    urlTemplate: '/mcp/full'
  }
}

resource opFullPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2023-05-01-preview' = {
  parent: opFull
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <!-- Cowork bearer (xoxp-*) is forwarded as-is; no JWT validation here. -->
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

// /mcp/federated — enforce Entra JWT validation for the M365 federated route.
resource opFederated 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = {
  parent: api
  name: 'post-mcp-federated'
  properties: {
    displayName: 'POST /mcp/federated'
    method: 'POST'
    urlTemplate: '/mcp/federated'
  }
}

resource opFederatedPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2023-05-01-preview' = {
  parent: opFederated
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: format('''
<policies>
  <inbound>
    <base />
    <validate-jwt header-name="Authorization" failed-validation-httpcode="401" failed-validation-error-message="Unauthorized" require-expiration-time="true" require-signed-tokens="true">
      <openid-config url="{0}{1}/v2.0/.well-known/openid-configuration" />
      <audiences>
        <audience>{2}</audience>
      </audiences>
    </validate-jwt>
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
''', environment().authentication.loginEndpoint, tenantId, expectedAudience)
  }
}

// Health probes pass through
resource opHealthLive 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = {
  parent: api
  name: 'get-health-live'
  properties: {
    displayName: 'GET /health/live'
    method: 'GET'
    urlTemplate: '/health/live'
  }
}
resource opHealthReady 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = {
  parent: api
  name: 'get-health-ready'
  properties: {
    displayName: 'GET /health/ready'
    method: 'GET'
    urlTemplate: '/health/ready'
  }
}

output apimName string = apim.name
output gatewayUrl string = apim.properties.gatewayUrl
