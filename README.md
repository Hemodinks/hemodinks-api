# Hemodinks API

API ASP.NET Core/.NET 10 para gestao de usuarios, pacientes, agenda, licencas, dashboard, arquivos e consulta CBHPM.

## Stack

- .NET 10 e ASP.NET Core Minimal APIs
- Clean Architecture pragmatica em `Domain`, `Application`, `Infrastructure` e `Api`
- CQRS com MediatR e pipeline de validacao
- Entity Framework Core 10 com SQL Server/Azure SQL
- JWT Bearer para autenticacao e autorizacao por perfil/licenca
- Serilog para logs em console e arquivo
- Azure Blob Storage para fotos de perfil e anexos de pacientes
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

### Desenvolvimento local

```powershell
dotnet restore
dotnet user-secrets set --project HemodinksAPI.Api "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Issuer" "HemodinksAPI"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Audience" "HemodinksAPI"
dotnet run --project HemodinksAPI.Api
```

## Configuracao

Use variaveis de ambiente, `.env` no Docker ou User Secrets localmente.

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
| `POST` | `/api/pacientes` | admin | cria paciente |
| `PUT` | `/api/pacientes/{id}` | admin | atualiza paciente |
| `DELETE` | `/api/pacientes/{id}` | admin | exclui paciente |
| `POST` | `/api/pacientes/{id}/arquivos` | admin | upload de anexo do paciente |
| `DELETE` | `/api/pacientes/{id}/arquivos/{arquivoId}` | admin | exclui anexo |
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

A tabela `CBHPMGeral` e criada por migration e recebe seed automatico de procedimentos a partir do JSON gerado do PDF `Tabela-CBHPM-Geral.pdf`.

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

Migrations rodam no startup via `Database.MigrateAsync()` quando `Database__RunMigrationsOnStartup=true`. O blueprint de producao do Render habilita essa variavel para que deploy automatico atualize o schema antes dos workers da agenda. Para usar EF CLI depois da separacao em projetos:

```powershell
dotnet ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure --no-connect
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
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
dotnet test HemodinksAPI.slnx --no-build
```

## Documentos relacionados

- [Primeira execucao](./PRIMEIRA_EXECUCAO.md)
- [Implementacao](./IMPLEMENTACAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Deploy](./docs/deployment.md)
- [Documentacao tecnica](./docs/TECHNICAL_DOCUMENTATION.md)
- [Exemplos HTTP](./API.http)
- [Documentacao tecnica PDF](./docs/Hemodinks-Documentacao-Tecnica.pdf)
