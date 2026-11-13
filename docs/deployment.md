# Deployment

Producao em Azure Container Apps (API blue/green + workers), homologacao no Render, imagens no GHCR e pipelines no GitHub Actions.

## URLs

Local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` (`/readyz`; liveness em `/livez`) |
| Swagger | `http://localhost:5000/swagger` |
| Scalar | `http://localhost:5000/scalar` |
| OpenAPI | `http://localhost:5000/openapi/v1.json` |

Publicado:

| Recurso | URL |
| --- | --- |
| Front producao | `https://hemodinks.gestao-saude.tec.br` |
| Front homologacao (Vercel, branch `developer`) | `https://hemodinks-homologacao.gestao-saude.tec.br` |
| API producao | `https://<api-publica>` (Container App `hemodinks-api-prod`) |
| API homologacao | `https://hemodinks-api-1-90nb.onrender.com` |
| Swagger | `https://<api-publica>/swagger` quando `ApiDocumentation__Enabled=true` |
| Scalar | `https://<api-publica>/scalar` quando `ApiDocumentation__Enabled=true` |
| OpenAPI | `https://<api-publica>/openapi/v1.json` quando `ApiDocumentation__Enabled=true` |

## GitHub Actions

Workflows do `hemodinks-api`:

Cada SHA da `main` e testado uma vez, pelo job `validate` do Publish Production; o CI nao roda no push da `main`
(so no PR para ela). O restore usa `packages.lock.json` em locked mode e cache de NuGet: ao incluir ou atualizar pacote,
rode `dotnet restore ./HemodinksAPI.slnx` e commite os `packages.lock.json` (o `Hemodinks.AppHost` nao usa lock file,
porque o Aspire resolve pacotes por sistema operacional) (#221).

Actions externas ficam pinadas por SHA completo com a versao em comentario (`uses: owner/action@<sha> # vX.Y.Z`),
uma versao por Action; `scripts/tests/test_actions_pinned.py` bloqueia tag, branch ou SHA curto. O Dependabot
(`.github/dependabot.yml`) abre um PR semanal agrupado para `developer` atualizando SHA e comentario (#222).

| Workflow | Gatilho | O que faz |
| --- | --- | --- |
| `ci.yml` (CI) | push em `developer`, `dev-local`; PR para `main`, `developer`, `dev-local` | escopo, restore, build, testes (incl. SQL Server), validacao de migrations e `Login integration E2E` |
| `login-e2e.yml` | chamado pelo CI ou manual | cenarios de login no navegador |
| `publish-container.yml` (Publish Production) | push na `main`/tags `v*.*.*`: so publica imagens; manual com `deploy_to_azure=true` na `main`: migra e faz deploy | imagem da API `sha-<commit>`, bundle de migrations, `migrate-production`, `deploy-api` (blue/green) e `deploy-function-app` (Workers) |
| `apply-production-migrations.yml` | manual | valida e aplica migrations de producao isoladamente |
| `rollback-production.yml` | manual | devolve o trafego da API para a revisao anterior (nao reverte migrations) |
| `publish-homologation.yml` | manual na `developer` | `diagnose`, `diagnose-sql`, `prepare` ou `publish` no Render ([runbook](homologation-migrations.md)) |
| `generate-migration-script.yml` | manual | SQL idempotente para revisao |
| `azure-operational-tasks.yml` | manual | tarefas operacionais (ex.: seed CBHPM pontual) |
| `cleanup-ghcr.yml` / `ghcr-retention.yml` | manual/agendado | auditoria e limpeza de imagens no GHCR |

Imagem:

```text
ghcr.io/hemodinks/hemodinks-api
```

Desde o #228 a imagem `ghcr.io/hemodinks/hemodinks-api-workers` nao e mais publicada: os Workers tem um unico caminho
de deploy, o pacote de codigo na Function App (ver [Workers](#workers-function-app)). O `HemodinksAPI.Workers/Dockerfile`
continua apenas para o `docker-compose` local; versoes antigas da imagem no GHCR seguem a politica do `cleanup-ghcr.yml`.

## Azure Container Apps

Crie primeiro os recursos no Azure Portal ou Azure CLI; depois conecte os workflows do GitHub.

Container Apps recomendados:

| App | Origem | Ingress | Porta | Observacao |
| --- | --- | --- | --- | --- |
| `hemodinks-api` | `ghcr.io/hemodinks/hemodinks-api` | External | `8080` | API publica, health check em `/healthz` |

Os Workers (uploads, reset e exportacoes) nao rodam em Container Apps: ficam na Function App `hemodinks-workers-production`.

Nao e necessario um terceiro Container App para workers. Em producao o workflow fixa `EventReminders__RunHostedProcessor=false`: lembretes vencidos sao reservados por `EventReminderProcessor` (lease com `UPDATE ... OUTPUT`) quando a agenda ou o dashboard sao consultados, o que e seguro com mais de uma revisao ativa.

Configuracao da API no Container Apps:

| Campo | Valor |
| --- | --- |
| Target port | `8080` |
| Min/Max replicas | `0`/`1` por revisao (aplicado pelo workflow) |
| Liveness / readiness | `/livez` / `/readyz` (`/healthz` equivalente a readiness) |
| Revision mode | `Multiple`, com labels `blue`/`green` ([runbook blue/green](production-blue-green-runbook.md)) |

Variaveis obrigatorias da API:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<Azure SQL>
JwtSettings__SecretKey=<segredo forte>
JwtSettings__Issuer=HemodinksAPI
JwtSettings__Audience=HemodinksAPI
AzureStorage__ConnectionString=<Azure Storage>
AzureStorage__ContainerName=profile-photos
AzureStorage__PatientFilesContainerName=patient-files
Cors__AllowedOrigins__0=https://hemodinks.gestao-saude.tec.br
Frontend__ResetPasswordUrl=https://hemodinks.gestao-saude.tec.br/reset-password
Database__RunMigrationsOnStartup=false
Database__RunMaintenanceOnStartup=false
Seed__CbhpmOnStartup=false
Seed__UsersOnStartup=false
CpfProtection__CertificateBase64=<PFX em base64 com chave privada RSA>
CpfProtection__CertificatePassword=<senha do PFX>
CpfProtection__LookupHashKey=<32+ bytes aleatorios em base64>
```

As tres chaves `CpfProtection__*` (#227) sao obrigatorias fora de Development/Testing: sem elas a
API nao sobe. Gere o material com `scripts/New-CpfProtectionKeys.ps1` e, **antes** do deploy que contem a
migration `Schema_ProtectCpfAtRest`, crie no Container App apenas os tres **secrets** (nao edite variaveis):

| Secret do Container App | Arquivo gerado | Variavel na revisao |
| --- | --- | --- |
| `cpf-protection-certificate` | `CertificateBase64.txt` | `CpfProtection__CertificateBase64` |
| `cpf-protection-certificate-password` | `CertificatePassword.txt` | `CpfProtection__CertificatePassword` |
| `cpf-protection-lookup-hash-key` | `LookupHashKey.txt` | `CpfProtection__LookupHashKey` |

O deploy (`scripts/deploy/create-candidate-revision.sh`) copia as variaveis da revisao **ativa**, entao editar
variaveis pelo portal criaria uma revisao sem trafego que o deploy ignora. Por isso o script grava as tres
variaveis acima como `secretRef` em toda revisao candidata e falha (codigo 10), antes de qualquer mutacao, se
algum desses secrets nao existir. Backup, rotacao
(`CpfProtection__PreviousCertificates__0__*` e `CpfProtection__PreviousLookupHashKeys__0`, so durante a
troca) e rollback: [issue-227-cpf-em-repouso.md](issue-227-cpf-em-repouso.md).

### New Relic APM na API

O `Dockerfile` da API ja publica o agente .NET em `/app/newrelic` e configura
os caminhos do profiler. Para ativar o APM no Azure Container Apps, crie no
repositorio GitHub `Hemodinks/hemodinks-api` o secret de Actions:

```text
NEW_RELIC_LICENSE_KEY=<ingest license key da New Relic>
```

Use a chave de ingestao do agente, e nao uma `NEW_RELIC_API_KEY`. No proximo
deploy para `main`, o workflow:

- grava a chave como secret `new-relic-license-key` no Container App;
- referencia o secret pela variavel `NEW_RELIC_LICENSE_KEY`;
- habilita o profiler e o distributed tracing;
- identifica a entidade como `Hemodinks API`;
- encaminha ao New Relic so logs de nivel Warning ou acima
  (`NEW_RELIC_APPLICATION_LOGGING_FORWARDING_LOG_LEVEL_DENYLIST`), cortando o log
  Information de cada requisicao (sem o padrao do agente, todos os niveis eram encaminhados);
- do contexto do log, envia apenas `RequestId`, `ClinicId` e as contagens do
  snapshot de sessoes (`..._CONTEXT_DATA_INCLUDE`); `UserId` e `GlobalUserId` ficam de fora.
  Atencao: o filtro vale so para atributos de contexto. O texto renderizado e a excecao
  de um Warning/Error seguem inteiros e podem conter o caminho da requisicao (ex.:
  `/api/users/email/{email}`) ou a mensagem da excecao. Nao coloque dado pessoal no
  texto de logs Warning/Error;
- liga `ActiveSessionsSnapshot__Enabled`: a cada 5 minutos, se a replica recebeu
  requisicao autenticada nos ultimos 30, a API registra (nivel Warning, EventId 4300/4301)
  usuarios online, total de sessoes e contagem por `ClinicaId`. Sem nome, e-mail,
  perfil, IP ou navegador; a lista nominal continua so na tela do SuperAdministrador.

Se o secret do GitHub nao estiver configurado, o deploy continua sem alterar
uma configuracao New Relic que ja exista no Container App.

Consultas NRQL para acompanhar producao sem entrar no HemoDinks:

```sql
-- Erros da API
SELECT count(*) FROM TransactionError WHERE appName = 'Hemodinks API' SINCE 1 day ago TIMESERIES
SELECT message, RequestId, ClinicId FROM Log WHERE entity.name = 'Hemodinks API' AND level IN ('Error', 'ERROR', 'Fatal', 'FATAL') SINCE 1 day ago
-- Sessoes ativas (replicas registram o mesmo valor: use latest/max, nunca sum)
SELECT latest(numeric(UsuariosOnline)) AS 'Usuarios online', latest(numeric(TotalSessoes)) AS 'Sessoes' FROM Log WHERE message LIKE 'ActiveSessionsSnapshot %' SINCE 30 minutes ago
SELECT max(numeric(UsuariosOnline)) FROM Log WHERE message LIKE 'ActiveSessionsSnapshot %' SINCE 1 day ago TIMESERIES 15 minutes
SELECT latest(numeric(SnapshotUsuarios)) FROM Log WHERE message LIKE 'ActiveSessionsSnapshotClinica%' FACET SnapshotClinicaId SINCE 30 minutes ago
```

Sem snapshot recente significa que nenhuma replica recebeu requisicao autenticada
na janela (ninguem usando) ou que o agente esta desligado. Nao publique dashboards
com link publico: eles mostram ClinicaId e volume de uso por clinica.

Se uploads de fotos e arquivos forem processados por Azure Functions, configure tambem na API:

```text
StorageFunctions__BaseUrl=https://<function-app>.azurewebsites.net
StorageFunctions__FunctionKey=<function-key>
```

Sem essas duas variaveis validas, a API salva direto no Azure Blob Storage usando `AzureStorage__ConnectionString`.

Para Functions em Container Apps, habilite a opcao de Azure Functions no portal e configure:

```text
FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
AzureWebJobsStorage=<Azure Storage>
PasswordResetEmailQueueName=password-reset-emails
FileExportQueueName=file-export-jobs
ExportsContainerName=exports
AzureStorage__ConnectionString=<Azure Storage>
AzureStorage__ContainerName=profile-photos
AzureStorage__PatientFilesContainerName=patient-files
Email__Provider=GmailSmtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__Username=<usuario SMTP>
Email__Smtp__Password=<senha SMTP>
Email__FromEmail=<email remetente>
Email__FromName=Hemodinks
Email__BrandLogoUrl=<url publica da logomarca>
Frontend__ResetPasswordUrl=https://hemodinks.gestao-saude.tec.br/reset-password
```

Se as imagens GHCR estiverem privadas, configure o pull do Container Apps com um PAT do GitHub com `read:packages` ou publique as imagens como publicas. Exemplo:

```bash
az containerapp registry set \
  --name hemodinks-api \
  --resource-group <resource-group> \
  --server ghcr.io \
  --username <github-user> \
  --password <github-pat-read-packages>
```

### Deploy pelo GitHub Actions

O workflow `Publish Production` (`publish-container.yml`) sempre publica a imagem da API no GHCR em push para `main`.
O deploy no Azure exige execucao manual na `main` com `deploy_to_azure=true`: o job `migrate-production`
(environment `production`, com aprovacao) aplica o bundle uma unica vez e so depois `deploy-api` troca o trafego
e `deploy-function-app` publica os Workers.

Desde o #278 o mesmo commit nao e mais validado tres vezes. O job `release-source` (`scripts/release_source.py`)
decide a origem da release:

| Execucao | O que roda |
| --- | --- |
| PR para `main` (CI) | suite completa, incluindo os testes offline que antes so o publish rodava; ao final registra a arvore git testada (artefato `ci-tested-tree-<arvore>`) |
| Push na `main` | se um CI de PR bem-sucedido do proprio repositorio registrou **exatamente** a arvore do merge, pula validacao e E2E e so gera imagem e bundle; na duvida (`main` mudou, push direto, tag, API indisponivel) roda tudo |
| Manual com `deploy_to_azure=true` | nao reconstroi: localiza o run de push bem-sucedido do mesmo SHA, baixa o bundle `production-migrations-<sha>` e o manifesto `release-image-<sha>` e implanta a imagem **por digest**. Sem esse run, falha antes das aprovacoes |
| Manual sem deploy | build e testes completos, como antes |

O deploy manual depende do run de push do mesmo commit; como os dois usam o mesmo grupo de concorrencia, um deploy
disparado logo apos o merge espera o push terminar. Se o run de push falhou, rode novamente os jobs com falha dele e
so depois dispare o deploy. Os artefatos ficam 30 dias; para implantar um commit mais antigo, use "Re-run all jobs"
no run de push dele, que gera os artefatos de novo (o publish manual sem deploy nao serve, porque o deploy so aceita
runs de push). Aprovacoes, migration unica, smoke test e rollback de trafego nao mudaram.

Desde o #229 o bash de cada passo do `deploy-api` fica em `scripts/deploy/*.sh` (um script por passo, mesmas
variaveis de ambiente e as mesmas opcoes `-e -o pipefail` do `run:`); o YAML so declara `env:` e chama o script.
O CI roda `shellcheck` nesses scripts e `scripts/tests/test_production_deployment_gates.py` executa os scripts reais
com `az`/`curl` simulados. Para mudar o deploy, edite o script e o teste, nao o YAML.

Repository variables:

| Nome | Exemplo |
| --- | --- |
| `AZURE_RESOURCE_GROUP` | `rg-hemodinks-prod` |
| `AZURE_CONTAINER_APP_API_RESOURCE_GROUP` | opcional; sobrescreve `AZURE_RESOURCE_GROUP` para a API |
| `AZURE_CONTAINER_APP_API_NAME` | `hemodinks-api` |
| `AZURE_FUNCTION_APP_WORKERS_NAME` | `hemodinks-workers-production` (obrigatoria: sem ela o deploy dos Workers falha) |

`AZURE_CONTAINER_APPS_DEPLOY_ENABLED`, `AZURE_CONTAINER_APP_FUNCTIONS_NAME` e `AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP`
nao sao mais lidas pelo deploy (#228). `cleanup-ghcr.yml` ainda le as duas ultimas para inventariar consumidores da imagem
antiga de Workers.

Repository secrets para login OIDC no Azure:

| Nome |
| --- |
| `AZURE_CLIENT_ID` |
| `AZURE_TENANT_ID` |
| `AZURE_SUBSCRIPTION_ID` |

Permissoes minimas esperadas para a identidade:

- `Contributor` no resource group dos Container Apps, ou permissao equivalente para `Microsoft.App/containerApps`.
- Permissao de federated credential configurada no app registration para este repositorio/ambiente GitHub.

Merge em `main` publica as imagens; as revisoes dos Container Apps (tags `sha-<commit>`) so mudam na execucao manual com `deploy_to_azure=true`.
O workflow tambem atualiza `Deployment__CommitSha` no Container App para forcar nova revisao; a conferência pós-deploy lê esse valor diretamente da revisão candidata pelo Azure CLI. O `/healthz` público retorna apenas o estado agregado.

Se o Container App da API for movido para outro Resource Group, atualize `AZURE_RESOURCE_GROUP` ou defina o override `AZURE_CONTAINER_APP_API_RESOURCE_GROUP`. O workflow tenta descobrir o Resource Group pelo nome do Container App quando o valor configurado nao encontra o recurso.

<a id="workers-function-app"></a>
#### Workers (Function App)

Caminho unico desde o #228: o job `deploy-function-app` roda `dotnet publish` de `HemodinksAPI.Workers` no mesmo commit
e publica o pacote na Function App `AZURE_FUNCTION_APP_WORKERS_NAME` (Flex Consumption) com o login Azure OIDC do
environment `production`, depois de `migrate-production`. Se a variavel estiver vazia, o job falha em vez de ser pulado.
Nao ha blue/green nem rollback automatico para os Workers: para voltar, rode o workflow manual na `main` a partir do
commit anterior. A rota anterior por Container App (`deploy-functions`) foi removida: `AZURE_CONTAINER_APP_FUNCTIONS_NAME` nao estava
configurada e o job aparecia como `skipped` nas execucoes de producao (ex.: 10/11/2026).

### Migrations no pipeline

O CI ja valida se existem mudancas de modelo pendentes sem migration. Para producao, prefira gerar e revisar SQL antes de aplicar:

1. Execute o workflow manual `Generate Migration Script`.
2. Baixe o artefato `hemodinks-migrations-sql`.
3. Revise comandos destrutivos como `DROP`, `DELETE`, `ALTER COLUMN` e SQL manual.
4. Aplique no Azure SQL com backup/PITR confirmado.

Em producao, mantenha `Database__RunMigrationsOnStartup=false`. O caminho padrao e o proprio
`Publish Production` com `deploy_to_azure=true`, que aplica o bundle em `migrate-production` antes do deploy.
`Apply Production Migrations` continua disponivel para aplicar migrations isoladamente: confirme backup/PITR e
informe `APPLY-PRODUCTION`; o workflow valida a cadeia com `Hemodinks.SchemaGuard`, guarda o SQL idempotente
como artefato e aplica somente migrations pendentes conhecidas.

### Seed CBHPM pontual

Para atualizar CBHPM sem deixar `Seed__CbhpmOnStartup=true` fixo:

1. Execute o workflow manual `Azure Operational Tasks`.
2. Selecione `cbhpm-seed`.
3. O workflow liga `Seed__CbhpmOnStartup=true`, cria uma nova revisao, espera `/healthz` ficar saudavel e depois volta para `false`.

O seed atual insere/atualiza por codigo e nao remove procedimentos ausentes do JSON. Mesmo assim, revise o JSON antes de rodar em producao.

## Render (blueprint legado de producao)

Removido na issue #220: `render.yaml` (servico `hemodinks-api`, sem uso; a producao roda no Azure Container Apps)
e `vercel-deploy.yml`. O Render continua so para a homologacao, abaixo.

## Render homologacao: confirmation

`render.confirmation.yaml` define:

- service: `hemodinks-api-1` (https://hemodinks-api-1-90nb.onrender.com, regiao Virginia)
- runtime: `docker`
- branch: `developer`
- ambiente `Confirmation`
- health check: `/livez` (sem banco, para nao manter o SQL de homologacao acordado)
- auto deploy: desligado; a publicacao e feita pelo workflow `Publish Homologation` ([runbook](homologation-migrations.md))

Origens CORS configuradas no blueprint:

```text
Cors__AllowedOrigins__0=https://hemodinks-homologacao.gestao-saude.tec.br
```

Variaveis que devem diferir da producao:

| Chave | Recomendacao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | usar outro banco |
| `JwtSettings__SecretKey` | usar outra chave |
| `JwtSettings__Issuer` | `HemodinksAPI.Confirmation` |
| `JwtSettings__Audience` | `HemodinksAPI.Confirmation` |
| `CpfProtection__CertificateBase64` / `__CertificatePassword` / `__LookupHashKey` | gerar material proprio da homologacao (#227) |
| `Frontend__ResetPasswordUrl` | `https://hemodinks-homologacao.gestao-saude.tec.br/reset-password` |
| `NEW_RELIC_APP_NAME` | `Hemodinks API Confirmation` |
| `AzureStorage__ContainerName` | `profile-photos-confirmation` |
| `AzureStorage__PatientFilesContainerName` | `patient-files-confirmation` |
| `AsyncQueues__PasswordResetEnabled` | `false` para Render confirmation enviar o reset direto por SMTP e evitar worker antigo |
| `AsyncQueues__PasswordResetEmailQueueName` | `password-reset-emails-confirmation` |
| `AsyncQueues__FileExportQueueName` | `file-export-jobs-confirmation` |

O banco de homologacao deve ser Azure SQL ou uma instancia SQL Server com Full-Text
Search habilitado. Antes de aplicar migrations, valide que
`SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` retorna `1`.

Se `AsyncQueues__PasswordResetEnabled=true` ou `PasswordResetFunctions__BaseUrl` estiver configurado, o email de reset pode ser enviado pelo worker/Function em vez da API Render. Nesse caso, o worker tambem precisa estar no mesmo commit da API e usar:

```text
Frontend__ResetPasswordUrl=https://hemodinks-homologacao.gestao-saude.tec.br/reset-password
Email__BrandLogoUrl=<url publica da logomarca>
PasswordResetEmailQueueName=password-reset-emails-confirmation
```

## Azure SQL Database

Uso:

- persistencia relacional da API
- migrations aplicadas pelo `Publish Production` (`migrate-production`) ou pelo workflow manual `Apply Production Migrations`
- seed de perfis/usuarios/CBHPM conforme configuracao do ambiente

Checklist:

1. Criar servidor SQL e banco.
2. Liberar firewall para o host da API.
3. Configurar `ConnectionStrings__DefaultConnection`.
4. Confirmar que `SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` retorna `1`.
5. Validar migrations antes do deploy:

```powershell
dotnet tool restore
pwsh ./scripts/Test-Migrations.ps1
pwsh ./scripts/Export-MigrationScripts.ps1
```

## Azure Blob Storage

Containers usados:

- `profile-photos`
- `patient-files`

Checklist:

1. Criar Storage Account.
2. Criar containers ou permitir criacao pela API.
3. Configurar:

```text
AzureStorage__PublicBaseUrl=https://<storage-account>.blob.core.windows.net/profile-photos
AzureStorage__PatientFilesPublicBaseUrl=https://<storage-account>.blob.core.windows.net/patient-files
```

## Filas e Azure Functions

Functions de `HemodinksAPI.Workers`:

| Function | Gatilho | Uso |
| --- | --- | --- |
| `SendPasswordResetEmail` | fila `PasswordResetEmailQueueName` | email de reset de senha |
| `SendPasswordResetEmailSync` | HTTP | envio direto de reset (fallback) |
| `GenerateFileExport` | fila `FileExportQueueName` | exportacoes de `POST /api/exports` |
| `SendEventEmail` | HTTP | emails de eventos da agenda |
| `UploadPatientFile` / `UploadProfilePhoto` | HTTP (`StorageFunctions__*`) | uploads para o Blob Storage |

As Functions HTTP de producao podem ter a origem restrita aos IPs de saida da API, alem da function key:
workflow `Functions network restriction` (`docs/functions-network-restriction.md`).

Variaveis da API:

| Chave | Descricao |
| --- | --- |
| `AsyncQueues__Enabled` | fallback global das filas |
| `AsyncQueues__PasswordResetEnabled` | manda reset para fila/Function |
| `AsyncQueues__FileExportEnabled` | manda exportacoes para fila/Function |
| `AsyncQueues__ConnectionString` | storage das filas |
| `AsyncQueues__PasswordResetEmailQueueName` | padrao `password-reset-emails` |
| `AsyncQueues__FileExportQueueName` | padrao `file-export-jobs` |

Variaveis do Function App:

| Chave | Descricao |
| --- | --- |
| `AzureWebJobsStorage` | storage dos triggers e blobs |
| `PasswordResetEmailQueueName` | mesmo valor da API |
| `FileExportQueueName` | mesmo valor da API |
| `ExportsContainerName` | container dos arquivos gerados |
| `Email__*` | configuracao SMTP do worker |
| `Email__BrandLogoUrl` | URL publica opcional da logomarca no email de reset |
| `Frontend__ResetPasswordUrl` | URL publica da tela de reset |

## Documentacao interativa em producao

Se quiser expor Swagger/Scalar/OpenAPI publicamente:

```text
ApiDocumentation__Enabled=true
```

Depois valide:

```powershell
curl https://<api-publica>/openapi/v1.json
```

E no navegador:

- `https://<api-publica>/swagger`
- `https://<api-publica>/scalar`

## Validacao apos deploy

```powershell
curl https://<api-publica>/healthz
curl https://<api-publica>/openapi/v1.json
```

Fluxos para validar depois do login:

- `GET /api/dashboard/summary`
- `GET /api/licencas/current`
- `GET /api/pacientes?page=1&pageSize=10`
- `GET /api/events?from=<iso>&to=<iso>`
- `GET /api/faturamentos-medicos?page=1&pageSize=10`
- `GET /api/grupos-medicos/medicos`
- `GET /api/configuracoes-sistema/current`
- `GET /api/cbhpm?page=1&pageSize=10`
