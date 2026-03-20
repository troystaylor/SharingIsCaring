using './main.bicep'

param environmentName = readEnvironmentVariable('AZURE_ENV_NAME', 'dev')
param location = readEnvironmentVariable('AZURE_LOCATION', 'eastus')

// Salesforce credentials — sourced from azd env values
param sfInstanceUrl = readEnvironmentVariable('SF_INSTANCE_URL')
param sfClientId = readEnvironmentVariable('SF_CLIENT_ID')
param sfClientSecret = readEnvironmentVariable('SF_CLIENT_SECRET')
param sfRefreshToken = readEnvironmentVariable('SF_REFRESH_TOKEN')
param sfApiVersion = readEnvironmentVariable('SF_API_VERSION', 'v66.0')

// Graph / Entra credentials
param azureTenantId = readEnvironmentVariable('AZURE_TENANT_ID')
param azureClientId = readEnvironmentVariable('AZURE_CLIENT_ID')
param azureClientSecret = readEnvironmentVariable('AZURE_CLIENT_SECRET')

// Connector settings
param connectorId = readEnvironmentVariable('CONNECTOR_ID', 'salesforceconnector')
param connectorName = readEnvironmentVariable('CONNECTOR_NAME', 'Salesforce')
param connectorDescription = readEnvironmentVariable('CONNECTOR_DESCRIPTION', 'Salesforce CRM data for Microsoft 365 Copilot')
