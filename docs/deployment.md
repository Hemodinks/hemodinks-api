# Deployment

Este backend foi preparado para Docker, Render, GitHub Actions e uso de recursos Azure.

## URLs

Local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` |
| Swagger | `http://localhost:5000/swagger` |
| Scalar | `http://localhost:5000/scalar` |
| OpenAPI | `http://localhost:5000/openapi/v1.json` |

Producao:

| Recurso | URL |
| --- | --- |
| Frontend | `https://hemodinks-saude.vercel.app` |
| Frontend homologacao | `https://hemodinks-homologacao.vercel.app` |
| API | `https://<api-publica>` configurada em `VITE_API_URL` |
| Swagger | `https://<api-publica>/swagger` |
| Scalar | `https://<api-publica>/scalar` |
| OpenAPI | `https://<api-publica>/openapi/v1.json` |

Se o servico Render usar o nome `hemodinks-api`, a URL publica normalmente fica no formato `https://hemodinks-api.onrender.com`, mas confirme no dashboard do Render.

## GitHub Actions

Workflows:

- `.github/workflows/ci.yml`: restaura, compila e executa testes em push/pull request para `main`.
- `.github/workflows/publish-container.yml`: publica imagem Docker no GitHub Container Registry em push para `main`, tags `v*.*.*` e execucao manual.
- `.github/workflows/vercel-deploy.yml`: gancho opcional, desativado por padrao.

Imagem:

```text
ghcr.io/hemodinks/hemodinks-api
```

Secrets:

- CI nao exige secrets.
- GHCR usa `GITHUB_TOKEN`.
- Vercel opcional: `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID`.

Variables:

- `ENABLE_VERCEL_DEPLOY=true` habilita workflow opcional da Vercel.

## Render

O `render.yaml` define:

- service: `hemodinks-api`
- runtime: `docker`
- branch: `main`
- porta interna: `10000`
- health check: `/healthz`
- auto deploy: depois que checks passam

Variaveis obrigatorias no Render:

| Chave | Descricao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | connection string do Azure SQL ou SQL Server externo |
| `JwtSettings__SecretKey` | chave JWT com 32 bytes ou mais |
| `AzureStorage__ConnectionString` | connection string da Storage Account |
| `AzureStorage__PublicBaseUrl` | URL publica do container `profile-photos` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container `patient-files` |
| `Cors__AllowedOrigins__0` | `https://hemodinks-saude.vercel.app` |

Variaveis opcionais para observabilidade via New Relic:

| Chave | Descricao |
| --- | --- |
| `CORECLR_ENABLE_PROFILING` | ativa o profiler oficial quando `1` |
| `NEW_RELIC_LICENSE_KEY` | license key da conta New Relic |
| `NEW_RELIC_APP_NAME` | nome exibido no APM da New Relic |

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
| `AzureStorage__ContainerName` | `profile-photos` |
| `AzureStorage__MaxBytes` | `1048576` |
| `AzureStorage__PatientFilesContainerName` | `patient-files` |
| `AzureStorage__PatientFileMaxBytes` | `10485760` |

O `Dockerfile` ja deixa `CORECLR_PROFILER`, `CORECLR_NEWRELIC_HOME` e `CORECLR_PROFILER_PATH` apontando para `/app/newrelic`, que e publicado junto com a aplicacao pelo pacote `NewRelic.Agent`. Para a telemetria sair de fato, configure `NEW_RELIC_LICENSE_KEY` no servico publicado.

## Idempotencia em producao

Os fluxos de criacao de evento e reset de senha agora suportam `Idempotency-Key`. Para aproveitar isso em retries do front, gateway ou automacoes:

- gere uma chave unica por tentativa logica
- reuse a mesma chave apenas quando quiser repetir exatamente o mesmo payload
- trate `Idempotency-Status: replayed` como sucesso reaproveitado
- trate `409 Conflict` como erro de reutilizacao incorreta da chave

Render nao fornece SQL Server gerenciado. Use Azure SQL Database, SQL Server em VM ou outro provider SQL Server compativel.

### Homologacao Render: confirmation

O arquivo `render.confirmation.yaml` define um servico separado:

- service: `hemodinks-api-confirmation`
- runtime: `docker`
- branch: `developer`
- environment: `Confirmation`
- health check: `/healthz`
- origem CORS principal: `https://hemodinks-homologacao.vercel.app`
- origem CORS adicional opcional: `https://hemodinks-front-confirmation.onrender.com`

Use esse arquivo como blueprint/configuracao do ambiente de homologacao `confirmation`. Se o Render gerar uma URL diferente para o front, ajuste:

```text
Cors__AllowedOrigins__0=https://<front-confirmation>.onrender.com
```

Variaveis que devem ser diferentes de producao:

| Chave | Recomendacao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | usar outro banco, por exemplo `HemodinksDBConfirmation` |
| `JwtSettings__SecretKey` | usar outra chave JWT |
| `JwtSettings__Issuer` | `HemodinksAPI.Confirmation` |
| `JwtSettings__Audience` | `HemodinksAPI.Confirmation` |
| `NEW_RELIC_APP_NAME` | `Hemodinks API Confirmation` |
| `AzureStorage__ContainerName` | `profile-photos-confirmation` |
| `AzureStorage__PatientFilesContainerName` | `patient-files-confirmation` |
| `AzureStorage__PublicBaseUrl` | URL do container `profile-photos-confirmation` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL do container `patient-files-confirmation` |
| `Cors__AllowedOrigins__0` | URL do front de homologacao |

O arquivo `.env.confirmation.example` traz um modelo dessas variaveis.

Nao copie a connection string de producao para homologacao, a menos que queira intencionalmente que migrations, seeds, testes manuais e uploads usem os dados reais. Para homologacao segura, use banco e containers separados.

## Azure SQL Database

Uso:

- persistencia relacional da API
- migrations automaticas no startup quando `Database__RunMigrationsOnStartup=true`
- seed automatico de perfis, usuarios iniciais e CBHPM apenas quando habilitado por ambiente
- tabelas de usuarios, pacientes, licencas, agenda, CBHPM, hospitais e convenios

Checklist:

1. Crie o servidor SQL e o banco no Azure.
2. Libere firewall para o host da API.
3. Use connection string com `Encrypt=true;TrustServerCertificate=false` quando possivel.
4. Configure `ConnectionStrings__DefaultConnection` no Render.

Migrations ficam no projeto `HemodinksAPI.Infrastructure`. Para validar localmente:

```powershell
dotnet tool restore
pwsh ./scripts/Test-Migrations.ps1
dotnet tool run dotnet-ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --no-connect
```

Para gerar o SQL do rollout antes do deploy:

```powershell
pwsh ./scripts/Export-MigrationScripts.ps1
```

Se a release trouxer migration de `Data` ou `Repair`, prefira rollback por restore/PITR ou forward fix, nao apenas por `Down()`.

Se a agenda retornar `Invalid object name 'Events'` ou `Invalid column name 'NextReminderAt'`, publique a versao com a migration `EnsureEventReminderColumns`, confirme `Database__RunMigrationsOnStartup=true` no Render e reinicie o servico para o startup aplicar o reparo no banco.

## Azure Blob Storage

Containers usados:

- `profile-photos`: fotos de perfil de usuarios/pacientes.
- `patient-files`: anexos de pacientes.

Checklist:

1. Crie uma Storage Account.
2. Crie os containers ou permita que a API crie.
3. Configure o nivel de acesso de leitura conforme sua estrategia de seguranca.
4. Configure as URLs publicas:
   - `AzureStorage__PublicBaseUrl=https://<storage-account>.blob.core.windows.net/profile-photos`
   - `AzureStorage__PatientFilesPublicBaseUrl=https://<storage-account>.blob.core.windows.net/patient-files`

Se as URLs publicas nao forem informadas, a API usa a URL retornada pelo SDK do Azure Blob.

## Azure Queue / Service Bus

Azure Queue Storage agora e usado de forma opcional para dois fluxos assincronos:

- envio de email de reset de senha
- exportacoes PDF/XLSX solicitadas por `/api/exports`

A funcao de exportacao ja grava arquivos no container de exports com os metadados do job. O conteudo de negocio de cada relatorio deve evoluir dentro do `HemodinksAPI.Workers`, sem mover autorizacao, idempotencia ou regras sensiveis para fora da API.

Ative apenas depois de publicar o Function App `HemodinksAPI.Workers`.

Variaveis da API:

| Chave | Descricao |
| --- | --- |
| `PasswordReset__UseEmail` | `true` para gerar token e enviar reset por email |
| `Frontend__ResetPasswordUrl` | URL publica da tela `/reset-password` |
| `AsyncQueues__Enabled` | `true` para usar filas; `false` mantem SMTP direto e bloqueia exportacoes |
| `AsyncQueues__ConnectionString` | connection string da Storage Account das filas; se vazio, usa `AzureStorage__ConnectionString` |
| `AsyncQueues__PasswordResetEmailQueueName` | padrao `password-reset-emails` |
| `AsyncQueues__FileExportQueueName` | padrao `file-export-jobs` |
| `StorageFunctions__BaseUrl` | URL base do Function App quando a API deve terceirizar uploads de foto/anexos |
| `StorageFunctions__FunctionKey` | function key usada nos uploads HTTP |

Variaveis do Azure Functions:

| Chave | Descricao |
| --- | --- |
| `AzureWebJobsStorage` | Storage Account usada pelos triggers, filas e container de exports |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `PasswordResetEmailQueueName` | mesmo valor de `AsyncQueues__PasswordResetEmailQueueName` |
| `FileExportQueueName` | mesmo valor de `AsyncQueues__FileExportQueueName` |
| `ExportsContainerName` | container dos arquivos gerados, padrao `exports` |
| `AzureStorage__ConnectionString` | opcional; se vazio, os uploads HTTP usam `AzureWebJobsStorage` |
| `AzureStorage__ContainerName` | container das fotos de perfil |
| `AzureStorage__PublicBaseUrl` | URL publica do container de fotos |
| `AzureStorage__MaxBytes` | limite da foto de perfil |
| `AzureStorage__PatientFilesContainerName` | container dos anexos |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container de anexos |
| `AzureStorage__PatientFileMaxBytes` | limite dos anexos |
| `Email__Provider` | `GmailSmtp` ou `Smtp` |
| `Email__FromEmail` | remetente |
| `Email__FromName` | nome do remetente |
| `Email__Smtp__Host` | host SMTP |
| `Email__Smtp__Port` | porta SMTP |
| `Email__Smtp__Username` | usuario SMTP |
| `Email__Smtp__Password` | senha/app password SMTP |
| `Frontend__ResetPasswordUrl` | URL da tela de reset no frontend |

Para homologacao, use filas e container separados, por exemplo `password-reset-emails-confirmation`, `file-export-jobs-confirmation`, `exports-confirmation`, `profile-photos-confirmation` e `patient-files-confirmation`.

Quando `StorageFunctions__BaseUrl` estiver configurado, a API passa a usar o `HemodinksAPI.Workers` para uploads HTTP de:

- foto de perfil
- anexos de usuarios medicos
- anexos de pacientes

A agenda usa um `BackgroundService` interno no proprio processo da API. Esse desenho evita custo adicional no Render Free e e adequado para a fase atual.

Se `AsyncQueues__Enabled=true` e a Function nao estiver ativa, a API continuara respondendo `202/200` apos enfileirar, mas emails e arquivos ficarao parados na fila ate o worker processar.
- auditoria assincrona

## Frontend

O frontend usa Vercel e deve receber:

```text
VITE_API_URL=https://<api-publica>
```

Origem publica atual permitida por padrao no CORS:

```text
https://hemodinks-saude.vercel.app
```

Para outras origens, configure `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1` etc.

## Validacao apos deploy

```powershell
curl https://<api-publica>/healthz
curl https://<api-publica>/openapi/v1.json
```

No navegador:

- `https://<api-publica>/swagger`
- `https://<api-publica>/scalar`
- `https://hemodinks-saude.vercel.app`

Endpoints autenticados para validar depois do login:

- `GET /api/dashboard/summary`
- `GET /api/licencas/current`
- `GET /api/events`
- `GET /api/cbhpm?page=1&pageSize=10`
