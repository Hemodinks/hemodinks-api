# Implementacao - Hemodinks API

Este documento resume a implementacao atual do backend.

## Estrutura

| Projeto | Conteudo |
| --- | --- |
| `HemodinksAPI.Domain` | entidades (`User`, `Paciente`, `Event`, `Licenca`, etc.) e utilitarios puros (`CpfUtils`, `DefaultUserPassword`) |
| `HemodinksAPI.Application` | features por modulo, CQRS, DTOs, validadores, contratos e regras de aplicacao |
| `HemodinksAPI.Infrastructure` | `AppDbContext`, migrations, seeders, JWT, storage Azure, notificacoes e hosted services |
| `HemodinksAPI.Api` | endpoints Minimal API, autenticacao, autorizacao, CORS, Swagger/Scalar e DI |
| `HemodinksAPI.Tests` | testes unitarios e de integracao |

## Padrao de fluxo

1. Endpoint recebe a requisicao HTTP.
2. Autenticacao JWT e policies verificam acesso.
3. Endpoint monta command/query e envia via MediatR.
4. `ValidationBehavior` executa validadores registrados.
5. Handler aplica regras, usa contratos da Application e persiste via `IAppDbContext`.
6. Infrastructure fornece implementacoes concretas de banco, storage, notificacao e hash.

## Modulos

### Usuarios

Responsavel por autenticacao, cadastro de usuarios, senha, foto de perfil e documentos de cadastro medico.

Classes principais:

- `CreateUserCommandHandler`
- `AuthenticateUserCommandHandler`
- `GetAllUsersQueryHandler`
- `JwtTokenService`
- `PasswordHasher`
- `UserPatientSyncService`
- `AzureBlobProfilePhotoStorage`

### Pacientes

Responsavel pelo cadastro clinico/administrativo do paciente, vinculo com usuario do perfil Pacientes, anexos e selecao de procedimento CBHPM.

Regras relevantes:

- CPF e email obrigatorios.
- CPF e email nao podem duplicar.
- Medico so gerencia pacientes vinculados a ele.
- Procedimentos CBHPM sao normalizados quando `CbhpmCodigo` e informado.

### CBHPM

Responsavel pela consulta paginada e importacao administrativa da tabela CBHPM geral.

Fluxo:

1. `GET /api/cbhpm` recebe filtros.
2. `GetCbhpmGeralQueryHandler` consulta `ICbhpmCache`.
3. `CbhpmCache` carrega todos os itens da tabela `CBHPMGeral` na primeira chamada.
4. Filtro e paginacao rodam em memoria.
5. Importacao e seed invalidam o cache.

### Dashboard

Responsavel por resumo e notificacoes. As consultas respeitam perfil e licenca do usuario autenticado.

O dashboard tambem tenta processar lembretes vencidos da agenda, mas esse processamento e resiliente: falhas no worker de notificacao nao impedem a abertura do painel.

### Agenda

Responsavel por eventos, lembretes e notificacoes.

Endpoints principais:

- `GET /api/events`
- `GET /api/events/medical-users`
- `POST /api/events`
- `PUT /api/events/{id}`
- `POST /api/events/{id}/complete`
- `DELETE /api/events/{id}`

Regras:

- Titulo obrigatorio.
- `End` deve ser maior que `Start`.
- Periodo de lembrete deve ficar entre 15 minutos e 7 dias.
- `NextReminderAt` direciona a consulta de lembretes vencidos.
- Evento concluido deixa de gerar novos lembretes.

### Licencas

Responsavel por trial, plano completo e features liberadas para medicos.

Features atuais:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Pacientes.Gerenciar`
- `Cbhpm.Consultar`

Policies:

- `Licenca.Dashboard.Visualizar`
- `Licenca.Pacientes.Visualizar`
- `Licenca.Pacientes.Gerenciar`
- `Licenca.Cbhpm.Consultar`

## Documentacao da API

A API expoe:

- Swagger UI: `/swagger`
- Scalar UI: `/scalar`
- OpenAPI JSON: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

O documento OpenAPI inclui:

- titulo `Hemodinks API`
- versao `v1`
- descricao dos modulos
- esquema de seguranca `Bearer`

## Persistencia

Banco relacional: SQL Server local, SQL Server em container ou Azure SQL Database.

Tabelas principais:

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

Migrations ficam em:

```text
HemodinksAPI.Infrastructure/Data/Migrations
```

Comandos EF Core:

```powershell
dotnet ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure --no-connect
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
```

## Arquivos e Azure Storage

Fotos de perfil:

- contrato: `IProfilePhotoStorage`
- implementacao: `AzureBlobProfilePhotoStorage`
- container padrao: `profile-photos`

Anexos:

- contrato: `IPatientFileStorage`
- implementacao: `AzureBlobPatientFileStorage`
- container padrao: `patient-files`

Se a connection string do Azure Storage nao estiver configurada, operacoes sem upload continuam funcionando; uploads retornam erro de configuracao.

## Processamento de lembretes

Implementacao atual:

- `EventNotificationHostedService`
- `EventReminderProcessor`
- `INotificationService`
- `NotificationService` fake/log

Esse desenho nao exige custo adicional de fila/worker externo. Para producao em escala, a interface permite trocar a implementacao por push, email, FCM, APNs, fila ou worker dedicado.

## Testes

```powershell
dotnet build HemodinksAPI.slnx
dotnet test HemodinksAPI.slnx --no-build
```

Cobertura existente valida:

- comandos e queries de usuarios
- regras de pacientes
- consulta CBHPM com cache
- importacao/seed CBHPM
- licencas
- endpoints de agenda
- dashboard resiliente a falha de lembretes
- permissoes e filtros principais
