// Function App (Flex Consumption, .NET isolated) e seu Application Insights.
// Não declarado de propósito:
// - app settings: contêm segredos e connection strings; ficam no portal/Key Vault (ver docs/infra-as-code.md);
// - config/web (ipSecurityRestrictions): dono é o functions-network-restriction.yml (#224).
param name string
param location string
param planId string
param deploymentContainerUrl string
param logAnalyticsWorkspaceId string
param environmentTagName string
param insightsTags object

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  tags: insightsTags
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Redfield'
    Request_Source: 'IbizaWebAppExtensionCreate'
    IngestionMode: 'LogAnalytics'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    RetentionInDays: 90
    DisableLocalAuth: false
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  kind: 'functionapp,linux'
  tags: {
    '${environmentTagName}': environmentTagName
    'hidden-link: /app-insights-resource-id': insights.id
  }
  properties: {
    serverFarmId: planId
    reserved: true
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    clientAffinityEnabled: false
    clientCertEnabled: false
    clientCertMode: 'Required'
    keyVaultReferenceIdentity: 'SystemAssigned'
    // Sem appSettings: omitir a chave não altera as configurações existentes.
    siteConfig: {
      alwaysOn: false
      http20Enabled: false
      functionAppScaleLimit: 100
      minimumElasticInstanceCount: 0
      numberOfWorkers: 1
      acrUseManagedIdentityCreds: false
      netFrameworkVersion: 'v4.0'
      localMySqlEnabled: false
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: deploymentContainerUrl
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: 2048
        maximumInstanceCount: 100
      }
    }
  }
}
