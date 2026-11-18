// Ambiente do Azure Container Apps (perfil Consumption).
// O Container App da API (hemodinks-api-prod) não é declarado: imagem, revisões, tráfego blue/green
// e variáveis são estado de deploy do publish-container.yml. Ver docs/infra-as-code.md.
param location string

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: 'managedEnvironment-hemodinkscontai-b8db'
  location: location
  tags: {
    'ambiente-producao': 'ambiente-producao'
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    zoneRedundant: false
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    peerAuthentication: {
      mtls: {
        enabled: false
      }
    }
    peerTrafficConfiguration: {
      encryption: {
        enabled: false
      }
    }
  }
}

output id string = environment.id
