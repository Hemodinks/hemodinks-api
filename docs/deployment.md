# Deployment

Backend preparado para Docker, Render, GitHub Actions e recursos Azure.

## URLs

Local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` |
| Swagger | `http://localhost:5000/swagger` |
| Scalar | `http://localhost:5000/scalar` |
| OpenAPI | `http://localhost:5000/openapi/v1.json` |

Publicado:

| Recurso | URL |
| --- | --- |
| Front producao | `https://hemodinks-saude.vercel.app` |
| Front homologacao principal | `https://hemodinks-homologacao.vercel.app` |
| Front confirmation Render opcional | `https://hemodinks-front-confirmation.onrender.com` |
| API | `https://<api-publica>` |
| Swagger | `https://<api-publica>/swagger` quando `ApiDocumentation__Enabled=true` |
| Scalar | `https://<api-publica>/scalar` quando `ApiDocumentation__Enabled=true` |
| OpenAPI | `https://<api-publica>/openapi/v1.json` quando `ApiDocumentation__Enabled=true` |

## GitHub Actions

Workflows principais:

- `.github/workflows/ci.yml`: restore, build, testes e validacao de migrations
- `.github/workflows/publish-container.yml`: publica imagens Docker no GHCR e, quando habilitado, atualiza Azure Container Apps
- `.github/workflows/generate-migration-script.yml`: gera SQL idempotente de migrations para revisao/aplicacao controlada
- `.github/workflows/azure-operational-tasks.yml`: executa tarefas operacionais manuais no Azure, como seed CBHPM temporario
- `.github/workflows/vercel-deploy.yml`: gancho opcional para coordenacao com o front

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

Nao e necessario criar um terceiro Container App para workers neste momento. O processamento de lembretes roda dentro da API via `EventNotificationHostedService`; por isso mantenha a API com `max replicas = 1` ate esse processamento ser extraido para um job/worker dedicado com trava distribuida.

Configuracao da API no Container Apps:

| Campo | Valor |
| --- | --- |
| Target port | `8080` |
| Min replicas | `1` |
| Max replicas inicial | `1` |
| Health/readiness | `/healthz` |
| Revision mode | Single, salvo estrategia explicita de blue/green |

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
Cors__AllowedOrigins__0=https://hemodinks-saude.vercel.app
Frontend__ResetPasswordUrl=https://hemodinks-saude.vercel.app/reset-password
Database__RunMigrationsOnStartup=true
Seed__CbhpmOnStartup=false
Seed__UsersOnStartup=false
```

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
Frontend__ResetPasswordUrl=https://hemodinks-saude.vercel.app/reset-password
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

O workflow `Publish Containers` sempre publica as duas imagens no GHCR em push para `main`. O deploy no Azure so acontece quando as variaveis/secrets abaixo estiverem configuradas.

Repository variables:

| Nome | Exemplo |
| --- | --- |
| `AZURE_CONTAINER_APPS_DEPLOY_ENABLED` | `true` |
| `AZURE_RESOURCE_GROUP` | `rg-hemodinks-prod` |
| `AZURE_CONTAINER_APP_API_NAME` | `hemodinks-api` |
| `AZURE_CONTAINER_APP_FUNCTIONS_NAME` | `hemodinks-functions` |

Repository secrets para login OIDC no Azure:

| Nome |
| --- |
| `AZURE_CLIENT_ID` |
| `AZURE_TENANT_ID` |
| `AZURE_SUBSCRIPTION_ID` |

Permissoes minimas esperadas para a identidade:

- `Contributor` no resource group dos Container Apps, ou permissao equivalente para `Microsoft.App/containerApps`.
- Permissao de federated credential configurada no app registration para este repositorio/ambiente GitHub.

Com `AZURE_CONTAINER_APPS_DEPLOY_ENABLED=true`, merge em `main` publica as imagens e atualiza as revisoes dos Container Apps usando tags `sha-<commit>`.

### Migrations no pipeline

O CI ja valida se existem mudancas de modelo pendentes sem migration. Para producao, prefira gerar e revisar SQL antes de aplicar:

1. Execute o workflow manual `Generate Migration Script`.
2. Baixe o artefato `hemodinks-migrations-sql`.
3. Revise comandos destrutivos como `DROP`, `DELETE`, `ALTER COLUMN` e SQL manual.
4. Aplique no Azure SQL com backup/PITR confirmado.

Enquanto a operacao ainda estiver simples, `Database__RunMigrationsOnStartup=true` pode ser usado com `max replicas = 1`. Quando o ambiente amadurecer, mude para `Database__RunMigrationsOnStartup=false` e aplique migrations por etapa controlada.

### Seed CBHPM pontual

Para atualizar CBHPM sem deixar `Seed__CbhpmOnStartup=true` fixo:

1. Execute o workflow manual `Azure Operational Tasks`.
2. Selecione `cbhpm-seed`.
3. O workflow liga `Seed__CbhpmOnStartup=true`, cria uma nova revisao, espera `/healthz` ficar saudavel e depois volta para `false`.

O seed atual insere/atualiza por codigo e nao remove procedimentos ausentes do JSON. Mesmo assim, revise o JSON antes de rodar em producao.

## Render producao

`render.yaml` define:

- service: `hemodinks-api`
- runtime: `docker`
- branch: `main`
- porta interna: `10000`
- health check: `/healthz`
- auto deploy: `checksPass`

Variaveis obrigatorias:

| Chave | Descricao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | connection string do Azure SQL ou SQL Server externo |
| `JwtSettings__SecretKey` | chave JWT com 32 bytes ou mais |
| `AzureStorage__ConnectionString` | connection string da Storage Account |
| `AzureStorage__PublicBaseUrl` | URL publica do container `profile-photos` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container `patient-files` |
| `Cors__AllowedOrigins__0` | `https://hemodinks-saude.vercel.app` |
| `Frontend__ResetPasswordUrl` | `https://hemodinks-saude.vercel.app/reset-password` |

Variaveis opcionais importantes:

| Chave | Descricao |
| --- | --- |
| `ApiDocumentation__Enabled` | expoe Swagger/Scalar/OpenAPI fora de `Development` e `Testing` |
| `NEW_RELIC_LICENSE_KEY` | ativa envio para New Relic |
| `OTEL_EXPORTER_OTLP_EXTERNAL_ENDPOINT` | duplica telemetria para backend OTLP externo |
| `AsyncQueues__ConnectionString` | storage usada pelas filas |
| `Email__*` | SMTP quando reset por email direto estiver habilitado |
| `Email__BrandLogoUrl` | URL publica opcional da logomarca no email de reset |

Variaveis ja declaradas no blueprint:

| Chave | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://0.0.0.0:10000` |
| `CORECLR_ENABLE_PROFILING` | `1` |
| `Database__RunMigrationsOnStartup` | `true` |
| `Seed__CbhpmOnStartup` | `false` |
| `Seed__UsersOnStartup` | `false` |
| `JwtSettings__Issuer` | `HemodinksAPI` |
| `JwtSettings__Audience` | `HemodinksAPI` |
| `JwtSettings__ExpirationMinutes` | `60` |
| `NEW_RELIC_APP_NAME` | `Hemodinks API` |
| `PasswordReset__UseEmail` | `true` |
| `AsyncQueues__Enabled` | `false` |
| `AsyncQueues__PasswordResetEnabled` | `true` |
| `AsyncQueues__FileExportEnabled` | `true` |
| `AzureStorage__ContainerName` | `profile-photos` |
| `AzureStorage__PatientFilesContainerName` | `patient-files` |

Observacao: o blueprint deixa `AsyncQueues__Enabled=false`, mas ativa os recursos especificos `AsyncQueues__PasswordResetEnabled=true` e `AsyncQueues__FileExportEnabled=true`.

## Render homologacao: confirmation

`render.confirmation.yaml` define:

- service: `hemodinks-api-confirmation`
- runtime: `docker`
- branch: `developer`
- ambiente `Confirmation`
- health check: `/healthz`

Origens CORS configuradas no blueprint:

```text
Cors__AllowedOrigins__0=https://hemodinks-homologacao.vercel.app
Cors__AllowedOrigins__1=https://hemodinks-front-confirmation.onrender.com
```

Variaveis que devem diferir da producao:

| Chave | Recomendacao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | usar outro banco |
| `JwtSettings__SecretKey` | usar outra chave |
| `JwtSettings__Issuer` | `HemodinksAPI.Confirmation` |
| `JwtSettings__Audience` | `HemodinksAPI.Confirmation` |
| `Frontend__ResetPasswordUrl` | `https://hemodinks-homologacao.vercel.app/reset-password` |
| `NEW_RELIC_APP_NAME` | `Hemodinks API Confirmation` |
| `AzureStorage__ContainerName` | `profile-photos-confirmation` |
| `AzureStorage__PatientFilesContainerName` | `patient-files-confirmation` |
| `AsyncQueues__PasswordResetEmailQueueName` | `password-reset-emails-confirmation` |
| `AsyncQueues__FileExportQueueName` | `file-export-jobs-confirmation` |

## Azure SQL Database

Uso:

- persistencia relacional da API
- migrations automaticas no startup quando `Database__RunMigrationsOnStartup=true`
- seed de perfis/usuarios/CBHPM conforme configuracao do ambiente

Checklist:

1. Criar servidor SQL e banco.
2. Liberar firewall para o host da API.
3. Configurar `ConnectionStrings__DefaultConnection`.
4. Validar migrations antes do deploy:

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

Fluxos assincronos opcionais:

- reset de senha por email
- exportacoes PDF/XLSX via `POST /api/exports`

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
