# Hemodinks - Documentacao Tecnica

## Visao geral

O Hemodinks e composto por frontend React/Vite, API ASP.NET Core/.NET 10, persistencia em SQL Server/Azure SQL e armazenamento de arquivos em Azure Blob Storage. A API foi organizada em Clean Architecture pragmatica com CQRS, MediatR, validacao em pipeline, EF Core e Minimal APIs.

URLs principais:

| Recurso | URL |
| --- | --- |
| Frontend local | `http://localhost:5173` |
| Frontend producao | `https://hemodinks-saude.vercel.app` |
| Frontend homologacao | `https://hemodinks-homologacao.vercel.app` |
| API local | `http://localhost:5000` |
| Swagger | `/swagger` |
| Scalar | `/scalar` |
| OpenAPI JSON | `/openapi/v1.json` |

## Projetos e responsabilidades

| Projeto | Responsabilidade |
| --- | --- |
| `HemodinksAPI.Domain` | entidades, constantes de dominio e utilitarios puros |
| `HemodinksAPI.Application` | commands, queries, handlers, DTOs, validadores, contratos e regras de aplicacao |
| `HemodinksAPI.Infrastructure` | EF Core, migrations, seeders, JWT, storage, notificacoes, worker de agenda e implementacoes concretas |
| `HemodinksAPI.Api` | Minimal APIs, CORS, autenticacao/autorizacao, Swagger/Scalar, DI e composition root |
| `HemodinksAPI.Tests` | testes unitarios e de integracao |

Direcao permitida:

```mermaid
flowchart LR
    Api[HemodinksAPI.Api] --> Application[HemodinksAPI.Application]
    Api --> Infrastructure[HemodinksAPI.Infrastructure]
    Api --> Domain[HemodinksAPI.Domain]
    Infrastructure --> Application
    Infrastructure --> Domain
    Application --> Domain
```

## Componentes

```mermaid
flowchart LR
    Browser[Browser] --> Front[React/Vite Frontend]
    Front --> API[ASP.NET Core API]
    API --> Mediator[MediatR]
    Mediator --> Handlers[Application Handlers]
    Handlers --> DbContext[IAppDbContext]
    Handlers --> Contracts[Application Contracts]
    Contracts --> Infra[Infrastructure Services]
    Infra --> Sql[(Azure SQL / SQL Server)]
    Infra --> Blob[(Azure Blob Storage)]
    Infra --> Worker[EventNotificationHostedService]
    Handlers --> Cache[IMemoryCache CBHPM]
```

## MER principal

```mermaid
erDiagram
    PERFIS ||--o{ USERS : possui
    USERS ||--o| PACIENTES : sincroniza
    USERS ||--o{ USER_ARQUIVOS : possui
    USERS ||--o| LICENCAS : possui
    USERS ||--o{ EVENTS : cria
    USERS ||--o{ EVENTS : recebe_como_medico
    PACIENTES ||--o{ PACIENTE_ARQUIVOS : possui
    PACIENTES ||--o{ PACIENTE_PROCEDIMENTOS : possui
    HOSPITAIS ||--o{ PACIENTES : referencia
    CONVENIOS ||--o{ PACIENTES : referencia
    CBHPM_GERAL ||--o{ PACIENTES : codigo_logico

    PERFIS {
        int Id PK
        string Nome
    }

    USERS {
        int Id PK
        int PerfilId FK
        string Nome
        string Email
        string Telefone
        string Cpf
        string Crm
        string CrmUf
        string FotoPerfil
        string Senha
        datetime DataCadastro
        datetime DataAtualizacao
        datetime DataNascimento
        bool Ativo
        bool PrecisaTrocarSenha
    }

    LICENCAS {
        int Id PK
        int UserId FK
        string Plano
        string Status
        datetime DataInicioTrial
        datetime DataFimTrial
        datetime DataFimLicenca
        string FeaturesLiberadas
        string Observacoes
    }

    PACIENTES {
        int Id PK
        int UserId FK
        int HospitalId FK
        int MedicoUserId FK
        int ConvenioId FK
        datetime Data
        string NomePaciente
        string CbhpmCodigo
        string CbhpmPorte
        string Procedimento
        bool StatusPago
    }

    EVENTS {
        int Id PK
        int UserId FK
        int MedicalUserId FK
        string Title
        datetime Start
        datetime End
        bool NotifyMedicalProfile
        bool NotifyUser
        int ReminderPeriodMinutes
        datetime NextReminderAt
        datetime LastReminderSentAt
        bool IsCompleted
    }

    CBHPM_GERAL {
        int Id PK
        string Codigo UK
        string Procedimento
        string Porte
        decimal CustoOperacional
        decimal ValorReferencia
        string Capitulo
        string Grupo
        int PaginaPdf
    }
```

## Fluxo HTTP e CQRS

```mermaid
flowchart TB
    Request[HTTP Request] --> Endpoint[Endpoint Extension]
    Endpoint --> Auth[JWT + Policies]
    Auth --> Mediator[MediatR]
    Mediator --> Validation[ValidationBehavior]
    Validation --> Handler[Command/Query Handler]
    Handler --> Rules[Regras de Aplicacao]
    Handler --> Db[IAppDbContext]
    Handler --> Storage[IProfilePhotoStorage/IPatientFileStorage]
    Handler --> Notifications[INotificationService]
    Db --> Sql[(SQL Server)]
    Storage --> Blob[(Azure Blob)]
    Handler --> Response[DTO/Result]
```

## Fluxo de agenda

```mermaid
flowchart TD
    User[Usuario autenticado] --> Calendar[Calendario no frontend]
    Calendar --> Payload[EventRequest]
    Payload --> Create[POST /api/events]
    Create --> Validate[ValidationBehavior + EventFeatureRules]
    Validate --> Persist[Events]
    Persist --> Next[Calcula NextReminderAt]
    Worker[EventNotificationHostedService] --> Due[Consulta lembretes vencidos]
    Due --> Notify[INotificationService]
    Notify --> Recalc[Recalcula proximo lembrete]
    Recalc --> Complete{Evento concluido?}
    Complete -- nao --> Due
    Complete -- sim --> Stop[Para lembretes]
```

Notas:

- A agenda usa `NextReminderAt` para consultar apenas pendencias vencidas.
- O worker roda no proprio processo da API, adequado para Render Free e baixo custo inicial.
- O dashboard tenta processar pendencias sem bloquear a tela caso notificacoes falhem.
- A migration `EnsureEventReminderColumns` repara bancos que ja tinham `Events` sem colunas de lembrete.

## Fluxo de licencas

```mermaid
flowchart TD
    Login[Login] --> UserData[AuthenticateUserResponse]
    UserData --> Licenca[LicencaDto]
    Request[Requisicao protegida] --> Policy[LicencaFeatureAuthorizationHandler]
    Policy --> Service[ILicencaService]
    Service --> Db[(Licencas)]
    Db --> Allow{Feature ativa?}
    Allow -- sim --> Endpoint[Executa endpoint]
    Allow -- nao --> Forbidden[403]
```

Features atuais:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Pacientes.Gerenciar`
- `Cbhpm.Consultar`

## Fluxo de CBHPM

```mermaid
flowchart TD
    Startup[Startup da API] --> Migration[Migrations]
    Migration --> Seed[CbhpmSeeder]
    Seed --> Table[(CBHPMGeral)]
    Request[GET /api/cbhpm] --> Cache[ICbhpmCache]
    Cache --> Memory{Snapshot em memoria}
    Memory -- vazio --> Table
    Table --> Snapshot[Carrega snapshot ordenado]
    Snapshot --> Filter[Filtra por codigo, procedimento, porte ou search]
    Memory -- preenchido --> Filter
    Filter --> Pagination[Pagina resultado]
    Import[POST /api/cbhpm/import] --> Table
    Import --> Invalidate[Invalidate cache]
```

## Comunicacao com Azure e Render

```mermaid
flowchart LR
    Front[Vercel Frontend] -->|HTTPS REST + JWT| API[Render Docker API]
    API -->|EF Core SQL| AzureSql[(Azure SQL Database)]
    API -->|SDK Azure.Storage.Blobs| Photos[(Blob profile-photos)]
    API -->|SDK Azure.Storage.Blobs| Files[(Blob patient-files)]
    API -->|Memoria local| CbhpmCache[IMemoryCache]
    API -. futuro .-> Queue[(Azure Queue / Service Bus)]
```

## Recursos externos

| Recurso | Status | Uso |
| --- | --- | --- |
| Azure SQL Database | usado | banco relacional da aplicacao |
| Azure Blob Storage | usado | fotos e anexos |
| Azure Queue Storage / Service Bus | nao usado | reservado para notificacoes/filas futuras |
| Render Worker separado | nao usado | worker atual roda dentro da API |

## Migrations e banco

Migrations ficam em `HemodinksAPI.Infrastructure/Data/Migrations`.

Comandos uteis:

```powershell
dotnet ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure --no-connect
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
```

O startup da API executa `Database.MigrateAsync()` automaticamente em bancos relacionais.

## Documentacao interativa

Swagger e Scalar sao servidos pela propria API:

- Swagger UI: `/swagger`
- Scalar UI: `/scalar`
- OpenAPI JSON usado pelo Scalar: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

Os endpoints estao agrupados por tags:

- `Dashboard`
- `Usuarios`
- `Pacientes`
- `Agenda`
- `Licencas`
- `CBHPM`
- `Hospitais`
- `Convenios`

## Observacoes operacionais

- `IMemoryCache` reduz leituras repetidas da tabela CBHPM, mas e cache local por instancia.
- Azure SQL e Blob Storage sao recursos externos cobrados conforme plano/uso.
- O processamento de lembretes atual nao exige Hangfire, RabbitMQ, Azure Queue ou Service Bus.
- Para escala horizontal ou notificacoes reais em alto volume, o proximo passo natural e trocar o worker interno por fila/worker externo mantendo os contratos da Application.
