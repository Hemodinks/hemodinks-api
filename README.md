# Hemodinks API

Documentação de autorização: [matriz detalhada](docs/MATRIZ_PERFIS_PERMISSOES.md) e [relatório visual em PDF](docs/MATRIZ_PERFIS_PERMISSOES_VISUAL.pdf).

API ASP.NET Core/.NET 10 para usuarios, pacientes, observacoes, agenda/notificacoes, faturamento medico, grupos medicos, licencas, configuracao do sistema, arquivos, exportacoes e consulta CBHPM.

## Stack

- .NET 10 e ASP.NET Core Minimal APIs
- Clean Architecture pragmatica em `Domain`, `Application`, `Infrastructure` e `Api`
- CQRS com MediatR e pipeline de validacao
- Entity Framework Core 10 com SQL Server/Azure SQL
- JWT Bearer com autorizacao por perfil e por licenca
- Serilog para logs estruturados
- New Relic APM opcional
- Azure Blob Storage para fotos e anexos
- Azure Queue Storage + Azure Functions opcionais para reset por email e exportacoes
- `BackgroundService` interno para lembretes da agenda
- `IMemoryCache` para CBHPM
- Swagger/OpenAPI e Scalar
- Docker, Render e GitHub Actions

## Arquitetura

```text
HemodinksAPI.Domain
  Entidades, constantes e utilitarios puros.

HemodinksAPI.Application
  Commands, queries, handlers, DTOs, validadores, contratos e regras.

HemodinksAPI.Infrastructure
  EF Core, migrations, seeders, JWT, storage, notificacoes, filas e servicos concretos.

HemodinksAPI.Api
  Minimal APIs, auth, CORS, Swagger/Scalar e composition root.

HemodinksAPI.Workers
  Azure Functions isoladas para jobs assincronos.
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
| Front producao | `https://hemodinks.gestao-saude.tec.br` |
| Front producao legado | `https://hemodinks-saude.vercel.app` |
| Front homologacao principal | `https://hemodinks-homologacao.gestao-saude.tec.br` |
| Front homologacao legado | `https://hemodinks-homologacao.vercel.app` |
| Front confirmation Render opcional | `https://hemodinks-front-confirmation.onrender.com` |
| API | configure em `VITE_API_URL`, por exemplo `https://hemodinks-api.onrender.com` |

Em ambiente publicado, Swagger/Scalar/OpenAPI so aparecem quando `ApiDocumentation__Enabled=true`.

## Como executar

### Docker Compose

```powershell
Copy-Item .env.example .env
# Edite MSSQL_SA_PASSWORD e JWT_SECRET_KEY no .env
docker compose up -d --build api workers
```

A API aplica migrations no startup, cria perfis, seeda usuarios quando necessario e carrega CBHPM de `HemodinksAPI.Infrastructure/Data/SeedData/cbhpm-geral.json` quando o seed estiver habilitado.

O compose principal usa `restart: unless-stopped` para `api`, `workers`, `sqlserver` e `azurite`. Depois da primeira subida com o comando acima, o Docker Desktop volta a iniciar o stack da API junto com o engine. Se o compose avulso de `sqlserver/` ja tiver sido usado antes, pare o container antigo uma vez:

```powershell
docker compose -f sqlserver/docker-compose.yml stop
docker update --restart=no hemodinks-sqlserver-dev
```

### Docker Compose com observabilidade

```powershell
Copy-Item .env.example .env
docker compose -f docker-compose.observability.yml up -d
```

Endpoints locais do compose observavel:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Dashboard Aspire | `http://localhost:18888` |
| Collector OTLP gRPC | `http://localhost:4317` |
| Collector OTLP HTTP | `http://localhost:4318` |

### Desenvolvimento local

```powershell
dotnet restore
dotnet tool restore
dotnet user-secrets set --project HemodinksAPI.Api "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Issuer" "HemodinksAPI"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Audience" "HemodinksAPI"
dotnet run --project HemodinksAPI.Api
```

### Desenvolvimento local com Aspire

```powershell
dotnet restore HemodinksAPI.slnx
npm install --prefix ..\hemodinks-front
dotnet run --project Hemodinks.AppHost
```

O `AppHost` consegue subir API, front React/Vite e dashboard de observabilidade local no mesmo ambiente.

## Idempotencia

Os endpoints abaixo aceitam `Idempotency-Key`:

- `POST /api/events/`
- `POST /api/users/password/reset`
- `POST /api/users/password/reset/confirm`

Com a mesma chave e o mesmo payload:

- primeira execucao bem-sucedida retorna `Idempotency-Status: stored`
- retries reaproveitam a resposta e retornam `Idempotency-Status: replayed`
- mesma chave com payload diferente retorna `409 Conflict`

## Autenticacao, perfis e licencas

O login retorna um JWT usado em:

```text
Authorization: Bearer <token>
```

O login tambem grava um refresh token rotativo em cookie `HttpOnly`. A sessao expira depois de 30 minutos sem requisicoes autenticadas, mas permanece ativa durante o uso continuo. O refresh, isoladamente, nao conta como atividade e nao consegue manter uma sessao ociosa viva.

Clientes web devem usar `credentials: "include"` no login, refresh e logout. Ao se aproximar do vencimento do JWT (ou ao receber `401`), devem enviar um corpo JSON vazio (`{}`) para `POST /api/session/renovar`, armazenar o novo JWT e repetir a requisicao original. Interacoes locais que nao chamam a API podem ser registradas, com debounce, em `POST /api/session/atividade`. O logout tambem recebe `{}`.

Perfis seedados:

| Id | Perfil |
| --- | --- |
| 1 | Administrador |
| 2 | Medicos |
| 3 | Paciente |
| 4 | Controller |
| 5 | SuperAdministrador |
| 6 | Equipe |

Regras principais:

- Administrador gerencia usuarios, pacientes, agenda, licencas, grupos medicos, configuracao do sistema e exclusoes.
- Medico acessa seu proprio cadastro, pacientes vinculados, faturamento filtrado pelo seu escopo e agenda.
- Paciente acessa o proprio cadastro quando houver vinculo.
- Controller acessa pacientes, faturamento e operacoes liberadas por policy, sem dashboard nem agenda no front atual.
- Equipe usa uma identidade coletiva com identificacao individual do operador e acessa somente os dados da equipe configurada.
- Licencas controlam dashboard, pacientes e CBHPM para medicos.
- A assinatura comercial pertence a `Clinica`; licencas individuais de medicos continuam existindo como uma segunda camada de compatibilidade e liberacao de features.
- SuperAdministrador lista e provisiona clinicas pelos endpoints de plataforma. Para navegar, ele solicita um novo token tenant-scoped em `/api/session/selecionar-clinica`; headers nao alteram a clinica de uma sessao autenticada.

Features atuais de licenca:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Cbhpm.Consultar`

## Endpoints principais

### Plataforma multiclinica

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/api/platform/clinicas` | superadmin | lista todas as clinicas |
| `GET` | `/api/platform/clinicas/{id}` | superadmin | detalhe, assinatura e total de usuarios |
| `POST` | `/api/platform/clinicas` | superadmin | provisiona clinica, administrador, identidade local do superadmin e catalogos iniciais |
| `PUT` | `/api/platform/clinicas/{id}` | superadmin | atualiza nome, foto, cadastro, ativacao, plano e assinatura |
| `DELETE` | `/api/platform/clinicas/{id}` | superadmin | desativa logicamente a clinica e preserva seus dados |
| `GET` | `/api/platform/auditoria` | superadmin | consulta paginada da auditoria de plataforma |
| `GET` | `/api/public/clinicas` | publico | lista minima das clinicas ativas para o seletor do login |
| `GET` | `/api/public/clinicas/{slug}/foto` | publico | retorna a foto publica da clinica ativa |
| `GET` | `/api/session/clinicas` | autenticado | lista associacoes ativas da identidade global |
| `POST` | `/api/session/selecionar-clinica` | autenticado | valida `UsuarioClinica` e emite novo JWT para a clinica |

O seletor publico do login e servido pelo arquivo `HemodinksAPI.Api/Data/public-clinics.json`,
carregado uma vez em memoria sem consultar o SQL Server. O cadastro, a edicao, a ativacao e a
desativacao de clinicas sincronizam esse catalogo automaticamente. As demais operacoes continuam
usando exclusivamente o banco de dados. O caminho pode ser alterado com
`PublicClinicDirectory__FilePath`; em ambientes com filesystem efemero, configure esse caminho
em um volume persistente para que as alteracoes sobrevivam a novos deploys.

Configure os emails autorizados com `Platform__SuperAdminEmails__0`. No startup, o usuario correspondente e promovido e recebe uma associacao `UsuarioClinica` em cada clinica ativa. Administradores comuns permanecem restritos as associacoes explicitamente cadastradas.

`UsuarioGlobal` guarda a credencial unica; `UsuarioClinica` liga essa identidade ao usuario local, clinica e perfil. O `ClinicaId` do JWT e o tenant efetivo. Consultas sem contexto resolvido falham fechadas; gravacoes com `ClinicaId` divergente e relacionamentos internos entre clinicas diferentes sao recusados antes do `SaveChanges`.

O nome e a foto institucionais pertencem a `Clinica` e somente o SuperAdministrador pode altera-los pelo CRUD de plataforma. O antigo `PUT /api/configuracoes-sistema/current` nao e mais exposto.

### Auth e usuarios

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `POST` | `/api/users/authenticate` | nao | login JWT |
| `POST` | `/api/users/password/reset` | nao | solicitar reset de senha |
| `POST` | `/api/users/password/reset/confirm` | nao | confirmar reset com token temporario |
| `GET` | `/api/users` | admin | lista paginada de usuarios |
| `POST` | `/api/users` | admin | cria usuario |
| `GET` | `/api/users/{id}` | sim | detalhe de usuario |
| `GET` | `/api/users/{id}/foto-perfil` | sim | foto de perfil |
| `PUT` | `/api/users/{id}` | sim | atualiza usuario |
| `DELETE` | `/api/users/{id}` | admin | exclui usuario |
| `PUT` | `/api/users/{id}/password` | sim | altera senha |
| `PUT` | `/api/users/{id}/password/reset` | admin | reset administrativo para senha padrao |
| `POST` | `/api/users/{id}/arquivos` | sim | upload de documento medico |
| `DELETE` | `/api/users/{id}/arquivos/{arquivoId}` | sim | exclui documento medico |

### Pacientes, observacoes e catalogos

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/api/pacientes` | licenca | lista paginada de pacientes |
| `GET` | `/api/pacientes/{id}` | licenca | detalhe do paciente |
| `POST` | `/api/pacientes` | admin/medico/controller | cria paciente |
| `PUT` | `/api/pacientes/{id}` | admin/medico/controller | atualiza paciente |
| `DELETE` | `/api/pacientes/{id}` | admin | exclui paciente |
| `POST` | `/api/pacientes/{id}/arquivos` | admin/medico vinculado/controller | upload de anexo |
| `DELETE` | `/api/pacientes/{id}/arquivos/{arquivoId}` | admin/medico vinculado/controller | exclui anexo |
| `GET` | `/api/pacientes/{id}/observacoes` | sim | lista observacoes do paciente |
| `POST` | `/api/pacientes/{id}/observacoes` | sim | cria observacao |
| `POST` | `/api/pacientes/{id}/observacoes/marcar-lidas` | sim | marca observacoes como lidas |
| `GET` | `/api/hospitais` | sim | lista hospitais |
| `GET` | `/api/convenios` | sim | lista convenios |
| `GET` | `/api/opme` | sim | lista fornecedores OPME |
| `GET` | `/api/cbhpm` | licenca | consulta CBHPM paginada |
| `POST` | `/api/cbhpm/import` | admin | importa/substitui itens CBHPM |

### Dashboard, agenda, faturamento e grupos

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/api/dashboard/summary` | licenca | resumo do dashboard |
| `GET` | `/api/dashboard/notifications` | licenca | notificacoes do dashboard |
| `GET` | `/api/events` | sim | lista eventos por periodo |
| `GET` | `/api/events/{id}` | sim | detalhe do evento |
| `GET` | `/api/events/medical-users` | sim | medicos ativos da agenda |
| `GET` | `/api/events/notification-recipients` | sim | usuarios e grupos elegiveis para notificacao |
| `POST` | `/api/events/notifications/mark-read` | sim | marca notificacoes da agenda como lidas |
| `POST` | `/api/events` | sim | cria evento |
| `PUT` | `/api/events/{id}` | sim | atualiza evento |
| `POST` | `/api/events/{id}/complete` | sim | conclui evento |
| `DELETE` | `/api/events/{id}` | sim | exclui evento |
| `GET` | `/api/faturamentos-medicos` | sim | lista paginada de faturamentos medicos |
| `GET` | `/api/grupos-medicos` | admin | lista grupos medicos |
| `GET` | `/api/grupos-medicos/{id}` | admin | detalhe do grupo |
| `GET` | `/api/grupos-medicos/medicos` | sim | medicos disponiveis conforme escopo |
| `POST` | `/api/grupos-medicos` | policy | cria grupo medico |
| `PUT` | `/api/grupos-medicos/{id}` | admin | atualiza grupo medico |
| `DELETE` | `/api/grupos-medicos/{id}` | admin | exclui grupo medico |

### Configuracao do sistema, licencas e exportacoes

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/api/configuracoes-sistema/current` | nao | configuracao publica do sistema |
| `GET` | `/api/configuracoes-sistema/current/foto-empresa` | nao | foto da empresa |
| `PUT` | `/api/configuracoes-sistema/current` | admin | atualiza nome e foto da empresa |
| `GET` | `/api/licencas/current` | sim | licenca do usuario autenticado |
| `GET` | `/api/licencas/users/{userId}` | admin | consulta licenca de medico |
| `PUT` | `/api/licencas/users/{userId}` | admin | atualiza licenca |
| `POST` | `/api/licencas/users/{userId}/liberar-completa` | admin | libera plano completo |
| `POST` | `/api/exports` | sim | enfileira exportacao PDF/XLSX |
| `GET` | `/healthz` | nao | health check |

## Agenda, notificacoes e observacoes

A agenda permite:

- eventos com `title`, `description`, `start`, `end`
- notificacao para usuario, perfil medico, usuarios especificos e grupos medicos
- lembretes com `reminderPeriodMinutes`
- controle de leitura das notificacoes internas

O dashboard agrega:

- eventos proximos
- notificacoes da agenda
- observacoes de pacientes nao lidas

## CBHPM

A tabela `CBHPMGeral` e criada por migration e pode receber seed automatico a partir do JSON gerado do PDF da tabela. O backend usa `IMemoryCache` para acelerar filtros e paginacao em memoria apos o primeiro carregamento.

Consulta tipica:

```http
GET /api/cbhpm?page=1&pageSize=10&codigo=1.01&procedimento=consulta&porte=2B
Authorization: Bearer <token>
```

## Banco de dados

Entidades principais:

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

Em desenvolvimento, migrations podem rodar no startup com `Database__RunMigrationsOnStartup=true`.
Em produção, mantenha essa opção desabilitada e use o workflow manual `Apply Production Migrations`
antes de publicar a imagem que depende do novo schema.

## Documentacao interativa

Swagger e Scalar ficam ativos automaticamente em `Development` e `Testing`. Em ambiente publicado, habilite `ApiDocumentation__Enabled=true` para expor:

- `/swagger`
- `/scalar`
- `/openapi/v1.json`
- `/swagger/v1/swagger.json`

O documento OpenAPI inclui esquema `Bearer`.

## Testes

```powershell
dotnet build HemodinksAPI.slnx
pwsh ./scripts/Test-Migrations.ps1 -NoBuild
dotnet test HemodinksAPI.slnx --no-build
```

## Documentos relacionados

- [Implementacao](./IMPLEMENTACAO.md)
- [Deploy](./docs/deployment.md)
- [Documentacao tecnica](./docs/TECHNICAL_DOCUMENTATION.md)
- [Production readiness](./PRODUCTION_READINESS.md)
- [Primeira execucao](./PRIMEIRA_EXECUCAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Exemplos HTTP](./API.http)
- [Documentacao tecnica PDF](./docs/Hemodinks-Documentacao-Tecnica.pdf)
