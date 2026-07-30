# Runbook: API Production Blue/Green

Este runbook cobre somente `hemodinks-api-prod`, no Resource Group
`rg-hemodinks-prod`. Workers e homologacao nao usam Blue/Green.

## Modelo operacional

O workflow valida, testa, audita migrations, publica imagens imutaveis
`sha-<commit>`, gera um EF migration bundle e SQL idempotente, aplica a migration
uma vez e somente entao cria uma revisao candidata com 0% de trafego. A URL do
label da candidata passa por warm-up, liveness e readiness (incluindo acesso ao
banco e ausencia de migrations pendentes). Apenas depois disso o trafego muda de
100/0 para 0/100.

A revisao anterior continua ativa, com label e 0% de trafego. Nenhuma revisao e
excluida ou desativada pelo workflow. Os limites de replicas nao sao alterados.
Quando `minReplicas=0`, a primeira chamada ao label pode sofrer cold start; os
retries do warm-up absorvem esse custo antes da troca.

## GitHub Environment

Crie/proteja o Environment `production` e configure:

Secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_SQL_CONNECTION_STRING`

Variables preservadas para os workers:

- `AZURE_CONTAINER_APPS_DEPLOY_ENABLED=true`
- `AZURE_CONTAINER_APP_FUNCTIONS_NAME`
- `AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP` ou `AZURE_RESOURCE_GROUP`
- `AZURE_FUNCTION_APP_WORKERS_NAME`, quando o Function App for usado

Configure required reviewers e habilite "Prevent self-review". O Environment
libera seus secrets apenas depois da aprovacao.

Ha duas barreiras deliberadas para a API:

1. `migrate-production`: aprovacao antes da primeira alteracao em Producao;
2. `deploy-api`: nova aprovacao antes de criar a candidata e trocar o trafego.

Cada job que referencia um Environment cria seu proprio deployment e pode exigir
aprovacao. A segunda aprovacao evita que uma migration bem-sucedida autorize
implicitamente uma troca de trafego feita muito tempo depois. Jobs de workers
continuam usando `production` e podem gerar aprovacoes adicionais conforme a
regra configurada.

O workflow nao recebe nem regrava o segredo do New Relic: a candidata copia a
configuracao existente, incluindo `secretref`, da revisao atual. Rotacione esse
segredo por um procedimento operacional separado, sem coloca-lo em argumentos.

O identity OIDC precisa, no minimo, ler o Resource Group e atualizar o Container
App. Restrinja o RBAC ao menor escopo possivel. O runner que aplica migrations
precisa de conectividade de rede com o Azure SQL.

## Bootstrap unico

O bootstrap:

- valida subscription, Resource Group e nome exatos antes de alterar;
- muda para multiple revisions;
- reforca `Database__RunMigrationsOnStartup=false`;
- configura inicialmente probes HTTP compatíveis em `/healthz`, usando o target
  port atual; no primeiro rollout, o pipeline substitui startup/liveness por
  `/livez` e mantém readiness em `/healthz`;
- preserva imagem, env vars/secretrefs e recursos do container;
- nao envia a configuracao de escala;
- espera a nova revisao ficar `Healthy`/`Running`, aplica `blue` e direciona 100%.

Revise primeiro com `-WhatIf`, depois execute manualmente:

```powershell
az login
az account set --subscription "<subscription-id>"
./scripts/Bootstrap-ProductionBlueGreen.ps1 `
  -SubscriptionId "<subscription-id>" `
  -WhatIf
./scripts/Bootstrap-ProductionBlueGreen.ps1 `
  -SubscriptionId "<subscription-id>"
```

Nao execute o bootstrap pelo GitHub Actions e nao o use em homologacao.

## Deployment normal

Em Actions, abra **Publish Containers**, escolha **Run workflow** na ref desejada
e marque `deploy_to_azure=true`.

Ordem esperada:

1. `validate`;
2. `publish` e `prepare-migrations`;
3. aprovacao de `migrate-production`;
4. execucao unica do bundle;
5. aprovacao de `deploy-api`;
6. candidata a 0%, warm-up/smoke e troca atomica;
7. workers depois da migration.

O SQL idempotente e o bundle ficam no artifact
`production-migrations-<commit>` por 30 dias. O Job Summary registra commit,
imagem, revisoes, label, migration, smoke test e trafego final.

O workflow legado **Apply Production Migrations** permanece apenas para uma
aplicacao manual excepcional: ele agora executa restore/build/test/auditoria,
gera o mesmo bundle/SQL, usa o mesmo lock e so avanca para a ultima migration.
Ele nao publica a API; prefira sempre o fluxo completo **Publish Containers**.

Se build, testes, auditoria, bundle, migration, provisionamento ou smoke falhar,
o step de troca nao e executado e o trafego permanece na revisao anterior. Uma
migration concluida nao e revertida automaticamente quando o deploy posterior
falha.

## Migrations expand/contract

Como as duas revisoes compartilham o Azure SQL, toda mudanca deve ser compativel:

1. **expand**: adicionar tabelas/colunas/indices nullable ou com defaults seguros;
2. publicar codigo que funcione com schema antigo e novo;
3. migrar/backfill de dados de forma repetivel e observavel;
4. somente em deployment posterior, apos nenhuma revisao depender da estrutura,
   fazer o **contract**.

Nao misture remocao/rename obrigatorio com o deploy que introduz o substituto.
`Test-Migrations.ps1` sinaliza `DropTable`, `DropColumn`, `DeleteData`,
`DROP`, `TRUNCATE` e `DELETE FROM` no `Up()`. A opcao
`-FailOnDestructiveChanges` existe para revisoes direcionadas; migrations
historicas destrutivas ja presentes exigem revisao humana, por isso o pipeline
padrao sinaliza sem bloquear toda a cadeia historica.

Rollback de trafego nao desfaz schema nem dados. Para uma migration incompatível,
pare o rollout, mantenha a revisao compatível ativa e execute um plano revisado
de roll-forward. PITR/restore deve ser o ultimo recurso, coordenado fora deste
workflow.

## Rollback

Em Actions, abra **Roll back Production API**:

1. informe `ROLLBACK-PRODUCTION`;
2. deixe `target_revision` vazio para usar a revisao pronta anterior mais recente,
   ou copie exatamente uma revisao listada no Azure;
3. aprove o Environment `production`.

O workflow valida o alvo, move o label inativo para ele, testa `/livez` e
`/healthz` pela URL do label e somente depois troca o trafego. Ele nao executa ou
desfaz migrations e nao exclui revisoes.

## Diagnostico

Smoke test:

- confirme que a revisao esta `Healthy`, `Running` e `Provisioned`;
- teste `https://<app>---<label>.<default-domain>/livez`;
- teste `/healthz`; HTTP 503 indica banco inacessivel ou migration pendente;
- confira logs da revisao especifica e New Relic sem copiar payloads/secrets;
- confirme DNS/ingress e se um runner externo consegue acessar o Environment.

Labels/revisoes:

```bash
az containerapp show -g rg-hemodinks-prod -n hemodinks-api-prod \
  --query properties.configuration.ingress.traffic -o table
az containerapp revision list -g rg-hemodinks-prod -n hemodinks-api-prod \
  --all -o table
az containerapp revision label add -g rg-hemodinks-prod \
  -n hemodinks-api-prod --revision "<ready-revision>" --label blue --yes
az containerapp ingress traffic set -g rg-hemodinks-prod \
  -n hemodinks-api-prod --label-weight blue=100 green=0
```

Antes dos dois ultimos comandos, valide nome, RG, revision state, imagem e commit.
Use-os somente para recuperacao consciente. Nunca desative/exclua a revisao
anterior como parte de um rollout.

## Azure SQL privado: Container Apps Job

Nao abra firewall amplo para runners GitHub-hosted. Para banco privado, crie
previamente um Container Apps Job no mesmo Environment/VNet da API:

- trigger manual e `parallelism=1`, `replicaCompletionCount=1`;
- identidade gerenciada com permissao minima no SQL, preferencialmente com
  autenticacao Entra ID;
- bundle incorporado em uma imagem imutavel `sha-<commit>` separada, ou volume
  privado verificado;
- connection string como secretref/Key Vault, nunca argumento de processo;
- timeout e retry limitados para evitar duas execucoes concorrentes;
- logs sem connection string.

Substitua o step local do bundle por `az containerapp job start` e aguarde a
execution chegar a `Succeeded` antes de liberar `deploy-api`. Mantenha o mesmo
grupo de concurrency e Environment `production`. A configuracao exata depende
do Container Apps Environment, private DNS, identidade SQL e registry existentes;
ela nao e criada automaticamente por este repositorio.

Para smoke em ingress privado, use runner self-hosted dentro da VNet ou um Job
dedicado de smoke no mesmo Environment; nao exponha a API apenas para o deploy.
