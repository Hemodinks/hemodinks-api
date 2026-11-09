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
| Front producao legado | `https://hemodinks-saude.vercel.app` |
| Front homologacao principal | `https://hemodinks-homologacao.gestao-saude.tec.br` |
| Front homologacao legado | `https://hemodinks-homologacao.vercel.app` |
| Front confirmation Render opcional | `https://hemodinks-front-confirmation.onrender.com` |
| API producao | `https://<api-publica>` (Container App `hemodinks-api-prod`) |
| API homologacao | `https://hemodinks-api-1-90nb.onrender.com` |
| Swagger | `https://<api-publica>/swagger` quando `ApiDocumentation__Enabled=true` |
| Scalar | `https://<api-publica>/scalar` quando `ApiDocumentation__Enabled=true` |
| OpenAPI | `https://<api-publica>/openapi/v1.json` quando `ApiDocumentation__Enabled=true` |

## GitHub Actions

Workflows do `hemodinks-api`:

| Workflow | Gatilho | O que faz |
| --- | --- | --- |
| `ci.yml` (CI) | push/PR em `main`, `developer`, `dev-local` | escopo, restore, build, testes (incl. SQL Server), validacao de migrations e `Login integration E2E` |
| `login-e2e.yml` | chamado pelo CI ou manual | cenarios de login no navegador |
| `publish-container.yml` (Publish Production) | push na `main`/tags `v*.*.*`: so publica imagens; manual com `deploy_to_azure=true` na `main`: migra e faz deploy | imagens `sha-<commit>`, bundle de migrations, `migrate-production`, `deploy-api` (blue/green) e `deploy-functions` |
| `apply-production-migrations.yml` | manual | valida e aplica migrations de producao isoladamente |
| `rollback-production.yml` | manual | devolve o trafego da API para a revisao anterior (nao reverte migrations) |
| `publish-homologation.yml` | manual na `developer` | `diagnose`, `diagnose-sql`, `prepare` ou `publish` no Render ([runbook](homologation-migrations.md)) |
| `generate-migration-script.yml` | manual | SQL idempotente para revisao |
| `azure-operational-tasks.yml` | manual | tarefas operacionais (ex.: seed CBHPM pontual) |
| `cleanup-ghcr.yml` / `ghcr-retention.yml` | manual/agendado | auditoria e limpeza de imagens no GHCR |
| `vercel-deploy.yml` | push na `main` | gancho de coordenacao com o front na Vercel |

Imagens:

```text
ghcr.io/hemodinks/hemodinks-api
ghcr.io/hemodinks/hemodinks-api-workers
```

## Azure Container Apps

Crie primeiro os recursos no Azure Portal ou Azure CLI; depois conecte os workflows do GitHub.

Container Apps recomendados:

| App | Origem | Ingress | Porta | Observacao |
| --- | --- | --- | --- | --- |
| `hemodinks-api` | `ghcr.io/hemodinks/hemodinks-api` | External | `8080` | API publica, health check em `/healthz` |
| `hemodinks-functions` | `ghcr.io/hemodinks/hemodinks-api-workers` | External ou internal conforme uso HTTP | porta padrao da imagem Functions | Azure Functions em container para uploads, reset e exportacoes |

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
Cors__AllowedOrigins__1=https://hemodinks-saude.vercel.app
Frontend__ResetPasswordUrl=https://hemodinks.gestao-saude.tec.br/reset-password
Database__RunMigrationsOnStartup=false
Database__RunMaintenanceOnStartup=false
Seed__CbhpmOnStartup=false
Seed__UsersOnStartup=false
```

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
- identifica a entidade como `Hemodinks API`.

Se o secret do GitHub nao estiver configurado, o deploy continua sem alterar
uma configuracao New Relic que ja exista no Container App.

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

Repita para `hemodinks-functions`.

### Deploy pelo GitHub Actions

O workflow `Publish Production` (`publish-container.yml`) sempre publica as duas imagens no GHCR em push para `main`.
O deploy no Azure exige execucao manual na `main` com `deploy_to_azure=true`: o job `migrate-production`
(environment `production`, com aprovacao) aplica o bundle uma unica vez e so depois `deploy-api` troca o trafego.

Repository variables:

| Nome | Exemplo |
| --- | --- |
| `AZURE_CONTAINER_APPS_DEPLOY_ENABLED` | `true` |
| `AZURE_RESOURCE_GROUP` | `rg-hemodinks-prod` |
| `AZURE_CONTAINER_APP_API_RESOURCE_GROUP` | opcional; sobrescreve `AZURE_RESOURCE_GROUP` para a API |
| `AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP` | opcional; sobrescreve `AZURE_RESOURCE_GROUP` para o worker em Container Apps |
| `AZURE_CONTAINER_APP_API_NAME` | `hemodinks-api` |
| `AZURE_CONTAINER_APP_FUNCTIONS_NAME` | `hemodinks-functions` |
| `AZURE_FUNCTION_APP_WORKERS_NAME` | `hemodinks-workers-production` |

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

Se um Container App for movido para outro Resource Group, atualize `AZURE_RESOURCE_GROUP` ou defina o override especifico `AZURE_CONTAINER_APP_API_RESOURCE_GROUP` / `AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP`. O workflow tenta descobrir o Resource Group pelo nome do Container App quando o valor configurado nao encontra o recurso.

Se os workers estiverem em um Azure Function App tradicional em vez de Azure Container Apps, configure `AZURE_FUNCTION_APP_WORKERS_NAME` com o nome do Function App. O workflow publica `HemodinksAPI.Workers` diretamente nesse Function App usando o login Azure OIDC ja configurado.

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

A producao roda no Azure Container Apps. `render.yaml` permanece como blueprint de contingencia e define:

- service: `hemodinks-api`
- runtime: `docker`
- branch: `main`
- porta interna: `10000`
- health check: `/healthz`
- auto deploy: desligado (`autoDeployTrigger: off`)

Variaveis obrigatorias:

| Chave | Descricao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | connection string do Azure SQL ou SQL Server externo |
| `JwtSettings__SecretKey` | chave JWT com 32 bytes ou mais |
| `AzureStorage__ConnectionString` | connection string da Storage Account |
| `AzureStorage__PublicBaseUrl` | URL publica do container `profile-photos` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container `patient-files` |
| `Cors__AllowedOrigins__0` | `https://hemodinks.gestao-saude.tec.br` |
| `Cors__AllowedOrigins__1` | `https://hemodinks-saude.vercel.app`, se o dominio legado continuar ativo |
| `Frontend__ResetPasswordUrl` | `https://hemodinks.gestao-saude.tec.br/reset-password` |

Variaveis opcionais importantes:

| Chave | Descricao |
| --- | --- |
| `ApiDocumentation__Enabled` | expoe Swagger/Scalar/OpenAPI fora de `Development` e `Testing` |
| `NEW_RELIC_LICENSE_KEY` | ativa envio para New Relic |
| `AsyncQueues__ConnectionString` | storage usada pelas filas |
| `Email__*` | SMTP quando reset por email direto estiver habilitado |
| `Email__BrandLogoUrl` | URL publica opcional da logomarca no email de reset |

Variaveis ja declaradas no blueprint:

| Chave | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://0.0.0.0:10000` |
| `CORECLR_ENABLE_PROFILING` | `1` |
| `Database__RunMigrationsOnStartup` | `false` |
| `Database__RunMaintenanceOnStartup` | `false` |
| `EventReminders__RunHostedProcessor` | `false` na API com mais de uma replica |
| `Seed__CbhpmOnStartup` | `false` |
| `Seed__UsersOnStartup` | `false` |
| `JwtSettings__Issuer` | `HemodinksAPI` |
| `JwtSettings__Audience` | `HemodinksAPI` |
| `JwtSettings__ExpirationMinutes` | `30` |
| `AuthenticationSession__IdleTimeoutMinutes` | `30` |
| `AuthenticationSession__RefreshCookieName` | `hemodinks_refresh` |
| `AuthenticationSession__RefreshCookieLifetimeDays` | `30` |
| `NEW_RELIC_APP_NAME` | `Hemodinks API Production` |
| `AsyncQueues__Enabled` | `false` |
| `AsyncQueues__PasswordResetEnabled` | `true` |
| `AsyncQueues__FileExportEnabled` | `true` |
| `AzureStorage__ContainerName` | `profile-photos` |
| `AzureStorage__PatientFilesContainerName` | `patient-files` |

Observacao: o blueprint deixa `AsyncQueues__Enabled=false`, mas ativa os recursos especificos `AsyncQueues__PasswordResetEnabled=true` e `AsyncQueues__FileExportEnabled=true`.

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
Cors__AllowedOrigins__1=https://hemodinks-homologacao.vercel.app
Cors__AllowedOrigins__2=https://hemodinks-front-confirmation.onrender.com
```

Variaveis que devem diferir da producao:

| Chave | Recomendacao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | usar outro banco |
| `JwtSettings__SecretKey` | usar outra chave |
| `JwtSettings__Issuer` | `HemodinksAPI.Confirmation` |
| `JwtSettings__Audience` | `HemodinksAPI.Confirmation` |
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
