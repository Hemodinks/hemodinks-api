// Storage de produção: fotos de perfil, arquivos de pacientes, exports e pacote das Functions.
// Também guarda os containers *-confirmation da homologação.
param name string
param location string

var containers = [
  'app-package-hemodinks-workers-production-1e6fe11'
  'azure-webjobs-hosts'
  'azure-webjobs-secrets'
  'exports'
  'patient-files'
  'profile-photos'
  // Homologacao (Render) usa a mesma conta, com containers proprios (render.confirmation.yaml).
  'patient-files-confirmation'
  'profile-photos-confirmation'
]

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: {
    'ambiente-producao': ''
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
        file: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Allow'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 30
      allowPermanentDelete: false
    }
    cors: {
      corsRules: []
    }
  }
}

// Filas dos gatilhos das Functions (GenerateFileExport, SendPasswordResetEmail). Precisam existir mesmo com
// AsyncQueues desligado na API: sem elas o controlador de escala do Flex Consumption registra
// "Error querying for queue scale status" a cada poucos minutos. Nomes iguais a AsyncQueueNames.
var queues = [
  'file-export-jobs'
  'password-reset-emails'
]

// Serviço de fila existente: só as filas são declaradas, sem mudar a configuração do serviço.
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' existing = {
  parent: account
  name: 'default'
}

resource storageQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [
  for name in queues: {
    parent: queueService
    name: name
    properties: {
      metadata: {}
    }
  }
]

resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for name in containers: {
    parent: blob
    name: name
    properties: {
      publicAccess: 'None'
      defaultEncryptionScope: '$account-encryption-key'
      denyEncryptionScopeOverride: false
    }
  }
]
