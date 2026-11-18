// Azure SQL lógico compartilhado e os bancos da HemoDinks.
// O servidor também hospeda ESCOLA_API, de outro projeto: ele não é declarado aqui e o modo
// incremental o deixa intacto. Não trocar para modo Complete.
param location string
param serverName string
param administratorLogin string
param entraAdminLogin string
param entraAdminObjectId string

@secure()
param administratorLoginPassword string

var databases = [
  {
    name: 'HemodinksDB'
    capacity: 2
    // Pausa após 30 min sem uso (#209).
    autoPauseDelay: 30
  }
  {
    name: 'HemodinksDBConfirmation'
    capacity: 1
    autoPauseDelay: -1
  }
]

// Regras de rede de serviço. IPs de estação de trabalho (ClientIPAddress_*, QueryEditor*)
// são temporários e pessoais: não são versionados e o modo incremental não os remove.
var firewallRules = [
  {
    // 0.0.0.0 = "Permitir serviços do Azure" (Container Apps e Functions saem por IPs do Azure; #197).
    name: 'AllowAllWindowsAzureIps'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
  {
    name: 'Render'
    startIpAddress: '74.220.48.196'
    endIpAddress: '74.220.48.196'
  }
  {
    name: 'RenderVirginia'
    startIpAddress: '74.220.49.18'
    endIpAddress: '74.220.49.18'
  }
]

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  properties: {
    administratorLogin: administratorLogin
    // Só enviado quando informado (recriação). Servidor existente mantém a senha atual.
    administratorLoginPassword: empty(administratorLoginPassword) ? null : administratorLoginPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
    version: '12.0'
  }
}

resource entraAdmin 'Microsoft.Sql/servers/administrators@2023-08-01' = {
  parent: server
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: entraAdminLogin
    sid: entraAdminObjectId
    tenantId: tenant().tenantId
  }
}

resource rules 'Microsoft.Sql/servers/firewallRules@2023-08-01' = [
  for rule in firewallRules: {
    parent: server
    name: rule.name
    properties: {
      startIpAddress: rule.startIpAddress
      endIpAddress: rule.endIpAddress
    }
  }
]

resource db 'Microsoft.Sql/servers/databases@2023-08-01' = [
  for database in databases: {
    parent: server
    name: database.name
    location: location
    sku: {
      name: 'GP_S_Gen5'
      tier: 'GeneralPurpose'
      family: 'Gen5'
      capacity: database.capacity
    }
    properties: {
      autoPauseDelay: database.autoPauseDelay
      minCapacity: json('0.5')
      collation: 'SQL_Latin1_General_CP1_CI_AS'
      catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
      maxSizeBytes: 34359738368
      zoneRedundant: false
      readScale: 'Disabled'
      requestedBackupStorageRedundancy: 'Local'
      isLedgerOn: false
      useFreeLimit: true
      freeLimitExhaustionBehavior: 'BillOverUsage'
      maintenanceConfigurationId: subscriptionResourceId('Microsoft.Maintenance/publicMaintenanceConfigurations', 'SQL_Default')
    }
  }
]

// PITR de 7 dias (#198). Aumentar a retenção muda custo: só com what-if revisado.
resource shortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01' = [
  for (database, i) in databases: {
    parent: db[i]
    name: 'default'
    properties: {
      retentionDays: 7
      diffBackupIntervalInHours: 12
    }
  }
]
