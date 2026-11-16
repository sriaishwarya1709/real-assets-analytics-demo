targetScope = 'resourceGroup'

@description('Deploy this optional template only when a suitable Foundry project does not already exist.')
param location string = resourceGroup().location
param accountName string
param projectName string = 'real-assets'
@description('An available model name/version/deployment SKU must be chosen for the target region and quota.')
param modelName string
param modelVersion string
param modelDeploymentName string
param modelSku string = 'GlobalStandard'
@minValue(1)
param modelCapacity int = 10
@description('Object IDs of users or groups needing Foundry User access. No role grants are made to unspecified identities.')
param foundryUserPrincipalIds array = []

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Real Assets Intelligence'
    description: 'Prompt-agent analytics over governed Fabric data agents.'
  }
}

resource model 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: modelDeploymentName
  sku: {
    name: modelSku
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

var userRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
resource users 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in foundryUserPrincipalIds: {
  name: guid(project.id, principalId, userRole)
  scope: project
  properties: {
    roleDefinitionId: userRole
    principalId: principalId
  }
}]
resource runtimeUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(project.id, 'runtime-user', userRole)
  scope: project
  properties: {
    roleDefinitionId: userRole
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'
output projectResourceId string = project.id
output modelDeployment string = model.name
