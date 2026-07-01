# Hemodinks API

API ASP.NET Core/.NET 10 para gestao de usuarios, pacientes, agenda, licencas, dashboard, arquivos e consulta CBHPM.

## Stack

- .NET 10 e ASP.NET Core Minimal APIs
- Clean Architecture pragmatica em `Domain`, `Application`, `Infrastructure` e `Api`
- CQRS com MediatR e pipeline de validacao
- Entity Framework Core 10 com SQL Server/Azure SQL
- JWT Bearer para autenticacao e autorizacao por perfil/licenca
- Serilog para logs em console e arquivo
- New Relic APM via agente oficial .NET
- Azure Blob Storage para fotos de perfil e anexos de pacientes
- Azure Queue Storage + Azure Functions opcionais para email de reset e exportacoes PDF/XLSX
- `BackgroundService` interno para lembretes da agenda
- `IMemoryCache` para consulta CBHPM em memoria
- Swagger/OpenAPI e Scalar para documentacao interativa
- Docker, Docker Compose, Render e GitHub Actions

## Arquitetura

```text
HemodinksAPI.Domain
  Entidades, constantes de dominio e utilitarios puros.

HemodinksAPI.Application
  Commands, queries, handlers, DTOs, validadores, contratos e regras de aplicacao.

HemodinksAPI.Infrastructure
  EF Core, migrations, seeders, JWT, storage, notificacoes, workers e servicos concretos.

HemodinksAPI.Api
  Minimal APIs, CORS, auth, Swagger/Scalar, DI e composition root.
```

Direcao das dependencias:

```text
Api -> Application + Infrastructure + Domain
Infrastructure -> Application + Domain
Application -> Domain
Domain -> sem dependencia dos demais projetos
```

## URLs

Ambiente local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` |
| Swagger UI | `http://localhost:5000/swagger` |
| Scalar UI | `http://localhost:5000/scalar` |
| OpenAPI JSON | `http://localhost:5000/openapi/v1.json` |
| Swagger JSON | `http://localhost:5000/swagger/v1/swagger.json` |

Ambientes publicados:

| Recurso | URL |
| --- | --- |
| Frontend producao | `https://hemodinks-saude.vercel.app` |
| Frontend homologacao | `https://hemodinks-homologacao.vercel.app` |
| API | configure em `VITE_API_URL`, por exemplo `https://hemodinks-api.onrender.com` |

## Como executar

### Docker Compose

```powershell
Copy-Item .env.example .env
# Edite MSSQL_SA_PASSWORD e JWT_SECRET_KEY no .env
docker-compose up -d
```

A API aplica migrations no startup, cria perfis, seeda usuarios quando necessario e carrega CBHPM a partir de `HemodinksAPI.Infrastructure/Data/SeedData/cbhpm-geral.json`.

### Docker Compose com observabilidade

Se quiser a API rodando em container com collector OTLP e dashboard local do Aspire, use o compose dedicado:

```powershell
Copy-Item .env.example .env
# Edite MSSQL_SA_PASSWORD e JWT_SECRET_KEY no .env
docker compose -f docker-compose.observability.yml up -d
```

Endpoints locais do compose observavel:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Dashboard Aspire | `http://localhost:18888` |
| Collector OTLP gRPC | `http://localhost:4317` |
| Collector OTLP HTTP | `http://localhost:4318` |

Esse compose envia logs, traces e metrics da API para `otel-collector`, e o collector encaminha tudo para o dashboard do Aspire. O dashboard foi deixado sem login e com bind em `127.0.0.1`, entao ele e para uso local apenas.

Para o front React/Vite mandar traces para o mesmo collector enquanto voce desenvolve localmente:

```powershell
$env:VITE_OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4318"
npm run dev --prefix ..\hemodinks-front
```

Com isso, as traces do navegador passam a aparecer no mesmo dashboard junto das traces da API em container.

### Desenvolvimento local

```powershell
dotnet restore
dotnet tool restore
dotnet user-secrets set --project HemodinksAPI.Api "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Issuer" "HemodinksAPI"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Audience" "HemodinksAPI"
# Opcional para observabilidade local com New Relic ao usar `dotnet run`
# $env:CORECLR_ENABLE_PROFILING="1"
# $env:CORECLR_PROFILER="{36032161-FFC0-4B61-B559-F6C5D41BAE5A}"
# $env:CORECLR_NEWRELIC_HOME="$PWD\\HemodinksAPI.Api\\bin\\Debug\\net10.0\\newrelic"
# $env:CORECLR_PROFILER_PATH="$PWD\\HemodinksAPI.Api\\bin\\Debug\\net10.0\\newrelic\\NewRelic.Profiler.dll"
# $env:NEW_RELIC_LICENSE_KEY="<sua-license-key>"
# $env:NEW_RELIC_APP_NAME="Hemodinks API Local"
dotnet run --project HemodinksAPI.Api
```

### Desenvolvimento local com Aspire

Use o `AppHost` quando quiser subir a API com dashboard de observabilidade e o front React/Vite no mesmo ambiente local.

```powershell
dotnet restore HemodinksAPI.slnx
npm install --prefix ..\hemodinks-front
dotnet run --project Hemodinks.AppHost
```

O dashboard do Aspire abre com a API como recurso instrumentado por OpenTelemetry e o front como app Vite gerenciado pelo `AppHost`. O `VITE_API_URL` do front passa a apontar automaticamente para a API exposta pelo Aspire. O console imprime a `Dashboard URL` e a `Login URL`; use a `Login URL` para entrar direto no painel local.

Se preferir que o `AppHost` suba a API como container, mantendo o front local em Vite:

```powershell
dotnet user-secrets set --project Hemodinks.AppHost "MSSQL_SA_PASSWORD" "troque_por_uma_senha_forte"
dotnet user-secrets set --project Hemodinks.AppHost "JWT_SECRET_KEY" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet run --launch-profile https-container --project Hemodinks.AppHost
```

Nesse modo o `AppHost`:

- constroi a API a partir do `Dockerfile`
- sobe `sqlserver` e `azurite` como containers auxiliares
- mantem o front como app local em `..\hemodinks-front`
- continua alimentando o dashboard do Aspire com a telemetria da API e do front

Tambem funciona via variavel de ambiente:

```powershell
$env:HEMODINKS_API_MODE="container"
dotnet run --project Hemodinks.AppHost
```

## Configuracao

Use variaveis de ambiente, `.env` no Docker ou User Secrets localmente.

Variaveis opcionais de observabilidade:

| Chave | Descricao |
| --- | --- |
| `CORECLR_ENABLE_PROFILING` | ativa o profiler do New Relic quando `1` |
| `NEW_RELIC_LICENSE_KEY` | license key da conta New Relic |
| `NEW_RELIC_APP_NAME` | nome exibido no APM da New Relic |
| `OTEL_EXPORTER_OTLP_EXTERNAL_ENDPOINT` | endpoint OTLP externo adicional para logs, traces e metrics |
| `OTEL_EXPORTER_OTLP_EXTERNAL_PROTOCOL` | protocolo do exporter externo: `grpc` ou `http/protobuf` |
| `OTEL_EXPORTER_OTLP_EXTERNAL_HEADERS` | headers extras do exporter externo, no formato `chave=valor,chave2=valor2` |

No Docker/Render, o `Dockerfile` ja define `CORECLR_PROFILER`, `CORECLR_NEWRELIC_HOME` e `CORECLR_PROFILER_PATH` apontando para `/app/newrelic`, pasta que o pacote `NewRelic.Agent` publica junto com a API. Em execucao local com `dotnet run`, esses caminhos precisam ser configurados no shell antes de subir a aplicacao.

O agente New Relic observa requests ASP.NET Core, chamadas HTTP de saida, SQL Server e excecoes sem bootstrap adicional no codigo. Os logs estruturados continuam saindo por Serilog em console e arquivo.

Se a API estiver rodando via Aspire, ela continua exportando para o dashboard local e pode duplicar a telemetria para um backend OTLP externo usando as variaveis `OTEL_EXPORTER_OTLP_EXTERNAL_*`.

## Idempotencia

Os endpoints abaixo aceitam o header opcional `Idempotency-Key`:

- `POST /api/events/`
- `POST /api/users/password/reset`
- `POST /api/users/password/reset/confirm`

Quando a mesma requisicao bem-sucedida chega novamente com a mesma chave, a API reaproveita a resposta anterior e devolve `Idempotency-Status: replayed`. Na primeira execucao persistida, a resposta inclui `Idempotency-Status: stored`. Se a mesma chave for reutilizada com payload diferente, a API responde `409 Conflict`.

| Chave | Uso |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | SQL Server/Azure SQL |
| `JwtSettings__SecretKey` | chave HS256 com 32 bytes ou mais |
| `JwtSettings__Issuer` | emissor JWT |
| `JwtSettings__Audience` | audiencia JWT |
| `JwtSettings__ExpirationMinutes` | expiracao do token |
| `Database__RunMigrationsOnStartup` | aplica migrations no startup quando `true` |
| `Cors__AllowedOrigins__0` | origem adicional do frontend |
| `AzureStorage__ConnectionString` | Storage Account Azure |
| `AzureStorage__ContainerName` | container de fotos, padrao `profile-photos` |
| `AzureStorage__PublicBaseUrl` | URL publica do container de fotos |
| `AzureStorage__PatientFilesContainerName` | container de anexos, padrao `patient-files` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container de anexos |
| `AzureStorage__PatientFileMaxBytes` | limite de upload de anexos |
| `AsyncQueues__Enabled` | liga filas para reset por email e exportacoes quando `true` |
| `AsyncQueues__ConnectionString` | connection string da Storage Account usada pelas filas; se vazio, usa `AzureStorage__ConnectionString` |
| `AsyncQueues__PasswordResetEmailQueueName` | fila de emails de reset, padrao `password-reset-emails` |
| `AsyncQueues__FileExportQueueName` | fila de exportacoes, padrao `file-export-jobs` |
| `Licensing__TrialDays` | dias de trial para licencas medicas |

Segredos nao devem ser gravados em `appsettings.json`.

## Autenticacao, perfis e licencas

O login retorna um JWT usado em:

```text
Authorization: Bearer <token>
```

Perfis seedados:

| Id | Perfil |
| --- | --- |
| 1 | Administrador |
| 2 | Medicos |
| 3 | Pacientes |

Regras principais:

- Administrador gerencia usuarios, pacientes, CBHPM, agenda, licencas e exclusoes.
- Medico visualiza/edita seus dados, visualiza pacientes vinculados e eventos da sua agenda.
- Paciente acessa somente o proprio cadastro quando houver vinculo.
- Licencas controlam acesso a dashboard, pacientes e CBHPM para medicos.

## Endpoints principais

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/healthz` | nao | health check |
| `POST` | `/api/users/authenticate` | nao | login JWT |
| `POST` | `/api/users/password/reset` | nao | reset por email |
| `POST` | `/api/exports` | sim | enfileira exportacao PDF/XLSX |
| `GET` | `/api/users` | admin | lista paginada de usuarios |
| `POST` | `/api/users` | admin | cria usuario |
| `GET` | `/api/users/{id}` | sim | busca usuario |
| `PUT` | `/api/users/{id}` | sim | atualiza usuario |
| `DELETE` | `/api/users/{id}` | admin | exclui usuario |
| `PUT` | `/api/users/{id}/password` | sim | altera senha |
| `PUT` | `/api/users/{id}/password/reset` | admin | reset administrativo |
| `POST` | `/api/users/{id}/arquivos` | sim | upload de documento medico |
| `DELETE` | `/api/users/{id}/arquivos/{arquivoId}` | sim | exclui documento medico |
| `GET` | `/api/pacientes` | licenca | lista paginada de pacientes |
| `GET` | `/api/pacientes/{id}` | licenca | detalhe do paciente |
| `POST` | `/api/pacientes` | admin/medico/controller | cria paciente |
| `PUT` | `/api/pacientes/{id}` | admin/medico/controller | atualiza paciente |
| `DELETE` | `/api/pacientes/{id}` | admin | exclui paciente |
| `POST` | `/api/pacientes/{id}/arquivos` | admin/medico | upload de anexo do paciente |
| `DELETE` | `/api/pacientes/{id}/arquivos/{arquivoId}` | admin/medico | exclui anexo |
| `GET` | `/api/cbhpm` | licenca | consulta CBHPM paginada |
| `POST` | `/api/cbhpm/import` | admin | importa/substitui itens CBHPM |
| `GET` | `/api/dashboard/summary` | licenca | resumo do dashboard |
| `GET` | `/api/dashboard/notifications` | licenca | notificacoes do dashboard |
| `GET` | `/api/events` | sim | lista eventos da agenda por periodo |
| `GET` | `/api/events/medical-users` | sim | medicos ativos para notificacao |
| `GET` | `/api/events/{id}` | sim | detalhe do evento |
| `POST` | `/api/events` | sim | cria evento |
| `PUT` | `/api/events/{id}` | sim | atualiza evento |
| `POST` | `/api/events/{id}/complete` | sim | conclui evento |
| `DELETE` | `/api/events/{id}` | sim | exclui evento |
| `GET` | `/api/licencas/current` | sim | licenca do usuario autenticado |
| `GET` | `/api/licencas/users/{userId}` | admin | consulta licenca de medico |
| `PUT` | `/api/licencas/users/{userId}` | admin | atualiza licenca |
| `POST` | `/api/licencas/users/{userId}/liberar-completa` | admin | libera plano completo |
| `GET` | `/api/hospitais` | sim | lista hospitais |
| `GET` | `/api/convenios` | sim | lista convenios |

## Agenda e lembretes

A agenda permite criar eventos para qualquer data/hora, associar responsavel, notificar usuario e/ou perfil medico e configurar periodo de lembrete.

Campos principais:

- `title`, `description`, `start`, `end`
- `userId`, `medicalUserId`
- `notifyMedicalProfile`, `notifyUser`
- `reminderPeriodMinutes`
- `nextReminderAt`, `lastReminderSentAt`
- `isCompleted`, `completedAt`

O processamento atual usa um `BackgroundService` interno gratuito no proprio processo da API. Ele consulta eventos vencidos por `NextReminderAt` e reagenda o proximo lembrete ate a conclusao do evento. O dashboard tambem tenta processar pendencias de forma resiliente quando o usuario abre a aplicacao.

## CBHPM

A tabela `CBHPMGeral` e criada por migration e recebe seed automatico de procedimentos a partir do JSON gerado do PDF `docs/CBHPM-2022_versao-agosto-2023.pdf`. O seed inclui porte, custo operacional, grupo e `ValorReferencia` calculado.

Consulta:

```http
GET /api/cbhpm?page=1&pageSize=10&codigo=1.01&procedimento=consulta&porte=2B
Authorization: Bearer <token>
```

O backend usa `IMemoryCache` para manter a lista CBHPM em memoria. A primeira consulta carrega os dados do SQL Server; filtros, paginacao e busca passam a ser resolvidos em memoria ate expirar o cache ou ate uma importacao/seed invalidar a chave.

## Banco de dados

Entidades principais:

- `Perfis`
- `Users`
- `Licencas`
- `Pacientes`
- `PacienteArquivos`
- `PacienteProcedimentos`
- `UserArquivos`
- `CBHPMGeral`
- `Hospitais`
- `Convenios`
- `Events`

Migrations rodam no startup via `Database.MigrateAsync()` quando `Database__RunMigrationsOnStartup=true`. O blueprint de producao do Render habilita essa variavel para que deploy automatico atualize o schema antes da API atender trafego normal. A organizacao da pasta e a politica de rollback estao em [Migrations README](./HemodinksAPI.Infrastructure/Data/Migrations/README.md). Para validar e gerar artefatos locais:

```powershell
pwsh ./scripts/Test-Migrations.ps1
pwsh ./scripts/Export-MigrationScripts.ps1
```

Para usar EF CLI manualmente:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --no-connect
dotnet tool run dotnet-ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api
```

## Documentacao interativa

Swagger e Scalar ficam ativos em qualquer ambiente publicado:

- Swagger: `/swagger`
- Scalar: `/scalar`
- OpenAPI usado pelo Scalar: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

O documento OpenAPI inclui o esquema `Bearer`. Em producao, evite expor tokens reais em maquinas compartilhadas.

## Testes

```powershell
dotnet build HemodinksAPI.slnx
pwsh ./scripts/Test-Migrations.ps1 -NoBuild
dotnet test HemodinksAPI.slnx --no-build
```

## Documentos relacionados

- [Primeira execucao](./PRIMEIRA_EXECUCAO.md)
- [Implementacao](./IMPLEMENTACAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Deploy](./docs/deployment.md)
- [Documentacao tecnica](./docs/TECHNICAL_DOCUMENTATION.md)
- [Migrations README](./HemodinksAPI.Infrastructure/Data/Migrations/README.md)
- [Exemplos HTTP](./API.http)
- [Documentacao tecnica PDF](./docs/Hemodinks-Documentacao-Tecnica.pdf)
