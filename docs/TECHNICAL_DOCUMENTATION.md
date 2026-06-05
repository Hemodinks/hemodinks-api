# Hemodinks - Documentacao Tecnica

## Visao geral

O Hemodinks e composto por um frontend React/Vite, uma API ASP.NET Core/.NET 10, persistencia em SQL Server/Azure SQL e armazenamento de arquivos em Azure Blob Storage.

URLs principais:

| Recurso | URL |
| --- | --- |
| Frontend local | `http://localhost:5173` |
| Frontend producao | `https://hemodinks-saude.vercel.app` |
| API local | `http://localhost:5000` |
| Swagger | `/swagger` |
| Scalar | `/scalar` |
| OpenAPI JSON | `/openapi/v1.json` |

## Componentes

```mermaid
flowchart LR
    Browser[Browser] --> Front[React/Vite Frontend]
    Front --> API[ASP.NET Core API]
    API --> Cache[IMemoryCache CBHPM]
    API --> Sql[(Azure SQL / SQL Server)]
    API --> Blob[(Azure Blob Storage)]
    API -. futuro .-> Queue[(Azure Queue / Service Bus)]
```

## MER

```mermaid
erDiagram
    PERFIS ||--o{ USERS : possui
    USERS ||--o| PACIENTES : sincroniza
    USERS ||--o{ USER_ARQUIVOS : possui
    PACIENTES ||--o{ PACIENTE_ARQUIVOS : possui
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
        string FotoPerfil
        string Senha
        datetime DataCadastro
        datetime DataAtualizacao
        datetime DataNascimento
        bool Ativo
        bool PrecisaTrocarSenha
    }

    PACIENTES {
        int Id PK
        int UserId FK
        datetime Data
        string NomePaciente
        string Hospital
        string Medico
        string Convenio
        string CbhpmCodigo
        string CbhpmPorte
        string Procedimento
        string Autorizacao
        string Pagamento
        string RepasseGlosa
        bool StatusPago
    }

    CBHPM_GERAL {
        int Id PK
        string Codigo UK
        string Procedimento
        string Porte
        decimal CustoOperacional
        string Capitulo
        string Grupo
        int PaginaPdf
    }
```

## Fluxo de classes do backend

```mermaid
flowchart TB
    Program[Program.cs] --> Endpoints[Endpoint Extensions]
    Endpoints --> Mediator[MediatR]
    Mediator --> Commands[Command Handlers]
    Mediator --> Queries[Query Handlers]
    Commands --> Rules[Regras de dominio]
    Queries --> Rules
    Rules --> Db[AppDbContext]
    Rules --> Cache[ICbhpmCache]
    Rules --> Storage[Azure Blob Storage Services]
    Db --> Sql[(SQL Server / Azure SQL)]
    Storage --> Blob[(Blob Storage)]
```

## Fluxo de cadastro/edicao de paciente

```mermaid
flowchart TD
    User[Usuario autenticado] --> Form[Formulario paciente]
    Form --> Popup[Popup selecionar procedimento]
    Popup --> CbhpmApi[GET /api/cbhpm]
    CbhpmApi --> Cache{Cache CBHPM quente?}
    Cache -- nao --> Sql[(CBHPMGeral no SQL)]
    Sql --> Cache
    Cache -- sim --> Page[Filtros e paginacao em memoria]
    Page --> Select[Seleciona codigo/procedimento/porte]
    Select --> Payload[Payload do paciente]
    Payload --> Save[POST/PUT /api/pacientes]
    Save --> Validate[Valida permissao e CBHPM]
    Validate --> Persist[Salva User + Paciente]
    Persist --> Result[Retorna PacienteDto]
```

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

## Comunicacao com Azure

```mermaid
flowchart LR
    Front[Vercel Frontend] -->|HTTPS REST + JWT| API[Render Docker API]
    API -->|EF Core SQL| AzureSql[(Azure SQL Database)]
    API -->|SDK Azure.Storage.Blobs| Photos[(Blob profile-photos)]
    API -->|SDK Azure.Storage.Blobs| Files[(Blob patient-files)]
    API -->|Memoria local| CbhpmCache[IMemoryCache]
    API -. nao usado atualmente .-> Queue[(Azure Queue Storage / Service Bus)]
```

## Recursos Azure usados

| Recurso | Status | Uso |
| --- | --- | --- |
| Azure SQL Database | usado | banco relacional da aplicacao |
| Azure Blob Storage | usado | fotos e anexos |
| Azure Queue Storage / Service Bus | nao usado | reservado para funcionalidades assincronas futuras |

## Observacoes operacionais

- `IMemoryCache` reduz leituras repetidas da tabela CBHPM, mas e cache local por instancia.
- Azure SQL e Blob Storage sao recursos externos cobrados conforme plano/uso.
- Azure Queue nao gera custo neste projeto enquanto nao for criado/usado.
- Swagger e Scalar estao publicados em todos os ambientes para facilitar validacao e integracao.
