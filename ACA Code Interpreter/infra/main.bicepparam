using 'main.bicep'

// Replace with your user/SP object ID (az ad signed-in-user show --query id -o tsv)
param principalId = ''

// Customize these as needed
param location = 'westus2'
param resourceGroupName = 'rg-aca-code-interpreter'
param sandboxGroupName = 'code-interpreter'
param principalType = 'User'
param enableSystemIdentity = true
