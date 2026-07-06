# Implementacao - Hemodinks API

Este documento resume a implementacao atual do backend.

## Estrutura

| Projeto | Conteudo |
| --- | --- |
| `HemodinksAPI.Domain` | entidades (`User`, `Paciente`, `Event`, `Licenca`, `ConfiguracaoSistema`, `GrupoMedico`, etc.) e utilitarios puros |
| `HemodinksAPI.Application` | features por modulo, CQRS, DTOs, validadores, contratos e regras de aplicacao |
| `HemodinksAPI.Infrastructure` | `AppDbContext`, migrations, seeders, JWT, storage, filas, notificacoes e hosted services |
| `HemodinksAPI.Api` | endpoints Minimal API, autenticacao, autorizacao, CORS, Swagger/Scalar e DI |
| `HemodinksAPI.Workers` | Azure Functions para reset por email e exportacoes assincronas |
| `HemodinksAPI.Tests` | testes unitarios e de integracao |

## Padrao de fluxo

1. Endpoint recebe a requisicao HTTP.
2. JWT e policies verificam acesso.
3. Endpoint monta command/query e envia via MediatR.
4. `ValidationBehavior` executa validadores registrados.
5. Handler aplica regras, usa contratos da Application e persiste via `IAppDbContext`.
6. Infrastructure fornece banco, storage, notificacao, filas e hash.

## Modulos

### Usuarios e autenticacao

Responsavel por login, reset de senha, foto de perfil, documentos do cadastro medico e atualizacao cadastral.

Classes principais:

- `AuthenticateUserCommandHandler`
- `ResetUserPasswordByEmailCommandHandler`
- `ConfirmPasswordResetCommandHandler`
- `GetAllUsersQueryHandler`
- `GetUserProfilePhotoQueryHandler`
- `JwtTokenService`
- `PasswordHasher`
- `UserPatientSyncService`

### Pacientes e observacoes

Responsavel pelo cadastro clinico/administrativo do paciente, vinculo com usuario do perfil Paciente, anexos, observacoes e procedimentos CBHPM.

Regras relevantes:

- CPF e email sao unicos quando informados
- medico e controller podem criar paciente
- medico vinculado e controller podem editar dentro do escopo permitido
- observacoes suportam resposta, destinatario e marcacao de leitura
- procedimentos sao normalizados quando `CbhpmCodigo` e informado

### Dashboard

Responsavel por resumo e notificacoes. O modulo agrega agenda, observacoes nao lidas e indicadores operacionais respeitando perfil/licenca do usuario autenticado.

### Agenda e notificacoes

Responsavel por eventos, destinatarios permitidos, grupos medicos e lembretes internos.

Endpoints principais:

- `GET /api/events`
- `GET /api/events/notification-recipients`
- `POST /api/events/notifications/mark-read`
- `POST /api/events`
- `PUT /api/events/{id}`
- `POST /api/events/{id}/complete`

Implementacao principal:

- `EventCommandHandlers`
- `EventQueryHandlers`
- `AgendaNotificationQueryHandlers`
- `EventReminderProcessor`
- `EventNotificationHostedService`

### Faturamento medico

Responsavel por listar dados de faturamento medico a partir do cadastro de pacientes, pagamentos, glosas, procedimentos e anexos.

Classes principais:

- `GetAllFaturamentosMedicosQueryHandler`
- `FaturamentoMedicoScope`
- `FaturamentoMedicoFilters`

### Grupos medicos

Responsavel por criar grupos de medicos e disponibilizar destinatarios reutilizaveis para a agenda.

Classes principais:

- `CreateGrupoMedicoCommandHandler`
- `UpdateGrupoMedicoCommandHandler`
- `DeleteGrupoMedicoCommandHandler`
- `GrupoMedicoQueryHandlers`
- `MedicalGroupScope`

### Configuracao do sistema

Responsavel pelo nome e pela foto da empresa que o front usa em login, pontos de marca e configuracoes.

Classes principais:

- `GetConfiguracaoSistemaQuery`
- `GetConfiguracaoSistemaPhotoQuery`
- `UpdateConfiguracaoSistemaCommand`
- `ConfiguracaoSistemaRepository`

### Licencas

Responsavel por trial, plano completo e liberacao de features para medicos.

Features atuais:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Cbhpm.Consultar`

Policies relacionadas:

- `Licenca.Dashboard.Visualizar`
- `Licenca.Pacientes.Visualizar`
- `Licenca.Cbhpm.Consultar`

### CBHPM

Responsavel pela consulta paginada e importacao administrativa da tabela CBHPM geral.

Fluxo:

1. `GET /api/cbhpm` recebe filtros.
2. `GetCbhpmGeralQueryHandler` consulta `IAppDbContext`.
3. Filtros de codigo, procedimento e search usam `LIKE` no banco.
4. Ordenacao, contagem e paginacao rodam no SQL.
5. Importacao e seed invalidam o cache usado pela resolucao por codigo.

### Exportacoes assincronas

`POST /api/exports` enfileira jobs PDF/XLSX. A API continua dona de auth, rate limiting, idempotencia e payload. O `HemodinksAPI.Workers` executa o processamento pesado quando as filas estiverem habilitadas.

## Documentacao da API

Em `Development` e `Testing`, a API expoe:

- Swagger UI: `/swagger`
- Scalar UI: `/scalar`
- OpenAPI JSON: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

Em ambiente publicado, essas rotas exigem `ApiDocumentation__Enabled=true`.

## Persistencia

Tabelas principais:

- `Perfis`
- `Users`
- `UserArquivos`
- `Licencas`
- `Pacientes`
- `PacienteArquivos`
- `PacienteProcedimentos`
- `Observacoes`
- `AgendaNotifications`
- `Events`
- `Hospitais`
- `Convenios`
- `Opmes`
- `FaturamentosMedicos`
- `GrupoMedicos`
- `GrupoMedicoUsuarios`
- `ConfiguracoesSistema`
- `PasswordResetTokens`
- `IdempotencyRequests`
- `CBHPMGeral`

Migrations ficam em:

```text
HemodinksAPI.Infrastructure/Data/Migrations
```

## Arquivos e recursos externos

Fotos de perfil:

- contrato: `IProfilePhotoStorage`
- implementacoes: `AzureBlobProfilePhotoStorage`, `LocalDiskProfilePhotoStorage`, `FunctionBackedProfilePhotoStorage`

Anexos:

- contrato: `IPatientFileStorage`
- implementacoes: `AzureBlobPatientFileStorage`, `LocalDiskPatientFileStorage`, `FunctionBackedPatientFileStorage`

Reset por email:

- SMTP direto via `SmtpPasswordResetNotificationSender`
- envio mediado por fila/Function quando habilitado

## Testes

```powershell
dotnet build HemodinksAPI.slnx
dotnet test HemodinksAPI.slnx --no-build
```

Cobertura existente valida:

- autenticacao, senha e licencas
- queries e comandos de usuarios
- regras de pacientes e observacoes
- faturamento medico
- CBHPM com cache
- grupos medicos
- storage local/Azure
- endpoints principais e integracoes de API
