targetScope = 'resourceGroup'

param accountName string
param projectName string = 'real-assets'
param location string = resourceGroup().location
@description('Object ID of the developer/test user who will query this demo.')
param userPrincipalId string

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: accountName
}
resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Real Assets Demo'
    description: 'Portfolio Lens end-to-end synthetic real estate analytics'
  }
}
var userRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
resource developer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(project.id, userPrincipalId, userRole)
  scope: project
  properties: {
    roleDefinitionId: userRole
    principalId: userPrincipalId
    principalType: 'User'
  }
}
resource runtime 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(project.id, 'runtime-user', userRole)
  scope: project
  properties: {
    roleDefinitionId: userRole
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
output endpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'
output projectId string = project.id
