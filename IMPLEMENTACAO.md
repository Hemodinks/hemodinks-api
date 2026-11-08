# Implementacao - Hemodinks API

Este documento resume a implementacao atual do backend.

## Estrutura

| Projeto | Conteudo |
| --- | --- |
| `HemodinksAPI.Domain` | entidades (`Clinica`, `UsuarioGlobal`, `UsuarioClinica`, `User`, `Equipe`, `Paciente`, `AtendimentoCirurgico`, `Faturamento`, `ContaReceber`, `Event`, `Licenca`, `GrupoMedico`, etc.), enums (`TipoCirurgia`, `StatusCirurgia`) e utilitarios puros |
| `HemodinksAPI.Application` | features por modulo, CQRS, DTOs, validadores, contratos e regras de aplicacao |
| `HemodinksAPI.Infrastructure` | `AppDbContext`, migrations, seeders, JWT, storage, filas, notificacoes e hosted services |
| `HemodinksAPI.Api` | endpoints Minimal API, autenticacao, autorizacao, CORS, Swagger/Scalar e DI |
| `HemodinksAPI.Workers` | Azure Functions para reset por email, exportacoes, emails de eventos e uploads |
| `HemodinksAPI.Tests` | xUnit v3: unitarios, integracao (InMemory/SQLite) e `*SqlServerTests` |

## Padrao de fluxo

1. Endpoint recebe a requisicao HTTP.
2. Middlewares resolvem a sessao e a clinica (`ClinicaId` do JWT) e bloqueiam modulos nao contratados; JWT e policies verificam acesso.
3. Endpoint monta command/query e envia via MediatR.
4. `ValidationBehavior` executa validadores registrados.
5. Handler aplica regras, usa contratos da Application e persiste via `IAppDbContext`.
6. Infrastructure fornece banco, storage, notificacao, filas e hash.

## Modulos

### Multiclinica, sessao e plataforma

Responsavel por clinicas (plano, modulos, assinatura), identidade global (`UsuarioGlobal`) e vinculos por clinica
(`UsuarioClinica`), sessao com refresh em cookie `HttpOnly` (`/api/session/*`), troca de clinica pelo SuperAdministrador
e auditoria da plataforma. Principais pontos: `ClinicaResolutionMiddleware`, `AuthenticationSessionMiddleware`,
`ClinicaModuleAccessMiddleware`, filtro global de tenant em `AppDbContext` e `Features/Clinics`/`Features/Sessions`.

### Equipes

Identidade coletiva do perfil `Equipe`, membros, operadores com PIN e identificacao por operador
(`/api/equipes`, `/api/equipe-auth/*`). Operacoes sensiveis exigem `EquipeOperacaoSensivel` e sao auditadas
por `EquipeMutationAuditMiddleware`. Codigo em `Features/Teams`.

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
- `EventNotificationHostedService` (somente com `EventReminders__RunHostedProcessor=true`; em producao os lembretes sao processados nas consultas)
- `EventEmailDeliveryHostedService` e Function `SendEventEmail` para emails de eventos

### Faturamento medico

Responsavel por listar dados de faturamento medico a partir do cadastro de pacientes, pagamentos, glosas, procedimentos e anexos.

Classes principais:

- `GetAllFaturamentosMedicosQueryHandler`
- `FaturamentoMedicoScope`
- `FaturamentoMedicoFilters`

### Financeiro

Atendimentos cirurgicos, faturamentos (itens, status, glosas, recursos e retorno), contas a receber, recebimentos,
comprovantes, estornos, precos por convenio, resumo e auditoria financeira. Codigo em `Features/Financeiro`;
endpoints em `FinanceiroEndpointExtensions` com `FinanceiroAuditFilter`.

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

`POST /api/exports` monta no servidor o snapshot dos dados (`FileExportSnapshotBuilder`, com as mesmas queries e escopo do perfil) e o enfileira para `GenerateFileExport`. A API continua dona de auth, rate limiting, limite de tamanho da mensagem e filtros (`tipoCirurgia`, `statusCirurgia`). O `HemodinksAPI.Workers` gera o arquivo PDF/XLSX.

### LGPD e monitoramento

Aceites legais, preferencias de privacidade e consulta/limpeza de erros e eventos de seguranca (`/api/monitoramento/*`).

## Documentacao da API

Em `Development` e `Testing`, a API expoe:

- Swagger UI: `/swagger`
- Scalar UI: `/scalar`
- OpenAPI JSON: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

Em ambiente publicado, essas rotas exigem `ApiDocumentation__Enabled=true`.

## Persistencia

A lista de tabelas por area esta no [README](README.md#banco-de-dados); a configuracao de cada entidade fica em `HemodinksAPI.Infrastructure/Data/Configurations`.

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
dotnet test HemodinksAPI.slnx --no-build --filter "FullyQualifiedName!~LoginBrowserTests"
```

Cobertura existente valida:

- autenticacao, senha e licencas
- queries e comandos de usuarios
- regras de pacientes e observacoes
- faturamento medico
- CBHPM (consulta SQL e cache por codigo)
- isolamento por clinica, sessao, equipes e matriz de autorizacao
- financeiro, exportacoes e migrations (incluindo replay completo em SQL Server)
- grupos medicos
- storage local/Azure
- endpoints principais e integracoes de API
