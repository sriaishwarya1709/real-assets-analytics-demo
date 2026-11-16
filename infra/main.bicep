targetScope = 'resourceGroup'

@description('Short lowercase prefix. Resources incur App Service and Key Vault charges.')
@minLength(3)
@maxLength(16)
param namePrefix string = 'portfolio-lens'
param location string = resourceGroup().location
@description('Override the web hosting region when the primary region has no App Service capacity.')
param appServiceLocation string = location
@allowed(['Sample', 'Live'])
param analyticsMode string = 'Sample'
@description('Disclose illustrative data even when querying real Foundry/Fabric services.')
param syntheticData bool = false
param tenantId string
param aadClientId string
@secure()
param aadClientSecret string
param foundryProjectEndpoint string = ''
param agentName string = 'real-assets-analyst'
param agentVersion string = ''
@description('Enable only with a model that supports background responses. Stored Foundry responses are used when enabled.')
param backgroundQueries bool = false
@description('Live agent descriptors: label, name, description. Endpoint/connection values are used during agent provisioning, not web runtime.')
param fabricAgents array = []

var suffix = uniqueString(resourceGroup().id)
var webName = '${namePrefix}-${suffix}'
var vaultName = 'pl-${suffix}'
var keyVaultSecretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: '${namePrefix}-plan'
  location: appServiceLocation
  kind: 'linux'
  sku: {
    name: 'B1'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Disabled'
  }
}

resource clientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'web-entra-client-secret'
  properties: {
    value: aadClientSecret
  }
}

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: appServiceLocation
  properties: {
    addressSpace: {
      addressPrefixes: ['10.91.0.0/16']
    }
  }
}

resource integrationSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: network
  name: 'web-integration'
  properties: {
    addressPrefix: '10.91.1.0/24'
    delegations: [
      {
        name: 'appservice'
        properties: {
          serviceName: 'Microsoft.Web/serverFarms'
        }
      }
    ]
  }
}

resource endpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: network
  name: 'private-endpoints'
  properties: {
    addressPrefix: '10.91.2.0/24'
    privateEndpointNetworkPolicies: 'Disabled'
  }
  dependsOn: [integrationSubnet]
}

resource vaultDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
}

resource vaultDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: vaultDns
  name: '${namePrefix}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource vaultEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-vault-endpoint'
  location: appServiceLocation
  properties: {
    subnet: {
      id: endpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'vault'
        properties: {
          privateLinkServiceId: vault.id
          groupIds: ['vault']
        }
      }
    ]
  }
}

resource vaultDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: vaultEndpoint
  name: 'vault'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'vault'
        properties: {
          privateDnsZoneId: vaultDns.id
        }
      }
    ]
  }
}

resource web 'Microsoft.Web/sites@2024-11-01' = {
  name: webName
  location: appServiceLocation
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    virtualNetworkSubnetId: integrationSubnet.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      vnetRouteAllEnabled: true
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'AllowedHosts', value: '${webName}.azurewebsites.net' }
        { name: 'Authentication__Enabled', value: 'true' }
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: aadClientId }
        { name: 'AzureAd__ClientSecret', value: '@Microsoft.KeyVault(SecretUri=${clientSecret.properties.secretUriWithVersion})' }
        { name: 'Analytics__Mode', value: analyticsMode }
        { name: 'Analytics__SyntheticData', value: string(syntheticData) }
        { name: 'Analytics__ProjectEndpoint', value: foundryProjectEndpoint }
        { name: 'Analytics__AgentName', value: agentName }
        { name: 'Analytics__AgentVersion', value: agentVersion }
        { name: 'Analytics__Background', value: string(backgroundQueries) }
        { name: 'Analytics__FabricAgentsJson', value: string(fabricAgents) }
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
      ]
    }
  }
  dependsOn: [
    vaultDnsGroup
    vaultDnsLink
  ]
}

resource secretReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, web.id, keyVaultSecretsUserRole)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRole
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: web
  name: 'ftp'
  properties: {
    allow: false
  }
}

output webAppName string = web.name
output webUrl string = 'https://${web.properties.defaultHostName}'
output redirectUri string = 'https://${web.properties.defaultHostName}/signin-oidc'
output keyVaultName string = vault.name
