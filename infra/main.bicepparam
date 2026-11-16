using './main.bicep'

param namePrefix = readEnvironmentVariable('DEMO_NAME_PREFIX', 'portfolio-lens')
param location = readEnvironmentVariable('AZURE_LOCATION')
param appServiceLocation = readEnvironmentVariable('AZURE_APP_SERVICE_LOCATION', readEnvironmentVariable('AZURE_LOCATION'))
param tenantId = readEnvironmentVariable('AZURE_TENANT_ID')
param aadClientId = readEnvironmentVariable('AZURE_AD_CLIENT_ID')
param aadClientSecret = readEnvironmentVariable('AZURE_AD_CLIENT_SECRET')
param analyticsMode = readEnvironmentVariable('ANALYTICS_MODE', 'Sample')
param foundryProjectEndpoint = readEnvironmentVariable('FOUNDRY_PROJECT_ENDPOINT', '')
param agentName = readEnvironmentVariable('FOUNDRY_AGENT_NAME', 'real-assets-analyst')
param agentVersion = readEnvironmentVariable('FOUNDRY_AGENT_VERSION', '')
param backgroundQueries = bool(readEnvironmentVariable('FOUNDRY_BACKGROUND', 'false'))
param fabricAgents = json(readEnvironmentVariable('FABRIC_AGENTS_JSON', '[]'))
