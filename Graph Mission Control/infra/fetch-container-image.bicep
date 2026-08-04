@description('Whether the container app has been deployed before. When false nothing is read and the caller falls back to its placeholder.')
param exists bool

@description('Name of the container app to read the running image from.')
param name string

// This lives in its own module on purpose. Reading the app inside the same template that
// deploys it makes ARM treat the resource as depending on itself, and the deployment fails
// validation with a circular dependency. A nested deployment resolves against existing
// state before the outer template runs, which breaks the cycle.
resource deployed 'Microsoft.App/containerApps@2024-03-01' existing = if (exists) {
  name: name
}

output containers array = exists ? deployed!.properties.template.containers : []
