// Parâmetros de produção. Nada sensível aqui: nomes de recursos expostos e dados pessoais vêm de variáveis de ambiente
// (GitHub Environment "infra-what-if" no CI; shell local no runbook). Ver docs/infra-as-code.md.
using 'main.bicep'

param logAnalyticsWorkspaceId = '/subscriptions/${readEnvironmentVariable('AZURE_SUBSCRIPTION_ID')}/resourceGroups/DefaultResourceGroup-CQ/providers/Microsoft.OperationalInsights/workspaces/DefaultWorkspace-${readEnvironmentVariable('AZURE_SUBSCRIPTION_ID')}-CQ'
param sqlServerName = readEnvironmentVariable('HEMODINKS_SQL_SERVER_NAME')
param sqlAdministratorLogin = readEnvironmentVariable('HEMODINKS_SQL_ADMIN_LOGIN')
param storageAccountName = readEnvironmentVariable('HEMODINKS_STORAGE_ACCOUNT_NAME')
param sqlEntraAdminLogin = readEnvironmentVariable('HEMODINKS_SQL_ENTRA_ADMIN_LOGIN')
param sqlEntraAdminObjectId = readEnvironmentVariable('HEMODINKS_SQL_ENTRA_ADMIN_OBJECT_ID')
param sqlAdministratorLoginPassword = readEnvironmentVariable('HEMODINKS_SQL_ADMIN_PASSWORD', '')
