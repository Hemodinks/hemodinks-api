// Infraestrutura Azure da HemoDinks (issue #288).
// Escopo de assinatura: os recursos estão espalhados em quatro resource groups existentes.
// Este template descreve o estado atual; não cria recurso novo e não muda SKU/escala.
// Fora daqui (ver docs/infra-as-code.md): Container App da API, app settings/segredos,
// regras de IP das Functions (#224), regras de firewall de IP pessoal, Automation e alertas.
targetScope = 'subscription'

@description('Resource group principal (SQL, plano das Functions de produção, Static Web App).')
param mainResourceGroupName string = 'rg-hemodinks-prod'

@description('Resource group do ambiente do Container Apps.')
param containersResourceGroupName string = 'hemodinks-containers-prod'

@description('Resource group das Functions e do Storage de produção.')
param workersProductionResourceGroupName string = 'hemodinks-workers-production_group'

@description('Resource group das Functions de homologação.')
param workersConfirmationResourceGroupName string = 'hemodinks-workers-confirmation_group'

@description('Região dos recursos de dados e computação.')
param location string = 'brazilsouth'

@description('Região da Static Web App (o SKU Free não existe em brazilsouth).')
param staticWebAppLocation string = 'centralus'

@description('Workspace do Log Analytics usado pelo Application Insights das Functions.')
param logAnalyticsWorkspaceId string

@description('Nome do servidor Azure SQL. Vem de variável de ambiente; não versionar (repo público).')
param sqlServerName string

@description('Login SQL do administrador do servidor. Vem de variável de ambiente; não versionar.')
param sqlAdministratorLogin string

@description('Nome da storage account de produção. Vem de variável de ambiente; não versionar.')
param storageAccountName string

@description('Login do administrador Entra do SQL (e-mail). Vem de variável de ambiente; não versionar.')
param sqlEntraAdminLogin string

@description('Object ID do administrador Entra do SQL. Vem de variável de ambiente; não versionar.')
param sqlEntraAdminObjectId string

@secure()
@description('Senha do admin SQL. Só para recriação (DR); vazia em what-if e em servidor existente.')
param sqlAdministratorLoginPassword string = ''

resource mainRg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: mainResourceGroupName
}

resource containersRg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: containersResourceGroupName
}

resource workersProductionRg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: workersProductionResourceGroupName
}

resource workersConfirmationRg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: workersConfirmationResourceGroupName
}

module sql 'modules/sql.bicep' = {
  name: 'hemodinks-sql'
  scope: mainRg
  params: {
    location: location
    serverName: sqlServerName
    administratorLogin: sqlAdministratorLogin
    entraAdminLogin: sqlEntraAdminLogin
    entraAdminObjectId: sqlEntraAdminObjectId
    administratorLoginPassword: sqlAdministratorLoginPassword
  }
}

module containerEnvironment 'modules/container-environment.bicep' = {
  name: 'hemodinks-container-environment'
  scope: containersRg
  params: {
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'hemodinks-storage'
  scope: workersProductionRg
  params: {
    name: storageAccountName
    location: location
  }
}

module functionsProductionPlan 'modules/function-plan.bicep' = {
  name: 'hemodinks-functions-production-plan'
  scope: mainRg
  params: {
    name: 'ASP-hemodinksworkersproductiongroup-b18c'
    location: location
    tags: {
      'ambiente-producao': ''
    }
  }
}

module functionsProduction 'modules/function-app.bicep' = {
  name: 'hemodinks-functions-production'
  scope: workersProductionRg
  params: {
    name: 'hemodinks-workers-production'
    location: location
    planId: functionsProductionPlan.outputs.id
    // Literal (e não output do módulo storage): o what-if não resolve reference() e acusaria diferença.
    deploymentContainerUrl: 'https://${storageAccountName}.blob.${environment().suffixes.storage}/app-package-hemodinks-workers-production-1e6fe11'
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    environmentTagName: 'ambiente-producao'
    insightsTags: {
      'ambiente-producao': ''
    }
  }
}

module functionsConfirmationPlan 'modules/function-plan.bicep' = {
  name: 'hemodinks-functions-confirmation-plan'
  scope: workersConfirmationRg
  params: {
    name: 'ASP-hemodinksworkersconfirmationgro-a6fd'
    location: location
    tags: {}
  }
}

module functionsConfirmation 'modules/function-app.bicep' = {
  name: 'hemodinks-functions-confirmation'
  scope: workersConfirmationRg
  params: {
    name: 'hemodinks-workers-confirmation'
    location: location
    planId: functionsConfirmationPlan.outputs.id
    // Pacote de homologação fica numa conta fora destes resource groups (stgmtechsolution).
    deploymentContainerUrl: 'https://stgmtechsolution.blob.${environment().suffixes.storage}/app-package-hemodinks-workers-confirmation-97311c4'
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    environmentTagName: 'ambiente-homologacao'
    insightsTags: {}
  }
}

module frontend 'modules/static-web-app.bicep' = {
  name: 'hemodinks-frontend'
  scope: mainRg
  params: {
    location: staticWebAppLocation
  }
}
