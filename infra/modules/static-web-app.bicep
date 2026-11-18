// Static Web App do hemodinks-front.
// Recriar exige vincular o GitHub (token de repositório) pelo portal ou CLI; ver docs/infra-as-code.md.
param location string

resource frontend 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'hemodinks-saude'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    provider: 'GitHub'
    repositoryUrl: 'https://github.com/Hemodinks/hemodinks-front'
    branch: 'main'
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
    // A definição de tipo do Bicep não conhece a propriedade, mas o provedor a devolve e aceita;
    // sem ela o what-if acusa remoção.
    #disable-next-line BCP037
    deploymentAuthPolicy: 'DeploymentToken'
  }
}
