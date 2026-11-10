# Hemodinks API

Documentação de autorização: [matriz detalhada](docs/MATRIZ_PERFIS_PERMISSOES.md) e [relatório visual em PDF](docs/MATRIZ_PERFIS_PERMISSOES_VISUAL.pdf).

API ASP.NET Core/.NET 10 multiclinica para usuarios, equipes, pacientes e cirurgias, observacoes, agenda/notificacoes, faturamento medico, financeiro (atendimentos cirurgicos, faturamentos, glosas, contas a receber e precos por convenio), grupos medicos, licencas, plataforma de clinicas, arquivos, exportacoes e consulta CBHPM.

## Stack

- .NET 10 e ASP.NET Core Minimal APIs
- Clean Architecture pragmatica em `Domain`, `Application`, `Infrastructure` e `Api`
- CQRS com MediatR e pipeline de validacao
- Entity Framework Core 10 com SQL Server/Azure SQL
- JWT Bearer com autorizacao por perfil e por licenca
- Serilog para logs estruturados
- New Relic APM opcional
- Azure Blob Storage para fotos e anexos
- Azure Queue Storage + Azure Functions (`HemodinksAPI.Workers`) para reset de senha por email, exportacoes, emails de eventos e uploads
- lembretes da agenda processados por `EventReminderProcessor`: `BackgroundService` em desenvolvimento/compose e sob demanda nas consultas de agenda e dashboard em producao
- `IMemoryCache` para resolver procedimentos CBHPM por codigo
- Swagger/OpenAPI e Scalar
- .NET Aspire (`Hemodinks.AppHost`) para desenvolvimento local
- Docker, Azure Container Apps (producao, blue/green), Render (homologacao) e GitHub Actions

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
  Azure Functions isoladas: GenerateFileExport, SendPasswordResetEmail(Sync),
  SendEventEmail, UploadPatientFile e UploadProfilePhoto.

HemodinksAPI.Tests
  xUnit v3: unitarios, integracao (InMemory/SQLite) e *SqlServerTests.

Hemodinks.AppHost / Hemodinks.ServiceDefaults
  Orquestracao local com .NET Aspire.

tools/
  Hemodinks.SchemaGuard (verificador de historico de migrations) e ghcr_retention.
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
| Health check (prontidao) | `http://localhost:5000/healthz` e `/readyz` |
| Liveness | `http://localhost:5000/livez` |
| Swagger UI | `http://localhost:5000/swagger` |
| Scalar UI | `http://localhost:5000/scalar` |
| OpenAPI JSON | `http://localhost:5000/openapi/v1.json` |
| Swagger JSON | `http://localhost:5000/swagger/v1/swagger.json` |

Ambientes publicados:

| Recurso | URL |
| --- | --- |
| Front producao | `https://hemodinks.gestao-saude.tec.br` |
| Front homologacao principal | `https://hemodinks-homologacao.gestao-saude.tec.br` |
| API producao | Azure Container Apps (blue/green); a URL publica fica em `VITE_API_URL` do front |
| API homologacao | `https://hemodinks-api-1-90nb.onrender.com` (Render, servico `hemodinks-api-1`) |

Em ambiente publicado, Swagger/Scalar/OpenAPI so aparecem quando `ApiDocumentation__Enabled=true`.

## Como executar

### Docker Compose

```powershell
Copy-Item .env.example .env
# Edite MSSQL_SA_PASSWORD e JWT_SECRET_KEY no .env
docker compose up -d --build api workers
```

No compose, a API aplica migrations no startup (`Database__RunMigrationsOnStartup=true`), cria perfis, seeda usuarios quando necessario e carrega CBHPM de `HemodinksAPI.Infrastructure/Data/SeedData/cbhpm-geral.json` (`Seed__CbhpmOnStartup=true`). Em `appsettings.json` esses valores sao `false`; em `Development` migrations e manutencao sao habilitadas.

O compose principal usa `restart: unless-stopped` para `api`, `workers`, `sqlserver` e `azurite`. Depois da primeira subida com o comando acima, o Docker Desktop volta a iniciar o stack da API junto com o engine. Se o compose avulso de `sqlserver/` ja tiver sido usado antes, pare o container antigo uma vez:

```powershell
docker compose -f sqlserver/docker-compose.yml stop
docker update --restart=no hemodinks-sqlserver-dev
```

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

O `AppHost` sobe a API e o front React/Vite (`../hemodinks-front`, script `dev`) com o dashboard local do Aspire.

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

Clientes web devem usar `credentials: "include"` no login, refresh e logout. Antes de vencer o JWT individual, enviam
`{ "sessionId": "<claim sid>", "membershipId": <claim usuarioClinicaId>, "active": false }`
para `POST /api/session/renovar`, com o header `X-Session-Refresh: 1`. O cookie HttpOnly e a credencial;
os IDs apenas vinculam a renovacao a sessao e clinica esperadas, inclusive entre abas. Origens fora do CORS
configurado sao recusadas. O novo JWT vem em `token`, junto com `idleTimeoutMinutes`; nunca se devolve
o refresh token no JSON. Um conflito de gravacao retorna 409 e permite nova tentativa limitada, sem apagar cookies.
Um token antigo ou uma sessao revogada nao pode remover o cookie de uma sessao atual.

Interacoes locais que nao chamam a API sao registradas no maximo uma vez por minuto com um POST autenticado
em `/api/session/atividade`. Isso atualiza a atividade sem trocar o JWT. `active: true` no refresh so e usado
quando houve nova interacao ainda nao registrada. Timers e consultas automaticas nao contam como atividade
na interface; apos o limite de inatividade, ela encerra a sessao. Abas da mesma sessao compartilham sinais de
atividade via BroadcastChannel, quando disponivel. Rotacoes sao serializadas com Web Locks quando disponivel.

`POST /api/session/sair` usa o mesmo corpo e header do refresh e revoga apenas a sessao correspondente ao cookie
ou ao JWT ainda valido enviado no Authorization. A revogacao e atomica no banco para prevalecer sobre atividade
ou rotacao concorrente; a identidade e o vinculo do token devem corresponder ao corpo da solicitacao.
Equipes usam `/api/session/renovar-equipe` com o JWT ainda valido: o servidor revalida clinica, vinculo,
versoes da equipe e operador, preservando identificacao nominal e exigencias de troca de senha/PIN.
Tokens de equipe ja expirados exigem nova identificacao; tokens individuais podem ser recuperados pelo
cookie enquanto a sessao no servidor continuar ativa. JWTs antigos sem sessao/equipe continuam exigindo novo login.

Em navegadores que bloqueiam cookies entre sites, use uma origem de API no mesmo site do frontend ou um proxy;
CORS sozinho nao remove esse bloqueio.

Perfis seedados:

| Id | Perfil |
| --- | --- |
| 1 | Administrador |
| 2 | Medicos |
| 3 | Pacientes |
| 4 | Controller |
| 5 | SuperAdministrador |
| 6 | Equipe |

Regras principais:

- Administrador gerencia usuarios, pacientes, agenda, licencas, grupos medicos, configuracao do sistema e exclusoes.
- Medico acessa seu proprio cadastro, pacientes vinculados (inclusive exclusao no escopo), faturamento e atendimentos filtrados pelo seu escopo e agenda.
- Paciente acessa o proprio cadastro quando houver vinculo.
- Controller acessa pacientes, faturamento medico, financeiro (faturamentos, glosas, contas a receber, precos), grupos medicos e agenda propria; nao exclui pacientes.
- Equipe usa uma identidade coletiva com identificacao individual do operador e acessa somente os dados da equipe configurada.
- Licencas controlam dashboard, pacientes e CBHPM para medicos.
- A assinatura comercial pertence a `Clinica`; licencas individuais de medicos continuam existindo como uma segunda camada de compatibilidade e liberacao de features.
- Administrador consulta e atualiza a propria clinica e suas equipes em `/api/platform/clinicas`; somente o SuperAdministrador cria, desativa e alterna entre clinicas.
- SuperAdministrador lista e provisiona clinicas pelos endpoints de plataforma. Para navegar, ele solicita um novo token tenant-scoped em `/api/session/selecionar-clinica`; headers nao alteram a clinica de uma sessao autenticada.

Features atuais de licenca:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Pacientes.Gerenciar`
- `Cbhpm.Consultar`

A matriz completa de perfis x recursos esta em [docs/MATRIZ_PERFIS_PERMISSOES.md](docs/MATRIZ_PERFIS_PERMISSOES.md).

## Endpoints principais

Inventario gerado por `python3 scripts/generate_route_inventory.py` a partir dos `Map*` e `RequireAuthorization` dos endpoints. Legenda: Adm = Administrador,
Méd = Medicos, Pac = Pacientes, Ctrl = Controller, SA = SuperAdministrador, Eq = Equipe. "Autenticado" aceita
qualquer perfil, mas o handler restringe ao proprio registro, ao escopo medico/equipe ou a clinica da sessao.
Rotas da plataforma restringem o Administrador a clinica atual. Rotas de pacientes, faturamento medico, grupos
medicos, agenda e usuarios tambem dependem do modulo contratado no plano `Parcial` (403 quando ausente).

### Sessão

| Método | Rota | Acesso |
| --- | --- | --- |
| `POST` | `/api/session/atividade` | Autenticado |
| `GET` | `/api/session/clinicas` | Autenticado |
| `POST` | `/api/session/renovar` | Cookie de sessão (HttpOnly) |
| `POST` | `/api/session/renovar-equipe` | Autenticado |
| `POST` | `/api/session/restaurar` | Cookie de sessão (HttpOnly) |
| `POST` | `/api/session/sair` | Cookie de sessão (HttpOnly) |
| `POST` | `/api/session/selecionar-clinica` | Autenticado |

### Usuários

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/users` | Adm, SA, Eq |
| `POST` | `/api/users` | Adm, SA |
| `POST` | `/api/users/authenticate` | Público |
| `POST` | `/api/users/email/change` | Autenticado |
| `POST` | `/api/users/email/change/cancel` | Autenticado |
| `POST` | `/api/users/email/change/confirm` | Autenticado |
| `GET` | `/api/users/email/{email}` | Adm, SA |
| `POST` | `/api/users/login-context` | Público |
| `POST` | `/api/users/password/reset` | Público |
| `POST` | `/api/users/password/reset/confirm` | Público |
| `POST` | `/api/users/password/temporary/complete` | Autenticado |
| `GET` | `/api/users/perfis` | Adm, SA |
| `GET` | `/api/users/{id}` | Autenticado |
| `PUT` | `/api/users/{id}` | Autenticado |
| `DELETE` | `/api/users/{id}` | Adm, SA |
| `POST` | `/api/users/{id}/arquivos` | Autenticado |
| `DELETE` | `/api/users/{id}/arquivos/{arquivoId}` | Autenticado |
| `GET` | `/api/users/{id}/arquivos/{arquivoId}/download` | Autenticado |
| `GET` | `/api/users/{id}/foto-perfil` | Autenticado |
| `PUT` | `/api/users/{id}/password` | Autenticado |
| `PUT` | `/api/users/{id}/password/reset` | Adm, SA |

### Equipes — autenticação

| Método | Rota | Acesso |
| --- | --- | --- |
| `POST` | `/api/equipe-auth/identificar` | Login da equipe (rate limit) |
| `PUT` | `/api/equipe-auth/pin` | Eq |

### Equipes

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/equipes` | Adm, SA |
| `POST` | `/api/equipes` | Adm, SA |
| `PUT` | `/api/equipes/{id}` | Adm, SA |
| `POST` | `/api/equipes/{id}/membros` | Adm, SA |
| `DELETE` | `/api/equipes/{id}/membros/{userId}` | Adm, SA |
| `PUT` | `/api/equipes/{id}/operadores/{operadorId}/bloqueio` | Adm, SA |
| `POST` | `/api/equipes/{id}/operadores/{operadorId}/pin` | Adm, SA |

### Plataforma — clínicas

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/platform/clinicas` | Adm, SA |
| `POST` | `/api/platform/clinicas` | SA |
| `GET` | `/api/platform/clinicas/{id}` | Adm, SA |
| `PUT` | `/api/platform/clinicas/{id}` | Adm, SA |
| `DELETE` | `/api/platform/clinicas/{id}` | SA |
| `GET` | `/api/platform/clinicas/{id}/equipes` | Adm, SA |
| `GET` | `/api/platform/clinicas/{id}/equipes/usuarios` | Adm, SA |
| `PUT` | `/api/platform/clinicas/{id}/equipes/{teamId}` | Adm, SA |
| `POST` | `/api/platform/clinicas/{id}/equipes/{teamId}/membros` | Adm, SA |
| `DELETE` | `/api/platform/clinicas/{id}/equipes/{teamId}/membros/{userId}` | Adm, SA |
| `POST` | `/api/platform/clinicas/{id}/equipes/{teamId}/operadores/{operatorId}/pin` | Adm, SA |

### Plataforma — auditoria

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/platform/auditoria` | SA |

### Clínicas — público

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/public/clinicas` | Público |
| `GET` | `/api/public/clinicas/{slug}/foto` | Público |

### Configuração do sistema

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/configuracoes-sistema/current` | Público |
| `GET` | `/api/configuracoes-sistema/current/foto-empresa` | Público |

### Aceites legais

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/legal-acceptances/current` | Autenticado |
| `POST` | `/api/legal-acceptances/current` | Autenticado |

### Preferências de privacidade

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/privacy-preferences/current` | Autenticado |
| `PUT` | `/api/privacy-preferences/current` | Autenticado |

### Dashboard

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/dashboard/notifications` | Autenticado + licença `Dashboard.Visualizar` |
| `GET` | `/api/dashboard/summary` | Autenticado + licença `Dashboard.Visualizar` |

### Pacientes e cirurgias

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/pacientes` | Autenticado |
| `POST` | `/api/pacientes` | Adm, Méd, Ctrl, SA, Eq (Eq: operador identificado) |
| `GET` | `/api/pacientes/pendencias/data-consolidacao` | Adm, Méd, Ctrl, SA |
| `GET` | `/api/pacientes/{id}` | Autenticado |
| `PUT` | `/api/pacientes/{id}` | Adm, Méd, Ctrl, SA, Eq (Eq: operador identificado) |
| `DELETE` | `/api/pacientes/{id}` | Adm, Méd, SA |
| `POST` | `/api/pacientes/{id}/arquivos` | Adm, Méd, Ctrl, SA, Eq (Eq: operador identificado) |
| `DELETE` | `/api/pacientes/{id}/arquivos/{arquivoId}` | Adm, Méd, Ctrl, SA, Eq (Eq: operador identificado) |
| `GET` | `/api/pacientes/{id}/arquivos/{arquivoId}/download` | Autenticado |
| `GET` | `/api/pacientes/{id}/observacoes` | Adm, Méd, Ctrl, SA, Eq |
| `POST` | `/api/pacientes/{id}/observacoes` | Adm, Méd, Ctrl, SA, Eq (Eq: operador identificado) |
| `POST` | `/api/pacientes/{id}/observacoes/marcar-lidas` | Adm, Méd, Ctrl, SA, Eq |
| `GET` | `/api/pacientes/{id}/resumo-financeiro` | Autenticado |

### Atendimentos cirúrgicos

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/atendimentos-cirurgicos` | Adm, Méd, Ctrl, SA |
| `POST` | `/api/atendimentos-cirurgicos` | Adm, Méd, Ctrl, SA |
| `GET` | `/api/atendimentos-cirurgicos/{id}` | Adm, Méd, Ctrl, SA |
| `PUT` | `/api/atendimentos-cirurgicos/{id}` | Adm, Méd, Ctrl, SA |
| `DELETE` | `/api/atendimentos-cirurgicos/{id}` | Adm, Méd, Ctrl, SA |
| `POST` | `/api/atendimentos-cirurgicos/{id}/arquivos` | Adm, Méd, Ctrl, SA |
| `DELETE` | `/api/atendimentos-cirurgicos/{id}/arquivos/{arquivoId}` | Adm, Méd, Ctrl, SA |
| `GET` | `/api/atendimentos-cirurgicos/{id}/arquivos/{arquivoId}/download` | Adm, Méd, Ctrl, SA |

### Faturamento médico

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/faturamentos-medicos` | Adm, Méd, Ctrl, SA, Eq |
| `GET` | `/api/faturamentos-medicos/historico/arquivos` | Adm, Méd, Ctrl, SA, Eq |
| `DELETE` | `/api/faturamentos-medicos/historico/arquivos/{arquivoId}` | Adm, Ctrl, SA |
| `GET` | `/api/faturamentos-medicos/historico/arquivos/{arquivoId}/download` | Adm, Méd, Ctrl, SA, Eq |
| `POST` | `/api/faturamentos-medicos/historico/{ano}/{mes}/arquivos` | Adm, Ctrl, SA |

### Faturamento (lotes, glosas e recursos)

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/faturamentos` | Adm, Méd, Ctrl, SA |
| `POST` | `/api/faturamentos` | Adm, Ctrl, SA |
| `PUT` | `/api/faturamentos/glosas/{id}` | Adm, Ctrl, SA |
| `DELETE` | `/api/faturamentos/glosas/{id}` | Adm, Ctrl, SA |
| `POST` | `/api/faturamentos/glosas/{id}/recursos` | Adm, Ctrl, SA |
| `GET` | `/api/faturamentos/pesquisa` | Adm, Méd, Ctrl, SA |
| `PUT` | `/api/faturamentos/recursos-glosa/{id}` | Adm, Ctrl, SA |
| `DELETE` | `/api/faturamentos/recursos-glosa/{id}` | Adm, Ctrl, SA |
| `GET` | `/api/faturamentos/{id}` | Adm, Méd, Ctrl, SA |
| `PUT` | `/api/faturamentos/{id}` | Adm, Ctrl, SA |
| `DELETE` | `/api/faturamentos/{id}` | Adm, Ctrl, SA |
| `POST` | `/api/faturamentos/{id}/contas-receber` | Adm, Ctrl, SA |
| `POST` | `/api/faturamentos/{id}/glosas` | Adm, Ctrl, SA |
| `PUT` | `/api/faturamentos/{id}/itens/{itemId}` | Adm, Ctrl, SA |
| `POST` | `/api/faturamentos/{id}/retorno` | Adm, Ctrl, SA |
| `PUT` | `/api/faturamentos/{id}/status` | Adm, Ctrl, SA |

### Contas a receber

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/financeiro/contas-receber` | Adm, Ctrl, SA |
| `GET` | `/api/financeiro/contas-receber/pesquisa` | Adm, Ctrl, SA |
| `GET` | `/api/financeiro/contas-receber/recebimentos/{id}/comprovante` | Adm, Ctrl, SA |
| `POST` | `/api/financeiro/contas-receber/recebimentos/{id}/comprovante` | Adm, Ctrl, SA |
| `POST` | `/api/financeiro/contas-receber/recebimentos/{id}/estorno` | Adm, Ctrl, SA |
| `GET` | `/api/financeiro/contas-receber/{id}` | Adm, Ctrl, SA |
| `PUT` | `/api/financeiro/contas-receber/{id}` | Adm, Ctrl, SA |
| `POST` | `/api/financeiro/contas-receber/{id}/cancelamento` | Adm, Ctrl, SA |
| `POST` | `/api/financeiro/contas-receber/{id}/recebimentos` | Adm, Ctrl, SA |

### Relatórios financeiros

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/financeiro/auditoria` | Adm, Ctrl, SA |
| `GET` | `/api/financeiro/relatorios/resumo` | Adm, Ctrl, SA |

### Preços por convênio

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/convenios-procedimentos-precos` | Adm, Méd, Ctrl, SA |
| `POST` | `/api/convenios-procedimentos-precos` | Adm, Ctrl, SA |
| `PUT` | `/api/convenios-procedimentos-precos/{id}` | Adm, Ctrl, SA |
| `DELETE` | `/api/convenios-procedimentos-precos/{id}` | Adm, Ctrl, SA |

### Grupos médicos

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/grupos-medicos` | Adm, Ctrl, SA |
| `POST` | `/api/grupos-medicos` | Adm, Ctrl, SA |
| `GET` | `/api/grupos-medicos/medicos` | Autenticado |
| `GET` | `/api/grupos-medicos/{id}` | Adm, Ctrl, SA |
| `PUT` | `/api/grupos-medicos/{id}` | Adm, Ctrl, SA |
| `DELETE` | `/api/grupos-medicos/{id}` | Adm, Ctrl, SA |

### Agenda e notificações

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/events` | Autenticado |
| `POST` | `/api/events` | Autenticado (Eq: operador identificado) |
| `GET` | `/api/events/medical-users` | Autenticado |
| `GET` | `/api/events/notification-recipients` | Autenticado |
| `POST` | `/api/events/notifications/mark-read` | Autenticado |
| `GET` | `/api/events/{id}` | Autenticado |
| `PUT` | `/api/events/{id}` | Autenticado (Eq: operador identificado) |
| `DELETE` | `/api/events/{id}` | Autenticado (Eq: operador identificado) |
| `POST` | `/api/events/{id}/complete` | Autenticado (Eq: operador identificado) |
| `GET` | `/api/events/{id}/email-notification` | Autenticado |
| `POST` | `/api/events/{id}/email-notification` | Autenticado (Eq: operador identificado) |

### CBHPM

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/cbhpm` | Autenticado + licença `Cbhpm.Consultar` |
| `POST` | `/api/cbhpm/import` | Adm, SA |

### Hospitais

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/hospitais` | Autenticado |

### Convênios

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/convenios` | Autenticado |

### OPME

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/opme` | Autenticado |

### Licenças

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/licencas/current` | Autenticado |
| `GET` | `/api/licencas/users/{userId}` | Adm, SA |
| `PUT` | `/api/licencas/users/{userId}` | Adm, SA |
| `POST` | `/api/licencas/users/{userId}/liberar-completa` | Adm, SA |

### Exportações

| Método | Rota | Acesso |
| --- | --- | --- |
| `POST` | `/api/exports` | Autenticado (Eq: operador identificado) |

### Monitoramento

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/monitoramento/erros` | Adm, SA |
| `DELETE` | `/api/monitoramento/erros` | Adm, SA |
| `GET` | `/api/monitoramento/seguranca` | Adm, SA |

### Infraestrutura

| Método | Rota | Acesso |
| --- | --- | --- |
| `GET` | `/api/warmup` | Público (rate limit `Warmup`) |
| `GET`/`HEAD` | `/healthz`, `/readyz` | Público (somente status) |
| `GET`/`HEAD` | `/livez` e `/` | Público (liveness, sem banco) |

O seletor publico do login consulta diretamente o banco de dados e retorna somente as clinicas
ativas. A listagem e a foto publica usam a mesma fonte de dados utilizada na resolucao da clinica
durante a autenticacao.

Configure os emails autorizados com `Platform__SuperAdminEmails__0`. No startup, o usuario correspondente e promovido e recebe uma associacao `UsuarioClinica` em cada clinica ativa. Administradores comuns permanecem restritos as associacoes explicitamente cadastradas.

`UsuarioGlobal` guarda a credencial unica; `UsuarioClinica` liga essa identidade ao usuario local, clinica e perfil. O `ClinicaId` do JWT e o tenant efetivo. Consultas sem contexto resolvido falham fechadas; gravacoes com `ClinicaId` divergente e relacionamentos internos entre clinicas diferentes sao recusados antes do `SaveChanges`.

O nome e a foto institucionais pertencem a `Clinica` e somente o SuperAdministrador pode altera-los pelo CRUD de plataforma. O antigo `PUT /api/configuracoes-sistema/current` nao e mais exposto; a configuracao do sistema e somente leitura publica.

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

A tabela `CBHPMGeral` e criada por migration e pode receber seed a partir do JSON gerado do PDF da tabela
(`scripts/import_cbhpm_pdf.py`). `GET /api/cbhpm` filtra, ordena e pagina no SQL (LIKE e Full-Text Search quando
disponivel). O `IMemoryCache` (`cbhpm-geral:v1`, 12 h absoluta / 2 h deslizante) resolve procedimentos por codigo no
cadastro de pacientes e e invalidado pela importacao.

Consulta tipica:

```http
GET /api/cbhpm?page=1&pageSize=10&codigo=1.01&procedimento=consulta&porte=2B
Authorization: Bearer <token>
```

## Banco de dados

Tabelas principais (DbSets de `AppDbContext`), por area:

- Multiclinica e identidade: `Clinicas`, `UsuariosGlobais`, `UsuariosClinicas`, `Users`, `Perfis`, `AuthenticationSessions`, `TemporaryAccessCredentials`, `EmailChangeRequests`, `PasswordResetTokens`, `AuditoriasPlataforma`
- Equipes: `Equipes`, `EquipeMembros`, `EquipeOperadores`, `EquipeLoginDesafios`
- LGPD: `UserLegalAcceptances`, `UserPrivacyPreferences`
- Pacientes e cirurgias: `Pacientes` (inclui `TipoCirurgia` e `StatusCirurgia`), `PacienteArquivos`, `PacienteProcedimentos`, `Observacoes`
- Faturamento medico: `FaturamentosMedicos`, `FaturamentoHistoricoArquivos`
- Financeiro: `AtendimentosCirurgicos`, `AtendimentoProcedimentos`, `AtendimentoArquivos`, `Faturamentos`, `FaturamentoItens`, `Glosas`, `RecursosGlosa`, `ContasReceber`, `Recebimentos`, `ConvenioProcedimentoPrecos`, `FinanceiroMigracaoInconsistencias`
- Agenda: `Events`, `AgendaNotifications`, `EventEmailDeliveries`
- Catalogos e grupos: `Hospitais`, `Convenios`, `OPME`, `CbhpmGeral`, `GruposMedicos`, `GrupoMedicoUsuarios`
- Outros: `UserArquivos`, `Licencas`, `ConfiguracoesSistema`, `IdempotencyRequests`

O schema e definido em `HemodinksAPI.Infrastructure/Data/Configurations`; consulte la (e nao as migrations) para entender o modelo.

Em desenvolvimento, migrations podem rodar no startup com `Database__RunMigrationsOnStartup=true`.
Em produção, mantenha essa opção desabilitada e use o workflow manual `Apply Production Migrations`
antes de publicar a imagem que depende do novo schema.

### Tempo de inicializacao

O workflow `Publish Production` (`publish-container.yml`, push na `main`) executa `migrate-production` antes de `deploy-api`.
Somente nesse deploy, `Database__SchemaManagedByDeployment=true` dispensa a consulta
de migrations no startup e nos health checks. O padrao e `false`: Render e execucoes
fora desse fluxo continuam validando o schema. Nao habilite essa opcao em um deploy
que nao garanta a aplicacao do bundle da mesma imagem antes de liberar trafego.
Seeds, manutencao e migrations explicitamente habilitados continuam sendo executados.
`/healthz` e `/readyz` continuam retornando 503 se o banco estiver indisponivel;
`/livez` verifica apenas se o processo esta vivo.

A imagem da API usa ReadyToRun, mantendo o runtime compartilhado. Para comparar com
a compilacao anterior, construa a mesma revisao com
`docker build --build-arg PUBLISH_READY_TO_RUN=false -t hemodinks-api:baseline .`.
O padrao e `PUBLISH_READY_TO_RUN=true`; a imagem pode ficar maior, por isso compare
tambem o tempo de download e o tamanho final antes de concluir que houve ganho.

Os logs `Startup stage` registram duracoes em milissegundos de
`configuration_and_services`, `host_build`, `database_initialization`, `http_started`,
das primeiras tentativas `database_probe_connected`/`database_probe_failed` e
`first_database_ready`. `StartupElapsedMs` conta desde a entrada no programa;
`http_started` mede o intervalo apos a inicializacao do banco ate o servidor iniciar.
A primeira prontidao inclui o intervalo ate a plataforma chamar o health check.
Esses tempos nao incluem provisionamento do container, download da imagem ou o
carregamento do runtime antes da entrada no programa.

Para medir o cold start real, compare a mesma configuracao de recursos e banco,
com replicas inicialmente em zero, antes e depois do deploy. Registre o tempo total
da primeira chamada e correlacione seu horario com os eventos do Container Apps e
os logs acima. Repita algumas retomadas: uma unica amostra nao separa variacao da
plataforma de ganho da aplicacao. Nenhuma alteracao de replicas minimas ou keep-alive
e necessaria para estas otimizacoes.

O diretorio publico usa `IPublicClinicDirectory` na Application e
`SqlPublicClinicDirectory` na Infrastructure. O leitor SQL nao inicializa o modelo
completo do EF: consulta apenas clinicas ativas, limita a lista a 50 e retorna
Id, Nome, Slug e um indicador de foto. Referencias internas de storage nao entram
na listagem publica; a foto so e buscada apos encontrar a clinica ativa pelo slug.
Busca e slug sao parametros SQL, e os filtros nao dependem de headers de tenant.
Esse diretorio e global e publico por definicao; nao deve ser reutilizado para
dados de usuarios, pacientes, equipes ou faturamento. Esses fluxos mantem seus
contextos, filtros de tenant e autorizacoes existentes.

O health check depende de `IDatabaseReadinessProbe`; a implementacao SQL abre uma
conexao e executa `SELECT 1`. Quando o schema e gerenciado pelo deploy, esse probe
nao resolve o contexto EF. Fora desse fluxo, continua verificando migrations.
As respostas publicas de health continuam contendo somente o status, sem detalhes
de conexao ou excecoes. Cancelamento e descarte das conexoes sao propagados pelo
leitor e pelo probe. Autenticacao, rate limiting e instrumentacao permanecem ativos.

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
dotnet test HemodinksAPI.slnx --no-build --filter "FullyQualifiedName!~LoginBrowserTests"   # suite do CI
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName~<ClasseDeTeste>"                 # focado
```

Testes `*SqlServerTests` exigem `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` (ou `HEMODINKS_TEST_LOCALDB=1`); sem isso sao ignorados localmente,
mas o CI os executa e `scripts/check-test-results.py` falha se algum teste obrigatorio for pulado.
`LoginBrowserTests` rodam no workflow `Login integration E2E`.

## Documentos relacionados

- [Implementacao](./IMPLEMENTACAO.md)
- [Deploy](./docs/deployment.md)
- [Documentacao tecnica](./docs/TECHNICAL_DOCUMENTATION.md)
- [Production readiness](./PRODUCTION_READINESS.md)
- [Primeira execucao](./PRIMEIRA_EXECUCAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Exemplos HTTP](./API.http)
- [Documentacao tecnica PDF](./docs/Hemodinks-Documentacao-Tecnica.pdf)

### Homologacao

O merge na `developer` executa apenas o CI. Para publicar a API de homologacao (Render), rode manualmente
`Actions > Publish Homologation > Run workflow` na branch `developer` e escolha o modo:

| Modo | Efeito |
| --- | --- |
| `diagnose` | valida a configuracao do Render, sem banco e sem deploy |
| `diagnose-sql` | consulta conexao e historico de migrations, sem alterar nada |
| `prepare` (padrao) | gera bundle e SQL auditavel, sem acessar o banco |
| `publish` | aplica somente migrations pendentes conhecidas (quando houver) e publica o commit verificado |

Consulte a [ativacao e operacao](docs/homologation-migrations.md) antes de sincronizar o blueprint.
