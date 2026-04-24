using 'main.bicep'

// ============================================================================
// Parameter values for deployment
// Fill in your values below, or pass them via CLI:
//   az deployment group create --template-file infra/main.bicep --parameters infra/main.bicepparam
// ============================================================================

param baseName = 'snhandoff'
param location = 'eastus'
param appServicePlanSku = 'B1'

// Required — Azure Bot app registration
param botAppId = '' // TODO: Set your Azure Bot App Registration Client ID
param botAppSecret = '' // TODO: Set your Azure Bot App Registration Client Secret

// Optional — can be set after deployment via App Settings
param copilotStudioDirectConnectUrl = ''
param serviceNowInstanceUrl = ''
param serviceNowClientId = ''
param serviceNowClientSecret = ''
param serviceNowWebhookSecret = ''
