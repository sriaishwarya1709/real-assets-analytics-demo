targetScope = 'resourceGroup'

@description('Deploy into the resource group containing the existing Foundry account.')
param accountName string
param projectName string
@description('Each item requires label and serverUrl. URLs must target published Fabric data-agent MCP endpoints.')
param fabricAgents array

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: accountName
}
resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: account
  name: projectName
}
resource connections 'Microsoft.CognitiveServices/accounts/projects/connections@2025-10-01-preview' = [for agent in fabricAgents: {
  parent: project
  name: agent.label
  properties: {
    category: 'RemoteTool'
    // UserEntraToken is documented for Fabric IQ but absent from the published ARM type enum.
    #disable-next-line BCP036
    authType: 'UserEntraToken'
    target: agent.serverUrl
    audience: 'https://analysis.windows.net/powerbi/api'
    isSharedToAll: true
    metadata: {
      type: 'generic_mcp'
      audience: 'https://analysis.windows.net/powerbi/api'
    }
  }
}]

output connectionIds array = [for (agent, i) in fabricAgents: {
  label: agent.label
  connectionId: connections[i].id
}]
