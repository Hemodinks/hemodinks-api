# AGENTS.md — HemoDinks API

.NET 10, Minimal APIs, MediatR (CQRS), FluentValidation, EF Core + SQL Server.
Sistema de saúde multi-clínica: segurança e isolamento por clínica vêm antes de tudo.

## Mapa

- Entidades: `HemodinksAPI.Domain/Models` (`IClinicaOwnedEntity` = dado de uma clínica)
- Casos de uso: `HemodinksAPI.Application/Features/<Módulo>/` (Commands, Queries, validators)
- Endpoints: `HemodinksAPI.Api/Endpoints/` e `HemodinksAPI.Api/Features/**` — finos: mapeiam, validam e chamam MediatR
- Políticas de autorização: `HemodinksAPI.Api/ApiServiceCollectionExtensions.Auth.cs`; módulos do plano: `HemodinksAPI.Api/Middleware/ClinicaModuleAccessMiddleware.cs`
- Middlewares de tenant e auditoria: `HemodinksAPI.Api/Middleware/`; sessão: `HemodinksAPI.Api/Sessions/`; idempotência: `HemodinksAPI.Api/Idempotency/`; monitoramento: `HemodinksAPI.Api/Monitoring/`
- Dados: `HemodinksAPI.Infrastructure/Data/AppDbContext.cs` (filtro global de tenant e bloqueio de FK entre clínicas)
- Schema: `HemodinksAPI.Infrastructure/Data/Configurations/` (leia aqui, não nas migrations)
- Azure Functions: `HemodinksAPI.Workers/Functions/`
- Aspire: `Hemodinks.AppHost` (orquestração local) e `Hemodinks.ServiceDefaults` (health checks; observabilidade só pelo agente New Relic .NET — sem OpenTelemetry/Sentry)
- CI/CD: `.github/workflows/` (`ci.yml` em push e PR; produção em Container Apps blue/green; homologação no Render)
- Testes: `HemodinksAPI.Tests/<Área>*Tests.cs` (SQLite/InMemory). `*SqlServerTests` exigem `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING`; os obrigatórios ficam em `scripts/check-test-results.py`.
- Docs: `README.md` (inventário de rotas × perfis), `docs/` (runbooks), matriz gerada por `scripts/generate_permissions_matrix.py`.
- Codex: subagentes e política de esforço em `.codex/ORCHESTRATION.md`.

## Comandos

```bash
dotnet build HemodinksAPI.slnx
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName~<ClasseDeTeste>"          # focado
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName!~LoginBrowserTests"        # suíte do CI
dotnet ef migrations add <Nome> -p HemodinksAPI.Infrastructure -s HemodinksAPI.Api
```

## Não ler (gasta contexto e não ajuda)

`Data/Migrations/*.Designer.cs`, `*ModelSnapshot.cs`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, `logs/`, `out/`.
Para entender o modelo, leia `Data/Configurations` e a entidade. Abra uma migration só se a tarefa for sobre ela.

## Invariantes — nunca quebrar

1. Isolamento por clínica: o filtro global é fail-closed (sem `ClinicaId` → nada); não remover nem contornar.
   `ClinicaId` vem do contexto autenticado, nunca do payload. Escopo de plataforma só via `ClinicaContextFactory`
   (`SetPlatformScope`); não criar outro caminho. Novo `IgnoreQueryFilters` só com justificativa explícita no PR
   e entrada em `HemodinksAPI.Tests/IgnoreQueryFiltersAllowlistTests.cs`.
2. Perfis administrativos (Admin, SuperAdmin) não são bypass implícito de tenant. Autorização é sempre no backend.
3. Nunca registrar senha, PIN, token completo, secret, connection string ou CPF. Erros não retornam dado sensível.
4. Não reduzir iterações do PBKDF2-SHA256 (senha 600 mil, PIN 210 mil) nem criar criptografia própria.
5. Migration destrutiva só com pedido explícito.
6. Sem commit, push, merge ou deploy sem autorização explícita. PRs vão para `developer`.
7. Sem dependência nova ou serviço pago sem requisito explícito.

## Alto risco: plano curto antes, testes negativos depois

Autenticação, autorização, sessão/JWT/refresh, `SecurityVersion`, senha/PIN/recuperação/senha temporária, MFA,
primeiro acesso, `ClinicaId`/escopo de plataforma, middleware, forwarded headers e rate limit
(`AzureContainerAppsIngressMiddleware`, `AuthenticationRateLimiting`), migration, contrato consumido pelo `hemodinks-front`, workflows de
produção (`publish-*`, `apply-production-migrations`, `rollback-production`).

- Investigue o fluxo completo antes de editar.
- Teste obrigatório de tenant: recurso da Clínica A acessado pela Clínica B → negado ou 404, sem revelar existência.
- Mudou contrato de endpoint → avise que o `hemodinks-front` precisa acompanhar.
- Workflow: mantenha `permissions` mínimas, `timeout-minutes`, `concurrency` e actions com versão fixa; secret só via `env`, nunca em log.

## Escopo

Menor mudança correta. Sem refatoração oportunista, renomeação ou troca de padrão sem pedido.
Problema fora do escopo → reporte, não corrija. Não presuma classe, tabela, endpoint ou teste sem localizar no código.

## Documentação

- Mudou rota, política ou perfil → regere o inventário do `README.md` (`scripts/generate_route_inventory.py`), atualize os dados de `scripts/generate_permissions_matrix.py`,
  rode o script e regere o PDF da matriz (comando no cabeçalho do script).
- Nova migration → atualize o marcador de schema em `scripts/ValidateRestoredDatabase.sql`,
  `HemodinksAPI.Tests/RestoreDatabaseIntegrityTests.cs` e `docs/issue-184-backup-recovery.md`.
- `docs/issue-*.md` são registros históricos: não reescreva; marque o que ficou obsoleto.

## Pronto quando

Testes focados passam (amplie a suíte conforme o risco), diff revisado, nada sensível introduzido.
Resposta final, só isto: o que mudou · arquivos principais · testes executados e resultado · riscos reais.

## Prioridade

Segurança > isolamento de dados > correção funcional > arquitetura > testes > performance > conveniência.
